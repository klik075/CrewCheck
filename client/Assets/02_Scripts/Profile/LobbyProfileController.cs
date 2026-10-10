using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Common;

namespace WhoisntCitizen.Profile
{
    /// <summary>
    /// 로비 프로필 표시 + 내 프로필 팝업 담당 (Manager 오브젝트 등 아무 곳에나 부착).
    ///
    /// 동작
    ///   1) 로비 씬 시작 시 GET /api/users/me 로 내 프로필을 불러와 캐싱(Current)한다.
    ///   2) Canvas/SafeArea/TopBar/Profile_Button 에 닉네임/레벨을 표시한다.
    ///      (불러오기 전·실패 시에는 로그인 때 받은 닉네임만 표시)
    ///   3) Profile_Button 클릭 → 내 프로필 팝업 열기 (닉네임, 레벨, 골드, 전적, 승률, 닉네임 변경)
    ///   4) 닉네임 변경 성공 → 프로필 재조회 → 버튼/팝업/AuthSession 닉네임 갱신
    ///
    /// 인스펙터 연결
    ///   Profile Button      ← Profile_Button 오브젝트를 그대로 드래그 (Button 컴포넌트가 이미 있음)
    ///   Profile Button Text ← Profile_Button 자식의 TMP 텍스트 (비워 두면 자식에서 자동으로 찾는다)
    /// </summary>
    public class LobbyProfileController : MonoBehaviour
    {
        /// <summary>내 프로필 캐시. 로비에서 한 번 불러오면 다른 UI(친구 팝업 등)도 같이 쓴다. 불러오기 전에는 null.</summary>
        public static UserProfileResponse Current { get; private set; }

        /// <summary>Current가 갱신될 때마다 호출된다.</summary>
        public static event Action<UserProfileResponse> ProfileChanged;

        [Header("Profile Button (TopBar/Profile_Button)")]
        [Tooltip("Canvas/SafeArea/TopBar/Profile_Button 을 그대로 드래그한다.")]
        [SerializeField] private Button profileButton;
        [Tooltip("버튼에 닉네임/레벨을 표시할 TMP 텍스트. 비워 두면 profileButton 자식에서 첫 번째 TMP_Text를 찾는다.")]
        [SerializeField] private TMP_Text profileButtonText;
        [Tooltip("버튼 표시 형식. {nickname} {level} 을 쓸 수 있다.")]
        [SerializeField] private string buttonFormat = "{nickname}\nLv.{level}";

        [Header("Profile Popup")]
        [Tooltip("내 프로필 팝업 루트 오브젝트. 시작할 때 닫힌다.")]
        [SerializeField] private GameObject profilePopup;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text nicknameText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text goldText;
        [Tooltip("전적. 예) 12전 7승 5패")]
        [SerializeField] private TMP_Text recordText;
        [Tooltip("승률. 예) 58.3%")]
        [SerializeField] private TMP_Text winRateText;

        [Header("Nickname Change")]
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private Button changeNicknameButton;

        [Header("Status")]
        [Tooltip("팝업 안의 메시지 텍스트")]
        [SerializeField] private StatusMessageView statusMessage;

        [Header("Rules")]
        [Tooltip("닉네임 최소/최대 글자 수 (클라이언트 1차 검증. 최종 검증은 서버)")]
        [SerializeField] private int nicknameMinLength = 2;
        [SerializeField] private int nicknameMaxLength = 10;

        private bool isLoading;     // 프로필 조회 중
        private Action<bool> pendingDone; // 조회 중에 들어온 LoadProfile 호출의 콜백 (같은 응답으로 함께 완료한다)
        private bool isChanging;    // 닉네임 변경 중

        private void Awake()
        {
            // 인스펙터 OnClick에 직접 연결한 게 없을 때만 코드로 연결 (두 번 호출 방지)
            BindIfEmpty(profileButton, OnProfileButtonClicked);
            BindIfEmpty(closeButton, ClosePopup);
            BindIfEmpty(changeNicknameButton, OnChangeNicknameClicked);

            if (profileButtonText == null && profileButton != null)
                profileButtonText = profileButton.GetComponentInChildren<TMP_Text>(true);

            if (nicknameInput != null)
            {
                nicknameInput.lineType = TMP_InputField.LineType.SingleLine;
                nicknameInput.characterLimit = nicknameMaxLength;
                nicknameInput.onSubmit.AddListener(_ => OnChangeNicknameClicked());
            }
        }

        private void Start()
        {
            if (profilePopup != null) profilePopup.SetActive(false); // 씬에 켜 둔 채 저장했어도 닫고 시작

            // 로그인하지 않았으면 LobbyUIController가 타이틀로 보내므로 요청하지 않는다.
            if (!AuthSession.IsAuthenticated) return;

            // 다른 계정으로 다시 로그인했으면 이전 캐시는 버린다.
            if (Current != null && Current.userId != AuthSession.UserId) Current = null;

            RenderButton(Current); // 캐시가 있으면 바로, 없으면 로그인 닉네임으로 표시
            LoadProfile(null);
        }

        private void OnDestroy()
        {
            if (profileButton != null) profileButton.onClick.RemoveListener(OnProfileButtonClicked);
            if (closeButton != null) closeButton.onClick.RemoveListener(ClosePopup);
            if (changeNicknameButton != null) changeNicknameButton.onClick.RemoveListener(OnChangeNicknameClicked);
        }

        // ------------------------------------------------------------------
        // 프로필 불러오기
        // ------------------------------------------------------------------

        /// <summary>내 프로필을 서버에서 다시 불러와 버튼/팝업을 갱신한다.</summary>
        /// <param name="onDone">완료 콜백 (성공 여부). 필요 없으면 null</param>
        public void LoadProfile(Action<bool> onDone)
        {
            if (isLoading)
            {
                pendingDone += onDone;
                return;
            }

            isLoading = true;
            pendingDone += onDone;
            UserApi.GetMe(result =>
            {
                if (this == null) return; // 응답 전에 씬이 바뀐 경우

                isLoading = false;
                Action<bool> done = pendingDone;
                pendingDone = null;

                if (!result.success)
                {
                    Debug.LogWarning($"[Profile] 내 프로필 조회 실패: {result.message}");
                    if (IsPopupOpen) statusMessage?.ShowError(result.message);
                    done?.Invoke(false);
                    return;
                }

                SetCurrent(result.data);
                done?.Invoke(true);
            });
        }

        private void SetCurrent(UserProfileResponse profile)
        {
            Current = profile;
            if (!string.IsNullOrEmpty(profile.nickname)) AuthSession.SetNickname(profile.nickname);

            RenderButton(profile);
            if (IsPopupOpen) RenderPopup(profile);
            ProfileChanged?.Invoke(profile);
        }

        // ------------------------------------------------------------------
        // 화면 표시
        // ------------------------------------------------------------------

        private void RenderButton(UserProfileResponse profile)
        {
            if (profileButtonText == null) return;

            profileButtonText.text = profile == null
                ? AuthSession.Nickname
                : buttonFormat.Replace("{nickname}", profile.nickname).Replace("{level}", profile.level.ToString());
        }

        private void RenderPopup(UserProfileResponse p)
        {
            if (nicknameText != null) nicknameText.text = p.nickname;
            if (levelText != null) levelText.text = $"Lv.{p.level}";
            if (goldText != null) goldText.text = p.gold.ToString("N0");
            if (recordText != null) recordText.text = $"{p.playCount}전 {p.winCount}승 {p.lossCount}패";
            if (winRateText != null) winRateText.text = $"{p.WinRatePercent:0.#}%";
        }

        // ------------------------------------------------------------------
        // 팝업 열기 / 닫기
        // ------------------------------------------------------------------

        public bool IsPopupOpen => profilePopup != null && profilePopup.activeSelf;

        // Profile_Button 클릭 (인스펙터 OnClick에 직접 연결해도 된다)
        public void OnProfileButtonClicked()
        {
            if (SceneLoader.IsLoading || profilePopup == null) return;

            profilePopup.SetActive(true);
            statusMessage?.Clear();
            SetChanging(false);
            if (nicknameInput != null) nicknameInput.text = string.Empty;

            if (Current != null) RenderPopup(Current); // 캐시를 먼저 보여 주고 최신 값으로 갱신한다.
            else statusMessage?.ShowInfo("프로필을 불러오는 중...", keep: true);

            LoadProfile(ok =>
            {
                if (ok && statusMessage != null && statusMessage.CurrentMessage == "프로필을 불러오는 중...")
                    statusMessage.Clear();
            });
        }

        public void ClosePopup()
        {
            if (isChanging) return;
            statusMessage?.Clear();
            if (profilePopup != null) profilePopup.SetActive(false);
        }

        // ------------------------------------------------------------------
        // 닉네임 변경
        // ------------------------------------------------------------------

        // 닉네임 변경 버튼 (또는 입력칸에서 Enter)
        public void OnChangeNicknameClicked()
        {
            if (isChanging || SceneLoader.IsLoading) return;

            string nickname = nicknameInput != null ? nicknameInput.text.Trim() : string.Empty;

            // 클라이언트 1차 검증. 실패하면 서버에 보내지 않는다.
            if (nickname.Length < nicknameMinLength || nickname.Length > nicknameMaxLength)
            {
                statusMessage?.ShowError($"닉네임은 {nicknameMinLength}~{nicknameMaxLength}자로 입력하세요.");
                return;
            }
            if (Current != null && Current.nickname == nickname)
            {
                statusMessage?.ShowError("현재 닉네임과 같습니다.");
                return;
            }

            SetChanging(true);
            statusMessage?.ShowInfo("닉네임을 변경하는 중...", keep: true);

            UserApi.ChangeNickname(nickname, result =>
            {
                if (this == null) return;

                if (!result.success)
                {
                    SetChanging(false);
                    statusMessage?.ShowError(result.message); // 중복 닉네임(409) 등 서버 메시지
                    return;
                }

                // 변경됐으니 최신 프로필을 다시 받아 버튼/팝업을 갱신한다.
                if (nicknameInput != null) nicknameInput.text = string.Empty;
                LoadProfile(ok =>
                {
                    SetChanging(false);
                    if (ok) statusMessage?.ShowSuccess("닉네임이 변경되었습니다.");
                    else statusMessage?.ShowError("변경은 되었지만 프로필을 다시 불러오지 못했습니다.");
                });
            });
        }

        // 변경 요청 중에는 버튼/입력칸을 잠가 중복 요청을 막는다.
        private void SetChanging(bool value)
        {
            isChanging = value;
            if (changeNicknameButton != null) changeNicknameButton.interactable = !value;
            if (nicknameInput != null) nicknameInput.interactable = !value;
            if (closeButton != null) closeButton.interactable = !value;
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
