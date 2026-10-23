using System;
using System.Collections.Generic;

namespace WhoisntCitizen.Game.Tests
{
    /// <summary>
    /// 다른 IGameApi를 감싸서 호출 수를 세고, 실제 서버 상황을 흉내 낸다.
    /// Deferred: 응답을 Flush() 때까지 미룬다(비동기). Offline: 연결 실패(0). Gone: 게임이 지워짐(404).
    /// NoExtendApi: 토론 시간 연장 API가 없는 서버 (오류 code 없는 404).
    /// </summary>
    public sealed class TestGameApi : IGameApi
    {
        private readonly IGameApi inner;
        private readonly List<Action> pending = new List<Action>();
        private readonly List<KeyValuePair<string, Action>> responses = new List<KeyValuePair<string, Action>>();
        private readonly Dictionary<string, int> calls = new Dictionary<string, int>();

        public bool Deferred;
        public bool Offline;
        public bool Gone;
        public bool NoExtendApi;
        public bool HoldResponses; // 서버 처리는 끝내고 응답 전달만 미룬다 (GET/POST 도착 순서 재현)
        public bool OmitDaySkipCount;

        public TestGameApi(IGameApi inner)
        {
            this.inner = inner;
        }

        public int Calls(string method)
        {
            int count;
            return calls.TryGetValue(method, out count) ? count : 0;
        }

        public int PendingCount
        {
            get { return pending.Count; }
        }

        /// <summary>미뤄 둔 응답을 보낸 순서대로 돌려준다.</summary>
        public void Flush()
        {
            var now = new List<Action>(pending);
            pending.Clear();
            foreach (Action a in now)
            {
                a();
            }
        }

        public void GetState(string gameId, Action<GameApiResult<GameStateDto>> onDone)
        {
            Handle("GetState", onDone, cb => inner.GetState(gameId, result =>
            {
                if (OmitDaySkipCount && result.Success) result.Data.daySkipCount = -1;
                cb(result);
            }));
        }

        public void ReleaseResponse(int index = 0)
        {
            Action response = responses[index].Value;
            responses.RemoveAt(index);
            response();
        }

        /// <summary>보류한 응답 중 그 메서드의 첫 응답을 보낸다.</summary>
        public void ReleaseResponse(string method)
        {
            ReleaseResponse(responses.FindIndex(r => r.Key == method));
        }

        public void GetMe(string gameId, Action<GameApiResult<MyRoleDto>> onDone)
        {
            Handle("GetMe", onDone, cb => inner.GetMe(gameId, cb));
        }

        public void SubmitNightAction(string gameId, long targetId, string actionCode, Action<GameApiResult<NightActionResultDto>> onDone)
        {
            Handle("SubmitNightAction", onDone, cb => inner.SubmitNightAction(gameId, targetId, actionCode, cb));
        }

        public void SkipNightAction(string gameId, Action<GameApiResult<NightActionResultDto>> onDone)
        {
            Handle("SkipNightAction", onDone, cb => inner.SkipNightAction(gameId, cb));
        }

        public void SkipDay(string gameId, Action<GameApiResult<DaySkipResultDto>> onDone)
        {
            Handle("SkipDay", onDone, cb => inner.SkipDay(gameId, cb));
        }

        public void ExtendDay(string gameId, Action<GameApiResult<DayExtendResultDto>> onDone)
        {
            Handle("ExtendDay", onDone, cb =>
            {
                if (NoExtendApi)
                {
                    cb(GameApiResult<DayExtendResultDto>.Fail(404, null, "Not Found"));
                }
                else
                {
                    inner.ExtendDay(gameId, cb);
                }
            });
        }

        public void GetNightResult(string gameId, Action<GameApiResult<NightResultDto>> onDone)
        {
            Handle("GetNightResult", onDone, cb => inner.GetNightResult(gameId, cb));
        }

        public void Vote(string gameId, long targetId, bool confirm, Action<GameApiResult<VoteResultDto>> onDone)
        {
            Handle("Vote", onDone, cb => inner.Vote(gameId, targetId, confirm, cb));
        }

        public void GetExecutionResult(string gameId, Action<GameApiResult<ExecutionResultDto>> onDone)
        {
            Handle("GetExecutionResult", onDone, cb => inner.GetExecutionResult(gameId, cb));
        }

        public void GetResult(string gameId, Action<GameApiResult<GameResultDto>> onDone)
        {
            Handle("GetResult", onDone, cb => inner.GetResult(gameId, cb));
        }

        public void GetRoleSetupOptions(Action<GameApiResult<RoleSetupOptionsDto>> onDone)
        {
            Handle("GetRoleSetupOptions", onDone, cb => inner.GetRoleSetupOptions(cb));
        }

        private void Handle<T>(string method, Action<GameApiResult<T>> onDone, Action<Action<GameApiResult<T>>> forward) where T : class
        {
            calls[method] = Calls(method) + 1;
            Action run = () =>
            {
                if (Gone)
                {
                    onDone(GameApiResult<T>.Fail(404, GameErrorCodes.GameNotFound, "게임을 찾을 수 없습니다: " + FakeGameApi.FakeGameId));
                }
                else if (Offline)
                {
                    onDone(GameApiResult<T>.Fail(0, null, "서버에 연결할 수 없습니다."));
                }
                else
                {
                    forward(result =>
                    {
                        if (HoldResponses) responses.Add(new KeyValuePair<string, Action>(method, () => onDone(result)));
                        else onDone(result);
                    });
                }
            };
            if (Deferred)
            {
                pending.Add(run);
            }
            else
            {
                run();
            }
        }
    }

    /// <summary>
    /// 정해 둔 상태·내 역할·밤 결과만 돌려주고 밤 행동·투표 요청을 기록하는 서버 (가짜 서버에 없는 직업: 세이렌·크라켄·요리사).
    /// 밤 행동과 투표는 모두 받아 준다. 판정은 하지 않는다.
    /// </summary>
    public sealed class ScriptedGameApi : IGameApi
    {
        public GameStateDto State;
        public MyRoleDto Me;
        public NightResultDto NightResult; // null이면 밤 결과 조회는 실패한다
        public readonly List<KeyValuePair<long, string>> NightActions = new List<KeyValuePair<long, string>>(); // (targetId, actionCode)
        public readonly List<long> Votes = new List<long>(); // targetId

        public void GetState(string gameId, Action<GameApiResult<GameStateDto>> onDone)
        {
            onDone(GameApiResult<GameStateDto>.Ok(State));
        }

        public void GetMe(string gameId, Action<GameApiResult<MyRoleDto>> onDone)
        {
            onDone(GameApiResult<MyRoleDto>.Ok(Me));
        }

        public void SubmitNightAction(string gameId, long targetId, string actionCode, Action<GameApiResult<NightActionResultDto>> onDone)
        {
            NightActions.Add(new KeyValuePair<long, string>(targetId, actionCode));
            onDone(GameApiResult<NightActionResultDto>.Ok(
                new NightActionResultDto { accepted = true, phase = State.phase, phaseVersion = State.phaseVersion }));
        }

        public void SkipNightAction(string gameId, Action<GameApiResult<NightActionResultDto>> onDone)
        {
            NightActions.Add(new KeyValuePair<long, string>(0, "SKIP"));
            onDone(GameApiResult<NightActionResultDto>.Ok(
                new NightActionResultDto { accepted = true, phase = State.phase, phaseVersion = State.phaseVersion }));
        }

        public void SkipDay(string gameId, Action<GameApiResult<DaySkipResultDto>> onDone) { Unused(onDone); }
        public void ExtendDay(string gameId, Action<GameApiResult<DayExtendResultDto>> onDone) { Unused(onDone); }
        public void GetNightResult(string gameId, Action<GameApiResult<NightResultDto>> onDone)
        {
            if (NightResult == null)
            {
                Unused(onDone);
                return;
            }
            onDone(GameApiResult<NightResultDto>.Ok(NightResult));
        }

        public void Vote(string gameId, long targetId, bool confirm, Action<GameApiResult<VoteResultDto>> onDone)
        {
            Votes.Add(targetId);
            onDone(GameApiResult<VoteResultDto>.Ok(new VoteResultDto { accepted = true, phase = State.phase, phaseVersion = State.phaseVersion }));
        }
        public void GetExecutionResult(string gameId, Action<GameApiResult<ExecutionResultDto>> onDone) { Unused(onDone); }
        public void GetResult(string gameId, Action<GameApiResult<GameResultDto>> onDone) { Unused(onDone); }
        public void GetRoleSetupOptions(Action<GameApiResult<RoleSetupOptionsDto>> onDone) { Unused(onDone); }

        private static void Unused<T>(Action<GameApiResult<T>> onDone) where T : class
        {
            onDone(GameApiResult<T>.Fail(0, null, "테스트에서 쓰지 않는 API입니다."));
        }
    }

    /// <summary>GameSession이 화면에 알린 것을 기록한다.</summary>
    public sealed class RecordingView : IGameView
    {
        public readonly List<GameEvent> Phases = new List<GameEvent>();
        public readonly List<GameEvent> Deaths = new List<GameEvent>();
        public readonly List<MyRoleDto> MyRoles = new List<MyRoleDto>();
        public readonly List<NightResultDto> NightResults = new List<NightResultDto>();
        public readonly List<ExecutionResultDto> Executions = new List<ExecutionResultDto>();
        public readonly List<GameResultDto> GameResults = new List<GameResultDto>();
        public readonly List<NightActionResultDto> ActionsAccepted = new List<NightActionResultDto>();
        public readonly List<DaySkipResultDto> DaySkipsAccepted = new List<DaySkipResultDto>();
        public readonly List<DayExtendResultDto> DayExtendsAccepted = new List<DayExtendResultDto>();
        public readonly List<VoteResultDto> VotesAccepted = new List<VoteResultDto>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<bool> Connections = new List<bool>();
        public int States;
        public bool Closed;

        public int TotalCalls
        {
            get
            {
                return States + Phases.Count + Deaths.Count + MyRoles.Count + NightResults.Count + Executions.Count
                    + GameResults.Count + ActionsAccepted.Count + DaySkipsAccepted.Count + DayExtendsAccepted.Count + VotesAccepted.Count
                    + Errors.Count + Connections.Count
                    + (Closed ? 1 : 0);
            }
        }

        public void ShowState(GameStateDto state) { States++; }
        public void ShowPhase(GameEvent phaseChanged) { Phases.Add(phaseChanged); }
        public void ShowPlayerDied(GameEvent died) { Deaths.Add(died); }
        public void ShowMyRole(MyRoleDto me) { MyRoles.Add(me); }
        public void ShowNightResult(NightResultDto result) { NightResults.Add(result); }
        public void ShowExecutionResult(ExecutionResultDto result) { Executions.Add(result); }
        public void ShowGameResult(GameResultDto result) { GameResults.Add(result); }
        public void ShowActionAccepted(NightActionResultDto result) { ActionsAccepted.Add(result); }
        public void ShowDaySkipAccepted(DaySkipResultDto result) { DaySkipsAccepted.Add(result); }
        public void ShowDayExtendAccepted(DayExtendResultDto result) { DayExtendsAccepted.Add(result); }
        public void ShowVoteAccepted(VoteResultDto result) { VotesAccepted.Add(result); }
        public void ShowError(string message) { Errors.Add(message); }
        public void ShowConnection(bool connected) { Connections.Add(connected); }
        public void OnGameClosed() { Closed = true; }
    }
}
