using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Common;
using WhoisntCitizen.Profile;

namespace WhoisntCitizen.Friend
{
    /// <summary>
    /// 친구 팝업 전체 UI 담당 (친구 팝업 루트가 아닌, 항상 켜져 있는 오브젝트에 부착. 예: Manager)
    ///
    /// 탭
    ///   내 친구   : GET /api/friends          → ScrollView(FriendItemView) / 닉네임, 전적, 삭제 버튼
    ///   받은 요청 : GET /api/friends/requests → ScrollView(FriendRequestItemView) / 수락, 거절 버튼
    ///   친구 추가 : 닉네임 검색 GET /api/users/search → 결과 표시 → POST /api/friends/request
    ///
    /// 열기: openButton 클릭 또는 Open() 호출. 탭을 바꿀 때마다 해당 목록을 새로 불러온다.
    /// 삭제/거절/수락/요청 중에는 모든 버튼을 잠가 중복 요청을 막는다.
    /// </summary>
    public class FriendUIController : MonoBehaviour
    {
        private enum Tab { Friends, Requests, Add }

        [Header("Popup")]
        [Tooltip("친구 팝업을 여는 버튼 (예: 프로필 팝업 안의 친구 버튼). 비워 두면 Open()을 직접 호출한다.")]
        [SerializeField] private Button openButton;
        [Tooltip("친구 팝업 루트 오브젝트. 시작할 때 닫힌다.")]
        [SerializeField] private GameObject friendPopup;
        [SerializeField] private Button closeButton;

        [Header("Tabs")]
        [SerializeField] private Button friendsTabButton;
        [SerializeField] private Button requestsTabButton;
        [SerializeField] private Button addTabButton;
        [SerializeField] private GameObject friendsPanel;
        [SerializeField] private GameObject requestsPanel;
        [SerializeField] private GameObject addPanel;
        [Tooltip("(선택) 받은 요청 탭 버튼 옆에 요청 개수를 표시할 텍스트. 0이면 숨긴다.")]
        [SerializeField] private TMP_Text requestCountText;

        [Header("Friends Tab")]
        [SerializeField] private Transform friendListContent;     // Scroll View/Viewport/Content
        [SerializeField] private FriendItemView friendItemPrefab;
        [Tooltip("(선택) 친구가 없을 때 보여 줄 안내 텍스트 오브젝트")]
        [SerializeField] private GameObject friendEmptyNotice;

        [Header("Requests Tab")]
        [SerializeField] private Transform requestListContent;    // Scroll View/Viewport/Content
        [SerializeField] private FriendRequestItemView requestItemPrefab;
        [Tooltip("(선택) 받은 요청이 없을 때 보여 줄 안내 텍스트 오브젝트")]
        [SerializeField] private GameObject requestEmptyNotice;

        [Header("Add Tab")]
        [SerializeField] private TMP_InputField searchInput;
        [SerializeField] private Button searchButton;
        [Tooltip("검색 결과 영역. 검색 전/실패 시에는 숨긴다.")]
        [SerializeField] private GameObject resultRoot;
        [SerializeField] private TMP_Text resultNicknameText;
        [Tooltip("검색된 유저의 레벨/전적. 예) Lv.3 · 12전 7승 · 58.3%")]
        [SerializeField] private TMP_Text resultInfoText;
        [SerializeField] private Button sendRequestButton;

        [Header("Status")]
        [SerializeField] private StatusMessageView statusMessage; // 팝업 안의 메시지 텍스트

        [Header("Rules")]
        [Tooltip("닉네임 검색어 최대 글자 수")]
        [SerializeField] private int searchMaxLength = 10;

        private readonly List<FriendItemView> friendItems = new List<FriendItemView>();
        private readonly List<FriendRequestItemView> requestItems = new List<FriendRequestItemView>();

        private Tab currentTab = Tab.Friends;
        private bool isBusy;                     // 수락/거절/삭제/요청/검색 중 (모든 버튼 잠금)
        private int listVersion;                 // 탭을 빨리 바꿀 때 늦게 온 응답을 버리기 위한 번호
        private UserProfileResponse searchResult; // 지금 결과 영역에 표시 중인 유저

        public bool IsOpen => friendPopup != null && friendPopup.activeSelf;

        // ------------------------------------------------------------------
        // Unity 생명주기
        // ------------------------------------------------------------------

        private void Awake()
        {
            BindIfEmpty(openButton, Open);
            BindIfEmpty(closeButton, Close);
            BindIfEmpty(friendsTabButton, () => SwitchTab(Tab.Friends));
            BindIfEmpty(requestsTabButton, () => SwitchTab(Tab.Requests));
            BindIfEmpty(addTabButton, () => SwitchTab(Tab.Add));
            BindIfEmpty(searchButton, OnSearchClicked);
            BindIfEmpty(sendRequestButton, OnSendRequestClicked);

            if (searchInput != null)
            {
                searchInput.lineType = TMP_InputField.LineType.SingleLine;
                if (searchInput.characterLimit <= 0 || searchInput.characterLimit > searchMaxLength)
                    searchInput.characterLimit = searchMaxLength;
                searchInput.onSubmit.AddListener(_ => OnSearchClicked());
            }
        }

        private void Start()
        {
            if (friendPopup != null) friendPopup.SetActive(false); // 씬에 켜 둔 채 저장했어도 닫고 시작
        }

        // ------------------------------------------------------------------
        // 열기 / 닫기 / 탭
        // ------------------------------------------------------------------

        public void Open()
        {
            if (SceneLoader.IsLoading || friendPopup == null) return;

            friendPopup.SetActive(true);
            statusMessage?.Clear();
            ClearSearch();
            SwitchTab(Tab.Friends);
            RefreshRequestCount(); // 다른 탭에 있어도 받은 요청 개수 배지는 보여 준다
        }

        public void Close()
        {
            if (isBusy) return;
            statusMessage?.Clear();
            if (friendPopup != null) friendPopup.SetActive(false);
        }

        private void SwitchTab(Tab tab)
        {
            if (isBusy) return;

            currentTab = tab;
            statusMessage?.Clear();

            if (friendsPanel != null) friendsPanel.SetActive(tab == Tab.Friends);
            if (requestsPanel != null) requestsPanel.SetActive(tab == Tab.Requests);
            if (addPanel != null) addPanel.SetActive(tab == Tab.Add);

            // 선택된 탭 버튼은 눌린 상태(비활성)로 보여 준다.
            if (friendsTabButton != null) friendsTabButton.interactable = tab != Tab.Friends;
            if (requestsTabButton != null) requestsTabButton.interactable = tab != Tab.Requests;
            if (addTabButton != null) addTabButton.interactable = tab != Tab.Add;

            switch (tab)
            {
                case Tab.Friends: LoadFriends(); break;
                case Tab.Requests: LoadRequests(); break;
                case Tab.Add:
                    if (searchInput != null)
                    {
                        searchInput.Select();
                        searchInput.ActivateInputField();
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------
        // 내 친구 탭
        // ------------------------------------------------------------------

        private void LoadFriends()
        {
            int version = ++listVersion;
            statusMessage?.ShowInfo("친구 목록을 불러오는 중...", keep: true);

            FriendApi.GetFriends(result =>
            {
                if (this == null || version != listVersion) return;

                if (!result.success)
                {
                    statusMessage?.ShowError(result.message);
                    return;
                }

                statusMessage?.Clear();
                BuildFriendList(result.data);
            });
        }

        private void BuildFriendList(List<FriendResponse> friends)
        {
            ClearContent(friendListContent);
            friendItems.Clear();

            if (friendItemPrefab == null || friendListContent == null)
            {
                Debug.LogError("[Friend] friendItemPrefab 또는 friendListContent가 연결되지 않았습니다.");
                return;
            }

            foreach (FriendResponse friend in friends)
            {
                FriendItemView item = Instantiate(friendItemPrefab, friendListContent);
                item.name = $"FriendItem_{friend.friendshipId}";
                item.Bind(friend, OnRemoveFriend);
                friendItems.Add(item);
            }

            if (friendEmptyNotice != null) friendEmptyNotice.SetActive(friends.Count == 0);
        }

        // 친구 삭제 버튼
        private void OnRemoveFriend(FriendResponse friend)
        {
            if (isBusy) return;

            SetBusy(true);
            statusMessage?.ShowInfo("친구를 삭제하는 중...", keep: true);

            FriendApi.Remove(friend.friendshipId, result =>
            {
                if (this == null) return;

                SetBusy(false);
                if (!result.success)
                {
                    statusMessage?.ShowError(result.message);
                    return;
                }

                statusMessage?.ShowSuccess($"{friend.nickname}님을 친구에서 삭제했습니다.");
                ReloadCurrentListKeepingMessage();
            });
        }

        // ------------------------------------------------------------------
        // 받은 요청 탭
        // ------------------------------------------------------------------

        private void LoadRequests()
        {
            int version = ++listVersion;
            statusMessage?.ShowInfo("받은 요청을 불러오는 중...", keep: true);

            FriendApi.GetRequests(result =>
            {
                if (this == null || version != listVersion) return;

                if (!result.success)
                {
                    statusMessage?.ShowError(result.message);
                    return;
                }

                statusMessage?.Clear();
                BuildRequestList(result.data);
            });
        }

        private void BuildRequestList(List<FriendRequestResponse> requests)
        {
            ClearContent(requestListContent);
            requestItems.Clear();
            SetRequestCount(requests.Count);

            if (requestItemPrefab == null || requestListContent == null)
            {
                Debug.LogError("[Friend] requestItemPrefab 또는 requestListContent가 연결되지 않았습니다.");
                return;
            }

            foreach (FriendRequestResponse request in requests)
            {
                FriendRequestItemView item = Instantiate(requestItemPrefab, requestListContent);
                item.name = $"FriendRequestItem_{request.friendshipId}";
                item.Bind(request, OnAcceptRequest, OnRejectRequest);
                requestItems.Add(item);
            }

            if (requestEmptyNotice != null) requestEmptyNotice.SetActive(requests.Count == 0);
        }

        // 수락 버튼
        private void OnAcceptRequest(FriendRequestResponse request)
        {
            if (isBusy) return;

            SetBusy(true);
            statusMessage?.ShowInfo("요청을 수락하는 중...", keep: true);

            FriendApi.Accept(request.friendshipId, result =>
            {
                if (this == null) return;

                SetBusy(false);
                if (!result.success)
                {
                    statusMessage?.ShowError(result.message);
                    return;
                }

                statusMessage?.ShowSuccess($"{request.requesterNickname}님과 친구가 되었습니다.");
                ReloadCurrentListKeepingMessage();
            });
        }

        // 거절 버튼 (서버에서는 친구 삭제와 같은 DELETE)
        private void OnRejectRequest(FriendRequestResponse request)
        {
            if (isBusy) return;

            SetBusy(true);
            statusMessage?.ShowInfo("요청을 거절하는 중...", keep: true);

            FriendApi.Remove(request.friendshipId, result =>
            {
                if (this == null) return;

                SetBusy(false);
                if (!result.success)
                {
                    statusMessage?.ShowError(result.message);
                    return;
                }

                statusMessage?.ShowSuccess($"{request.requesterNickname}님의 요청을 거절했습니다.");
                ReloadCurrentListKeepingMessage();
            });
        }

        // ------------------------------------------------------------------
        // 친구 추가 탭
        // ------------------------------------------------------------------

        // 검색 버튼 (또는 입력칸에서 Enter)
        public void OnSearchClicked()
        {
            if (isBusy || SceneLoader.IsLoading) return;

            string nickname = searchInput != null ? searchInput.text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(nickname))
            {
                statusMessage?.ShowError("검색할 닉네임을 입력하세요.");
                return;
            }

            ClearSearch();
            SetBusy(true);
            statusMessage?.ShowInfo("검색 중...", keep: true);

            UserApi.SearchByNickname(nickname, result =>
            {
                if (this == null) return;

                SetBusy(false);
                if (!result.success)
                {
                    statusMessage?.ShowError(result.message); // 없는 닉네임(404) 등 서버 메시지
                    return;
                }

                UserProfileResponse found = PickResult(result.data, nickname);
                if (found == null)
                {
                    statusMessage?.ShowError($"'{nickname}' 유저를 찾을 수 없습니다.");
                    return;
                }

                statusMessage?.Clear();
                ShowSearchResult(found);
            });
        }

        // 검색 결과 중 닉네임이 정확히 같은 유저를 우선하고, 없으면 첫 번째 유저
        private static UserProfileResponse PickResult(List<UserProfileResponse> users, string nickname)
        {
            if (users == null || users.Count == 0) return null;
            foreach (UserProfileResponse user in users)
                if (user.nickname == nickname) return user;
            return users[0];
        }

        private void ShowSearchResult(UserProfileResponse user)
        {
            searchResult = user;
            if (resultRoot != null) resultRoot.SetActive(true);
            if (resultNicknameText != null) resultNicknameText.text = user.nickname;
            if (resultInfoText != null)
                resultInfoText.text = $"Lv.{user.level} · {user.playCount}전 {user.winCount}승 · {user.WinRatePercent:0.#}%";

            // 나 자신에게는 요청을 보낼 수 없다.
            bool isMe = user.userId == AuthSession.UserId;
            if (sendRequestButton != null) sendRequestButton.interactable = !isMe && !isBusy;
            if (isMe) statusMessage?.ShowInfo("나 자신에게는 친구 요청을 보낼 수 없습니다.");
        }

        private void ClearSearch()
        {
            searchResult = null;
            if (resultRoot != null) resultRoot.SetActive(false);
        }

        // 친구 요청 보내기 버튼
        public void OnSendRequestClicked()
        {
            if (isBusy || searchResult == null || SceneLoader.IsLoading) return;

            UserProfileResponse target = searchResult;
            SetBusy(true);
            statusMessage?.ShowInfo("친구 요청을 보내는 중...", keep: true);

            FriendApi.SendRequest(target.userId, result =>
            {
                if (this == null) return;

                SetBusy(false);
                if (!result.success)
                {
                    statusMessage?.ShowError(result.message); // 이미 친구/이미 요청함(409) 등 서버 메시지
                    return;
                }

                statusMessage?.ShowSuccess($"{target.nickname}님에게 친구 요청을 보냈습니다.");
                if (searchInput != null) searchInput.text = string.Empty;
                ClearSearch(); // 같은 사람에게 연속으로 보내지 않도록 결과를 닫는다
            });
        }

        // ------------------------------------------------------------------
        // 공통
        // ------------------------------------------------------------------

        // 처리 직후 목록을 다시 받는다. (목록 갱신은 상태 메시지를 건드리지 않아 방금 띄운 성공 메시지가 유지된다)
        private void ReloadCurrentListKeepingMessage()
        {
            int version = ++listVersion;

            if (currentTab == Tab.Requests)
            {
                FriendApi.GetRequests(result =>
                {
                    if (this == null || version != listVersion || !result.success) return;
                    BuildRequestList(result.data);
                });
            }
            else
            {
                FriendApi.GetFriends(result =>
                {
                    if (this == null || version != listVersion || !result.success) return;
                    BuildFriendList(result.data);
                });
                RefreshRequestCount();
            }
        }

        // 받은 요청 개수 배지만 갱신 (requestCountText가 있을 때만 요청한다)
        private void RefreshRequestCount()
        {
            if (requestCountText == null) return;

            FriendApi.GetRequests(result =>
            {
                if (this == null || !result.success) return;
                SetRequestCount(result.data.Count);
            });
        }

        private void SetRequestCount(int count)
        {
            if (requestCountText == null) return;
            requestCountText.gameObject.SetActive(count > 0);
            requestCountText.text = count.ToString();
        }

        // 요청 중에는 모든 버튼을 잠근다.
        private void SetBusy(bool value)
        {
            isBusy = value;

            foreach (FriendItemView item in friendItems) if (item != null) item.SetLocked(value);
            foreach (FriendRequestItemView item in requestItems) if (item != null) item.SetLocked(value);

            if (closeButton != null) closeButton.interactable = !value;
            if (searchButton != null) searchButton.interactable = !value;
            if (searchInput != null) searchInput.interactable = !value;
            if (sendRequestButton != null)
                sendRequestButton.interactable = !value && searchResult != null && searchResult.userId != AuthSession.UserId;

            // 탭 버튼: 선택된 탭은 계속 비활성, 나머지는 요청 중에만 잠근다.
            if (friendsTabButton != null) friendsTabButton.interactable = !value && currentTab != Tab.Friends;
            if (requestsTabButton != null) requestsTabButton.interactable = !value && currentTab != Tab.Requests;
            if (addTabButton != null) addTabButton.interactable = !value && currentTab != Tab.Add;
        }

        private static void ClearContent(Transform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);
        }

        private static void BindIfEmpty(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null && button.onClick.GetPersistentEventCount() == 0) button.onClick.AddListener(action);
        }
    }
}
