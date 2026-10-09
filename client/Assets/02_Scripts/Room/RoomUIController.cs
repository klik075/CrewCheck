using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Common;

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// Room 씬(대기실) 전체 UI 담당 (Canvas 등 씬 오브젝트에 부착).
    ///
    /// 흐름
    ///   Lobby에서 방 생성/입장 → Room 씬 → (방장) 게임 시작 → 모두 GameScene
    ///
    /// 기능
    ///   1) pollInterval초마다 GET /api/v1/rooms/{roomId} 로 방 상태를 받아
    ///      제목, 인원(현재 / 최대), 참가자 목록(RoomPlayer 프리팹)을 갱신한다.
    ///   2) Button_StartGame 은 방장에게만 보인다. (방장이 나가면 서버가 다음 사람에게 넘기므로 매번 다시 계산)
    ///      minPlayersToStart명 미만이거나 방장을 뺀 참가자 중 준비 안 한 사람이 있으면 비활성화한다.
    ///   2-1) Button_Ready 는 방장이 아닌 참가자에게만 보인다. 누르면 준비 ↔ 준비 취소.
    ///   3) 방장이 시작을 누르면 POST /api/v1/rooms/{roomId}/games 호출
    ///      → 성공 응답의 gameId를 RoomSession에 저장하고 GameScene으로 이동한다.
    ///   4) 방장이 아닌 참가자는 서버 푸시가 없으므로 polling 중에 status == IN_GAME 이 되면
    ///      gameId를 저장하고 GameScene으로 이동한다. (서버는 게임 생성에 성공했을 때만 IN_GAME으로 바꾼다)
    ///   5) Button_ExitRoom: DELETE /api/v1/rooms/{roomId}/players/me → 세션 비우고 Lobby로 이동
    ///   6) 방장은 다른 참가자 줄의 [관리] 버튼 → 플레이어 관리 메뉴에서 [방장 위임] / [추방] → 확인 팝업 [예] 순서로
    ///      방장을 넘기거나 내보낼 수 있다.
    ///      위임받은 사람은 다음 polling에서 방장이 되어 시작 버튼이 보이고, 이전 방장에게는 준비 버튼이 보인다.
    ///      추방된 사람은 다음 polling에서 목록에 자신이 없음을 보고 로비로 돌아간다. (그 방에는 다시 들어갈 수 없음)
    ///
    /// 진입 조건
    ///   로그인 안 됨 → Title, 방 정보 없음 → Lobby
    /// </summary>
    public class RoomUIController : MonoBehaviour
    {
        [Header("Room Info")]
        [SerializeField] private TMP_Text titleText;       // Canvas/Title
        [SerializeField] private TMP_Text memberCountText; // Text_memberCntInfo ("00 / 00")

        [Header("Player List")]
        [SerializeField] private Transform playerListContent; // PlayerList/Viewport/Content
        [Tooltip("03_Prefabs/Room/RoomPlayer. RoomPlayerView가 없으면 생성할 때 자동으로 붙인다.")]
        [SerializeField] private GameObject roomPlayerPrefab;

        [Header("Buttons")]
        [SerializeField] private Button startGameButton; // Buttons/Button_StartGame (방장만 보임)
        [SerializeField] private Button exitRoomButton;  // Buttons/Button_ExitRoom
        [SerializeField] private Button readyButton;     // Buttons/Button_Ready (방장이 아닌 참가자만 보임)
        [Tooltip("준비 버튼 문구. 비워 두면 readyButton 자식의 첫 번째 TMP_Text를 사용한다.")]
        [SerializeField] private TMP_Text readyButtonLabel;
        [SerializeField] private string readyLabel = "준비";
        [SerializeField] private string unreadyLabel = "준비 취소";

        [Header("Popup (선택)")]
        [Tooltip("플레이어 관리 메뉴(Popup_PlayerManage). 참가자 줄의 [관리] 버튼으로 연다. 비워 두면 위임/추방 기능이 꺼진다.")]
        [SerializeField] private PlayerManageMenu playerManageMenu;
        [Tooltip("위임/추방 확인 팝업(Popup_Confirm). 비워 두면 묻지 않고 바로 요청한다.")]
        [SerializeField] private RoomConfirmPopup confirmPopup;

        [Header("Status (선택)")]
        [SerializeField] private StatusMessageView statusMessage;

        [Header("Options")]
        [Tooltip("방 상태 조회 간격(초). 게임이 시작되면 첫 밤 타이머가 바로 돌기 때문에 짧게 둔다.")]
        [SerializeField] private float pollInterval = 1f;
        [Tooltip("게임 시작에 필요한 최소 인원 (서버 RoleAssigner.MIN_PLAYERS = 4)")]
        [SerializeField] private int minPlayersToStart = 4;
        [Tooltip("로그인(토큰) 남은 시간이 이 분보다 적으면 다시 로그인하라고 안내하고, 방장은 게임을 시작하지 못하게 한다. " +
                 "게임 중 토큰이 만료되면 상태 조회가 끊겨 서버가 60초 뒤 사망 처리한다.")]
        [SerializeField] private int minLoginMinutesForGame = 20;

        private const string RoomGoneNotice = "방이 사라져 로비로 돌아왔습니다.";
        private const string KickedNotice = "방장에 의해 추방되어 로비로 돌아왔습니다.";
        private const string RemovedFromRoomNotice = "방에서 제외되어 로비로 돌아왔습니다. (게임 중 연결이 60초 넘게 끊기면 탈락하고 방에서도 빠집니다)";

        private readonly List<RoomPlayerView> spawnedPlayers = new List<RoomPlayerView>();
        private string lastListSignature; // 참가자/방장이 바뀌었을 때만 목록을 다시 그리기 위한 값
        private int lastPlayerCount;
        private bool lastInGame;
        private bool lastAllGuestsReady; // 방장을 뺀 모든 참가자가 준비했는지
        private bool myReady;            // 내 준비 상태
        private int readySeq;            // 준비 요청마다 증가. 요청 전에 보낸 방 조회 응답(이전 준비 상태)을 무시하기 위한 값

        private bool isFetching;      // GET /rooms/{id} 요청 중
        private bool isStarting;      // POST /rooms/{id}/games 요청 중
        private bool isLeaving;       // DELETE /rooms/{id}/players/me 요청 중
        private bool isReadying;      // POST/DELETE /rooms/{id}/players/me/ready 요청 중
        private bool isManaging;      // 방장 위임 / 추방 요청 중
        private bool isTransitioning; // 다른 씬으로 이동 시작함 (이후 모든 요청/응답 무시)
        private Coroutine pollRoutine;

        // ------------------------------------------------------------------
        // Unity 생명주기
        // ------------------------------------------------------------------

        private void Awake()
        {
            BindIfEmpty(startGameButton, OnStartGameClicked);
            BindIfEmpty(exitRoomButton, OnExitRoomClicked);
            BindIfEmpty(readyButton, OnReadyClicked);
            if (readyButtonLabel == null && readyButton != null) readyButtonLabel = readyButton.GetComponentInChildren<TMP_Text>(true);
        }

        private void Start()
        {
            if (!AuthSession.IsAuthenticated)
            {
                Debug.LogWarning("[Room] 로그인 정보가 없어 타이틀 씬으로 이동합니다.");
                isTransitioning = true;
                SceneLoader.Load(SceneType.Title);
                return;
            }

            if (!RoomSession.HasRoom)
            {
                Debug.LogWarning("[Room] 방 정보가 없어 로비로 이동합니다.");
                isTransitioning = true;
                SceneLoader.Load(SceneType.Lobby);
                return;
            }

            // 첫 응답 전까지는 세션 값으로 먼저 보여준다.
            if (titleText != null) titleText.text = RoomSession.RoomTitle;
            if (memberCountText != null) memberCountText.text = $"- / {RoomSession.MaxPlayers}";
            ClearPlayerList(); // Content에 놓여 있던 샘플 아이템 제거
            UpdateStartButton();
            UpdateReadyButton();
            if (LoginExpiresSoon(out int minutesLeft))
            {
                statusMessage?.ShowError($"로그인 유지 시간이 약 {minutesLeft}분 남았습니다. 게임 중 만료되면 연결이 끊겨 탈락하니, 로비에서 로그아웃 후 다시 로그인해 주세요.", keep: true);
            }

            pollRoutine = StartCoroutine(PollLoop());
        }

        /// <summary>토큰 남은 시간이 게임 한 판을 버티기에 부족한지. 남은 시간을 알 수 없으면 false.</summary>
        private bool LoginExpiresSoon(out int minutesLeft)
        {
            double? left = AuthSession.TokenMinutesLeft;
            minutesLeft = left.HasValue ? Mathf.Max(0, Mathf.FloorToInt((float)left.Value)) : 0;
            return left.HasValue && left.Value < minLoginMinutesForGame;
        }

        private void OnDestroy()
        {
            if (startGameButton != null) startGameButton.onClick.RemoveListener(OnStartGameClicked);
            if (exitRoomButton != null) exitRoomButton.onClick.RemoveListener(OnExitRoomClicked);
            if (readyButton != null) readyButton.onClick.RemoveListener(OnReadyClicked);
        }

        // ------------------------------------------------------------------
        // 버튼 핸들러 (인스펙터 OnClick에 직접 연결해도 된다)
        // ------------------------------------------------------------------

        /// <summary>Button_StartGame: 게임 시작 (방장만)</summary>
        public void OnStartGameClicked()
        {
            if (isStarting || isLeaving || isReadying || isManaging || isTransitioning || SceneLoader.IsLoading) return;

            if (!RoomSession.IsHost)
            {
                statusMessage?.ShowError("방장만 게임을 시작할 수 있습니다.");
                return;
            }

            if (lastPlayerCount < minPlayersToStart)
            {
                statusMessage?.ShowError($"게임을 시작하려면 최소 {minPlayersToStart}명이 필요합니다.");
                return;
            }

            if (!lastAllGuestsReady)
            {
                statusMessage?.ShowError("모든 참가자가 준비해야 게임을 시작할 수 있습니다.");
                return;
            }

            if (LoginExpiresSoon(out int minutesLeft))
            {
                statusMessage?.ShowError($"로그인 유지 시간이 약 {minutesLeft}분밖에 남지 않아 게임을 시작할 수 없습니다. 로비에서 로그아웃 후 다시 로그인해 주세요.");
                return;
            }

            isStarting = true;
            UpdateStartButton();
            statusMessage?.ShowInfo("게임을 시작하는 중...", keep: true);

            RoomApi.StartGame(RoomSession.RoomId, result =>
            {
                if (this == null) return;
                isStarting = false;
                if (isTransitioning) return; // polling이 먼저 IN_GAME을 보고 이동을 시작한 경우

                if (result.success && result.data != null && !string.IsNullOrEmpty(result.data.gameId))
                {
                    Debug.Log($"[Room] 게임 시작 성공: gameId={result.data.gameId}, phase={result.data.phase}");
                    GoToGameScene(result.data.gameId);
                    return;
                }

                // 403: 방장 아님, 409: 이미 게임 중 / 4명 미만 / 준비 안 된 참가자 있음 / 직업 배정 실패, 400: 방 없음
                string message = result.success ? "게임 정보를 받지 못했습니다. 다시 시도해 주세요." : result.message;
                Debug.LogWarning($"[Room] 게임 시작 실패 ({result.statusCode}): {message}");
                statusMessage?.ShowError(message);
                UpdateStartButton();
            });
        }

        /// <summary>Button_ExitRoom: 방 나가기 → Lobby</summary>
        public void OnExitRoomClicked()
        {
            if (isStarting || isLeaving || isReadying || isManaging || isTransitioning || SceneLoader.IsLoading) return;

            isLeaving = true;
            SetButtonsInteractable(false);
            statusMessage?.ShowInfo("방에서 나가는 중...", keep: true);

            RoomApi.LeaveRoom(RoomSession.RoomId, result =>
            {
                if (this == null) return;
                isLeaving = false;
                if (isTransitioning) return;

                // 400 = 이미 사라진 방 → 어차피 나간 것과 같다.
                if (result.success || result.statusCode == 400)
                {
                    ReturnToLobby();
                    return;
                }

                // 409: 게임이 이미 시작됨 / 참가 중이 아님 → 다음 polling이 GameScene 또는 Lobby로 보낸다.
                statusMessage?.ShowError(result.message);
                SetButtonsInteractable(true);
                UpdateStartButton();
                UpdateReadyButton();
            });
        }

        /// <summary>Button_Ready: 준비 ↔ 준비 취소 (방장 제외)</summary>
        public void OnReadyClicked()
        {
            if (isStarting || isLeaving || isReadying || isManaging || isTransitioning || SceneLoader.IsLoading) return;
            if (RoomSession.IsHost || lastInGame) return;

            bool nextReady = !myReady;
            isReadying = true;
            readySeq++;
            UpdateReadyButton();

            RoomApi.SetReady(RoomSession.RoomId, nextReady, result =>
            {
                if (this == null) return;
                isReadying = false;
                readySeq++; // 요청 중에 보낸 방 조회 응답도 이전 상태일 수 있으므로 무시한다.
                if (isTransitioning) return;

                if (result.success) myReady = nextReady; // 참가자 목록의 준비 표시는 다음 polling에서 갱신된다.
                else if (!result.IsUnauthorized)
                {
                    // 409: 게임이 이미 시작됨 / 방장이 됨 / 참가 중이 아님 → 다음 polling이 상태를 맞춘다.
                    Debug.LogWarning($"[Room] 준비 상태 변경 실패 ({result.statusCode}): {result.message}");
                    statusMessage?.ShowError(result.message);
                }
                UpdateReadyButton();
            });
        }

        /// <summary>참가자 줄의 [관리] 버튼 (방장에게만, 내 줄 제외) → 플레이어 관리 메뉴를 연다.</summary>
        private void OnManageClicked(RoomPlayerView target)
        {
            if (target == null) return;
            if (isStarting || isLeaving || isReadying || isManaging || isTransitioning || SceneLoader.IsLoading) return;
            if (!RoomSession.IsHost || lastInGame) return;

            if (playerManageMenu == null)
            {
                Debug.LogWarning("[Room] playerManageMenu가 연결되지 않아 플레이어 관리 메뉴를 열 수 없습니다.");
                return;
            }

            // 줄(target)은 polling으로 다시 만들어질 수 있으므로 값만 꺼내 둔다.
            long targetUserId = target.UserId;
            string nickname = target.Nickname;
            playerManageMenu.Open(targetUserId, nickname,
                () => OnTransferHostChosen(targetUserId, nickname),
                () => OnKickChosen(targetUserId, nickname));
        }

        /// <summary>관리 메뉴에서 [방장 위임] 선택 → 확인 → 요청</summary>
        private void OnTransferHostChosen(long targetUserId, string nickname)
        {
            ConfirmThen($"{nickname}님에게 방장을 넘길까요?", targetUserId, () =>
                RequestHostAction(
                    $"{nickname}님에게 방장을 넘기는 중...",
                    $"{nickname}님이 방장이 되었습니다.",
                    "방장 위임",
                    done => RoomApi.TransferHost(RoomSession.RoomId, targetUserId, done)));
        }

        /// <summary>관리 메뉴에서 [추방] 선택 → 확인 → 요청</summary>
        private void OnKickChosen(long targetUserId, string nickname)
        {
            ConfirmThen($"{nickname}님을 추방할까요?\n추방된 사람은 이 방에 다시 들어올 수 없습니다.", targetUserId, () =>
                RequestHostAction(
                    $"{nickname}님을 추방하는 중...",
                    $"{nickname}님을 추방했습니다.",
                    "추방",
                    done => RoomApi.KickPlayer(RoomSession.RoomId, targetUserId, done)));
        }

        /// <summary>확인 팝업이 연결돼 있으면 물어본 뒤, 없으면 바로 실행한다.</summary>
        private void ConfirmThen(string message, long targetUserId, System.Action action)
        {
            if (isStarting || isLeaving || isReadying || isManaging || isTransitioning || SceneLoader.IsLoading) return;
            if (!RoomSession.IsHost || lastInGame) return;

            if (confirmPopup != null) confirmPopup.Open(message, targetUserId, action);
            else action();
        }

        /// <summary>
        /// 관리 메뉴나 확인 팝업이 열려 있는 동안 상태가 바뀌면 닫는다.
        /// (내가 방장이 아니게 됨, 게임 시작, 대상이 나감·추방됨) 그래도 요청이 나가면 서버가 403/409로 막는다.
        /// </summary>
        private void CloseStaleManagePopups(RoomDetailResponse room)
        {
            bool canManage = RoomSession.IsHost && !room.IsInGame;

            if (playerManageMenu != null && playerManageMenu.IsOpen
                && (!canManage || !room.HasPlayer(playerManageMenu.TargetUserId)))
                playerManageMenu.Close();

            if (confirmPopup != null && confirmPopup.IsOpen && confirmPopup.TargetUserId != 0
                && (!canManage || !room.HasPlayer(confirmPopup.TargetUserId)))
            {
                confirmPopup.Close();
                statusMessage?.ShowInfo("방 상태가 바뀌어 요청을 취소했습니다.");
            }
        }

        /// <summary>씬을 떠날 때 열려 있는 관리 메뉴·확인 팝업을 닫는다.</summary>
        private void CloseManagePopups()
        {
            if (playerManageMenu != null && playerManageMenu.IsOpen) playerManageMenu.Close();
            if (confirmPopup != null && confirmPopup.IsOpen) confirmPopup.Close();
        }

        /// <summary>
        /// 방장 위임 / 추방 요청 공통 처리.
        /// 성공하면 다음 polling을 기다리지 않고 바로 방을 다시 조회해 목록과 버튼을 갱신한다.
        /// 실패: 403 방장이 아님(그사이 방장이 바뀜), 409 게임 시작됨 / 대상이 이미 나감 → 메시지만 보여 주고 polling이 상태를 맞춘다.
        /// </summary>
        private void RequestHostAction(string progressMessage, string successMessage, string actionName,
            System.Action<System.Action<WhoisntCitizen.Network.ApiResult>> send)
        {
            // 팝업이 열려 있는 동안 상태가 바뀌었을 수 있으므로 한 번 더 확인한다.
            if (isStarting || isLeaving || isReadying || isManaging || isTransitioning || SceneLoader.IsLoading) return;
            if (!RoomSession.IsHost || lastInGame) return;

            SetManaging(true);
            statusMessage?.ShowInfo(progressMessage, keep: true);

            send(result =>
            {
                if (this == null) return;
                SetManaging(false);
                if (isTransitioning) return;

                if (result.success)
                {
                    statusMessage?.ShowSuccess(successMessage);
                    if (!isFetching) FetchRoom();
                    return;
                }
                if (result.IsUnauthorized) return; // ApiClient가 세션을 비우고 Title로 보낸다.

                Debug.LogWarning($"[Room] {actionName} 실패 ({result.statusCode}): {result.message}");
                statusMessage?.ShowError(result.message);
            });
        }

        private void SetManaging(bool value)
        {
            isManaging = value;
            foreach (RoomPlayerView view in spawnedPlayers)
                if (view != null) view.SetManageInteractable(!value);
            UpdateStartButton();
            UpdateReadyButton();
        }

        // ------------------------------------------------------------------
        // 방 상태 polling
        // ------------------------------------------------------------------

        private IEnumerator PollLoop()
        {
            var wait = new WaitForSeconds(Mathf.Max(0.2f, pollInterval));
            while (!isTransitioning)
            {
                if (!isFetching && !SceneLoader.IsLoading) FetchRoom();
                yield return wait;
            }
            pollRoutine = null;
        }

        private void FetchRoom()
        {
            isFetching = true;
            long roomId = RoomSession.RoomId;
            int seqAtRequest = readySeq;

            RoomApi.GetRoom(roomId, result =>
            {
                if (this == null) return;
                isFetching = false;
                if (isTransitioning) return;

                if (!result.success)
                {
                    if (result.IsUnauthorized) return; // ApiClient가 세션을 비우고 Title로 보낸다.

                    if (result.statusCode == 400 || result.IsNotFound)
                    {
                        Debug.LogWarning($"[Room] 방 #{roomId}이(가) 더 이상 없습니다: {result.message}");
                        ReturnToLobby(RoomGoneNotice);
                        return;
                    }

                    // 연결 끊김 등: 메시지만 띄우고 다음 polling에서 다시 시도
                    statusMessage?.ShowError(result.message);
                    return;
                }

                RoomDetailResponse room = result.data;
                if (room == null) return;

                // 내가 참가자 목록에 없으면 (방장이 추방함, 게임 중 연결이 끊겨 제외됨, 다른 기기에서 나감, 서버 초기화 등) 로비로 돌아간다.
                if (!room.HasPlayer(AuthSession.UserId))
                {
                    bool kicked = room.IsKicked(AuthSession.UserId);
                    Debug.LogWarning($"[Room] 방 #{roomId} 참가자 목록에 내가 없어 로비로 이동합니다. (추방: {kicked})");
                    ReturnToLobby(kicked ? KickedNotice : RemovedFromRoomNotice);
                    return;
                }

                RoomSession.Set(room); // 방장 변경, gameId 반영

                // 준비 요청을 보내기 전(또는 보내는 중)에 출발한 조회 응답은 이전 준비 상태라 화면에 반영하지 않는다.
                bool staleReady = isReadying || seqAtRequest != readySeq;
                if (!staleReady) Render(room);

                // 방장이 게임을 시작했다 → 모두 GameScene으로
                // (방금 끝내고 돌아온 게임은 서버가 아직 IN_GAME으로 보여 줄 수 있으므로 다시 들어가지 않는다)
                if (room.IsInGame && !string.IsNullOrEmpty(room.gameId) && room.gameId != RoomSession.FinishedGameId)
                    GoToGameScene(room.gameId);
            });
        }

        // ------------------------------------------------------------------
        // 화면 갱신
        // ------------------------------------------------------------------

        private void Render(RoomDetailResponse room)
        {
            List<RoomPlayerResponse> players = room.players ?? new List<RoomPlayerResponse>();
            lastPlayerCount = players.Count;
            lastInGame = room.IsInGame;
            lastAllGuestsReady = room.AllGuestsReady();
            myReady = room.IsReady(AuthSession.UserId);

            if (titleText != null) titleText.text = room.title;
            if (memberCountText != null) memberCountText.text = $"{players.Count} / {room.maxPlayers}";

            // 참가자 또는 방장이 바뀌었을 때만 목록을 다시 만든다.
            string signature = BuildSignature(room.hostUserId, players);
            if (signature != lastListSignature)
            {
                lastListSignature = signature;
                RebuildPlayerList(room.hostUserId, players);
            }

            CloseStaleManagePopups(room);
            UpdateStartButton();
            UpdateReadyButton();
        }

        private void RebuildPlayerList(long hostUserId, List<RoomPlayerResponse> players)
        {
            ClearPlayerList();
            if (playerListContent == null || roomPlayerPrefab == null)
            {
                Debug.LogWarning("[Room] playerListContent 또는 roomPlayerPrefab이 연결되지 않았습니다.");
                return;
            }

            long myUserId = AuthSession.UserId;
            bool canManage = hostUserId == myUserId; // 내가 방장이면 다른 사람 줄에 관리 버튼
            foreach (RoomPlayerResponse p in players)
            {
                GameObject go = Instantiate(roomPlayerPrefab, playerListContent);
                go.name = $"RoomPlayer_{p.userId}";
                RoomPlayerView view = go.GetComponent<RoomPlayerView>();
                if (view == null) view = go.AddComponent<RoomPlayerView>();
                view.Bind(p, p.userId == hostUserId, p.userId == myUserId, canManage, OnManageClicked);
                view.SetManageInteractable(!isManaging);
                spawnedPlayers.Add(view);
            }
        }

        private void ClearPlayerList()
        {
            spawnedPlayers.Clear();
            if (playerListContent == null) return;
            for (int i = playerListContent.childCount - 1; i >= 0; i--)
                Destroy(playerListContent.GetChild(i).gameObject);
        }

        /// <summary>시작 버튼: 방장에게만 보이고, 인원이 충분하고 모두 준비했고 다른 요청 중이 아닐 때만 누를 수 있다.</summary>
        private void UpdateStartButton()
        {
            if (startGameButton == null) return;

            bool isHost = RoomSession.IsHost;
            startGameButton.gameObject.SetActive(isHost);
            startGameButton.interactable = isHost
                                           && lastPlayerCount >= minPlayersToStart
                                           && lastAllGuestsReady
                                           && !lastInGame
                                           && !isStarting && !isLeaving && !isManaging && !isTransitioning;
        }

        /// <summary>준비 버튼: 방장이 아닌 참가자에게만 보이고, 내 상태에 따라 "준비" / "준비 취소"를 표시한다.</summary>
        private void UpdateReadyButton()
        {
            if (readyButton == null) return;

            bool isGuest = !RoomSession.IsHost;
            readyButton.gameObject.SetActive(isGuest);
            readyButton.interactable = isGuest
                                       && !lastInGame
                                       && !isReadying && !isStarting && !isLeaving && !isManaging && !isTransitioning;
            if (readyButtonLabel != null) readyButtonLabel.text = myReady ? unreadyLabel : readyLabel;
        }

        private void SetButtonsInteractable(bool value)
        {
            if (startGameButton != null) startGameButton.interactable = value;
            if (exitRoomButton != null) exitRoomButton.interactable = value;
            if (readyButton != null) readyButton.interactable = value;
        }

        private static string BuildSignature(long hostUserId, List<RoomPlayerResponse> players)
        {
            var sb = new StringBuilder();
            sb.Append(hostUserId).Append('|');
            foreach (RoomPlayerResponse p in players)
                sb.Append(p.userId).Append(':').Append(p.nickname).Append(':').Append(p.ready ? '1' : '0').Append(',');
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 씬 이동
        // ------------------------------------------------------------------

        private void GoToGameScene(string gameId)
        {
            if (isTransitioning) return;

            RoomSession.SetGameId(gameId);
            BeginTransition();
            statusMessage?.ShowSuccess("게임을 시작합니다!", keep: true);
            Debug.Log($"[Room] GameScene으로 이동: roomId={RoomSession.RoomId}, gameId={gameId}");

            if (!SceneLoader.Load(SceneType.Game))
            {
                // Build Settings 누락 등: 다시 시도할 수 있도록 polling을 재개한다.
                statusMessage?.ShowError("GameScene으로 이동하지 못했습니다. (Build Settings 확인)", keep: true);
                CancelTransition();
            }
        }

        /// <param name="notice">로비에서 보여 줄 돌아온 이유. 직접 나간 경우는 null</param>
        private void ReturnToLobby(string notice = null)
        {
            if (isTransitioning) return;

            BeginTransition();
            RoomSession.Clear();
            if (notice != null) RoomSession.SetLobbyNotice(notice);
            SceneLoader.Load(SceneType.Lobby);
        }

        private void BeginTransition()
        {
            isTransitioning = true;
            CloseManagePopups();
            if (pollRoutine != null)
            {
                StopCoroutine(pollRoutine);
                pollRoutine = null;
            }
            SetButtonsInteractable(false);
        }

        private void CancelTransition()
        {
            isTransitioning = false;
            if (exitRoomButton != null) exitRoomButton.interactable = true;
            UpdateStartButton();
            UpdateReadyButton();
            if (pollRoutine == null) pollRoutine = StartCoroutine(PollLoop());
        }

        private static void BindIfEmpty(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null && button.onClick.GetPersistentEventCount() == 0)
                button.onClick.AddListener(action);
        }
    }
}
