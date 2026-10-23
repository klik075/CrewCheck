using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Common;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// 비밀방 비밀번호 입력 팝업 (Popup_RoomPassword 오브젝트에 부착).
    ///
    /// 흐름 (LobbyUIController가 제어)
    ///   1) 비밀방의 Enter 클릭 → LobbyUIController가 Open(room, onSubmit, onCancel) 호출
    ///   2) Button_Confirm (또는 입력칸에서 Enter) → 형식 검사 → onSubmit(비밀번호) 호출 → 입력 잠금
    ///      실제 입장 요청(RoomApi.JoinRoom)은 LobbyUIController가 한다. (공개방 입장과 같은 처리 코드를 쓰기 위해)
    ///   3) 결과
    ///      - 성공: LobbyUIController가 Room 씬으로 이동한다. (팝업은 씬과 함께 사라짐)
    ///      - 비밀번호 틀림(403 WRONG_ROOM_PASSWORD): ShowWrongPassword() → 입력칸을 비우고 다시 입력받는다. (횟수 제한 없음)
    ///      - 그 밖의 실패(정원 초과, 게임 시작 등): LobbyUIController가 Close() 후 로비 상태 메시지로 알린다.
    ///   4) Button_Cancel (또는 ESC) → Close() + onCancel 호출
    ///
    /// 입력칸은 Pin(숫자만 + * 가림)이고 길이 제한은 없다. 규칙은 RoomPasswordRule(숫자 4자리 이상).
    /// </summary>
    public class RoomPasswordPopup : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_InputField passwordInput;    // InputField (TMP)

        [Header("Buttons")]
        [SerializeField] private Button confirmButton;            // Button_Confirm
        [SerializeField] private Button cancelButton;             // Button_Cancel

        [Header("Status")]
        [SerializeField] private StatusMessageView statusMessage; // 팝업 안의 StatusMessageText

        private Action<string> onSubmit; // 확인: 입력한 비밀번호를 넘긴다
        private Action onCancel;         // 취소/ESC
        private bool isRequesting;       // 입장 요청 중 (중복 요청, 요청 중 닫기 방지)
        private bool initialized;

        /// <summary>팝업이 열려 있는지</summary>
        public bool IsOpen => gameObject.activeSelf;

        // ------------------------------------------------------------------
        // 초기화
        // ------------------------------------------------------------------

        // 팝업은 처음에 꺼져 있어 Awake가 늦게 불릴 수 있으므로 Open()에서도 호출한다. (한 번만 실행됨)
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;

            if (passwordInput != null)
            {
                // Pin: 숫자만 입력되고 *로 가려진다. 길이 제한 없음(0). 앞자리 0도 그대로 문자열로 유지된다.
                passwordInput.contentType = TMP_InputField.ContentType.Pin;
                passwordInput.characterLimit = 0;
                passwordInput.ForceLabelUpdate();
                passwordInput.onSubmit.AddListener(_ => OnConfirmClicked()); // 입력칸에서 Enter
            }

            // 인스펙터 OnClick에 직접 연결한 게 없을 때만 코드로 연결 (두 번 호출 방지)
            if (confirmButton != null && confirmButton.onClick.GetPersistentEventCount() == 0)
                confirmButton.onClick.AddListener(OnConfirmClicked);
            if (cancelButton != null && cancelButton.onClick.GetPersistentEventCount() == 0)
                cancelButton.onClick.AddListener(OnCancelClicked);
        }

        private void Awake()
        {
            Initialize();
        }

        private void Update()
        {
            // ESC로 취소 (요청 중에는 닫지 않음)
            if (EscapePressed() && !isRequesting) OnCancelClicked();
        }

        // ------------------------------------------------------------------
        // 열기 / 닫기 (LobbyUIController에서 호출)
        // ------------------------------------------------------------------

        /// <summary>비밀방 입장용으로 팝업을 연다.</summary>
        /// <param name="submit">확인을 눌렀을 때 호출. 인자는 형식 검사를 통과한 비밀번호</param>
        /// <param name="cancel">취소/ESC로 닫았을 때 호출</param>
        public void Open(Action<string> submit, Action cancel)
        {
            Initialize();
            onSubmit = submit;
            onCancel = cancel;

            gameObject.SetActive(true);
            SetRequesting(false);
            ClearAndFocus();
            statusMessage?.ShowInfo($"비밀번호({RoomPasswordRule.Description})를 입력하세요.", keep: true);
        }

        /// <summary>팝업을 닫는다. 콜백은 호출하지 않는다. (취소 콜백은 OnCancelClicked에서만)</summary>
        public void Close()
        {
            isRequesting = false;
            onSubmit = null;
            onCancel = null;
            if (passwordInput != null) passwordInput.text = string.Empty; // 입력한 비밀번호를 남기지 않는다
            statusMessage?.Clear();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 서버가 403 WRONG_ROOM_PASSWORD를 돌려줬을 때 호출.
        /// 팝업은 그대로 두고 입력칸을 비운 뒤 다시 입력받는다. (횟수 제한 없음)
        /// </summary>
        public void ShowWrongPassword(string message)
        {
            SetRequesting(false);
            ClearAndFocus();
            statusMessage?.ShowError(string.IsNullOrEmpty(message) ? "비밀번호가 일치하지 않습니다." : message, keep: true);
        }

        // ------------------------------------------------------------------
        // 버튼
        // ------------------------------------------------------------------

        // Button_Confirm 클릭 (또는 입력칸에서 Enter)
        public void OnConfirmClicked()
        {
            if (isRequesting || SceneLoader.IsLoading) return;

            string password = passwordInput != null ? passwordInput.text.Trim() : string.Empty;

            // 클라이언트 1차 검사. 비밀방 비밀번호는 항상 규칙을 지켜 만들어지므로
            // 규칙에 맞지 않으면 서버에 보내 봐야 틀린 비밀번호다. (빈 값, 4자리 미만)
            if (string.IsNullOrEmpty(password))
            {
                statusMessage?.ShowError("비밀번호를 입력하세요.", keep: true);
                return;
            }
            if (!RoomPasswordRule.IsValid(password))
            {
                statusMessage?.ShowError($"비밀번호는 {RoomPasswordRule.Description}입니다.", keep: true);
                return;
            }

            SetRequesting(true);
            statusMessage?.ShowInfo("입장하는 중...", keep: true);
            onSubmit?.Invoke(password);
        }

        // Button_Cancel 클릭 (또는 ESC)
        public void OnCancelClicked()
        {
            if (isRequesting) return;

            Action cancel = onCancel; // Close()가 콜백을 비우므로 먼저 꺼내 둔다
            Close();
            cancel?.Invoke();
        }

        // ------------------------------------------------------------------
        // 내부
        // ------------------------------------------------------------------

        private void ClearAndFocus()
        {
            if (passwordInput == null) return;
            passwordInput.text = string.Empty;
            passwordInput.Select();
            passwordInput.ActivateInputField();
        }

        /// <summary>요청 중에는 입력칸과 버튼을 잠가서 중복 요청을 막는다.</summary>
        private void SetRequesting(bool value)
        {
            isRequesting = value;
            if (passwordInput != null) passwordInput.interactable = !value;
            if (confirmButton != null) confirmButton.interactable = !value;
            if (cancelButton != null) cancelButton.interactable = !value;
        }

        /// <summary>이번 프레임에 ESC가 눌렸는지 (새 Input System / 기존 Input 모두 지원)</summary>
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
