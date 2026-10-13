using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// 플레이어 관리 메뉴 (Room 씬의 Popup_PlayerManage 오브젝트에 부착, 씬에 하나만 둔다).
    ///
    /// 흐름
    ///   방장이 참가자 줄의 [관리] 버튼 클릭 → RoomUIController가 Open(대상) 호출
    ///   → [방장 위임] / [추방] 선택 → 메뉴를 닫고 콜백 호출 → RoomUIController가 확인 팝업을 띄운다
    ///   [닫기] / 배경 클릭 / ESC → 닫기만 한다.
    ///
    /// 참가자 줄(RoomPlayer)은 polling으로 목록이 바뀔 때마다 다시 만들어지므로
    /// 메뉴는 프리팹 안이 아니라 씬에 두고, 대상은 userId로만 기억한다.
    /// 처음에는 꺼 둔 상태로 저장한다.
    /// </summary>
    public class PlayerManageMenu : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;          // 대상 닉네임 표시 (선택)
        [SerializeField] private Button transferHostButton;   // Button_TransferHost
        [SerializeField] private Button kickButton;           // Button_Kick
        [SerializeField] private Button closeButton;          // Button_Close (선택)
        [Tooltip("화면 전체를 덮는 반투명 배경(Button). 누르면 닫힌다. (선택)")]
        [SerializeField] private Button blocker;

        [SerializeField] private string titleFormat = "{0}님 관리";

        private Action onTransferHost;
        private Action onKick;
        private bool initialized;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>지금 관리 중인 플레이어 userId. 닫혀 있으면 0.</summary>
        public long TargetUserId { get; private set; }

        // 팝업은 처음에 꺼져 있어 Awake가 늦게 불릴 수 있으므로 Open()에서도 호출한다. (한 번만 실행됨)
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;

            if (transferHostButton != null) transferHostButton.onClick.AddListener(() => Choose(onTransferHost));
            if (kickButton != null) kickButton.onClick.AddListener(() => Choose(onKick));
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (blocker != null) blocker.onClick.AddListener(Close);
        }

        private void Awake()
        {
            Initialize();
        }

        private void Update()
        {
            if (EscapePressed()) Close();
        }

        /// <param name="userId">관리할 플레이어</param>
        /// <param name="nickname">메뉴 제목에 표시할 닉네임</param>
        /// <param name="transferHost">[방장 위임] 선택 시 (메뉴가 닫힌 뒤 호출)</param>
        /// <param name="kick">[추방] 선택 시 (메뉴가 닫힌 뒤 호출)</param>
        public void Open(long userId, string nickname, Action transferHost, Action kick)
        {
            Initialize();
            TargetUserId = userId;
            onTransferHost = transferHost;
            onKick = kick;
            if (titleText != null) titleText.text = string.Format(titleFormat, nickname);
            SetInteractable(true);
            gameObject.SetActive(true);
            transform.SetAsLastSibling(); // 다른 UI 위에 보이게
        }

        public void Close()
        {
            TargetUserId = 0;
            onTransferHost = null;
            onKick = null;
            gameObject.SetActive(false);
        }

        public void SetInteractable(bool value)
        {
            if (transferHostButton != null) transferHostButton.interactable = value;
            if (kickButton != null) kickButton.interactable = value;
        }

        private void Choose(Action action)
        {
            Action chosen = action; // Close()가 콜백을 비우므로 먼저 꺼내 둔다
            Close();
            chosen?.Invoke();
        }

        private static bool EscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
