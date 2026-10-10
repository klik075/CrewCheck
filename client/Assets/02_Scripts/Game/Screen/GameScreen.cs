using System.Collections.Generic;
using TMPro;
using UnityEngine;
using WhoisntCitizen.Chat; // [공개 안내 자동 판단] 서버 채팅(GameChatController) 상태로 공개 안내 여부를 정한다
using WhoisntCitizen.GameUI;
using WhoisntCitizen.Vote;
using WhoisntCitizen.Lobby;
using UnityEngine.UI;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 게임 화면의 단일 진입점(IGameView). GameSession이 알려 주는 것을 각 패널에 나눠 준다.
    /// - feat/vote(klik075)의 부품: ChatLogView(시스템 메시지), BottomTabController(하단 시트), VotePanelController(투표)
    /// - G의 부품: NightActionPanel(밤 능력), DaySkipPanel(낮 토론 넘기기), RoleCardView(직업 카드), GameResultPanel(결과 + "대기실로")
    /// 패널끼리는 서로 모르고, 입력(투표·밤 능력·대기실로)은 모두 여기서 GameController·WaitingRoomController로 넘긴다.
    /// GameController의 gameView 칸에 이 컴포넌트를 넣는다.
    /// 하단 시트: 밤에는 [+] 서랍에 NightActionPanel을, 낮에는 DaySkipPanel을, 그 밖에는 VotePanel을 보여 준다(모두 Drawer 안에 나란히 둔다).
    /// 투표: VOTE 페이즈가 되면 부채꼴 카드패(VoteCardHandController)가 아래에서 올라온다. 카드를 뽑으면 임시 투표(confirm=false),
    ///       뽑은 카드를 다시 눌러 되돌리면 표를 거둔다(기권, targetId 0). 오른쪽 하단 [투표 완료]를 누르면 지금 상태로 고정(confirm=true)하고
    ///       카드패가 내려간다. 아무 카드도 뽑지 않은 채 시간이 끝나면 서버가 기권으로 처리한다(별도 기권 버튼 없음).
    ///       기존 VotePanel 투표 처리는 [카드패 투표로 교체 - 주석 처리] 표시로 막아 두었다(패널은 보기 전용으로 남는다).
    // [공개 안내 수동 설정 - 주석 처리]
    // /// 채팅 기록: 공개 안내는 실제 서버면 서버 채팅이 보내므로 AnnouncePublic일 때만 남긴다.
    /// 채팅 기록: 공개 안내(페이즈·밤 결과·처형·승리)는 서버 채팅(GameChatController)이 같은 채팅창에 보여 주면 남기지 않고,
    /// 서버 채팅이 없거나 꺼져 있거나 멈췄을 때, 또는 가짜 서버 게임일 때만 직접 남긴다(AnnouncePublic).
    /// 나만 아는 것(내 직업, 개인 밤 결과, 접선, 접수 확인, 오류, 내 사망)과 득표 수는 항상 남긴다.
    /// </summary>
    public sealed class GameScreen : MonoBehaviour, IGameView
    {
        [Header("진행 (비우면 씬에서 찾는다)")]
        [SerializeField] private GameController controller;
        [SerializeField] private WaitingRoomController waitingRoom;

        [Header("게임 화면 루트 (대기실에서는 꺼진다. 비우면 끄지 않는다)")]
        [SerializeField] private GameObject gameRoot;

        [Header("상단")]
        [SerializeField] private TextMeshProUGUI phaseText;
        [SerializeField] private TextMeshProUGUI timerText;
        [SerializeField] private GameObject disconnectedBanner;

        [Header("feat/vote 부품")]
        [SerializeField] private ChatLogView chatLog;
        [SerializeField] private BottomTabController bottomTab;
        [SerializeField] private VotePanelController votePanel;

        [Header("투표 카드패 (비우면 실행 시 이 캔버스 아래에 만든다)")]
        [SerializeField] private VoteCardHandController voteCardHand;

        [Header("G 부품")]
        [SerializeField] private NightActionPanel nightPanel;
        [SerializeField] private DaySkipPanel dayPanel;
        [SerializeField] private RoleCardView roleCard;
        [SerializeField] private GameResultPanel resultPanel;

        [Header("초상화 (플레이어 순서대로 돌려 쓴다. 비우면 없음)")]
        [SerializeField] private Sprite[] portraits;

        // [공개 안내 수동 설정 - 주석 처리]
        // [Header("공개 안내")]
        // [Tooltip("실제 서버는 페이즈·밤 결과·처형·승리 안내를 방 채팅 시스템 메시지로 보낸다(서버 PR #18). " +
        //          "그 채팅이 이 ChatLogView에 나오면 꺼 두어 같은 안내가 두 번 나오지 않게 한다. " +
        //          "가짜 서버는 채팅이 없으므로 이 값과 상관없이 직접 남긴다.")]
        // [SerializeField] private bool announcePublicWithRealServer = false;

        [Header("공개 안내 (비우면 씬에서 찾는다)")]
        [Tooltip("실제 서버는 페이즈·밤 결과·처형·승리 안내를 방 채팅 시스템 메시지로 보낸다. " +
                 "이 서버 채팅이 같은 ChatLogView에 안내를 보여 주는 동안에는 이 화면이 공개 안내를 남기지 않는다(중복 방지). " +
                 "서버 채팅이 없거나 꺼져 있거나 연결에 실패해 멈췄으면, 그리고 가짜 서버 게임이면 직접 남긴다.")]
        [SerializeField] private GameChatController serverChat;

        private readonly List<PlayerView> playerViews = new List<PlayerView>();
        private readonly Dictionary<long, int> playerOrder = new Dictionary<long, int>();
        private string currentPhase;
        private DayRoundTableView dayTable;
        private DayTableVoteCards voteCards;
        private string announcedRole;
        private string announcedTeammates;
        private bool nightDrawerOpened;
        private bool dayDrawerOpened;
        private int shownSeconds = -1;
        private bool shownWaiting;
        private bool deadVoteNoticeShown;
        private readonly List<PlayerView> voteTargets = new List<PlayerView>();

        // 투표 전송 대기: 앞 요청(밤 행동·투표)이 처리 중이면 보낼 수 없으므로, 마지막으로 원한 상태만 남겨 두었다가 보낸다
        private bool hasPendingVote;
        private long pendingVoteTarget;   // 0 = 기권
        private bool pendingVoteConfirm;
        private bool voteConfirmAnnounced; // 이번 투표의 "투표 완료"를 채팅에 알렸는지

        private GameSession Session
        {
            get { return controller != null ? controller.Session : null; }
        }

        private long MyId
        {
            get { return Session != null && Session.Me != null ? Session.Me.playerId : 0; }
        }

        // [공개 안내 수동 설정 - 주석 처리]
        // /// <summary>공개 안내(페이즈·밤 결과 요약·처형 결과·승리)를 이 화면이 직접 남길지. 실제 서버면 서버 채팅이 보낸다.</summary>
        // private bool AnnouncePublic
        // {
        //     get { return controller != null && (controller.IsFakeGame || announcePublicWithRealServer); }
        // }

        /// <summary>
        /// 공개 안내(페이즈·밤 결과 요약·처형 결과·승리)를 이 화면이 직접 남길지.
        /// 서버 안내가 이 채팅창에 넘어오면 끔(false), 넘어오지 않으면 켬(true). 가짜 서버 게임은 서버 안내가 없으므로 항상 켬.
        /// </summary>
        private bool AnnouncePublic
        {
            get { return controller != null && (controller.IsFakeGame || !ServerAnnouncesHere); }
        }

        /// <summary>서버 채팅이 서버 시스템 메시지를 이 화면과 같은 채팅창에 보여 주는 중인지.</summary>
        private bool ServerAnnouncesHere
        {
            get { return serverChat != null && serverChat.ShowsServerMessages && serverChat.chatLog == chatLog; }
        }

        // ================================================================ Unity

        private void Awake()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<GameController>();
            }
            if (waitingRoom == null)
            {
                waitingRoom = FindFirstObjectByType<WaitingRoomController>();
            }
            // [공개 안내 자동 판단] 서버 채팅을 씬에서 찾는다 (꺼져 있는 오브젝트 포함)
            if (serverChat == null)
            {
                serverChat = FindFirstObjectByType<GameChatController>(FindObjectsInactive.Include);
            }
            if (votePanel != null)
            {
                votePanel.PortraitResolver = PortraitOf;
                // [카드패 투표로 교체 - 주석 처리] 기존 투표 패널의 확정 → 서버 투표
                // votePanel.VoteConfirmed += OnVoteConfirmed;
            }
            SetUpVoteCardHand();
            dayTable = gameObject.AddComponent<DayRoundTableView>();
            Transform background = transform.Find("Background");
            dayTable.Initialize(background != null ? background.GetComponent<Image>() : null,
                chatLog != null ? chatLog.transform as RectTransform : null,
                phaseText != null ? phaseText.font : null, null, RoomSession.HasRoom ? RoomSession.MaxPlayers : 12);
            dayTable.SetPhase(currentPhase);
            // 투표 카드: 내 표는 테이블 위 대상 자리로 던지고, 처형 때 공개 득표를 각 자리에 더미로 놓는다
            voteCards = gameObject.AddComponent<DayTableVoteCards>();
            voteCards.Bind(dayTable, phaseText != null ? phaseText.font : null);
            if (nightPanel != null)
            {
                nightPanel.PortraitResolver = PortraitOf;
                nightPanel.Confirmed += OnNightConfirmed;
                nightPanel.Skipped += OnNightSkipped;
            }
            if (dayPanel != null)
            {
                dayPanel.PortraitResolver = PortraitOf;
                dayPanel.Skipped += OnDaySkipped;
            }
            if (resultPanel != null)
            {
                resultPanel.BackRequested += OnBackRequested;
                resultPanel.Hide();
            }
            if (bottomTab != null)
            {
                bottomTab.ModeChanged += OnDrawerModeChanged;
            }
            if (disconnectedBanner != null)
            {
                disconnectedBanner.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            // [카드패 투표로 교체 - 주석 처리]
            // if (votePanel != null)
            // {
            //     votePanel.VoteConfirmed -= OnVoteConfirmed;
            // }
            if (voteCardHand != null)
            {
                voteCardHand.VoteRequested -= OnCardVoteRequested;
                voteCardHand.VoteConfirmed -= OnCardVoteConfirmed;
            }
            if (nightPanel != null)
            {
                nightPanel.Confirmed -= OnNightConfirmed;
                nightPanel.Skipped -= OnNightSkipped;
            }
            if (dayPanel != null)
            {
                dayPanel.Skipped -= OnDaySkipped;
            }
            if (resultPanel != null)
            {
                resultPanel.BackRequested -= OnBackRequested;
            }
            if (bottomTab != null)
            {
                bottomTab.ModeChanged -= OnDrawerModeChanged;
            }
        }

        private void Update()
        {
            if (hasPendingVote)
            {
                TrySendPendingVote(); // 앞 요청이 끝나면 보낸다
            }
            GameSession session = Session;
            if (gameRoot != null && gameRoot.activeSelf != (session != null))
            {
                gameRoot.SetActive(session != null);
            }
            if (timerText == null)
            {
                return;
            }
            // 매 프레임 문자열을 만들지 않도록 값이 바뀔 때만 다시 쓴다
            bool waiting = session != null && !session.IsEnded && session.IsWaitingForServer;
            int seconds = session != null && !session.IsEnded ? session.RemainingWholeSeconds : -1;
            if (seconds != shownSeconds || waiting != shownWaiting)
            {
                shownSeconds = seconds;
                shownWaiting = waiting;
                timerText.text = seconds < 0 ? string.Empty : (waiting ? GameScreenText.WaitingForServer : GameScreenText.Timer(seconds));
            }
        }

        // ================================================================ IGameView

        public void ShowState(GameStateDto state)
        {
            RefreshPlayers(state);
        }

        public void ShowPhase(GameEvent phaseChanged)
        {
            if (phaseChanged.PreviousPhase == null)
            {
                ResetScreen(); // 새 게임 (또는 재접속)
            }
            if (currentPhase == GamePhases.Vote && phaseChanged.Phase != GamePhases.Vote)
            {
                AnnounceVoteTimeout(); // [투표 완료] 없이 시간이 끝났으면 결과(기권 등)를 알린다
            }
            ClearPendingVote(); // 페이즈가 바뀌면(새 투표 또는 투표 끝) 남은 전송은 버린다
            voteConfirmAnnounced = false;
            currentPhase = phaseChanged.Phase;
            if (dayTable != null) dayTable.SetPhase(currentPhase);
            if (voteCards != null && currentPhase != GamePhases.Execution)
            {
                voteCards.Clear(); // 처형 결과 동안만 남기고, 새 투표·밤에는 테이블을 비운다
            }
            if (phaseText != null)
            {
                phaseText.text = GameScreenText.PhaseTitle(phaseChanged.Day, phaseChanged.Phase);
            }
            if (AnnouncePublic)
            {
                AddSystem(GameScreenText.PhaseAnnouncement(phaseChanged));
            }

            switch (phaseChanged.Phase)
            {
                case GamePhases.Night:
                    if (nightPanel != null)
                    {
                        nightPanel.ResetSelection();
                    }
                    nightDrawerOpened = false;
                    TryOpenNightDrawer();
                    break;
                case GamePhases.Day:
                    dayDrawerOpened = false;
                    if (Session == null || !Session.CanSkipDay)
                    {
                        CloseDrawer(); // 사망자 등 넘길 수 없으면 예전처럼 서랍을 내린다
                    }
                    TryOpenDayDrawer();
                    break;
                case GamePhases.Vote:
                    // [카드패 투표로 교체 - 주석 처리] 기존: 투표 패널 초기화 후 하단 서랍을 연다
                    // if (votePanel != null)
                    // {
                    //     votePanel.ResetVote();
                    // }
                    // OpenDrawer();
                    CloseDrawer(); // 열려 있던 [+] 서랍은 내리고 카드패를 올린다
                    TryShowVoteHand();
                    break;
                default:
                    CloseDrawer();
                    break;
            }
            if (phaseChanged.Phase != GamePhases.Vote)
            {
                HideVoteHand(); // 투표 여부와 관계없이 투표 시간이 끝나면 카드패를 내린다
            }
            ApplyDrawerContent(bottomTab != null ? bottomTab.Mode : BottomTabMode.Closed);
            RefreshPanels();
        }

        public void ShowPlayerDied(GameEvent died)
        {
            if (died.PlayerId == MyId)
            {
                AddSystem(GameScreenText.YouDied); // 누가 죽었는지는 밤·처형 결과 문구가 알린다
            }
        }

        public void ShowMyRole(MyRoleDto me)
        {
            if (announcedRole != me.role)
            {
                announcedRole = me.role;
                AddSystem(GameScreenText.RoleAnnouncement(me));
            }
            GameStateDto state = Session != null ? Session.State : null;
            string teammates = ReportFormatter.TeammatesLine(state, me);
            if (!string.IsNullOrEmpty(teammates) && teammates != announcedTeammates)
            {
                announcedTeammates = teammates;
                AddSystem(teammates); // 접선으로 동료가 늘면 다시 알린다
            }
            if (roleCard != null)
            {
                roleCard.Show(me, state);
            }
            RefreshPlayers(state);
            TryOpenNightDrawer();
            TryOpenDayDrawer();
        }

        public void ShowNightResult(NightResultDto result)
        {
            if (AnnouncePublic)
            {
                AddSystem(ReportFormatter.NightSummary(result));
            }
            foreach (string line in ReportFormatter.NightReports(result))
            {
                AddSystem(line); // 개인 결과 (나에게만 온다. 서버 채팅에는 없다)
            }
        }

        public void ShowExecutionResult(ExecutionResultDto result)
        {
            if (AnnouncePublic)
            {
                AddSystem(ReportFormatter.ExecutionSummary(result));
            }
            AddSystem(GameScreenText.VoteSummary(result)); // 득표 수는 서버 채팅 안내에 없어서 항상 남긴다
            if (votePanel != null)
            {
                votePanel.SetVoteCounts(GameScreenText.VoteCounts(result));
            }
            if (voteCards != null && currentPhase == GamePhases.Execution)
            {
                voteCards.ShowTally(result.votes); // 공개된 득표 수만큼 각 대상 자리에 카드 더미 (누가 냈는지는 없다)
            }
        }

        public void ShowGameResult(GameResultDto result)
        {
            if (AnnouncePublic)
            {
                AddSystem(ReportFormatter.ResultHeadline(result));
                AddSystem(ReportFormatter.CancelReason(result.endReason)); // 취소가 아니면 빈 문자열
            }
            CloseDrawer();
            HideVoteHand();
            if (resultPanel != null)
            {
                resultPanel.Show(result, Session != null ? Session.Me : null);
            }
        }

        public void ShowActionAccepted(NightActionResultDto result)
        {
            GameSession session = Session;
            if (session == null || session.Me == null)
            {
                return;
            }
            AddSystem(GameScreenText.ActionAccepted(result, session.Me.actionCode, session.MyNightTarget, session.SkippedTonight, session.State));
            RefreshPanels();
        }

        public void ShowDaySkipAccepted(DaySkipResultDto result)
        {
            // 실제 서버는 방 채팅에 "OO님이 토론을 넘겼습니다. (3/5)"를 보내므로, 서버 안내가 이 채팅창에 없을 때만 남긴다
            if (AnnouncePublic)
            {
                AddSystem(GameScreenText.DaySkipAccepted(result));
            }
            RefreshPanels(); // 넘긴 인원은 패널 안내에 항상 보인다
        }

        public void ShowVoteAccepted(VoteResultDto result)
        {
            GameSession session = Session;
            // [넘기기(기권) 기능 - 주석 처리] 기존: 카드를 뽑을 때마다(바로 확정) "OO님에게 투표했습니다"
            // if (session != null && session.MyVoteTarget != 0)
            // {
            //     AddSystem(GameScreenText.VoteAccepted(session.MyVoteTarget, session.State));
            // }
            // 카드 선택은 임시 선택이라 채팅에 남기지 않는다(카드패 안내 문구로 보인다). [투표 완료]를 서버가 받으면 한 번 알린다
            if (session != null && session.MyVoteConfirmed && !voteConfirmAnnounced)
            {
                voteConfirmAnnounced = true;
                AddSystem(GameScreenText.VoteConfirmed(session.MyVoteTarget, session.State));
            }
            if (!hasPendingVote)
            {
                SyncDrawnCardWithServer(); // 빠르게 바꿔 누른 경우 등, 화면의 뽑은 카드를 서버가 받은 표에 맞춘다 (더 새 선택이 대기 중이면 그대로 둔다)
            }
            SyncTableVote(); // [투표 완료]를 서버가 받으면 내 앞에 카드를 내려놓고 대상 자리로 던진다
        }

        public void ShowError(string message)
        {
            if (chatLog != null && !string.IsNullOrEmpty(message))
            {
                chatLog.AddLine("<color=#FF6B6B>[알림] " + GameScreenText.NoRichText(message) + "</color>");
                chatLog.ScrollToLatest();
            }
            // 투표 중 오류(서버 거절·연결 실패)면 뽑은 카드를 서버가 실제로 가진 내 표로 되돌린다
            if (currentPhase == GamePhases.Vote && !hasPendingVote)
            {
                SyncDrawnCardWithServer();
                UnlockIfServerNotConfirmed(); // [투표 완료]가 거절됐으면 카드패를 다시 올린다
                SyncTableVote();
            }
        }

        public void ShowConnection(bool connected)
        {
            if (disconnectedBanner != null)
            {
                disconnectedBanner.SetActive(!connected);
            }
        }

        public void OnGameClosed()
        {
            // 취소된 게임은 서버가 방도 지우므로 로비로 간다 (WaitingRoomController)
            bool cancelled = Session != null && Session.IsCancelled;
            AddSystem(cancelled ? GameScreenText.CancelledGameClosed : GameScreenText.GameClosed);
        }

        // ================================================================ 입력

        // [카드패 투표로 교체 - 주석 처리] 기존 투표 패널의 확정 처리
        // private void OnVoteConfirmed(long targetId)
        // {
        //     if (controller != null)
        //     {
        //         controller.Vote(targetId);
        //     }
        // }

        // [넘기기(기권) 기능 - 주석 처리] 기존: 카드를 뽑으면 바로 투표 완료(confirm=true)로 보냈다
        // /// <summary>카드패에서 카드를 뽑음 → 서버로 투표. 보내지 못했으면(앞 요청 처리 중 등) 카드를 서버가 가진 표로 되돌린다.</summary>
        // private void OnCardVoteRequested(long targetId)
        // {
        //     bool sent = controller != null && controller.Vote(targetId);
        //     if (!sent)
        //     {
        //         SyncDrawnCardWithServer();
        //     }
        // }

        /// <summary>
        /// 카드패에서 카드를 뽑거나(targetId) 뽑은 카드를 되돌림(0 = 기권) → 서버에 임시 선택으로 보낸다(confirm=false).
        /// 시간이 끝나면 이 선택이 그대로 집계된다. 아무 카드도 없으면 기권.
        /// </summary>
        private void OnCardVoteRequested(long targetId)
        {
            QueueVote(targetId, false);
        }

        /// <summary>[투표 완료] → 지금 상태(뽑은 카드, 없으면 기권)로 고정해 보낸다(confirm=true). 카드패는 VoteCardHandController가 내린다.</summary>
        private void OnCardVoteConfirmed(long targetId)
        {
            QueueVote(targetId, true);
        }

        private void QueueVote(long targetId, bool confirm)
        {
            hasPendingVote = true;
            pendingVoteTarget = targetId;
            pendingVoteConfirm = confirm; // 마지막으로 원한 상태만 보낸다 (완료 뒤에는 카드패가 잠겨 임시 선택이 오지 않는다)
            TrySendPendingVote();
        }

        /// <summary>대기 중인 투표를 보낸다. 앞 요청이 처리 중이면 다음 프레임에 다시 시도한다.</summary>
        private void TrySendPendingVote()
        {
            GameSession session = Session;
            if (!hasPendingVote)
            {
                return;
            }
            if (session == null || controller == null || currentPhase != GamePhases.Vote)
            {
                ClearPendingVote();
                return;
            }
            if (session.IsActionInFlight)
            {
                return; // 응답이 오면 Update에서 다시 보낸다
            }
            long target = pendingVoteTarget;
            bool confirm = pendingVoteConfirm;
            ClearPendingVote();
            bool sent = controller.Vote(target, confirm);
            if (!sent)
            {
                // 보낼 수 없는 상태(사망·종료 등): 화면을 서버가 가진 내 표로 되돌린다
                SyncDrawnCardWithServer();
                UnlockIfServerNotConfirmed();
            }
        }

        private void ClearPendingVote()
        {
            hasPendingVote = false;
            pendingVoteTarget = 0;
            pendingVoteConfirm = false;
        }

        /// <summary>카드패는 [투표 완료]로 잠겼는데 서버는 완료를 받지 않았으면(거절·대상 이탈로 표 삭제) 다시 고를 수 있게 푼다.</summary>
        private void UnlockIfServerNotConfirmed()
        {
            GameSession session = Session;
            if (voteCardHand == null || session == null || !voteCardHand.IsLocked || hasPendingVote || session.IsActionInFlight)
            {
                return;
            }
            if (!session.MyVoteConfirmed && currentPhase == GamePhases.Vote && session.CanVote)
            {
                voteCardHand.Unlock();
                voteConfirmAnnounced = false;
            }
        }

        /// <summary>투표 시간이 [투표 완료] 없이 끝났다: 마지막 선택이 집계되고, 고른 카드가 없으면 기권이다.</summary>
        private void AnnounceVoteTimeout()
        {
            if (voteCardHand == null || !voteCardHand.IsShown || voteCardHand.IsLocked)
            {
                return; // 투표하지 않았거나(사망 등) 이미 완료했다
            }
            GameSession session = Session;
            long drawnId = voteCardHand.DrawnPlayerId;
            string nickname = drawnId != 0 && session != null ? GameStateQueries.NicknameOf(session.State, drawnId) : null;
            AddSystem(GameScreenText.VoteTimedOut(nickname));
        }

        private void OnNightConfirmed(long targetId)
        {
            if (controller != null)
            {
                controller.SubmitNightAction(targetId);
            }
        }

        private void OnNightSkipped()
        {
            if (controller != null)
            {
                controller.SkipNightAction();
            }
        }

        private void OnDaySkipped()
        {
            if (controller != null)
            {
                controller.SkipDay();
            }
        }

        private void OnBackRequested()
        {
            if (resultPanel != null)
            {
                resultPanel.Hide();
            }
            if (waitingRoom != null)
            {
                waitingRoom.ReturnToWaitingRoom(); // 가짜 서버면 새 판이 시작된다
            }
        }

        // ================================================================ 화면 갱신

        private void ResetScreen()
        {
            currentPhase = null;
            if (dayTable != null) dayTable.Clear();
            announcedRole = null;
            announcedTeammates = null;
            playerViews.Clear();
            playerOrder.Clear();
            if (chatLog != null)
            {
                chatLog.Clear();
            }
            if (votePanel != null)
            {
                votePanel.ResetVote();
            }
            if (nightPanel != null)
            {
                nightPanel.ResetSelection();
            }
            if (resultPanel != null)
            {
                resultPanel.Hide();
            }
            CloseDrawer();
            deadVoteNoticeShown = false;
            ClearPendingVote();
            voteConfirmAnnounced = false;
            if (voteCardHand != null)
            {
                voteCardHand.HideImmediate();
            }
            if (voteCards != null)
            {
                voteCards.Clear();
            }
        }

        /// <summary>투표 패널은 "나"를 처음 만들 때 정하므로, /me를 받은 뒤부터 플레이어 목록을 넘긴다.</summary>
        private void RefreshPlayers(GameStateDto state)
        {
            if (state == null) return;
            if (dayTable != null)
            {
                // 자리·스킨은 서버의 플레이어 순서로 정한다 (모든 화면에서 같은 사람이 같은 자리·같은 스킨)
                dayTable.SetPlayers(state.players);
                dayTable.SetPhase(state.phase);
            }
            GameSession session = Session;
            if (state == null || session == null || session.Me == null)
            {
                return;
            }
            playerViews.Clear();
            for (int i = 0; i < state.players.Count; i++)
            {
                PlayerViewDto p = state.players[i];
                playerViews.Add(new PlayerView(p.playerId, p.nickname, p.alive));
                if (!playerOrder.ContainsKey(p.playerId))
                {
                    playerOrder.Add(p.playerId, i);
                }
            }
            if (votePanel != null)
            {
                votePanel.SetPlayers(playerViews, session.Me.playerId);
                // [카드패 투표로 교체 - 주석 처리] 기존: 투표 페이즈에만 투표 패널 조작 허용
                // votePanel.SetVotingOpen(state.phase == GamePhases.Vote); // 사망 여부는 패널이 players로 판단한다
                votePanel.SetVotingOpen(false); // 투표는 카드패로 한다. 기존 패널은 보기 전용(처형 후 득표 수 표시)
            }
            if (state.phase == GamePhases.Vote)
            {
                if (voteCardHand != null && voteCardHand.IsShown)
                {
                    voteCardHand.SyncPlayers(BuildVoteTargets(state, session.Me.playerId));
                    // 투표 완료한 표의 대상이 나가면 서버가 그 표와 완료를 지운다 → 다시 고를 수 있게 카드패를 올린다
                    if (voteConfirmAnnounced)
                    {
                        UnlockIfServerNotConfirmed();
                    }
                }
                else
                {
                    TryShowVoteHand(); // /me가 늦게 왔거나 투표 도중 재접속한 경우
                }
                SyncTableVote(); // 내가 투표한 사람이 게임에서 나가 표가 지워졌으면 카드도 거둔다
            }
            RefreshPanels();
        }

        // ================================================================ 투표 카드패

        /// <summary>카드패가 씬에 없으면 이 캔버스 아래에 만든다. 하단 탭 바 위에 놓이도록 하단 시트를 기준으로 삼는다.</summary>
        private void SetUpVoteCardHand()
        {
            if (voteCardHand == null)
            {
                var go = new GameObject("VoteCardHand", typeof(RectTransform));
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false); // GameScreen은 GameCanvas에 붙어 있다
                voteCardHand = go.AddComponent<VoteCardHandController>();
            }
            if (phaseText != null)
            {
                voteCardHand.SetFont(phaseText.font); // 한글이 나오는 글꼴을 그대로 쓴다
            }
            if (bottomTab != null)
            {
                voteCardHand.SetBottomAnchor((RectTransform)bottomTab.transform);
            }
            voteCardHand.VoteRequested += OnCardVoteRequested;
            voteCardHand.VoteConfirmed += OnCardVoteConfirmed;
        }

        /// <summary>VOTE 페이즈이고 내가 투표할 수 있으면 카드패를 올린다. 이미 올라와 있으면 아무것도 하지 않는다.</summary>
        private void TryShowVoteHand()
        {
            GameSession session = Session;
            if (voteCardHand == null || voteCardHand.IsShown || session == null || session.State == null || session.Me == null
                || currentPhase != GamePhases.Vote)
            {
                return;
            }
            if (!session.CanVote)
            {
                if (!deadVoteNoticeShown)
                {
                    deadVoteNoticeShown = true;
                    AddSystem("사망한 플레이어는 투표할 수 없습니다.");
                }
                return;
            }
            voteCardHand.Show(BuildVoteTargets(session.State, session.Me.playerId), PortraitOf, session.MyVoteTarget, session.MyVoteConfirmed);
        }

        private void HideVoteHand()
        {
            if (voteCardHand != null)
            {
                voteCardHand.Hide();
            }
        }

        /// <summary>테이블 위 내 투표 카드를 서버가 받아 둔 내 표에 맞춘다. 같은 표면 아무것도 하지 않는다.
        /// 카드패에서 고른 카드는 임시 선택이라, [투표 완료]를 서버가 받은 뒤에만 테이블에 낸다 (기권 완료는 카드 없음).</summary>
        private void SyncTableVote()
        {
            GameSession session = Session;
            if (voteCards != null && session != null && currentPhase == GamePhases.Vote)
            {
                voteCards.ShowMyVote(MyId, session.MyVoteConfirmed ? session.MyVoteTarget : 0);
            }
        }

        /// <summary>뽑은 카드를 서버가 받아 둔 내 표(MyVoteTarget, 없으면 0)에 맞춘다.</summary>
        private void SyncDrawnCardWithServer()
        {
            GameSession session = Session;
            // 보낸 투표의 응답을 기다리는 중이면 서버 값이 아직 옛 값이다 → 응답(ShowVoteAccepted·ShowError) 때 맞춘다
            if (voteCardHand != null && session != null && !session.IsActionInFlight)
            {
                voteCardHand.SetDrawnSilently(session.MyVoteTarget);
            }
        }

        /// <summary>카드패에 넣을 플레이어: 살아 있는 다른 플레이어 (기존 투표 패널처럼 자기 자신은 뺀다).</summary>
        private List<PlayerView> BuildVoteTargets(GameStateDto state, long myId)
        {
            voteTargets.Clear();
            foreach (PlayerViewDto p in state.players)
            {
                if (p.alive && p.playerId != myId)
                {
                    voteTargets.Add(new PlayerView(p.playerId, p.nickname, p.alive));
                }
            }
            return voteTargets;
        }

        private void RefreshPanels()
        {
            GameSession session = Session;
            if (session == null)
            {
                return;
            }
            if (nightPanel != null)
            {
                nightPanel.Refresh(session);
            }
            if (dayPanel != null)
            {
                dayPanel.Refresh(session);
            }
            if (roleCard != null && session.Me != null)
            {
                roleCard.Show(session.Me, session.State);
            }
        }

        /// <summary>밤이 되면 쓸 수 있는 능력이 있을 때 한 번 서랍을 열어 준다. /me가 늦게 와도 받은 뒤에 연다.</summary>
        private void TryOpenNightDrawer()
        {
            GameSession session = Session;
            if (nightDrawerOpened || currentPhase != GamePhases.Night || session == null || session.NightAbility != AbilityBlock.None)
            {
                return;
            }
            nightDrawerOpened = true;
            OpenDrawer();
            ApplyDrawerContent(BottomTabMode.Vote);
        }

        /// <summary>낮이 되면 살아 있는 사람에게 한 번 서랍을 열어 토론 넘기기 패널을 보여 준다. /me가 늦게 와도 받은 뒤에 연다.</summary>
        private void TryOpenDayDrawer()
        {
            GameSession session = Session;
            if (dayDrawerOpened || currentPhase != GamePhases.Day || session == null || !session.CanSkipDay)
            {
                return;
            }
            dayDrawerOpened = true;
            OpenDrawer();
            ApplyDrawerContent(BottomTabMode.Vote);
        }

        private void OpenDrawer()
        {
            if (bottomTab != null)
            {
                bottomTab.OpenVote();
            }
        }

        private void CloseDrawer()
        {
            if (bottomTab != null && bottomTab.Mode == BottomTabMode.Vote)
            {
                bottomTab.Close();
            }
        }

        private void OnDrawerModeChanged(BottomTabMode mode)
        {
            ApplyDrawerContent(mode);
        }

        /// <summary>
        /// 서랍([+])이 열려 있으면 밤에는 NightActionPanel, 낮에는 DaySkipPanel, 그 밖에는 VotePanel을 보여 준다.
        /// BottomTabController가 VotePanel을 켠 뒤(ModeChanged)에 불려서 밤이면 다시 바꿔 놓는다.
        /// </summary>
        private void ApplyDrawerContent(BottomTabMode mode)
        {
            if (mode == BottomTabMode.Closed)
            {
                return; // 내려가는 동안은 그대로 둔다. VotePanel은 다 내려간 뒤 BottomTabController가 끈다
            }
            bool night = mode == BottomTabMode.Vote && currentPhase == GamePhases.Night;
            bool day = mode == BottomTabMode.Vote && currentPhase == GamePhases.Day && dayPanel != null;
            if (nightPanel != null)
            {
                nightPanel.gameObject.SetActive(night);
            }
            if (dayPanel != null)
            {
                dayPanel.gameObject.SetActive(day);
            }
            if (votePanel != null && mode == BottomTabMode.Vote)
            {
                votePanel.gameObject.SetActive(!night && !day);
            }
        }

        private Sprite PortraitOf(long playerId)
        {
            int index;
            if (portraits == null || portraits.Length == 0 || !playerOrder.TryGetValue(playerId, out index))
            {
                return null;
            }
            return portraits[index % portraits.Length];
        }

        private void AddSystem(string text)
        {
            if (chatLog != null && !string.IsNullOrEmpty(text))
            {
                chatLog.AddSystemMessage(GameScreenText.NoRichText(text));
                chatLog.ScrollToLatest();
            }
        }
    }
}
