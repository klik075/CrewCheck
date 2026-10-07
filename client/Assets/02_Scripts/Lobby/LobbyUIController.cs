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
    ///   4) RoomListArea : RoomItem 프리팹으로 방 목록 표시, Enter → 방 입장 → Room 씬
    ///      비밀방이면 Enter → 비밀번호 팝업(RoomPasswordPopup) → 방 입장 → Room 씬
    ///   5) 로그아웃 버튼 (선택) : 연결하면 세션을 비우고 타이틀 씬으로 이동
    ///   6) FindRoomArea : 방 제목 검색. 입력칸에서 Enter 또는 검색 버튼을 눌렀을 때만 검색한다.
    ///      검색어를 비우고 Enter/버튼 → 전체 목록. 새로고침 버튼은 현재 검색어를 유지한 채 다시 불러온다.
    ///      ClearButton → 검색어를 지우고 전체 목록으로 돌아간다.
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

        [Header("Search (FindRoomArea)")]
        [Tooltip("방 제목 검색 입력칸. Enter를 누르면 검색한다. 비워 두면 검색 기능을 쓰지 않는다.")]
        [SerializeField] private TMP_InputField searchInput;  // FindRoomArea/InputField (TMP)
        [Tooltip("검색 버튼 (돋보기 아이콘 등). 비워 두면 Enter로만 검색한다.")]
        [SerializeField] private Button searchButton;
        [Tooltip("검색어 지우기 버튼. 누르면 입력칸을 비우고 전체 방 목록을 보여 준다. 비워 두면 사용하지 않는다.")]
        [SerializeField] private Button clearButton;          // FindRoomArea/ClearButton

        [Header("Popup")]
        [SerializeField] private CreateRoomPopup createRoomPopup; // Popup/Popup_CreateRoom
        [Tooltip("비밀방 입장 시 비밀번호 입력 팝업. 비워 두면 비밀방에 들어갈 수 없다.")]
        [SerializeField] private RoomPasswordPopup roomPasswordPopup; // Popup/Popup_RoomPassword

        [Header("Status")]
        [SerializeField] private StatusMessageView statusMessage; // Canvas/StatusMessageText

        // 방이 하나도 없을 때 보여주는 안내 문구
        private const string EmptyRoomNotice = "만들어진 방이 없습니다. 방을 만들어 보세요!";

        // 화면에 생성한 방 목록 아이템 (새로고침할 때 지우고 다시 만든다)
        private readonly List<RoomItemView> spawnedItems = new List<RoomItemView>();

        private bool isRefreshing; // 방 목록 요청 중
        private bool isJoining;    // 방 입장 요청 중 (이 동안은 목록 갱신/다른 입장을 막는다)
        private string lobbyNotice; // 로비로 돌아온 이유 (방 목록을 처음 불러온 뒤 한 번 보여 준다)

        // 검색어 최대 길이. 서버 RoomService.MAX_SEARCH_KEYWORD_LENGTH(30)와 맞춘다.
        private const int SearchKeywordMaxLength = 30;

        private string currentKeyword = string.Empty; // 지금 목록에 적용된 검색어 ("" = 전체 목록)
        private bool hasPendingSearch;                // 목록 요청 중에 검색을 눌렀으면, 응답 뒤에 한 번 더 요청한다

        // ------------------------------------------------------------------
        // Unity 생명주기
        // ------------------------------------------------------------------

        private void Awake()
        {
            // 인스펙터 OnClick에 직접 연결한 게 없을 때만 코드로 연결 (두 번 호출 방지)
            BindIfEmpty(createRoomButton, OnCreateRoomClicked);
            BindIfEmpty(refreshButton, OnRefreshClicked);
            BindIfEmpty(logoutButton, OnLogoutClicked);
            BindIfEmpty(searchButton, OnSearchClicked);
            BindIfEmpty(clearButton, OnClearSearchClicked);

            if (searchInput != null)
            {
                // Enter로 제출되려면 한 줄 입력이어야 한다. (MultiLine이면 Enter가 줄바꿈이 된다)
                searchInput.lineType = TMP_InputField.LineType.SingleLine;
                if (searchInput.characterLimit <= 0 || searchInput.characterLimit > SearchKeywordMaxLength) searchInput.characterLimit = SearchKeywordMaxLength;
                searchInput.onSubmit.AddListener(OnSearchSubmitted);
            }
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
            //    (Room 씬에서 로비로 돌아올 때는 Room 씬 쪽에서 먼저 방 나가기 API를 호출한다)
            RoomSession.Clear();
            // 방이 사라졌거나 방에서 제외되어 돌아왔으면 그 이유를 방 목록을 불러온 뒤 보여 준다.
            lobbyNotice = RoomSession.TakeLobbyNotice();

            // 3) 화면 초기 상태
            ShowUserInfo();
            if (createRoomPopup != null) createRoomPopup.Close(); // 씬에서 켜 둔 채 저장했어도 닫고 시작
            if (roomPasswordPopup != null) roomPasswordPopup.Close();
            ClearRoomList();                                      // Content에 놓여 있던 샘플 RoomItem 제거

            // 4) 방 목록 불러오기 (이후에는 새로고침 버튼을 눌렀을 때만 갱신한다)
            RefreshRooms(silent: false);
        }

        private void OnDestroy()
        {
            if (createRoomButton != null) createRoomButton.onClick.RemoveListener(OnCreateRoomClicked);
            if (refreshButton != null) refreshButton.onClick.RemoveListener(OnRefreshClicked);
            if (logoutButton != null) logoutButton.onClick.RemoveListener(OnLogoutClicked);
            if (searchButton != null) searchButton.onClick.RemoveListener(OnSearchClicked);
            if (clearButton != null) clearButton.onClick.RemoveListener(OnClearSearchClicked);
            if (searchInput != null) searchInput.onSubmit.RemoveListener(OnSearchSubmitted);
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

        // FindRoomArea 검색 버튼: 입력칸의 검색어로 검색
        public void OnSearchClicked()
        {
            Search(searchInput != null ? searchInput.text : string.Empty);
        }

        // FindRoomArea ClearButton: 검색어를 지우고 전체 목록으로
        public void OnClearSearchClicked()
        {
            if (isJoining || SceneLoader.IsLoading) return;
            if (searchInput != null) searchInput.text = string.Empty;
            if (string.IsNullOrEmpty(currentKeyword)) return; // 이미 전체 목록이면 입력칸만 비운다. (불필요한 요청 방지)

            Search(string.Empty);
        }

        // FindRoomArea 입력칸에서 Enter
        private void OnSearchSubmitted(string text)
        {
            Search(text);
        }

        /// <summary>
        /// 검색어를 적용하고 목록을 다시 불러온다. 빈 검색어면 전체 목록으로 돌아간다.
        /// 목록 요청이 이미 진행 중이면 그 응답은 버리고, 끝난 직후 새 검색어로 다시 요청한다.
        /// </summary>
        private void Search(string keyword)
        {
            if (isJoining || SceneLoader.IsLoading) return;

            currentKeyword = (keyword ?? string.Empty).Trim();

            if (isRefreshing)
            {
                hasPendingSearch = true;
                return;
            }
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
        /// <param name="silent">true면 "불러오는 중"/"n개를 불러왔습니다" 문구를 띄우지 않는다.
        /// (입장 실패 직후 갱신할 때 에러 메시지를 덮어쓰지 않기 위해 사용)</param>
        private void RefreshRooms(bool silent)
        {
            // 이미 요청 중이거나, 입장 처리 중이거나, 씬 이동 중이면 건너뛴다.
            if (isRefreshing || isJoining || SceneLoader.IsLoading) return;

            isRefreshing = true;
            if (refreshButton != null) refreshButton.interactable = false;
            string keyword = currentKeyword; // 응답이 올 때까지 검색어가 바뀌어도 이 요청의 검색어로 문구를 만든다
            bool searching = !string.IsNullOrEmpty(keyword);
            if (!silent) statusMessage?.ShowInfo(searching ? $"'{keyword}' 검색 중..." : "방 목록을 불러오는 중...", keep: true);

            RoomApi.GetRooms(keyword, result =>
            {
                if (this == null) return; // 응답 전에 씬이 바뀐 경우

                isRefreshing = false;
                if (refreshButton != null) refreshButton.interactable = !isJoining;

                // 요청 중에 검색어가 바뀌었으면 이 응답은 그리지 않고 새 검색어로 다시 요청한다.
                if (hasPendingSearch)
                {
                    hasPendingSearch = false;
                    RefreshRooms(silent: false);
                    return;
                }

                if (!result.success)
                {
                    // 실패는 silent여도 표시한다. (서버가 꺼진 걸 사용자가 알 수 있도록)
                    statusMessage?.ShowError(result.message);
                    return;
                }

                // 입장 요청이 그 사이에 시작됐다면 목록을 바꾸지 않는다. (잠금 상태가 풀리지 않도록)
                if (isJoining) return;

                BuildRoomList(result.data);

                if (!string.IsNullOrEmpty(lobbyNotice))
                {
                    statusMessage?.ShowInfo(lobbyNotice, keep: true); // 로비로 돌아온 이유가 "n개를 불러왔습니다"보다 중요하다
                    lobbyNotice = null;
                }
                else if (result.data.Count == 0)
                    statusMessage?.ShowInfo(searching
                        ? $"'{keyword}'에 해당하는 방이 없습니다."
                        : EmptyRoomNotice, keep: true);  // 방이 없으면 안내 문구 유지
                else if (!silent)
                    statusMessage?.ShowSuccess(searching
                        ? $"'{keyword}' 검색 결과 {result.data.Count}개"
                        : $"방 {result.data.Count}개를 불러왔습니다.");
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

        // ------------------------------------------------------------------
        // 방 입장
        // ------------------------------------------------------------------

        /// <summary>
        /// RoomItem의 Enter 버튼을 눌렀을 때 호출된다.
        ///
        /// 공개방과 비밀방의 차이는 "API 호출 전에 비밀번호 팝업을 거치느냐" 하나뿐이다.
        ///   공개방: Enter → RequestJoin(room, null)
        ///   비밀방: Enter → 비밀번호 팝업 → 확인 → RequestJoin(room, 비밀번호)
        /// 입장 요청과 결과 처리(성공 → Room 씬, 409 → 이미 참가 중 확인, 그 밖 → 에러)는 RequestJoin 한 곳에서 같이 처리한다.
        /// </summary>
        private void JoinRoom(RoomResponse room)
        {
            if (isJoining || SceneLoader.IsLoading) return;

            if (!room.privateRoom)
            {
                RequestJoin(room, null);
                return;
            }

            if (roomPasswordPopup == null)
            {
                Debug.LogError("[Lobby] roomPasswordPopup이 연결되지 않아 비밀방에 입장할 수 없습니다.");
                statusMessage?.ShowError("비밀번호 입력 창을 열 수 없습니다.");
                return;
            }

            // 팝업이 열려 있는 동안은 입장 중으로 보고 목록 갱신/다른 방 입장/방 만들기를 막는다.
            SetJoining(true);
            statusMessage?.ShowInfo($"'{room.title}' 방은 비밀방입니다. 비밀번호를 입력하세요.", keep: true);
            roomPasswordPopup.Open(
                submit: password => RequestJoin(room, password),
                cancel: () =>
                {
                    SetJoining(false);
                    statusMessage?.Clear();
                });
        }

        /// <summary>
        /// 방 입장 API 호출. 공개방/비밀방 공통.
        /// 성공하면 방 정보를 RoomSession에 저장하고 Room 씬으로 이동한다.
        /// </summary>
        /// <param name="password">비밀방 비밀번호. 공개방이면 null (body 없이 요청)</param>
        private void RequestJoin(RoomResponse room, string password)
        {
            if (SceneLoader.IsLoading) return;

            SetJoining(true); // 공개방은 여기서 처음 잠그고, 비밀방은 팝업을 열 때 이미 잠근 상태
            statusMessage?.ShowInfo($"'{room.title}' 방에 입장하는 중...", keep: true);

            RoomApi.JoinRoom(room.id, password, result =>
            {
                if (this == null) return;

                if (result.success)
                {
                    // 팝업을 닫고 이동한다. (씬 이동이 실패해 로비에 남아도 팝업이 잠긴 채로 남지 않도록)
                    ClosePasswordPopup();
                    EnterRoomScene(result.data);
                    return;
                }

                // 비밀번호 틀림(403): 팝업을 닫지 않고 다시 입력받는다. 입장 중 잠금도 그대로 둔다. (횟수 제한 없음)
                if (result.errorCode == RoomErrorCode.WrongRoomPassword && roomPasswordPopup != null && roomPasswordPopup.IsOpen)
                {
                    roomPasswordPopup.ShowWrongPassword(result.message); // 에러 문구는 팝업 안에 표시
                    return;
                }

                // 그 밖의 실패는 비밀번호 문제가 아니므로 팝업을 닫고 기존 처리로 넘긴다.
                ClosePasswordPopup();

                // 409 중에서 "이미 참가 중"인 경우는 실패가 아니다.
                // (서버 재시작 후 같은 방에 다시 들어가는 경우 등 - 작업정리 문서 남은 과제 3번)
                // 방 상세를 조회해서 내가 참가자 목록에 있으면 그대로 Room 씬으로 들어간다.
                // 서버는 "이미 참가 중"을 비밀번호 검사보다 먼저 하므로 비밀방이어도 비밀번호 없이 이 경로로 온다.
                if (result.IsConflict)
                {
                    CheckAlreadyJoined(room, result.message);
                    return;
                }

                OnJoinFailed(result.message);
            });
        }

        /// <summary>비밀번호 팝업이 열려 있으면 닫는다. (취소 콜백은 부르지 않음)</summary>
        private void ClosePasswordPopup()
        {
            if (roomPasswordPopup != null && roomPasswordPopup.IsOpen) roomPasswordPopup.Close();
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
                    GoToRoomScene(detail.data.title);
                    return;
                }

                // 정말로 입장할 수 없는 경우 (정원 초과, 게임 시작됨 등): 서버 메시지를 보여준다.
                OnJoinFailed(conflictMessage);
            });
        }

        private void EnterRoomScene(RoomResponse room)
        {
            RoomSession.Set(room);
            Debug.Log($"[Lobby] 방 입장 완료: #{room.id} {room.title} ({room.currentPlayers}/{room.maxPlayers})");
            GoToRoomScene(room.title);
        }

        private void GoToRoomScene(string roomTitle)
        {
            statusMessage?.ShowSuccess($"'{roomTitle}' 방에 입장했습니다.", keep: true);
            ChatNotice.Post($"'{roomTitle}' 방에 입장했습니다.");

            // 씬 이동을 시작하지 못하면(Build Settings 누락 등) 잠금을 풀어서 다시 시도할 수 있게 한다.
            if (!SceneLoader.Load(SceneType.Room))
                OnJoinFailed("Room 씬으로 이동하지 못했습니다. (Build Settings 확인)");
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
            if (searchInput != null) searchInput.interactable = !value;
            if (searchButton != null) searchButton.interactable = !value;
            if (clearButton != null) clearButton.interactable = !value;
        }

        // ------------------------------------------------------------------
        // 유틸
        // ------------------------------------------------------------------

        private static void BindIfEmpty(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null && button.onClick.GetPersistentEventCount() == 0) button.onClick.AddListener(action);
        }
    }
}
