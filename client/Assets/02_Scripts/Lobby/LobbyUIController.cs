using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Common;
using WhoisntCitizen.Chat; // ChatNotice: 내 화면 전용 안내 (채팅창 + 콘솔)

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// Lobby 씬 전체 UI 담당 (Manager 오브젝트에 부착).
    ///
    /// 기능
    ///   1) UserInfoArea : Name = 로그인 아이디(username), ID = userId 표시
    ///   2) Button_CreatRoom : 방 만들기 팝업(CreateRoomPopup) 열기
    ///   3) Button_Refresh : 방 목록 새로고침
    ///   4) RoomListArea : RoomItem 프리팹으로 방 목록 표시, Enter → 방 입장 → Game 씬
    ///   5) 자동 새로고침 : autoRefreshInterval초마다 조용히 목록 갱신 (팝업이 열려 있거나 요청 중이면 건너뜀)
    ///   6) 로그아웃 버튼 (선택) : 연결하면 세션을 비우고 타이틀 씬으로 이동
    ///
    /// 진입 조건
    ///   로그인하지 않은 상태(토큰 없음)로 이 씬에 들어오면 타이틀 씬으로 돌려보낸다.
    /// </summary>
    public class LobbyUIController : MonoBehaviour
    {
        [Header("User Info (UserInfoArea)")]
        [SerializeField] private TMP_Text userNameValueText; // Area_UserCreate/Text (TMP) (1)  → username
        [SerializeField] private TMP_Text userIdValueText;   // Area_ID/Text (TMP) (1)          → userId

        [Header("Buttons")]
        [SerializeField] private Button createRoomButton;    // Buttons/Button_CreatRoom
        [SerializeField] private Button refreshButton;       // Buttons/Button_Refresh
        [Tooltip("(선택) 로그아웃 버튼. 비워 두면 사용하지 않는다.")]
        [SerializeField] private Button logoutButton;

        [Header("Room List (RoomListArea)")]
        [SerializeField] private Transform roomListContent;  // Scroll View/Viewport/Content
        [SerializeField] private RoomItemView roomItemPrefab; // 03_Prefabs/RoomItem (RoomItemView 부착 필요)

        [Header("Popup")]
        [SerializeField] private CreateRoomPopup createRoomPopup; // Popup/Popup_CreateRoom

        [Header("Status")]
        [SerializeField] private StatusMessageView statusMessage; // Canvas/StatusMessageText

        [Header("Options")]
        [Tooltip("방 목록 자동 새로고침 간격(초). 0이면 자동 새로고침을 끈다.")]
        [SerializeField] private float autoRefreshInterval = 5f;

        // 방이 하나도 없을 때 보여주는 안내 문구
        private const string EmptyRoomNotice = "만들어진 방이 없습니다. 방을 만들어 보세요!";

        // 화면에 생성한 방 목록 아이템 (새로고침할 때 지우고 다시 만든다)
        private readonly List<RoomItemView> spawnedItems = new List<RoomItemView>();

        private bool isRefreshing; // 방 목록 요청 중
        private bool isJoining;    // 방 입장 요청 중 (이 동안은 목록 갱신/다른 입장을 막는다)
        private Coroutine autoRefreshRoutine;

        // ------------------------------------------------------------------
        // Unity 생명주기
        // ------------------------------------------------------------------

        private void Awake()
        {
            // 인스펙터 OnClick에 직접 연결한 게 없을 때만 코드로 연결 (두 번 호출 방지)
            BindIfEmpty(createRoomButton, OnCreateRoomClicked);
            BindIfEmpty(refreshButton, OnRefreshClicked);
            BindIfEmpty(logoutButton, OnLogoutClicked);
        }

        private void Start()
        {
            // 1) 로그인 확인: 토큰이 없으면 이 씬에 있을 수 없으므로 타이틀로 보낸다.
            if (!AuthSession.IsAuthenticated)
            {
                Debug.LogWarning("[Lobby] 로그인 정보가 없어 타이틀 씬으로 이동합니다.");
                SceneLoader.Load(SceneType.Title);
                return;
            }

            // 2) 로비에 들어왔다는 것은 어떤 방에도 들어가 있지 않다는 뜻이므로 방 세션을 비운다.
            //    (Game 씬에서 로비로 돌아올 때는 Game 씬 쪽에서 먼저 방 나가기 API를 호출해야 한다)
            RoomSession.Clear();

            // 3) 화면 초기 상태
            ShowUserInfo();
            if (createRoomPopup != null) createRoomPopup.Close(); // 씬에서 켜 둔 채 저장했어도 닫고 시작
            ClearRoomList();                                      // Content에 놓여 있던 샘플 RoomItem 제거

            // 4) 방 목록을 불러오고 자동 새로고침 시작
            RefreshRooms(silent: false);
            if (autoRefreshInterval > 0f) autoRefreshRoutine = StartCoroutine(AutoRefreshLoop());
        }

        private void OnDestroy()
        {
            if (createRoomButton != null) createRoomButton.onClick.RemoveListener(OnCreateRoomClicked);
            if (refreshButton != null) refreshButton.onClick.RemoveListener(OnRefreshClicked);
            if (logoutButton != null) logoutButton.onClick.RemoveListener(OnLogoutClicked);
        }

        // ------------------------------------------------------------------
        // 버튼 핸들러 (인스펙터 OnClick에 직접 연결해도 된다)
        // ------------------------------------------------------------------

        // Button_CreatRoom: 방 만들기 팝업 열기
        public void OnCreateRoomClicked()
        {
            if (isJoining || SceneLoader.IsLoading || createRoomPopup == null) return;
            statusMessage?.Clear();
            createRoomPopup.Open();
        }

        // Button_Refresh: 방 목록 새로고침
        public void OnRefreshClicked()
        {
            RefreshRooms(silent: false);
        }

        // (선택) 로그아웃
        public void OnLogoutClicked()
        {
            if (isJoining || SceneLoader.IsLoading) return;

            AuthSession.Clear();
            ChatNotice.Post("로그아웃되었습니다.");
            RoomSession.Clear();
            SceneLoader.Load(SceneType.Title);
        }

        // ------------------------------------------------------------------
        // 유저 정보
        // ------------------------------------------------------------------

        private void ShowUserInfo()
        {
            if (userNameValueText != null) userNameValueText.text = AuthSession.Username;
            if (userIdValueText != null) userIdValueText.text = AuthSession.UserId.ToString();
        }

        // ------------------------------------------------------------------
        // 방 목록
        // ------------------------------------------------------------------

        /// <summary>
        /// 서버에서 방 목록을 받아 화면을 다시 그린다.
        /// </summary>
        /// <param name="silent">true면 자동 새로고침용: "불러오는 중" 같은 안내 문구를 띄우지 않는다.</param>
        private void RefreshRooms(bool silent)
        {
            // 이미 요청 중이거나, 입장 처리 중이거나, 씬 이동 중이면 건너뛴다.
            if (isRefreshing || isJoining || SceneLoader.IsLoading) return;

            isRefreshing = true;
            if (refreshButton != null) refreshButton.interactable = false;
            if (!silent) statusMessage?.ShowInfo("방 목록을 불러오는 중...", keep: true);

            RoomApi.GetRooms(result =>
            {
                if (this == null) return; // 응답 전에 씬이 바뀐 경우

                isRefreshing = false;
                if (refreshButton != null) refreshButton.interactable = true;

                if (!result.success)
                {
                    // 자동 새로고침 실패도 표시한다. (서버가 꺼진 걸 사용자가 알 수 있도록)
                    statusMessage?.ShowError(result.message);
                    return;
                }

                // 입장 요청이 그 사이에 시작됐다면 목록을 바꾸지 않는다. (잠금 상태가 풀리지 않도록)
                if (isJoining) return;

                BuildRoomList(result.data);

                if (result.data.Count == 0)
                    statusMessage?.ShowInfo(EmptyRoomNotice, keep: true);  // 방이 없으면 안내 문구 유지
                else if (!silent)
                    statusMessage?.ShowSuccess($"방 {result.data.Count}개를 불러왔습니다.");
                else
                    ClearEmptyNoticeIfShown(); // 자동 새로고침으로 방이 생겼으면 "방 없음" 문구만 지운다
            });
        }

        /// <summary>
        /// 기존 아이템을 모두 지우고 방 목록을 새로 만든다.
        /// 정렬: 입장 가능한 방 → 정원 찬 방 → 게임 중인 방, 같은 그룹 안에서는 최근에 만든 방(id 큰 순)이 위.
        /// </summary>
        private void BuildRoomList(List<RoomResponse> rooms)
        {
            ClearRoomList();

            if (roomItemPrefab == null || roomListContent == null)
            {
                Debug.LogError("[Lobby] roomItemPrefab 또는 roomListContent가 연결되지 않았습니다.");
                return;
            }

            rooms.Sort((a, b) =>
            {
                int group = SortGroup(a).CompareTo(SortGroup(b));
                return group != 0 ? group : b.id.CompareTo(a.id);
            });

            foreach (RoomResponse room in rooms)
            {
                RoomItemView item = Instantiate(roomItemPrefab, roomListContent);
                item.name = $"RoomItem_{room.id}";
                item.Bind(room, JoinRoom);
                spawnedItems.Add(item);
            }
        }

        // 정렬 그룹: 0 = 입장 가능, 1 = 정원 참, 2 = 게임 중
        private static int SortGroup(RoomResponse room)
        {
            if (room.IsInGame) return 2;
            if (room.IsFull) return 1;
            return 0;
        }

        /// <summary>
        /// Content 아래의 아이템을 모두 지운다.
        /// spawnedItems뿐 아니라 씬에 미리 놓여 있던 샘플 RoomItem 같은 자식도 함께 지운다.
        /// </summary>
        private void ClearRoomList()
        {
            spawnedItems.Clear();
            if (roomListContent == null) return;

            for (int i = roomListContent.childCount - 1; i >= 0; i--)
                Destroy(roomListContent.GetChild(i).gameObject);
        }

        // 방이 다시 생겼을 때 "만들어진 방이 없습니다" 문구가 계속 남아 있지 않도록 지운다.
        // 다른 메시지(예: 입장 실패 에러)가 떠 있으면 건드리지 않는다.
        private void ClearEmptyNoticeIfShown()
        {
            if (statusMessage != null && statusMessage.CurrentMessage == EmptyRoomNotice)
                statusMessage.Clear();
        }

        /// <summary>autoRefreshInterval초마다 조용히 방 목록을 갱신한다.</summary>
        private IEnumerator AutoRefreshLoop()
        {
            var wait = new WaitForSeconds(autoRefreshInterval);
            while (true)
            {
                yield return wait;

                // 팝업에서 입력 중일 때는 목록을 다시 그리지 않는다.
                bool popupOpen = createRoomPopup != null && createRoomPopup.IsOpen;
                if (!popupOpen) RefreshRooms(silent: true);
            }
        }

        // ------------------------------------------------------------------
        // 방 입장
        // ------------------------------------------------------------------

        /// <summary>
        /// RoomItem의 Enter 버튼을 눌렀을 때 호출된다.
        /// 성공하면 방 정보를 RoomSession에 저장하고 Game 씬으로 이동한다.
        /// </summary>
        private void JoinRoom(RoomResponse room)
        {
            if (isJoining || SceneLoader.IsLoading) return;

            SetJoining(true);
            statusMessage?.ShowInfo($"'{room.title}' 방에 입장하는 중...", keep: true);

            RoomApi.JoinRoom(room.id, result =>
            {
                if (this == null) return;

                if (result.success)
                {
                    EnterGameScene(result.data);
                    return;
                }

                // 409 중에서 "이미 참가 중"인 경우는 실패가 아니다.
                // (서버 재시작 후 같은 방에 다시 들어가는 경우 등 - 작업정리 문서 남은 과제 3번)
                // 방 상세를 조회해서 내가 참가자 목록에 있으면 그대로 Game 씬으로 들어간다.
                if (result.IsConflict)
                {
                    CheckAlreadyJoined(room, result.message);
                    return;
                }

                OnJoinFailed(result.message);
            });
        }

        /// <summary>입장이 409로 실패했을 때, 이미 그 방의 참가자인지 확인한다.</summary>
        private void CheckAlreadyJoined(RoomResponse room, string conflictMessage)
        {
            RoomApi.GetRoom(room.id, detail =>
            {
                if (this == null) return;

                if (detail.success && detail.data.HasPlayer(AuthSession.UserId) && !detail.data.IsInGame)
                {
                    Debug.Log($"[Lobby] 이미 방 #{room.id}의 참가자라 그대로 입장합니다.");
                    RoomSession.Set(detail.data);
                    GoToGameScene(detail.data.title);
                    return;
                }

                // 정말로 입장할 수 없는 경우 (정원 초과, 게임 시작됨 등): 서버 메시지를 보여준다.
                OnJoinFailed(conflictMessage);
            });
        }

        private void EnterGameScene(RoomResponse room)
        {
            RoomSession.Set(room);
            Debug.Log($"[Lobby] 방 입장 완료: #{room.id} {room.title} ({room.currentPlayers}/{room.maxPlayers})");
            GoToGameScene(room.title);
        }

        private void GoToGameScene(string roomTitle)
        {
            statusMessage?.ShowSuccess($"'{roomTitle}' 방에 입장했습니다.", keep: true);
            ChatNotice.Post($"'{roomTitle}' 방에 입장했습니다.");

            // 씬 이동을 시작하지 못하면(Build Settings 누락 등) 잠금을 풀어서 다시 시도할 수 있게 한다.
            if (!SceneLoader.Load(SceneType.Game))
                OnJoinFailed("Game 씬으로 이동하지 못했습니다. (Build Settings 확인)");
        }

        private void OnJoinFailed(string message)
        {
            SetJoining(false);
            statusMessage?.ShowError(message); // 몇 초 뒤 자동으로 사라짐

            // 방 상태가 바뀌었을 가능성이 높으므로(정원 참, 게임 시작 등) 목록을 다시 받는다.
            RefreshRooms(silent: true);
        }

        /// <summary>입장 요청 중에는 모든 방 버튼과 상단 버튼을 잠근다.</summary>
        private void SetJoining(bool value)
        {
            isJoining = value;
            foreach (RoomItemView item in spawnedItems)
                if (item != null) item.SetLocked(value);

            if (createRoomButton != null) createRoomButton.interactable = !value;
            if (refreshButton != null) refreshButton.interactable = !value && !isRefreshing;
        }

        // ------------------------------------------------------------------
        // 유틸
        // ------------------------------------------------------------------

        private static void BindIfEmpty(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null && button.onClick.GetPersistentEventCount() == 0)
                button.onClick.AddListener(action);
        }
    }
}
