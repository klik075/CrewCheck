using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Common;

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// 로비 설정 팝업 (톱니바퀴 버튼 → 팝업). 지금은 로그아웃 버튼만 있다.
    /// 로그아웃: 세션/토큰을 비우고 실시간 연결을 정상 종료한 뒤 타이틀 씬으로 이동 (LobbyUIController.Logout).
    /// </summary>
    public class LobbySettingsPopup : MonoBehaviour
    {
        [Tooltip("톱니바퀴 버튼 (TopBar/Setting_Button). 누르면 팝업을 연다.")]
        [SerializeField] private Button openButton;
        [Tooltip("설정 팝업 루트 오브젝트. 시작할 때 닫힌다.")]
        [SerializeField] private GameObject popup;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button logoutButton;

        private void Awake()
        {
            BindIfEmpty(openButton, Open);
            BindIfEmpty(closeButton, Close);
            BindIfEmpty(logoutButton, OnLogoutClicked);
        }

        private void Start()
        {
            if (popup != null) popup.SetActive(false); // 씬에 켜 둔 채 저장했어도 닫고 시작
        }

        private void OnDestroy()
        {
            if (openButton != null) openButton.onClick.RemoveListener(Open);
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
            if (logoutButton != null) logoutButton.onClick.RemoveListener(OnLogoutClicked);
        }

        public void Open()
        {
            if (SceneLoader.IsLoading || popup == null) return;
            popup.SetActive(true);
        }

        public void Close()
        {
            if (popup != null) popup.SetActive(false);
        }

        public void OnLogoutClicked()
        {
            if (SceneLoader.IsLoading) return;
            LobbyUIController.Logout();
        }

        private static void BindIfEmpty(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null && button.onClick.GetPersistentEventCount() == 0) button.onClick.AddListener(action);
        }
    }
}
