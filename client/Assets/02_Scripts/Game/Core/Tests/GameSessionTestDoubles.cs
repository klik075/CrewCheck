using System;
using System.Collections.Generic;

namespace WhoisntCitizen.Game.Tests
{
    /// <summary>
    /// 다른 IGameApi를 감싸서 호출 수를 세고, 실제 서버 상황을 흉내 낸다.
    /// Deferred: 응답을 Flush() 때까지 미룬다(비동기). Offline: 연결 실패(0). Gone: 게임이 지워짐(404).
    /// </summary>
    public sealed class TestGameApi : IGameApi
    {
        private readonly IGameApi inner;
        private readonly List<Action> pending = new List<Action>();
        private readonly Dictionary<string, int> calls = new Dictionary<string, int>();

        public bool Deferred;
        public bool Offline;
        public bool Gone;

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
            Handle("GetState", onDone, cb => inner.GetState(gameId, cb));
        }

        public void GetMe(string gameId, Action<GameApiResult<MyRoleDto>> onDone)
        {
            Handle("GetMe", onDone, cb => inner.GetMe(gameId, cb));
        }

        public void SubmitNightAction(string gameId, long targetId, Action<GameApiResult<NightActionResultDto>> onDone)
        {
            Handle("SubmitNightAction", onDone, cb => inner.SubmitNightAction(gameId, targetId, cb));
        }

        public void SkipNightAction(string gameId, Action<GameApiResult<NightActionResultDto>> onDone)
        {
            Handle("SkipNightAction", onDone, cb => inner.SkipNightAction(gameId, cb));
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
                    forward(onDone);
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
                    + GameResults.Count + ActionsAccepted.Count + VotesAccepted.Count + Errors.Count + Connections.Count
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
        public void ShowVoteAccepted(VoteResultDto result) { VotesAccepted.Add(result); }
        public void ShowError(string message) { Errors.Add(message); }
        public void ShowConnection(bool connected) { Connections.Add(connected); }
        public void OnGameClosed() { Closed = true; }
    }
}
