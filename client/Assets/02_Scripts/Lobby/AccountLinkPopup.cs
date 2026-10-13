using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Common;
using WhoisntCitizen.Network;

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// 설정 팝업의 "계정 연동" 기능 (Popup_Setting 오브젝트에 부착).
    ///
    /// 현재 로그인한 계정(게스트 또는 일반)에 구글/카카오 계정을 연동한다.
    ///   1) 구글/카카오 연동 버튼 → 소셜 SDK에서 토큰 수신 (SocialAuth.Provider, 에디터에서는 Mock)
    ///   2) AuthService.LinkSocial → POST /api/auth/link/{google|kakao}
    ///      Authorization 헤더에 현재 JWT가 자동으로 붙는다.
    ///   3) 200: 성공 메시지 / 409: 이미 다른 계정에 연동된 소셜 계정 / 그 밖: 서버 메시지
    /// LobbyUIController의 설정 버튼이 Open()을 호출한다.
    /// </summary>
    public class AccountLinkPopup : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_Text accountInfoText;        // 현재 계정 표시 (예: "게스트1234 (게스트 계정)")
        [SerializeField] private Button googleLinkButton;
        [SerializeField] private Button kakaoLinkButton;
        [SerializeField] private Button closeButton;

        [Header("Status")]
        [SerializeField] private StatusMessageView statusMessage; // 팝업 안의 StatusMessageText

        private bool isRequesting;
        private bool initialized;

        public bool IsOpen => gameObject.activeSelf;

        // 팝업은 처음에 꺼져 있어 Awake가 늦게 불릴 수 있으므로 Open()에서도 호출한다. (한 번만 실행됨)
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;

            BindIfEmpty(googleLinkButton, OnGoogleClicked);
            BindIfEmpty(kakaoLinkButton, OnKakaoClicked);
            BindIfEmpty(closeButton, Close);
        }

        public void Open()
        {
            Initialize();
            gameObject.SetActive(true);
            isRequesting = false;
            SetButtons(true);
            if (statusMessage != null) statusMessage.Clear();

            if (accountInfoText != null)
            {
                string name = string.IsNullOrEmpty(AuthSession.Nickname) ? AuthSession.Username : AuthSession.Nickname;
                accountInfoText.text = $"{name} ({(AuthService.IsGuestAccount ? "게스트 계정" : "일반 계정")})";
            }
        }

        /// <summary>팝업 닫기. 연동 요청 중에는 응답을 받을 때까지 닫지 않는다.</summary>
        public void Close()
        {
            if (isRequesting) return;
            gameObject.SetActive(false);
        }

        public void OnGoogleClicked() => Link(SocialProvider.Google);
        public void OnKakaoClicked() => Link(SocialProvider.Kakao);

        private void Link(SocialProvider provider)
        {
            if (isRequesting) return;

            isRequesting = true;
            SetButtons(false);
            if (statusMessage != null) statusMessage.ShowInfo($"{provider} 계정 연동 중...", keep: true);

            RunLink(provider);
        }

        private async void RunLink(SocialProvider provider)
        {
            string token = null;
            string error = null;
            try { token = await SocialAuth.GetTokenAsync(provider); }
            catch (System.Exception e) { error = e.Message; }

            if (this == null) return;

            if (string.IsNullOrEmpty(token))
            {
                Finish(false, error ?? $"{provider} 로그인에 실패했습니다.");
                return;
            }
            // 현재 JWT가 Authorization 헤더로 자동 첨부된다.
            AuthService.LinkSocial(provider, token, result => OnLinkResult(result, provider));
        }

        private void OnLinkResult(ApiResult<SocialLinkResponse> result, SocialProvider provider)
        {
            if (this == null) return;

            if (result.success)
                Finish(true, !string.IsNullOrEmpty(result.data.message) ? result.data.message : $"{provider} 계정 연동이 완료되었습니다.");
            else if (result.IsConflict)
                Finish(false, "이미 다른 계정에 연동된 소셜 계정입니다.");
            else
                Finish(false, result.message);
        }

        private void Finish(bool success, string message)
        {
            isRequesting = false;
            SetButtons(true);
            if (statusMessage == null) return;
            if (success) statusMessage.ShowSuccess(message);
            else statusMessage.ShowError(message);
        }

        private void SetButtons(bool value)
        {
            if (googleLinkButton != null) googleLinkButton.interactable = value;
            if (kakaoLinkButton != null) kakaoLinkButton.interactable = value;
            if (closeButton != null) closeButton.interactable = value;
        }

        private static void BindIfEmpty(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null && button.onClick.GetPersistentEventCount() == 0)
                button.onClick.AddListener(action);
        }
    }
}
