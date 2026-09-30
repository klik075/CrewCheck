using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 입력/버튼/상태 메시지 UI 담당. 통신은 AuthNetworkManager에 위임.
public class AuthUIController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private AuthNetworkManager network;

    [Header("UI")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Button signupButton;
    [SerializeField] private Button loginButton;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Colors")]
    [SerializeField] private Color infoColor = Color.white;
    [SerializeField] private Color successColor = new Color(0.3f, 0.85f, 0.4f);
    [SerializeField] private Color errorColor = new Color(1f, 0.4f, 0.4f);

    private bool isBusy;

    private void Awake()
    {
        passwordInput.contentType = TMP_InputField.ContentType.Password;
        passwordInput.ForceLabelUpdate();
    }

    // 인스펙터(OnClick)에 영구 리스너가 연결돼 있으면 그쪽을 사용하고, 없을 때만 코드로 연결
    private void OnEnable()
    {
        if (signupButton.onClick.GetPersistentEventCount() == 0) signupButton.onClick.AddListener(OnSignupClicked);
        if (loginButton.onClick.GetPersistentEventCount() == 0) loginButton.onClick.AddListener(OnLoginClicked);
    }

    private void OnDisable()
    {
        signupButton.onClick.RemoveListener(OnSignupClicked);
        loginButton.onClick.RemoveListener(OnLoginClicked);
    }

    private void Start()
    {
        SetStatus("아이디와 비밀번호를 입력하세요.", infoColor);
    }

    public void OnSignupClicked() => Submit(isSignup: true);
    public void OnLoginClicked() => Submit(isSignup: false);

    private void Submit(bool isSignup)
    {
        if (isBusy) return;

        string username = usernameInput.text.Trim();
        string password = passwordInput.text;

        // 1차 검증 실패 시 서버 통신 차단
        if (!AuthValidator.Validate(username, password, out string error))
        {
            SetStatus(error, errorColor);
            return;
        }

        SetBusy(true);
        SetStatus(isSignup ? "회원가입 중..." : "로그인 중...", infoColor);

        if (isSignup) network.Signup(username, password, OnResult);
        else network.Login(username, password, OnResult);
    }

    private void OnResult(AuthResult result)
    {
        SetBusy(false);

        string message = result.message;
        // 로그인 성공 시 세션에 저장된 토큰 앞자리를 표시해 저장 여부를 눈으로 확인
        if (result.success && AuthSession.IsAuthenticated && result.data != null
            && result.data.accessToken == AuthSession.AccessToken)
        {
            message += $"\n토큰 획득: {AuthSession.TokenPreview()}";
            Debug.Log($"[Auth] 세션 저장 완료 - memberId={AuthSession.MemberId}, username={AuthSession.Username}, token={AuthSession.TokenPreview()}");
        }

        SetStatus(message, result.success ? successColor : errorColor);
    }

    private void SetBusy(bool busy)
    {
        isBusy = busy;
        signupButton.interactable = !busy;
        loginButton.interactable = !busy;
    }

    private void SetStatus(string message, Color color)
    {
        statusText.text = message;
        statusText.color = color;
    }
}
