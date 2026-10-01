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
    ///      minPlayersToStart명 미만이면 비활성화한다.
    ///   3) 방장이 시작을 누르면 POST /api/v1/rooms/{roomId}/games 호출
    ///      → 성공 응답의 gameId를 RoomSession에 저장하고 GameScene으로 이동한다.
    ///   4) 방장이 아닌 참가자는 서버 푸시가 없으므로 polling 중에 status == IN_GAME 이 되면
    ///      gameId를 저장하고 GameScene으로 이동한다. (서버는 게임 생성에 성공했을 때만 IN_GAME으로 바꾼다)
    ///   5) Button_ExitRoom: DELETE /api/v1/rooms/{roomId}/players/me → 세션 비우고 Lobby로 이동
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

        [Header("Status (선택)")]
        [SerializeField] private StatusMessageView statusMessage;

        [Header("Options")]
        [Tooltip("방 상태 조회 간격(초). 게임이 시작되면 첫 밤 타이머가 바로 돌기 때문에 짧게 둔다.")]
        [SerializeField] private float pollInterval = 1f;
        [Tooltip("게임 시작에 필요한 최소 인원 (서버 RoleAssigner.MIN_PLAYERS = 4)")]
        [SerializeField] private int minPlayersToStart = 4;

        private readonly List<RoomPlayerView> spawnedPlayers = new List<RoomPlayerView>();
        private string lastListSignature; // 참가자/방장이 바뀌었을 때만 목록을 다시 그리기 위한 값
        private int lastPlayerCount;
        private bool lastInGame;

        private bool isFetching;      // GET /rooms/{id} 요청 중
        private bool isStarting;      // POST /rooms/{id}/games 요청 중
        private bool isLeaving;       // DELETE /rooms/{id}/players/me 요청 중
        private bool isTransitioning; // 다른 씬으로 이동 시작함 (이후 모든 요청/응답 무시)
        private Coroutine pollRoutine;

        // ------------------------------------------------------------------
        // Unity 생명주기
        // ------------------------------------------------------------------

        private void Awake()
        {
            BindIfEmpty(startGameButton, OnStartGameClicked);
            BindIfEmpty(exitRoomButton, OnExitRoomClicked);
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

            pollRoutine = StartCoroutine(PollLoop());
        }

        private void OnDestroy()
        {
            if (startGameButton != null) startGameButton.onClick.RemoveListener(OnStartGameClicked);
            if (exitRoomButton != null) exitRoomButton.onClick.RemoveListener(OnExitRoomClicked);
        }

        // ------------------------------------------------------------------
        // 버튼 핸들러 (인스펙터 OnClick에 직접 연결해도 된다)
        // ------------------------------------------------------------------

        /// <summary>Button_StartGame: 게임 시작 (방장만)</summary>
        public void OnStartGameClicked()
        {
            if (isStarting || isLeaving || isTransitioning || SceneLoader.IsLoading) return;

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

                // 409: 방장 아님 / 이미 게임 중 / 4명 미만 / 직업 배정 실패, 400: 방 없음
                string message = result.success ? "게임 정보를 받지 못했습니다. 다시 시도해 주세요." : result.message;
                Debug.LogWarning($"[Room] 게임 시작 실패 ({result.statusCode}): {message}");
                statusMessage?.ShowError(message);
                UpdateStartButton();
            });
        }

        /// <summary>Button_ExitRoom: 방 나가기 → Lobby</summary>
        public void OnExitRoomClicked()
        {
            if (isStarting || isLeaving || isTransitioning || SceneLoader.IsLoading) return;

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
            });
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
                        ReturnToLobby();
                        return;
                    }

                    // 연결 끊김 등: 메시지만 띄우고 다음 polling에서 다시 시도
                    statusMessage?.ShowError(result.message);
                    return;
                }

                RoomDetailResponse room = result.data;
                if (room == null) return;

                // 내가 참가자 목록에 없으면 (다른 기기에서 나감, 서버 초기화 등) 로비로 돌아간다.
                if (!room.HasPlayer(AuthSession.UserId))
                {
                    Debug.LogWarning($"[Room] 방 #{roomId} 참가자 목록에 내가 없어 로비로 이동합니다.");
                    ReturnToLobby();
                    return;
                }

                RoomSession.Set(room); // 방장 변경, gameId 반영
                Render(room);

                // 방장이 게임을 시작했다 → 모두 GameScene으로
                if (room.IsInGame && !string.IsNullOrEmpty(room.gameId))
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

            if (titleText != null) titleText.text = room.title;
            if (memberCountText != null) memberCountText.text = $"{players.Count} / {room.maxPlayers}";

            // 참가자 또는 방장이 바뀌었을 때만 목록을 다시 만든다.
            string signature = BuildSignature(room.hostUserId, players);
            if (signature != lastListSignature)
            {
                lastListSignature = signature;
                RebuildPlayerList(room.hostUserId, players);
            }

            UpdateStartButton();
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
            foreach (RoomPlayerResponse p in players)
            {
                GameObject go = Instantiate(roomPlayerPrefab, playerListContent);
                go.name = $"RoomPlayer_{p.userId}";
                RoomPlayerView view = go.GetComponent<RoomPlayerView>();
                if (view == null) view = go.AddComponent<RoomPlayerView>();
                view.Bind(p, p.userId == hostUserId, p.userId == myUserId);
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

        /// <summary>시작 버튼: 방장에게만 보이고, 인원이 충분하고 다른 요청 중이 아닐 때만 누를 수 있다.</summary>
        private void UpdateStartButton()
        {
            if (startGameButton == null) return;

            bool isHost = RoomSession.IsHost;
            startGameButton.gameObject.SetActive(isHost);
            startGameButton.interactable = isHost
                                           && lastPlayerCount >= minPlayersToStart
                                           && !lastInGame
                                           && !isStarting && !isLeaving && !isTransitioning;
        }

        private void SetButtonsInteractable(bool value)
        {
            if (startGameButton != null) startGameButton.interactable = value;
            if (exitRoomButton != null) exitRoomButton.interactable = value;
        }

        private static string BuildSignature(long hostUserId, List<RoomPlayerResponse> players)
        {
            var sb = new StringBuilder();
            sb.Append(hostUserId).Append('|');
            foreach (RoomPlayerResponse p in players) sb.Append(p.userId).Append(':').Append(p.nickname).Append(',');
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

        private void ReturnToLobby()
        {
            if (isTransitioning) return;

            BeginTransition();
            RoomSession.Clear();
            SceneLoader.Load(SceneType.Lobby);
        }

        private void BeginTransition()
        {
            isTransitioning = true;
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
            if (pollRoutine == null) pollRoutine = StartCoroutine(PollLoop());
        }

        private static void BindIfEmpty(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null && button.onClick.GetPersistentEventCount() == 0)
                button.onClick.AddListener(action);
        }
    }
}
