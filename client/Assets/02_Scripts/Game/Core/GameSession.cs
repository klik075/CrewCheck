using System;
using System.Collections.Generic;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 게임 한 판의 진행. 상태를 폴링하고, 바뀐 것을 IGameView에 알리고, 페이즈에 맞는 API를 부른다.
    /// 판정은 서버가 하고, 여기서는 받아 와서 보여 주기만 한다. MonoBehaviour가 아니라서 EditMode로 테스트한다.
    /// - Tick()을 매 프레임 부른다. 폴링 간격이 지났고 앞선 폴링 응답을 받았으면 GET /games/{id}를 보낸다.
    /// - 페이즈가 바뀌면 /me를 다시 받는다. 밤 결과·처형 결과는 "그날 것을 아직 못 보여 줬으면" 받아 온다.
    ///   그래서 폴링 사이에 NIGHT_RESULT·EXECUTION을 놓쳐도, 승패가 나서 바로 ENDED가 돼도 결과가 빠지지 않는다.
    /// - ENDED가 되면 게임 결과를 받고 폴링을 멈춘다. 404(서버에서 게임이 지워짐)면 닫는다.
    /// - Close() 뒤에 도착한 응답은 무시한다.
    /// </summary>
    public sealed class GameSession
    {
        public const string CannotVoteMessage = "지금은 투표할 수 없습니다.";

        private readonly IGameApi api;
        private readonly IGameView view;
        private readonly Func<double> clock;
        private readonly double pollInterval;
        private readonly PhaseTracker tracker = new PhaseTracker();
        private readonly PhaseClock phaseClock = new PhaseClock();

        private bool closed;
        private bool connected = true;
        private double nextPollAt;          // 0 → 다음 Tick에 바로
        private bool pollInFlight;
        private bool actionInFlight;        // 밤 행동·넘기기·투표는 한 번에 하나만
        private bool meNeeded = true;
        private bool meInFlight;
        private bool nightInFlight;
        private bool executionInFlight;
        private bool resultInFlight;
        private int nightShownDay;          // 보여 준 마지막 밤 결과의 일차
        private int executionShownDay;      // 보여 준(또는 볼 것이 없다고 확인한) 마지막 처형 결과의 일차

        private long lockedVersion = -1;    // 앵무새가 접선한 밤의 phaseVersion
        private long nightChoiceVersion = -1;
        private long nightChoiceTarget;     // 0 = 넘김
        private long voteVersion = -1;
        private long voteTarget;

        /// <param name="clock">단조 증가 시간(초). Unity에서는 () => Time.realtimeSinceStartupAsDouble</param>
        public GameSession(IGameApi api, string gameId, IGameView view, Func<double> clock, double pollIntervalSeconds)
        {
            if (api == null)
            {
                throw new ArgumentNullException("api");
            }
            if (string.IsNullOrEmpty(gameId))
            {
                throw new ArgumentException("gameId가 없습니다.");
            }
            if (view == null)
            {
                throw new ArgumentNullException("view");
            }
            if (clock == null)
            {
                throw new ArgumentNullException("clock");
            }
            this.api = api;
            GameId = gameId;
            this.view = view;
            this.clock = clock;
            pollInterval = Math.Max(0.2, pollIntervalSeconds);
        }

        // ================================================================ 화면이 읽는 값

        public string GameId { get; private set; }

        /// <summary>마지막으로 받은 상태. 첫 응답 전에는 null.</summary>
        public GameStateDto State
        {
            get { return tracker.Current; }
        }

        /// <summary>내 역할. 첫 /me 응답 전에는 null.</summary>
        public MyRoleDto Me { get; private set; }

        public NightResultDto LastNightResult { get; private set; }
        public ExecutionResultDto LastExecutionResult { get; private set; }
        public GameResultDto Result { get; private set; }

        public bool IsClosed
        {
            get { return closed; }
        }

        public bool IsConnected
        {
            get { return connected; }
        }

        public bool IsEnded
        {
            get { return State != null && State.phase == GamePhases.Ended; }
        }

        /// <summary>남은 초 (표시용 올림). 마감이 없으면 0.</summary>
        public int RemainingWholeSeconds
        {
            get { return phaseClock.RemainingWholeSeconds(clock()); }
        }

        /// <summary>마감이 지났는데 서버가 아직 다음 페이즈로 넘기지 않음 ("판정 중" 표시용).</summary>
        public bool IsWaitingForServer
        {
            get { return phaseClock.IsExpired(clock()); }
        }

        /// <summary>앵무새가 이번 밤 접선해서 행동이 확정됨 (다음 밤에 풀린다).</summary>
        public bool LockedTonight
        {
            get { return IsCurrentNight(lockedVersion); }
        }

        /// <summary>이번 밤 내가 고른 대상. 고르지 않았거나 넘겼으면 0.</summary>
        public long MyNightTarget
        {
            get { return IsCurrentNight(nightChoiceVersion) ? nightChoiceTarget : 0; }
        }

        /// <summary>이번 밤 능력을 넘겼는지.</summary>
        public bool SkippedTonight
        {
            get { return IsCurrentNight(nightChoiceVersion) && nightChoiceTarget == 0; }
        }

        /// <summary>이번 투표에서 내가 고른 대상. 아직 투표하지 않았으면 0.</summary>
        public long MyVoteTarget
        {
            get { return State != null && State.phase == GamePhases.Vote && voteVersion == State.phaseVersion ? voteTarget : 0; }
        }

        /// <summary>지금 밤 능력을 쓸 수 있는지. None이 아니면 ReportFormatter.AbilityBlockMessage로 이유를 보여준다.</summary>
        public AbilityBlock NightAbility
        {
            get
            {
                if (State == null || Me == null)
                {
                    return AbilityBlock.NotNight;
                }
                return AbilityRules.CheckNightAbility(State, Me, LockedTonight);
            }
        }

        public List<PlayerViewDto> NightTargets
        {
            get { return AbilityRules.NightTargets(State, Me); }
        }

        public bool CanVote
        {
            get { return Me != null && AbilityRules.CanVote(State, Me.playerId); }
        }

        public List<PlayerViewDto> VoteTargets
        {
            get { return AbilityRules.VoteTargets(State); }
        }

        // ================================================================ 진행

        /// <summary>매 프레임 부른다. 폴링할 때가 됐으면 상태를 요청한다.</summary>
        public void Tick()
        {
            if (closed || pollInFlight || IsFinished())
            {
                return;
            }
            double now = clock();
            if (now < nextPollAt)
            {
                return;
            }
            nextPollAt = now + pollInterval;
            pollInFlight = true;
            api.GetState(GameId, OnState);
        }

        /// <summary>그만 진행한다. 이후 도착한 응답은 무시한다.</summary>
        public void Close()
        {
            closed = true;
        }

        // ================================================================ 내 입력

        /// <summary>밤 능력 사용. 보냈으면 true. 쓸 수 없는 상태면 이유를 보여주고 보내지 않는다.</summary>
        public bool SubmitNightAction(long targetId)
        {
            if (!CanSendAction())
            {
                return false;
            }
            AbilityBlock block = NightAbility;
            if (block != AbilityBlock.None)
            {
                view.ShowError(ReportFormatter.AbilityBlockMessage(block));
                return false;
            }
            long version = State.phaseVersion;
            actionInFlight = true;
            api.SubmitNightAction(GameId, targetId, r =>
            {
                if (!OnActionResponse(r))
                {
                    return;
                }
                nightChoiceVersion = version;
                nightChoiceTarget = targetId;
                if (r.Data.contactedPirateIds.Count > 0)
                {
                    lockedVersion = version;
                }
                view.ShowActionAccepted(r.Data);
                PollNowIfChanged(version, r.Data.phaseVersion);
            });
            return true;
        }

        /// <summary>이번 밤 능력을 넘긴다. 보냈으면 true.</summary>
        public bool SkipNightAction()
        {
            if (!CanSendAction())
            {
                return false;
            }
            AbilityBlock block = NightAbility;
            if (block != AbilityBlock.None)
            {
                view.ShowError(ReportFormatter.AbilityBlockMessage(block));
                return false;
            }
            long version = State.phaseVersion;
            actionInFlight = true;
            api.SkipNightAction(GameId, r =>
            {
                if (!OnActionResponse(r))
                {
                    return;
                }
                nightChoiceVersion = version;
                nightChoiceTarget = 0;
                view.ShowActionAccepted(r.Data);
                PollNowIfChanged(version, r.Data.phaseVersion);
            });
            return true;
        }

        /// <summary>투표. 보냈으면 true. 서버 규칙상 자기 자신에게도 투표할 수 있다.</summary>
        public bool Vote(long targetId)
        {
            if (!CanSendAction())
            {
                return false;
            }
            if (!CanVote)
            {
                view.ShowError(CannotVoteMessage);
                return false;
            }
            long version = State.phaseVersion;
            actionInFlight = true;
            api.Vote(GameId, targetId, r =>
            {
                if (!OnActionResponse(r))
                {
                    return;
                }
                voteVersion = version;
                voteTarget = targetId;
                view.ShowVoteAccepted(r.Data);
                PollNowIfChanged(version, r.Data.phaseVersion);
            });
            return true;
        }

        // ================================================================ 응답 처리

        private void OnState(GameApiResult<GameStateDto> r)
        {
            pollInFlight = false;
            if (closed)
            {
                return;
            }
            if (!r.Success)
            {
                if (!CloseIfGone(r))
                {
                    SetConnected(false); // 폴링 실패는 다음 Tick에 다시 시도한다
                }
                return;
            }
            SetConnected(true);

            GameStateDto state = r.Data;
            List<GameEvent> events = tracker.Update(state);
            if (tracker.Current != state)
            {
                return; // 늦게 도착한 옛 응답
            }
            phaseClock.Sync(state, clock());
            view.ShowState(state);
            foreach (GameEvent e in events)
            {
                if (e.Type == GameEventType.PhaseChanged)
                {
                    meNeeded = true;
                    view.ShowPhase(e);
                }
                else if (e.Type == GameEventType.PlayerDied)
                {
                    view.ShowPlayerDied(e);
                }
            }
            CatchUp(state);
        }

        /// <summary>지금 상태에서 아직 받지 못한 것을 요청한다. 실패하면 다음 폴링 때 다시 시도한다.</summary>
        private void CatchUp(GameStateDto state)
        {
            if (meNeeded && !meInFlight)
            {
                FetchMe();
            }
            if (IsAfterNight(state.phase) && nightShownDay < state.day && !nightInFlight)
            {
                FetchNightResult(state.day);
            }
            int executionDay = ExpectedExecutionDay(state);
            if (executionDay > executionShownDay && !executionInFlight)
            {
                FetchExecutionResult(executionDay, state.phase == GamePhases.Ended);
            }
            if (state.phase == GamePhases.Ended && Result == null && !resultInFlight)
            {
                FetchResult();
            }
        }

        private void FetchMe()
        {
            meNeeded = false;
            meInFlight = true;
            api.GetMe(GameId, r =>
            {
                meInFlight = false;
                if (!OnFetchResponse(r))
                {
                    meNeeded = true;
                    return;
                }
                Me = r.Data;
                view.ShowMyRole(r.Data);
            });
        }

        private void FetchNightResult(int expectedDay)
        {
            nightInFlight = true;
            api.GetNightResult(GameId, r =>
            {
                nightInFlight = false;
                if (!OnFetchResponse(r) || r.Data.day < expectedDay)
                {
                    return; // 아직 그날 결과가 아니면 다음 폴링에서 다시
                }
                nightShownDay = r.Data.day;
                LastNightResult = r.Data;
                view.ShowNightResult(r.Data);
            });
        }

        /// <param name="ended">ENDED에서 부름. 밤에 승패가 났으면 그날 처형 결과가 없으므로, 없다고 확인되면 더 묻지 않는다.</param>
        private void FetchExecutionResult(int expectedDay, bool ended)
        {
            executionInFlight = true;
            api.GetExecutionResult(GameId, r =>
            {
                executionInFlight = false;
                if (closed)
                {
                    return;
                }
                if (r.Success && r.Data.day >= expectedDay)
                {
                    SetConnected(true);
                    executionShownDay = r.Data.day;
                    LastExecutionResult = r.Data;
                    view.ShowExecutionResult(r.Data);
                    return;
                }
                if (ended && (r.Success || r.IsRuleViolation))
                {
                    executionShownDay = expectedDay; // 그날은 투표 전에 끝났다
                    return;
                }
                if (!r.Success && !CloseIfGone(r) && r.IsConnectionError)
                {
                    SetConnected(false);
                }
            });
        }

        private void FetchResult()
        {
            resultInFlight = true;
            api.GetResult(GameId, r =>
            {
                resultInFlight = false;
                if (!OnFetchResponse(r) || !r.Data.ended)
                {
                    return;
                }
                Result = r.Data;
                view.ShowGameResult(r.Data);
            });
        }

        /// <summary>조회 응답 공통 처리. 성공이면 true. 실패는 조용히 넘기고 다음 폴링 때 다시 받는다.</summary>
        private bool OnFetchResponse<T>(GameApiResult<T> r) where T : class
        {
            if (closed)
            {
                return false;
            }
            if (r.Success)
            {
                SetConnected(true);
                return true;
            }
            if (!CloseIfGone(r) && r.IsConnectionError)
            {
                SetConnected(false);
            }
            return false;
        }

        /// <summary>내 입력 응답 공통 처리. 성공이면 true. 실패하면 서버 메시지를 보여준다.</summary>
        private bool OnActionResponse<T>(GameApiResult<T> r) where T : class
        {
            actionInFlight = false;
            if (closed)
            {
                return false;
            }
            if (r.Success)
            {
                SetConnected(true);
                return true;
            }
            if (CloseIfGone(r))
            {
                return false;
            }
            if (r.IsConnectionError)
            {
                SetConnected(false);
            }
            view.ShowError(r.Message);
            return false;
        }

        // ================================================================ 도우미

        private bool CanSendAction()
        {
            return !closed && !actionInFlight && State != null;
        }

        private bool CloseIfGone<T>(GameApiResult<T> r) where T : class
        {
            if (!r.IsNotFound)
            {
                return false;
            }
            closed = true;
            view.OnGameClosed();
            return true;
        }

        private void SetConnected(bool value)
        {
            if (connected == value)
            {
                return;
            }
            connected = value;
            view.ShowConnection(value);
        }

        private void PollNowIfChanged(long sentVersion, long responseVersion)
        {
            if (responseVersion != sentVersion)
            {
                nextPollAt = 0; // 내 제출로 판정됐다 → 결과를 바로 받으러 간다
            }
        }

        private bool IsCurrentNight(long version)
        {
            return State != null && State.phase == GamePhases.Night && version == State.phaseVersion;
        }

        /// <summary>끝났고 보여 줄 것을 다 보여 줬으면 더 폴링하지 않는다.</summary>
        private bool IsFinished()
        {
            return IsEnded && Result != null && nightShownDay >= State.day && executionShownDay >= State.day;
        }

        private static bool IsAfterNight(string phase)
        {
            return phase == GamePhases.NightResult || phase == GamePhases.Day || phase == GamePhases.Vote
                || phase == GamePhases.Execution || phase == GamePhases.Ended;
        }

        /// <summary>지금까지 나왔어야 할 처형 결과의 일차. EXECUTION·ENDED는 그날, 다음 밤은 전날. 없으면 0.</summary>
        private static int ExpectedExecutionDay(GameStateDto state)
        {
            switch (state.phase)
            {
                case GamePhases.Execution:
                case GamePhases.Ended:
                    return state.day;
                case GamePhases.Night:
                    return state.day - 1;
                default:
                    return 0;
            }
        }
    }
}
