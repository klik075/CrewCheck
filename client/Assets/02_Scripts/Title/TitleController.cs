using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Common;
using WhoisntCitizen.Network;
using WhoisntCitizen.Chat; // ChatNotice: 내 화면 전용 안내 (채팅창 + 콘솔)

namespace WhoisntCitizen.Title
{
    // Title 씬: 게스트 / 소셜 / 일반 계정 로그인과 회원가입 처리.
    //
    // 화면 흐름
    //   Main        : [게스트 로그인] [로그인]
    //   LoginSelect : [구글 로그인] [카카오 로그인] [계정 로그인]   ("로그인" 버튼으로 여는 선택 팝업, 메인 위에 겹쳐 표시)
    //   Account     : ID/PW 입력(+ 회원가입 패널). 기존 Login 그룹을 그대로 사용한다.
    // 어떤 방식이든 로그인에 성공하면 AuthSession에 JWT와 유저 정보를 저장하고 로비 씬으로 이동한다.
    //
    // 필드 연결은 Tools > Bind Title Scene (TitleSceneBindTool)이 자동으로 해 준다.
    // 서버 주소/타임아웃은 ApiConfig, 씬 이름은 SceneLoader, API 호출은 AuthService가 담당한다.
    public class TitleController : MonoBehaviour
    {
        private enum TitlePanel { Main, LoginSelect, Account }

        [Header("Main")]
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private Button guestLoginButton;
        [SerializeField] private Button openLoginSelectButton;
        [SerializeField] private TextMeshProUGUI mainMessageText;

        [Header("Login Select (소셜/계정 선택 팝업)")]
        [SerializeField] private GameObject loginSelectPanel;
        [SerializeField] private Button googleLoginButton;
        [SerializeField] private Button kakaoLoginButton;
        [SerializeField] private Button accountLoginButton;
        [SerializeField] private Button loginSelectCloseButton;
        [SerializeField] private TextMeshProUGUI loginSelectMessageText;

        [Header("Account Login (ID/PW)")]
        [SerializeField] private GameObject accountPanel; // 기존 Login 그룹
        [SerializeField] private TMP_InputField loginIdInput;
        [SerializeField] private TMP_InputField loginPasswordInput;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button openRegisterButton;
        [SerializeField] private Button accountBackButton;
        [SerializeField] private TextMeshProUGUI loginMessageText;

        [Header("Register")]
        [SerializeField] private GameObject registerPanel;
        [SerializeField] private TMP_InputField registerIdInput;
        [SerializeField] private TMP_InputField registerPasswordInput;
        [SerializeField] private TMP_InputField registerPasswordConfirmInput;
        [SerializeField] private TMP_InputField registerNicknameInput;
        [SerializeField] private Button registerButton;
        [SerializeField] private Button registerCloseButton; // 회원가입 패널의 X 버튼
        [SerializeField] private TextMeshProUGUI registerMessageText;

        [Header("Editor Mock (소셜 SDK 대신 쓰는 더미 토큰)")]
        [SerializeField] private MockSocialAuthProvider mockSocialAuth = new MockSocialAuthProvider();

        [Header("Colors")]
        [SerializeField] private Color errorColor = new Color(1f, 0.4f, 0.4f);
        [SerializeField] private Color successColor = new Color(0.3f, 0.85f, 0.4f);
        [SerializeField] private Color infoColor = Color.white;

        private TitlePanel currentPanel = TitlePanel.Main;
        private bool isBusy;

        private void Awake()
        {
#if UNITY_EDITOR
            // 에디터에서는 인스펙터에서 수정한 Mock 토큰을 사용한다. (실기기 빌드에서는 SocialAuth.Provider를 실제 SDK 구현으로 교체)
            SocialAuth.Provider = mockSocialAuth;
#endif

            // 비밀번호 칸이 평문으로 보이지 않도록 보장한다.
            MaskPassword(loginPasswordInput);
            MaskPassword(registerPasswordInput);
            MaskPassword(registerPasswordConfirmInput);
        }

        // 에디터 툴이 OnClick 영구 리스너를 연결해 두므로, 없을 때만 코드로 연결한다.
        private void OnEnable()
        {
            BindIfEmpty(guestLoginButton, OnGuestLoginClicked);
            BindIfEmpty(openLoginSelectButton, OnOpenLoginSelectClicked);
            BindIfEmpty(googleLoginButton, OnGoogleLoginClicked);
            BindIfEmpty(kakaoLoginButton, OnKakaoLoginClicked);
            BindIfEmpty(accountLoginButton, OnAccountLoginClicked);
            BindIfEmpty(loginSelectCloseButton, OnCloseLoginSelectClicked);
            BindIfEmpty(accountBackButton, OnAccountBackClicked);
            BindIfEmpty(loginButton, OnLoginClicked);
            BindIfEmpty(openRegisterButton, OnOpenRegisterClicked);
            BindIfEmpty(registerButton, OnRegisterClicked);
            BindIfEmpty(registerCloseButton, OnCloseRegisterClicked);
        }

        private void OnDisable()
        {
            Unbind(guestLoginButton, OnGuestLoginClicked);
            Unbind(openLoginSelectButton, OnOpenLoginSelectClicked);
            Unbind(googleLoginButton, OnGoogleLoginClicked);
            Unbind(kakaoLoginButton, OnKakaoLoginClicked);
            Unbind(accountLoginButton, OnAccountLoginClicked);
            Unbind(loginSelectCloseButton, OnCloseLoginSelectClicked);
            Unbind(accountBackButton, OnAccountBackClicked);
            Unbind(loginButton, OnLoginClicked);
            Unbind(openRegisterButton, OnOpenRegisterClicked);
            Unbind(registerButton, OnRegisterClicked);
            Unbind(registerCloseButton, OnCloseRegisterClicked);
        }

        private void Start()
        {
            // 이미 로그인된 상태로 타이틀에 들어오면 로그인 화면을 건너뛰고 로비로 보낸다.
            // (로그아웃 / 토큰 만료로 돌아온 경우는 세션이 비어 있어서 여기에 걸리지 않는다)
            if (AuthSession.IsAuthenticated)
            {
                SceneLoader.Load(SceneType.Lobby);
                return;
            }

            ShowPanel(TitlePanel.Main);
            ClearAllMessages();
        }

        // ---------- 패널 전환 ----------

        // 한 번에 한 화면만 보이도록 모든 패널의 활성 상태를 정리한다.
        //   Main        : 메인만
        //   LoginSelect : 메인 + 선택 팝업 (팝업이 메인 위에 겹침)
        //   Account     : 계정 로그인 패널만 (회원가입 패널은 열기 전까지 닫힘)
        private void ShowPanel(TitlePanel panel)
        {
            currentPanel = panel;
            SetActive(mainPanel, panel != TitlePanel.Account);
            SetActive(loginSelectPanel, panel == TitlePanel.LoginSelect);
            SetActive(accountPanel, panel == TitlePanel.Account);
            SetActive(registerPanel, false);
        }

        // ---------- 메인 ----------

        public void OnGuestLoginClicked()
        {
            if (isBusy) return;

            SetMessage(mainMessageText, "게스트 로그인 중...", infoColor);
            SetBusy(true);
            // UUID는 GuestAccountManager가 PlayerPrefs에서 읽거나 새로 발급한다.
            AuthService.GuestLogin(result => OnLoginResult(result, "게스트"));
        }

        public void OnOpenLoginSelectClicked()
        {
            if (isBusy) return;

            ClearAllMessages();
            ShowPanel(TitlePanel.LoginSelect);
        }

        // ---------- 로그인 수단 선택 ----------

        public void OnCloseLoginSelectClicked()
        {
            if (isBusy) return;

            ClearAllMessages();
            ShowPanel(TitlePanel.Main);
        }

        public void OnGoogleLoginClicked() => StartSocialLogin(SocialProvider.Google);
        public void OnKakaoLoginClicked() => StartSocialLogin(SocialProvider.Kakao);

        public void OnAccountLoginClicked()
        {
            if (isBusy) return;

            ClearAllMessages();
            ShowPanel(TitlePanel.Account);
        }

        // 소셜 SDK에서 토큰을 받고(비동기) → 서버 소셜 로그인 API 호출
        private void StartSocialLogin(SocialProvider provider)
        {
            if (isBusy) return;

            SetMessage(loginSelectMessageText, $"{provider} 로그인 중...", infoColor);
            SetBusy(true);

            RunSocialLogin(provider);
        }

        private async void RunSocialLogin(SocialProvider provider)
        {
            string token = null;
            string error = null;
            try { token = await SocialAuth.GetTokenAsync(provider); }
            catch (Exception e) { error = e.Message; }

            if (this == null) return; // 대기 중 씬이 바뀐 경우

            if (string.IsNullOrEmpty(token))
            {
                SetBusy(false);
                SetMessage(loginSelectMessageText, error ?? $"{provider} 로그인에 실패했습니다.", errorColor);
                return;
            }
            AuthService.SocialLogin(provider, token, result => OnLoginResult(result, provider.ToString()));
        }

        // ---------- 계정(ID/PW) 로그인 / 회원가입 ----------

        public void OnAccountBackClicked()
        {
            if (isBusy) return;

            ClearAllMessages();
            ShowPanel(TitlePanel.LoginSelect);
        }

        // Register 버튼: 회원가입 패널 열기 (이미 열려 있으면 입력 내용을 유지한다)
        public void OnOpenRegisterClicked()
        {
            if (isBusy || registerPanel == null || registerPanel.activeSelf) return;

            ClearRegisterFields();
            SetMessage(registerMessageText, "", infoColor);
            registerPanel.SetActive(true);
        }

        // 패널의 X 버튼: 입력 내용을 지우고 패널을 닫는다. (요청 중에는 응답을 받을 때까지 닫지 않는다)
        public void OnCloseRegisterClicked()
        {
            if (isBusy || registerPanel == null) return;

            ClearRegisterFields();
            SetMessage(registerMessageText, "", infoColor);
            registerPanel.SetActive(false);
        }

        public void OnLoginClicked()
        {
            if (isBusy) return;

            string username = loginIdInput.text.Trim();
            string password = loginPasswordInput.text;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                SetMessage(loginMessageText, "아이디와 비밀번호를 입력하세요.", errorColor);
                ChatNotice.Post("아이디와 비밀번호를 입력해 주세요.", false);
                return;
            }

            SetMessage(loginMessageText, "로그인 중...", infoColor);
            SetBusy(true);
            AuthService.AccountLogin(username, password, result => OnLoginResult(result, "계정"));
        }

        public void OnRegisterClicked()
        {
            if (isBusy) return;

            string username = registerIdInput.text.Trim();
            string password = registerPasswordInput.text;
            string passwordConfirm = registerPasswordConfirmInput.text;
            string nickname = registerNicknameInput.text.Trim();

            // 서버로 보내기 전에 1차 검증 (규칙은 서버 MemberDto와 동일)
            if (!AuthValidator.Validate(username, password, out string error))
            {
                SetMessage(registerMessageText, error, errorColor);
                return;
            }
            if (password != passwordConfirm)
            {
                SetMessage(registerMessageText, "비밀번호 확인이 일치하지 않습니다.", errorColor);
                return;
            }
            if (nickname.Length < 2 || nickname.Length > 10)
            {
                SetMessage(registerMessageText, "닉네임은 2~10자여야 합니다.", errorColor);
                ChatNotice.Post("닉네임은 2~10자로 입력해 주세요.", false);
                return;
            }

            SetMessage(registerMessageText, "가입 중...", infoColor);
            SetBusy(true);

            // 회원가입도 토큰이 필요 없는 요청. 응답 본문은 쓰지 않으므로 파싱 없는 Post를 사용한다.
            var body = new SignupRequest { username = username, password = password, nickname = nickname };
            ApiClient.Post(AuthService.SignupPath, body, result => OnRegisterResult(result, username), requireAuth: false);
        }

        // ---------- 응답 처리 ----------

        // 게스트/소셜/계정 로그인 공통 응답 처리. (세션 저장은 AuthService가 이미 끝낸 상태)
        private void OnLoginResult(ApiResult<AuthResponse> result, string label)
        {
            // 응답 전에 씬이 바뀌어 이 오브젝트가 파괴됐으면 아무것도 하지 않는다.
            if (this == null) return;

            if (!result.success)
            {
                SetBusy(false);
                SetPanelMessage(result.message, errorColor);
                return;
            }

            AuthResponse res = result.data;
            Debug.Log($"[Title] {label} 로그인 성공 - userId={res.userId}, username={res.username}, nickname={res.nickname}");
            ChatNotice.Post($"{(string.IsNullOrEmpty(res.nickname) ? res.username : res.nickname)}님, 환영합니다.");

            if (loginPasswordInput != null) loginPasswordInput.text = "";
            SetPanelMessage("로그인 성공!", successColor);

            // 로비로 이동. 씬 이동 중에는 버튼을 잠근 상태로 둔다.
            // 이동을 시작하지 못하면(Build Settings 누락) 원인을 보여주고 잠금을 푼다.
            if (!SceneLoader.Load(SceneType.Lobby))
            {
                SetBusy(false);
                SetPanelMessage(
                    $"로그인은 성공했지만 '{SceneLoader.GetSceneName(SceneType.Lobby)}' 씬이 Build Settings에 없습니다. (Tools > Bind Title Scene 실행)",
                    errorColor);
            }
        }

        private void OnRegisterResult(ApiResult result, string username)
        {
            if (this == null) return;
            SetBusy(false);

            if (!result.success)
            {
                SetMessage(registerMessageText, result.message, errorColor);
                return;
            }

            // 가입 성공: 패널을 닫고, 로그인 칸에 아이디를 채워 바로 로그인할 수 있게 한다.
            registerPanel.SetActive(false);
            ClearRegisterFields();
            loginIdInput.text = username;
            loginPasswordInput.text = "";
            SetMessage(loginMessageText, "회원가입이 완료되었습니다. 로그인해 주세요.", successColor);
        }

        // ---------- 공통 ----------

        // 요청 하나를 실행하는 동안 중복 클릭을 막는다. (요청 시작 시 true, 응답을 받으면 false)
        private void SetBusy(bool busy)
        {
            isBusy = busy;
            bool value = !busy;
            SetInteractable(guestLoginButton, value);
            SetInteractable(openLoginSelectButton, value);
            SetInteractable(googleLoginButton, value);
            SetInteractable(kakaoLoginButton, value);
            SetInteractable(accountLoginButton, value);
            SetInteractable(loginSelectCloseButton, value);
            SetInteractable(accountBackButton, value);
            SetInteractable(loginButton, value);
            SetInteractable(openRegisterButton, value);
            SetInteractable(registerButton, value);
        }

        // 현재 화면의 메시지 칸에 표시한다. (로그인 수단마다 보이는 패널이 달라서)
        private void SetPanelMessage(string message, Color color)
        {
            switch (currentPanel)
            {
                case TitlePanel.Account: SetMessage(loginMessageText, message, color); break;
                case TitlePanel.LoginSelect: SetMessage(loginSelectMessageText, message, color); break;
                default: SetMessage(mainMessageText, message, color); break;
            }
        }

        private void ClearAllMessages()
        {
            SetMessage(mainMessageText, "", infoColor);
            SetMessage(loginSelectMessageText, "", infoColor);
            SetMessage(loginMessageText, "", infoColor);
            SetMessage(registerMessageText, "", infoColor);
        }

        private void ClearRegisterFields()
        {
            if (registerIdInput != null) registerIdInput.text = "";
            if (registerPasswordInput != null) registerPasswordInput.text = "";
            if (registerPasswordConfirmInput != null) registerPasswordConfirmInput.text = "";
            if (registerNicknameInput != null) registerNicknameInput.text = "";
        }

        private static void MaskPassword(TMP_InputField input)
        {
            if (input == null) return;
            input.contentType = TMP_InputField.ContentType.Password;
            input.ForceLabelUpdate();
        }

        private static void SetMessage(TextMeshProUGUI text, string message, Color color)
        {
            if (text == null) return;
            text.text = message;
            text.color = color;
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        private static void SetInteractable(Button button, bool value)
        {
            if (button != null) button.interactable = value;
        }

        private static void BindIfEmpty(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null && button.onClick.GetPersistentEventCount() == 0)
                button.onClick.AddListener(action);
        }

        private static void Unbind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.RemoveListener(action);
        }
    }
}
