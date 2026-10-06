using System.Collections.Generic;
using TMPro;
using UnityEngine;
// using WhoisntCitizen.Chat; // [공개 안내 자동 판단 - 주석 처리]
using WhoisntCitizen.GameUI;
using WhoisntCitizen.Vote;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 게임 화면의 단일 진입점(IGameView). GameSession이 알려 주는 것을 각 패널에 나눠 준다.
    /// - feat/vote(klik075)의 부품: ChatLogView(시스템 메시지), BottomTabController(하단 시트), VotePanelController(투표)
    /// - G의 부품: NightActionPanel(밤 능력), RoleCardView(직업 카드), GameResultPanel(결과 + "대기실로")
    /// 패널끼리는 서로 모르고, 입력(투표·밤 능력·대기실로)은 모두 여기서 GameController·WaitingRoomController로 넘긴다.
    /// GameController의 gameView 칸에 이 컴포넌트를 넣는다.
    /// 하단 시트: 밤에는 [+] 서랍에 NightActionPanel을, 그 밖에는 VotePanel을 보여 준다(둘 다 Drawer 안에 나란히 둔다).
    /// 투표: VOTE 페이즈가 되면 부채꼴 카드패(VoteCardHandController)가 아래에서 올라온다. 카드를 뽑으면 투표한다.
    ///       기존 VotePanel 투표 처리는 [카드패 투표로 교체 - 주석 처리] 표시로 막아 두었다(패널은 보기 전용으로 남는다).
    /// 채팅 기록: 공개 안내는 실제 서버면 서버 채팅이 보내므로 AnnouncePublic일 때만 남긴다.
    // [공개 안내 자동 판단 - 주석 처리]
    // /// 채팅 기록: 공개 안내(페이즈·밤 결과·처형·승리)는 서버 채팅(GameChatController)이 같은 채팅창에 보여 주면 남기지 않고,
    // /// 서버 채팅이 없거나 꺼져 있거나 멈췄을 때, 또는 가짜 서버 게임일 때만 직접 남긴다(AnnouncePublic).
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
        [SerializeField] private RoleCardView roleCard;
        [SerializeField] private GameResultPanel resultPanel;

        [Header("초상화 (플레이어 순서대로 돌려 쓴다. 비우면 없음)")]
        [SerializeField] private Sprite[] portraits;

        [Header("공개 안내")]
        [Tooltip("실제 서버는 페이즈·밤 결과·처형·승리 안내를 방 채팅 시스템 메시지로 보낸다(서버 PR #18). " +
                 "그 채팅이 이 ChatLogView에 나오면 꺼 두어 같은 안내가 두 번 나오지 않게 한다. " +
                 "가짜 서버는 채팅이 없으므로 이 값과 상관없이 직접 남긴다.")]
        [SerializeField] private bool announcePublicWithRealServer = false;

        // [공개 안내 자동 판단 - 주석 처리]
        // [Header("공개 안내 (비우면 씬에서 찾는다)")]
        // [Tooltip("실제 서버는 페이즈·밤 결과·처형·승리 안내를 방 채팅 시스템 메시지로 보낸다. " +
        //          "이 서버 채팅이 같은 ChatLogView에 안내를 보여 주는 동안에는 이 화면이 공개 안내를 남기지 않는다(중복 방지). " +
        //          "서버 채팅이 없거나 꺼져 있거나 연결에 실패해 멈췄으면, 그리고 가짜 서버 게임이면 직접 남긴다.")]
        // [SerializeField] private GameChatController serverChat;

        private readonly List<PlayerView> playerViews = new List<PlayerView>();
        private readonly Dictionary<long, int> playerOrder = new Dictionary<long, int>();
        private string currentPhase;
        private string announcedRole;
        private string announcedTeammates;
        private bool nightDrawerOpened;
        private int shownSeconds = -1;
        private bool shownWaiting;
        private bool deadVoteNoticeShown;
        private readonly List<PlayerView> voteTargets = new List<PlayerView>();

        private GameSession Session
        {
            get { return controller != null ? controller.Session : null; }
        }

        private long MyId
        {
            get { return Session != null && Session.Me != null ? Session.Me.playerId : 0; }
        }

        /// <summary>공개 안내(페이즈·밤 결과 요약·처형 결과·승리)를 이 화면이 직접 남길지. 실제 서버면 서버 채팅이 보낸다.</summary>
        private bool AnnouncePublic
        {
            get { return controller != null && (controller.IsFakeGame || announcePublicWithRealServer); }
        }

        // [공개 안내 자동 판단 - 주석 처리]
        // /// <summary>
        // /// 공개 안내(페이즈·밤 결과 요약·처형 결과·승리)를 이 화면이 직접 남길지.
        // /// 서버 안내가 이 채팅창에 넘어오면 끔(false), 넘어오지 않으면 켬(true). 가짜 서버 게임은 서버 안내가 없으므로 항상 켬.
        // /// </summary>
        // private bool AnnouncePublic
        // {
        //     get { return controller != null && (controller.IsFakeGame || !ServerAnnouncesHere); }
        // }
        //
        // /// <summary>서버 채팅이 서버 시스템 메시지를 이 화면과 같은 채팅창에 보여 주는 중인지.</summary>
        // private bool ServerAnnouncesHere
        // {
        //     get { return serverChat != null && serverChat.ShowsServerMessages && serverChat.chatLog == chatLog; }
        // }

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
            // [공개 안내 자동 판단 - 주석 처리]
            // if (serverChat == null)
            // {
            //     serverChat = FindFirstObjectByType<GameChatController>(FindObjectsInactive.Include);
            // }
            if (votePanel != null)
            {
                votePanel.PortraitResolver = PortraitOf;
                // [카드패 투표로 교체 - 주석 처리] 기존 투표 패널의 확정 → 서버 투표
                // votePanel.VoteConfirmed += OnVoteConfirmed;
            }
            SetUpVoteCardHand();
            if (nightPanel != null)
            {
                nightPanel.PortraitResolver = PortraitOf;
                nightPanel.Confirmed += OnNightConfirmed;
                nightPanel.Skipped += OnNightSkipped;
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
            }
            if (nightPanel != null)
            {
                nightPanel.Confirmed -= OnNightConfirmed;
                nightPanel.Skipped -= OnNightSkipped;
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
            currentPhase = phaseChanged.Phase;
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

        public void ShowVoteAccepted(VoteResultDto result)
        {
            GameSession session = Session;
            if (session != null && session.MyVoteTarget != 0)
            {
                AddSystem(GameScreenText.VoteAccepted(session.MyVoteTarget, session.State));
            }
            SyncDrawnCardWithServer(); // 빠르게 바꿔 누른 경우 등, 화면의 뽑은 카드를 서버가 받은 표에 맞춘다
        }

        public void ShowError(string message)
        {
            if (chatLog != null && !string.IsNullOrEmpty(message))
            {
                chatLog.AddLine("<color=#FF6B6B>[알림] " + GameScreenText.NoRichText(message) + "</color>");
                chatLog.ScrollToLatest();
            }
            // 투표 중 오류(서버 거절·연결 실패)면 뽑은 카드를 서버가 실제로 가진 내 표로 되돌린다
            if (currentPhase == GamePhases.Vote)
            {
                SyncDrawnCardWithServer();
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

        /// <summary>카드패에서 카드를 뽑음 → 서버로 투표. 보내지 못했으면(앞 요청 처리 중 등) 카드를 서버가 가진 표로 되돌린다.</summary>
        private void OnCardVoteRequested(long targetId)
        {
            bool sent = controller != null && controller.Vote(targetId);
            if (!sent)
            {
                SyncDrawnCardWithServer();
            }
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
            if (voteCardHand != null)
            {
                voteCardHand.HideImmediate();
            }
        }

        /// <summary>투표 패널은 "나"를 처음 만들 때 정하므로, /me를 받은 뒤부터 플레이어 목록을 넘긴다.</summary>
        private void RefreshPlayers(GameStateDto state)
        {
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
                }
                else
                {
                    TryShowVoteHand(); // /me가 늦게 왔거나 투표 도중 재접속한 경우
                }
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
            voteCardHand.Show(BuildVoteTargets(session.State, session.Me.playerId), PortraitOf, session.MyVoteTarget);
        }

        private void HideVoteHand()
        {
            if (voteCardHand != null)
            {
                voteCardHand.Hide();
            }
        }

        /// <summary>뽑은 카드를 서버가 받아 둔 내 표(MyVoteTarget, 없으면 0)에 맞춘다.</summary>
        private void SyncDrawnCardWithServer()
        {
            GameSession session = Session;
            if (voteCardHand != null && session != null)
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
        /// 서랍([+])이 열려 있으면 밤에는 NightActionPanel, 그 밖에는 VotePanel을 보여 준다.
        /// BottomTabController가 VotePanel을 켠 뒤(ModeChanged)에 불려서 밤이면 다시 바꿔 놓는다.
        /// </summary>
        private void ApplyDrawerContent(BottomTabMode mode)
        {
            if (mode == BottomTabMode.Closed)
            {
                return; // 내려가는 동안은 그대로 둔다. VotePanel은 다 내려간 뒤 BottomTabController가 끈다
            }
            bool night = mode == BottomTabMode.Vote && currentPhase == GamePhases.Night;
            if (nightPanel != null)
            {
                nightPanel.gameObject.SetActive(night);
            }
            if (votePanel != null && mode == BottomTabMode.Vote)
            {
                votePanel.gameObject.SetActive(!night);
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
