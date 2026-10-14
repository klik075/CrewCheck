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
    /// - 실시간 신호(WebSocket game.changed)가 오면 RequestPollNow()로 기다리지 않고 바로 가져온다.
    ///   신호를 받는 동안에는 SetPollInterval(15)로 폴링을 안전망으로 늦추고, 끊기면 원래 간격으로 되돌린다.
    /// </summary>
    public sealed class GameSession
    {
        public const string CannotVoteMessage = "지금은 투표할 수 없습니다.";
        public const string CannotStrikeMessage = "표식을 남긴 살아 있는 사람이 없어 발동할 수 없습니다.";
        public const string CannotSkipDayMessage = "지금은 토론을 넘길 수 없습니다.";

        private readonly IGameApi api;
        private readonly IGameView view;
        private readonly Func<double> clock;
        private double pollInterval;
        private readonly PhaseTracker tracker = new PhaseTracker();
        private readonly PhaseClock phaseClock = new PhaseClock();

        private bool closed;
        private bool connected = true;
        private double nextPollAt;          // 0 → 다음 Tick에 바로
        private bool pollInFlight;
        private bool pollAgain;             // 응답을 기다리는 중에 RequestPollNow → 응답이 오면 한 번 더
        private double lastPollAt = double.NaN; // 마지막으로 GET을 보낸 시각. 아직 없으면 NaN
        private bool actionInFlight;        // 밤 행동·넘기기·토론 넘기기·투표는 한 번에 하나만
        private bool meNeeded = true;
        private bool meInFlight;
        private bool nightInFlight;
        private bool executionInFlight;
        private bool resultInFlight;
        private int nightShownDay;          // 보여 준 마지막 밤 결과의 일차
        private int executionShownDay;      // 보여 준(또는 볼 것이 없다고 확인한) 마지막 처형 결과의 일차

        private long lockedVersion = -1;    // 앵무새가 접선한 밤의 phaseVersion
        private long nightChoiceVersion = -1;
        private long nightChoiceTarget;     // 0 = 넘김 (크라켄 발동도 대상이 없어 0)
        private bool nightChoiceStrike;     // 크라켄이 발동(KRAKEN_STRIKE)을 골랐는지
        private long voteVersion = -1;
        private long voteTarget;            // 0 = 표 없음 (기권)
        private bool voteConfirmed;         // "투표 완료"를 서버가 받았는지
        private long daySkipVersion = -1;   // 토론을 넘긴 낮의 phaseVersion
        private DaySkipResultDto daySkipResult;
        private bool markedByKraken;        // 크라켄의 표식이 내게 남아 있음 (KRAKEN_MARKED를 받았고 그 뒤 발동이 없었다)
        private readonly HashSet<long> krakenVictims = new HashSet<long>(); // 크라켄에게 끌려가 죽은 사람 (밤 결과의 deaths, 전체 공개)
        private RoleSetupOptionsDto roleSetupOptions; // 직업 목록·추천 구성 (서버가 켜져 있는 동안 바뀌지 않아 한 번만 받는다)
        private readonly List<Action<RoleSetupOptionsDto>> roleSetupWaiters = new List<Action<RoleSetupOptionsDto>>();

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
            pollInterval = double.IsNaN(pollIntervalSeconds) ? 0.2 : Math.Max(0.2, pollIntervalSeconds);
        }

        // ================================================================ 화면이 읽는 값

        public string GameId { get; private set; }

        /// <summary>지금 폴링 간격(초).</summary>
        public double PollIntervalSeconds
        {
            get { return pollInterval; }
        }

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

        /// <summary>승리 팀 없이 취소된 게임. 서버가 결과 조회 시간 뒤 방을 지우므로 대기실이 아니라 로비로 돌아간다.</summary>
        public bool IsCancelled
        {
            get { return State != null && EndReasons.IsCancelled(State.endReason); }
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
            get { return IsCurrentNight(nightChoiceVersion) && nightChoiceTarget == 0 && !nightChoiceStrike; }
        }

        /// <summary>크라켄이 이번 밤 발동(표식된 사람을 모두 처치)을 골랐는지.</summary>
        public bool StruckTonight
        {
            get { return IsCurrentNight(nightChoiceVersion) && nightChoiceStrike; }
        }

        /// <summary>크라켄이 표식을 남긴 생존자. 크라켄이 아니면 빈 목록.</summary>
        public List<PlayerViewDto> KrakenMarks
        {
            get { return AbilityRules.KrakenMarks(State, Me); }
        }

        /// <summary>
        /// 크라켄의 표식이 내게 남아 있는지 (표식 당한 사람이 자기 명단 칸에만 보는 배지). 서버 /me에는 없어서 밤 결과로 기억한다:
        /// KRAKEN_MARKED를 받으면 켜고, 누군가 크라켄에게 끌려간 밤(발동하면 서버가 표식을 모두 지운다)이나 내가 죽으면 끈다.
        /// 재접속하면 처음부터 다시 기억하므로 사라질 수 있다. 발동을 선의 보호로 막아 아무도 끌려가지 않은 밤은 알 수 없어 남는다.
        /// </summary>
        public bool MarkedByKraken
        {
            get { return markedByKraken && Me != null && State != null && GameStateQueries.IsAlive(State, Me.playerId); }
        }

        /// <summary>이 사람이 크라켄에게 끌려가 죽었는지 (밤 결과의 사망 원인, 모두에게 공개된 정보).</summary>
        public bool WasDraggedByKraken(long playerId)
        {
            return krakenVictims.Contains(playerId);
        }

        /// <summary>크라켄이 지금 발동할 수 있는지 (능력을 쓸 수 있고 표식을 남긴 생존자가 있다).</summary>
        public bool CanStrike
        {
            get { return State != null && Me != null && AbilityRules.CanStrike(State, Me, NightAbility); }
        }

        /// <summary>이번 낮 토론을 넘겼는지. 다음 낮(phaseVersion이 바뀜)이 되면 저절로 false가 된다.</summary>
        public bool SkippedToday
        {
            get { return State != null && State.phase == GamePhases.Day && daySkipVersion == State.phaseVersion; }
        }

        /// <summary>이번 낮에 넘겼을 때 서버가 알려 준 인원. 넘기지 않았으면 null.</summary>
        public DaySkipResultDto DaySkipProgress
        {
            get { return SkippedToday ? daySkipResult : null; }
        }

        /// <summary>지금 낮 토론을 넘길 수 있는지 (낮, 내가 살아 있음, 아직 넘기지 않음).</summary>
        public bool CanSkipDay
        {
            get
            {
                return Me != null && State != null && State.phase == GamePhases.Day
                    && GameStateQueries.IsAlive(State, Me.playerId) && !SkippedToday;
            }
        }

        /// <summary>이번 투표에서 내가 고른 대상. 아직 투표하지 않았거나 표를 거뒀으면(기권) 0.</summary>
        public long MyVoteTarget
        {
            get { return IsCurrentVote() ? voteTarget : 0; }
        }

        /// <summary>이번 투표에서 "투표 완료"를 서버가 받았는지. (MyVoteTarget이 0이면 기권으로 완료)</summary>
        public bool MyVoteConfirmed
        {
            get { return IsCurrentVote() && voteConfirmed; }
        }

        /// <summary>밤 행동·넘기기·투표 요청의 응답을 기다리는 중인지. (한 번에 하나만 보낸다)</summary>
        public bool IsActionInFlight
        {
            get { return actionInFlight; }
        }

        private bool IsCurrentVote()
        {
            return State != null && State.phase == GamePhases.Vote && voteVersion == State.phaseVersion;
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

        /// <summary>지금 투표할 수 있는지: VOTE 페이즈의 생존자이고, 요리사에게 투표를 금지당하지 않았다.</summary>
        public bool CanVote
        {
            get { return Me != null && AbilityRules.CanVote(State, Me.playerId) && !VoteBannedToday; }
        }

        /// <summary>요리사 때문에 오늘 투표할 수 없는지 (오늘 아침 밤 결과에 VOTE_BANNED가 왔다).</summary>
        public bool VoteBannedToday
        {
            get { return AbilityRules.IsVoteBanned(State, LastNightResult); }
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
            lastPollAt = now;
            pollInFlight = true;
            api.GetState(GameId, OnState);
        }

        /// <summary>
        /// 다음 Tick에 간격을 기다리지 않고 상태를 가져온다. (서버가 "게임이 바뀌었다"고 알려 줬을 때)
        /// 이미 응답을 기다리는 중이면 그 응답은 신호보다 먼저 출발했을 수 있으므로, 응답이 온 뒤 한 번 더 가져온다.
        /// 여러 번 불러도 추가 요청은 한 번뿐이다. 닫혔거나 끝난 게임이면 아무 일도 없다.
        /// </summary>
        public void RequestPollNow()
        {
            if (closed || IsFinished())
            {
                return;
            }
            if (pollInFlight)
            {
                pollAgain = true;
                return;
            }
            nextPollAt = 0;
        }

        /// <summary>
        /// 폴링 간격을 바꾼다(최소 0.2초). 신호를 받는 동안 15초(안전망), 끊기면 원래 간격(1초)으로 되돌릴 때 쓴다.
        /// 다음 폴링 시각은 "마지막 폴링 + 새 간격"으로 다시 잡는다. → 15초에서 1초로 줄이면 늦어도 1초 안에 가져온다.
        /// </summary>
        public void SetPollInterval(double seconds)
        {
            double next = double.IsNaN(seconds) ? 0.2 : Math.Max(0.2, seconds);
            if (next == pollInterval)
            {
                return;
            }
            pollInterval = next;
            if (!pollInFlight && !double.IsNaN(lastPollAt) && nextPollAt > 0)
            {
                nextPollAt = lastPollAt + pollInterval;
            }
        }

        /// <summary>그만 진행한다. 이후 도착한 응답은 무시한다.</summary>
        public void Close()
        {
            closed = true;
        }

        /// <summary>
        /// 직업 목록과 인원수별 추천 구성 (지도 버튼의 직업 정보 화면). 처음 한 번만 받고 그다음은 바로 돌려준다.
        /// 실패하면 null로 부르고, 다음 호출 때 다시 받는다. 닫힌 세션이면 부르지 않는다.
        /// </summary>
        public void LoadRoleSetupOptions(Action<RoleSetupOptionsDto> onLoaded)
        {
            if (onLoaded == null || closed)
            {
                return;
            }
            if (roleSetupOptions != null)
            {
                onLoaded(roleSetupOptions);
                return;
            }
            roleSetupWaiters.Add(onLoaded);
            if (roleSetupWaiters.Count > 1)
            {
                return; // 이미 받는 중
            }
            api.GetRoleSetupOptions(r =>
            {
                if (closed)
                {
                    roleSetupWaiters.Clear();
                    return;
                }
                if (r.Success)
                {
                    roleSetupOptions = r.Data;
                }
                var waiters = new List<Action<RoleSetupOptionsDto>>(roleSetupWaiters);
                roleSetupWaiters.Clear();
                foreach (Action<RoleSetupOptionsDto> waiter in waiters)
                {
                    waiter(roleSetupOptions);
                }
            });
        }

        // ================================================================ 내 입력

        /// <summary>밤 능력 사용 (직업의 기본 능력). 보냈으면 true. 쓸 수 없는 상태면 이유를 보여주고 보내지 않는다.</summary>
        public bool SubmitNightAction(long targetId)
        {
            return SendNightAction(targetId, null);
        }

        /// <summary>크라켄 발동: 표식을 남긴 생존자를 모두 처치한다(대상 없음). 보냈으면 true. 판정 전에는 표식으로 다시 바꿀 수 있다.</summary>
        public bool StrikeKraken()
        {
            if (CanSendAction() && NightAbility == AbilityBlock.None && !CanStrike)
            {
                view.ShowError(CannotStrikeMessage); // 능력을 못 쓰는 이유는 SendNightAction이 알린다
                return false;
            }
            return SendNightAction(0, ActionCodes.KrakenStrike);
        }

        /// <param name="actionCode">null이면 직업의 기본 능력. 크라켄 발동만 KRAKEN_STRIKE(targetId는 0)</param>
        private bool SendNightAction(long targetId, string actionCode)
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
            api.SubmitNightAction(GameId, targetId, actionCode, r =>
            {
                if (!OnActionResponse(r))
                {
                    return;
                }
                nightChoiceVersion = version;
                nightChoiceTarget = targetId;
                nightChoiceStrike = actionCode == ActionCodes.KrakenStrike;
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
                nightChoiceStrike = false;
                view.ShowActionAccepted(r.Data);
                PollNowIfChanged(version, r.Data.phaseVersion);
            });
            return true;
        }

        /// <summary>이번 낮 토론을 넘긴다. 보냈으면 true. 살아 있는 전원이 넘기면 서버가 바로 투표로 넘긴다.</summary>
        public bool SkipDay()
        {
            if (!CanSendAction())
            {
                return false;
            }
            if (!CanSkipDay)
            {
                view.ShowError(CannotSkipDayMessage);
                return false;
            }
            long version = State.phaseVersion;
            actionInFlight = true;
            api.SkipDay(GameId, r =>
            {
                if (!OnActionResponse(r))
                {
                    return;
                }
                daySkipVersion = version;
                daySkipResult = r.Data;
                view.ShowDaySkipAccepted(r.Data);
                PollNowIfChanged(version, r.Data.phaseVersion);
            });
            return true;
        }

        /// <summary>투표 + 투표 완료 (한 번에 확정). 보냈으면 true.</summary>
        public bool Vote(long targetId)
        {
            return Vote(targetId, true);
        }

        /// <summary>
        /// 투표. 보냈으면 true. 서버 규칙상 자기 자신에게도 투표할 수 있다.
        /// targetId 0 = 표를 거둔다(기권). confirm = "투표 완료"(지금 상태로 고정), 아니면 임시 선택(시간이 끝나면 집계된다).
        /// </summary>
        public bool Vote(long targetId, bool confirm)
        {
            if (!CanSendAction())
            {
                return false;
            }
            if (!CanVote)
            {
                view.ShowError(VoteBannedToday ? ReportFormatter.VoteBannedMessage : CannotVoteMessage);
                return false;
            }
            long version = State.phaseVersion;
            actionInFlight = true;
            api.Vote(GameId, targetId, confirm, r =>
            {
                if (!OnActionResponse(r))
                {
                    return;
                }
                if (voteVersion != version)
                {
                    voteConfirmed = false; // 새 투표
                }
                voteVersion = version;
                voteTarget = targetId;
                voteConfirmed = voteConfirmed || confirm;
                view.ShowVoteAccepted(r.Data);
                PollNowIfChanged(version, r.Data.phaseVersion);
            });
            return true;
        }

        // ================================================================ 응답 처리

        private void OnState(GameApiResult<GameStateDto> r)
        {
            pollInFlight = false;
            if (pollAgain)
            {
                pollAgain = false;
                nextPollAt = 0; // 기다리는 동안 신호가 왔다 → 다음 Tick에 바로 한 번 더
            }
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
            List<string> lostChoices = ForgetChoicesOnDeaths(events); // 패널을 다시 그리기 전에 지운다
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
            foreach (string message in lostChoices)
            {
                view.ShowError(message);
            }
            CatchUp(state);
        }

        /// <summary>
        /// 이번 페이즈에 내가 고른 사람이 페이즈 도중에 죽었으면(연결이 끊겨 게임에서 나감) 그 선택을 지운다.
        /// 서버도 그 표·밤 행동을 지우므로(접선으로 고정된 앵무새도 풀림) 다시 고르라고 알린다.
        /// 밤 습격·처형으로 죽는 것은 페이즈가 바뀔 때라 여기에 걸리지 않는다.
        /// 시체 대상 능력(주정뱅이)은 이미 죽은 사람을 고르므로 그 사람이 다시 죽는 일이 없다.
        /// </summary>
        private List<string> ForgetChoicesOnDeaths(List<GameEvent> events)
        {
            var messages = new List<string>();
            foreach (GameEvent e in events)
            {
                if (e.Type != GameEventType.PlayerDied)
                {
                    continue;
                }
                if (MyVoteTarget == e.PlayerId)
                {
                    voteVersion = -1;
                    messages.Add(GameScreenText.VoteTargetGone(e.Nickname));
                }
                if (MyNightTarget == e.PlayerId)
                {
                    nightChoiceVersion = -1;
                    lockedVersion = -1;
                    messages.Add(GameScreenText.NightTargetGone(e.Nickname));
                }
            }
            return messages;
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
                RememberKraken(r.Data);
                view.ShowNightResult(r.Data);
            });
        }

        /// <summary>밤 결과로 크라켄 표식(내 것)과 크라켄 희생자를 기억한다. 발동이 표식을 지우므로 지우기를 먼저, 새 표식을 나중에 본다.</summary>
        private void RememberKraken(NightResultDto night)
        {
            bool struck = false;
            foreach (DeathDto death in night.Deaths)
            {
                if (death.cause != DeathCauses.Kraken) continue;
                struck = true;
                krakenVictims.Add(death.playerId);
            }
            if (struck)
            {
                markedByKraken = false;
            }
            foreach (ReportDto report in night.reports)
            {
                if (report != null && report.type == ReportTypes.KrakenMarked)
                {
                    markedByKraken = true;
                }
            }
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
