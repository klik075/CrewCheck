using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WhoisntCitizen.LobbyTest;
using WhoisntCitizen.Network;

namespace WhoisntCitizen.Title
{
    // Title 씬: 로그인 / 회원가입 처리.
    // 로그인 성공 시 AuthSession에 JWT를 저장하고 로비 씬으로 이동한다.
    // 필드 연결은 Tools > Bind Title Scene (TitleSceneBindTool)이 자동으로 해 준다.
    public class TitleController : MonoBehaviour
    {
        [Header("Server")]
        [SerializeField] private string baseUrl = ApiClient.DefaultBaseUrl;
        [SerializeField] private int timeoutSeconds = 10;

        [Header("Scene")]
        [SerializeField] private string lobbySceneName = "Lobby";

        [Header("Login")]
        [SerializeField] private TMP_InputField loginIdInput;
        [SerializeField] private TMP_InputField loginPasswordInput;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button openRegisterButton;
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

        [Header("Colors")]
        [SerializeField] private Color errorColor = new Color(1f, 0.4f, 0.4f);
        [SerializeField] private Color successColor = new Color(0.3f, 0.85f, 0.4f);
        [SerializeField] private Color infoColor = Color.white;

        private bool isBusy;

        private void Awake()
        {
            // 비밀번호 칸이 평문으로 보이지 않도록 보장한다.
            MaskPassword(loginPasswordInput);
            MaskPassword(registerPasswordInput);
            MaskPassword(registerPasswordConfirmInput);
        }

        // 에디터 툴이 OnClick 영구 리스너를 연결해 두므로, 없을 때만 코드로 연결한다.
        private void OnEnable()
        {
            BindIfEmpty(loginButton, OnLoginClicked);
            BindIfEmpty(openRegisterButton, OnOpenRegisterClicked);
            BindIfEmpty(registerButton, OnRegisterClicked);
            BindIfEmpty(registerCloseButton, OnCloseRegisterClicked);
        }

        private void OnDisable()
        {
            if (loginButton != null) loginButton.onClick.RemoveListener(OnLoginClicked);
            if (openRegisterButton != null) openRegisterButton.onClick.RemoveListener(OnOpenRegisterClicked);
            if (registerButton != null) registerButton.onClick.RemoveListener(OnRegisterClicked);
            if (registerCloseButton != null) registerCloseButton.onClick.RemoveListener(OnCloseRegisterClicked);
        }

        private void Start()
        {
            if (registerPanel != null) registerPanel.SetActive(false);
            SetMessage(loginMessageText, "", infoColor);
            SetMessage(registerMessageText, "", infoColor);
        }

        // ---------- 버튼 핸들러 ----------

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
                return;
            }

            SetMessage(loginMessageText, "로그인 중...", infoColor);
            var body = new LoginRequest { username = username, password = password };
            StartCoroutine(Run(ApiClient.Send(baseUrl, "POST", "/api/members/login", body, false, timeoutSeconds, OnLoginResult)));
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
                return;
            }

            SetMessage(registerMessageText, "가입 중...", infoColor);
            var body = new SignupRequest { username = username, password = password, nickname = nickname };
            StartCoroutine(Run(ApiClient.Send(baseUrl, "POST", "/api/members/signup", body, false, timeoutSeconds,
                result => OnRegisterResult(result, username))));
        }

        // ---------- 응답 처리 ----------

        private void OnLoginResult(ApiResult result)
        {
            if (!result.success)
            {
                SetMessage(loginMessageText, result.message, errorColor);
                return;
            }

            AuthResponse res = SafeParse<AuthResponse>(result.body);
            if (res == null || string.IsNullOrEmpty(res.accessToken))
            {
                SetMessage(loginMessageText, "로그인 응답이 올바르지 않습니다.", errorColor);
                return;
            }

            AuthSession.SetSession(res.memberId, res.username, res.accessToken);
            loginPasswordInput.text = "";
            SetMessage(loginMessageText, "로그인 성공!", successColor);

            if (!Application.CanStreamedLevelBeLoaded(lobbySceneName))
            {
                SetMessage(loginMessageText,
                    $"로그인은 성공했지만 '{lobbySceneName}' 씬이 Build Settings에 없습니다. (Tools > Bind Title Scene 실행)", errorColor);
                return;
            }
            SceneManager.LoadScene(lobbySceneName);
        }

        private void OnRegisterResult(ApiResult result, string username)
        {
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

        // 요청 하나를 실행하는 동안 중복 클릭을 막는다.
        private IEnumerator Run(IEnumerator request)
        {
            isBusy = true;
            SetButtons(false);
            yield return request;
            isBusy = false;
            SetButtons(true);
        }

        private void SetButtons(bool value)
        {
            if (loginButton != null) loginButton.interactable = value;
            if (openRegisterButton != null) openRegisterButton.interactable = value;
            if (registerButton != null) registerButton.interactable = value;
        }

        private void ClearRegisterFields()
        {
            registerIdInput.text = "";
            registerPasswordInput.text = "";
            registerPasswordConfirmInput.text = "";
            registerNicknameInput.text = "";
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

        private static void BindIfEmpty(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null && button.onClick.GetPersistentEventCount() == 0)
                button.onClick.AddListener(action);
        }

        private static T SafeParse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (System.Exception) { return null; }
        }
    }
}
