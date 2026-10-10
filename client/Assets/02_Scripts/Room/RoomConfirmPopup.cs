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
    /// Room 씬의 예/아니오 확인 팝업 (Popup_Confirm 오브젝트에 부착). 플레이어 관리 메뉴에서 위임·추방을 고르면 한 번 묻는다.
    ///
    ///   confirmPopup.Open("철수님을 추방할까요?", () => { ...실제 요청... });
    ///
    /// 확인을 누르면 팝업을 닫고 onConfirm을 부른다. 요청 결과는 RoomUIController의 상태 메시지로 보여 준다.
    /// 취소/ESC는 닫기만 한다. 처음에는 꺼 둔 상태로 저장한다.
    /// </summary>
    public class RoomConfirmPopup : MonoBehaviour
    {
        [SerializeField] private TMP_Text messageText;  // 질문 문구
        [SerializeField] private Button confirmButton;  // Button_Confirm
        [SerializeField] private Button cancelButton;   // Button_Cancel

        private Action onConfirm;
        private bool initialized;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>확인 대상 플레이어 userId (상태가 바뀌면 자동으로 닫기 위해 기억). 없으면 0.</summary>
        public long TargetUserId { get; private set; }

        // 팝업은 처음에 꺼져 있어 Awake가 늦게 불릴 수 있으므로 Open()에서도 호출한다. (한 번만 실행됨)
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;

            if (confirmButton != null && confirmButton.onClick.GetPersistentEventCount() == 0)
                confirmButton.onClick.AddListener(OnConfirmClicked);
            if (cancelButton != null && cancelButton.onClick.GetPersistentEventCount() == 0)
                cancelButton.onClick.AddListener(Close);
        }

        private void Awake()
        {
            Initialize();
        }

        private void Update()
        {
            if (EscapePressed()) Close();
        }

        public void Open(string message, Action confirm)
        {
            Open(message, 0, confirm);
        }

        /// <param name="targetUserId">이 확인이 대상으로 하는 플레이어 (그 사람이 나가면 RoomUIController가 닫는다)</param>
        public void Open(string message, long targetUserId, Action confirm)
        {
            Initialize();
            onConfirm = confirm;
            TargetUserId = targetUserId;
            if (messageText != null) messageText.text = message;
            gameObject.SetActive(true);
            transform.SetAsLastSibling(); // 관리 메뉴 등 다른 UI 위에 보이게
        }

        public void Close()
        {
            onConfirm = null;
            TargetUserId = 0;
            gameObject.SetActive(false);
        }

        public void OnConfirmClicked()
        {
            Action confirm = onConfirm; // Close()가 콜백을 비우므로 먼저 꺼내 둔다
            Close();
            confirm?.Invoke();
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
