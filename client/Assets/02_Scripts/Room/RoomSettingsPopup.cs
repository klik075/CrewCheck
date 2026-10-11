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
    /// 방 설정 변경 팝업 (Popup_RoomSettings 오브젝트에 부착). 방장만 연다.
    /// 프리팹은 Lobby 씬의 Popup_CreateRoom을 복제해서 만들면 된다. (입력칸 구성이 같다)
    ///
    /// 동작
    ///   - RoomUIController가 설정 버튼 클릭 시 Open(현재 방, 성공 콜백)을 호출한다.
    ///   - 현재 제목 / 최대 인원 / 비밀방 여부를 미리 채워 둔다. 비밀번호는 서버가 알려주지 않으므로 항상 빈 칸에서 시작한다.
    ///   - Button_Save (또는 입력칸에서 Enter): 입력 검증 → PUT /rooms/{id}/settings → 성공하면 닫고 콜백
    ///   - Button_Cancel (또는 ESC): 닫기
    ///
    /// 비밀번호 칸
    ///   - 원래 비밀방: 비워 두면 기존 비밀번호 유지, 입력하면 새 비밀번호로 교체
    ///   - 원래 공개방 → 비밀방으로 바꿀 때: 필수
    ///   - 비밀방 토글을 끄면 칸을 숨기고 지운다. (공개방이 되면 서버가 비밀번호를 지운다)
    ///
    /// 입력 검증은 서버 규칙과 같다. (제목 필수·최대 titleMaxLength자, 인원 4~12명 + 현재 인원 이상, 비밀번호 숫자 4자리 이상)
    /// 최종 검증은 서버가 다시 한다.
    /// 참가자의 준비 상태는 바뀌지 않는다.
    /// </summary>
    public class RoomSettingsPopup : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private TMP_InputField titleInput;      // Area_Title/InputField (TMP)
        [SerializeField] private TMP_InputField maxPlayersInput; // Area_Member/InputField (TMP)

        [Header("Private Room")]
        [SerializeField] private Toggle privateToggle;           // Area_Private/Toggle
        [Tooltip("비밀번호 입력칸. Content Type은 코드에서 Pin(숫자만 + 가림)으로 바꾼다.")]
        [SerializeField] private TMP_InputField passwordInput;   // Area_PasswordText/InputField (TMP)
        [Tooltip("비밀방일 때만 보이는 비밀번호 영역(라벨 + 입력칸). 비워 두면 입력칸만 활성/비활성으로 바꾼다.")]
        [SerializeField] private GameObject passwordArea;        // Items/Area_PasswordText

        [Header("Buttons")]
        [SerializeField] private Button saveButton;              // Button_Save (복제한 Button_Create)
        [SerializeField] private Button cancelButton;            // Button_Cancel

        [Header("Status")]
        [SerializeField] private StatusMessageView statusMessage; // 팝업 안의 StatusMessageText

        [Header("Rules")]
        [Tooltip("방 제목 최대 글자 수 (CreateRoomPopup과 같게)")]
        [SerializeField] private int titleMaxLength = 20;
        [SerializeField] private string keepPasswordPlaceholder = "변경하지 않으려면 비워 두세요";
        [SerializeField] private string newPasswordPlaceholder = "비밀번호 (숫자 4자리 이상)";

        private long roomId;
        private string originalTitle;
        private int originalMaxPlayers;
        private bool wasPrivate;        // 팝업을 열 때 비밀방이었는지 (비밀번호를 비워 두면 유지 가능)
        private int currentPlayerCount; // 최대 인원 하한. 열려 있는 동안 RoomUIController가 OnRoomUpdated로 갱신한다.
        private Action<string> onSaved;

        private bool isRequesting;
        private bool initialized;

        public bool IsOpen => gameObject.activeSelf;
        public bool IsRequesting => isRequesting;

        // ------------------------------------------------------------------
        // 초기화
        // ------------------------------------------------------------------

        // 팝업은 처음에 꺼져 있어 Awake가 늦게 불릴 수 있으므로 Open()에서도 호출한다. (한 번만 실행됨)
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;

            if (titleInput != null)
            {
                titleInput.characterLimit = titleMaxLength;
                titleInput.onSubmit.AddListener(_ => OnSaveClicked());
            }

            if (maxPlayersInput != null)
            {
                maxPlayersInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                maxPlayersInput.characterLimit = 2;
                maxPlayersInput.ForceLabelUpdate();
                maxPlayersInput.onSubmit.AddListener(_ => OnSaveClicked());
            }

            if (passwordInput != null)
            {
                passwordInput.contentType = TMP_InputField.ContentType.Pin;
                passwordInput.characterLimit = 0;
                passwordInput.ForceLabelUpdate();
                passwordInput.onSubmit.AddListener(_ => OnSaveClicked());
            }

            if (privateToggle != null) privateToggle.onValueChanged.AddListener(OnPrivateToggleChanged);

            if (saveButton != null && saveButton.onClick.GetPersistentEventCount() == 0)
                saveButton.onClick.AddListener(OnSaveClicked);
            if (cancelButton != null && cancelButton.onClick.GetPersistentEventCount() == 0)
                cancelButton.onClick.AddListener(OnCancelClicked);
        }

        private void Awake()
        {
            Initialize();
        }

        private void Update()
        {
            if (EscapePressed() && !isRequesting) Close();
        }

        // ------------------------------------------------------------------
        // 열기 / 닫기
        // ------------------------------------------------------------------

        /// <summary>현재 방 설정을 채워 팝업을 연다.</summary>
        /// <param name="saved">변경에 성공하면 안내 문구와 함께 호출된다. (RoomUIController가 방을 다시 조회)</param>
        public void Open(RoomDetailResponse room, Action<string> saved)
        {
            if (room == null) return;
            Initialize();
            gameObject.SetActive(true);

            roomId = room.id;
            originalTitle = room.title ?? string.Empty;
            originalMaxPlayers = room.maxPlayers;
            wasPrivate = room.privateRoom;
            currentPlayerCount = PlayerCount(room);
            onSaved = saved;

            if (titleInput != null) titleInput.text = originalTitle;
            if (maxPlayersInput != null) maxPlayersInput.text = originalMaxPlayers.ToString();
            // 초기화는 "사용자가 토글을 바꾼 것"이 아니므로 이벤트 없이 값만 바꾼다. (CreateRoomPopup.ResetInputs와 같은 이유)
            if (privateToggle != null) privateToggle.SetIsOnWithoutNotify(wasPrivate);
            ClearPassword();
            SetPasswordPlaceholder(wasPrivate ? keepPasswordPlaceholder : newPasswordPlaceholder);

            SetRequesting(false);
            ShowGuide();

            if (titleInput != null)
            {
                titleInput.Select();
                titleInput.ActivateInputField();
            }
        }

        /// <summary>팝업을 닫는다. (콜백은 부르지 않는다)</summary>
        public void Close()
        {
            ClearPassword(); // 입력한 비밀번호가 다음에 열 때 남지 않도록
            statusMessage?.Clear();
            onSaved = null;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 팝업이 열려 있는 동안 방 조회 결과가 오면 RoomUIController가 부른다.
        /// 최대 인원 하한(현재 인원)만 맞춘다. 입력 중인 값은 건드리지 않는다.
        /// </summary>
        public void OnRoomUpdated(RoomDetailResponse room)
        {
            if (room == null || room.id != roomId) return;
            currentPlayerCount = PlayerCount(room);
        }

        private void ShowGuide()
        {
            int min = Mathf.Max(CreateRoomPopup.MinPlayers, currentPlayerCount);
            string message = $"최대 인원은 {min}~{CreateRoomPopup.MaxPlayers}명으로 설정할 수 있습니다.";
            if (privateToggle != null)
                message += wasPrivate
                    ? "\n비밀번호를 비워 두면 기존 비밀번호가 유지됩니다."
                    : $"\n비밀방으로 바꾸려면 비밀번호({RoomPasswordRule.Description})가 필요합니다.";
            statusMessage?.ShowInfo(message, keep: true);
        }

        // ------------------------------------------------------------------
        // 비밀방 토글 / 비밀번호 칸
        // ------------------------------------------------------------------

        private bool IsPrivateSelected => privateToggle != null ? privateToggle.isOn : wasPrivate;

        private void OnPrivateToggleChanged(bool isOn)
        {
            if (!isOn) ClearPassword();
            RefreshPasswordInput();

            // 공개방이던 방을 비밀방으로 바꿀 때는 비밀번호가 필수라 바로 입력하게 한다.
            if (isOn && !wasPrivate && passwordInput != null && passwordInput.interactable && gameObject.activeInHierarchy)
            {
                passwordInput.Select();
                passwordInput.ActivateInputField();
            }
        }

        private void RefreshPasswordInput()
        {
            bool visible = IsPrivateSelected;
            if (passwordArea != null && passwordArea.activeSelf != visible) passwordArea.SetActive(visible);
            if (passwordInput != null) passwordInput.interactable = visible && !isRequesting;
        }

        private void ClearPassword()
        {
            if (passwordInput == null) return;
            passwordInput.text = string.Empty;
            passwordInput.DeactivateInputField();
        }

        private void SetPasswordPlaceholder(string text)
        {
            if (passwordInput != null && passwordInput.placeholder is TMP_Text placeholder) placeholder.text = text;
        }

        // ------------------------------------------------------------------
        // 버튼
        // ------------------------------------------------------------------

        public void OnCancelClicked()
        {
            if (isRequesting) return;
            Close();
        }

        public void OnSaveClicked()
        {
            if (isRequesting || !IsOpen) return;

            if (!TryReadInputs(out string title, out int maxPlayers, out bool privateRoom, out string password, out string error))
            {
                statusMessage?.ShowError(error, keep: true);
                return;
            }

            // 바뀐 것이 없으면 요청하지 않고 닫는다. (비밀번호 칸이 비어 있으면 비밀번호도 그대로)
            bool unchanged = title == originalTitle && maxPlayers == originalMaxPlayers
                             && privateRoom == wasPrivate && string.IsNullOrEmpty(password);
            if (unchanged)
            {
                Close();
                return;
            }

            SetRequesting(true);
            statusMessage?.ShowInfo("방 설정을 변경하는 중...", keep: true);

            long requestRoomId = roomId;
            RoomApi.UpdateSettings(requestRoomId, title, maxPlayers, privateRoom, password, result =>
            {
                if (this == null) return;
                SetRequesting(false);
                if (!IsOpen || requestRoomId != roomId) return; // 응답 전에 닫혔음 (방장이 바뀜, 게임 시작, 씬 이동 등)

                if (result.success)
                {
                    Action<string> saved = onSaved;
                    // 비밀번호는 로그에 남기지 않는다.
                    Debug.Log($"[Room] 방 설정 변경 완료: #{requestRoomId} '{title}' 최대 {maxPlayers}명" + (privateRoom ? " [비밀방]" : " [공개방]"));
                    Close();
                    saved?.Invoke("방 설정을 변경했습니다.");
                    return;
                }
                if (result.IsUnauthorized) return; // ApiClient가 세션을 비우고 Title로 보낸다.

                // 403: 방장이 아님(그사이 위임됨), 409: 게임 중 / 현재 인원보다 적음, 400: 형식 오류 → 팝업에 표시하고 다시 입력받는다.
                Debug.LogWarning($"[Room] 방 설정 변경 실패 ({result.statusCode}): {result.message}");
                statusMessage?.ShowError(result.message, keep: true);
            });
        }

        // ------------------------------------------------------------------
        // 내부
        // ------------------------------------------------------------------

        /// <param name="password">비밀방이면 입력한 새 비밀번호 (비워 두면 "" = 기존 유지), 공개방이면 null</param>
        private bool TryReadInputs(out string title, out int maxPlayers, out bool privateRoom, out string password, out string error)
        {
            title = titleInput != null ? titleInput.text.Trim() : originalTitle;
            maxPlayers = 0;
            privateRoom = IsPrivateSelected;
            password = null;
            error = null;

            if (string.IsNullOrEmpty(title))
            {
                error = "방 제목을 입력하세요.";
                return false;
            }
            if (title.Length > titleMaxLength)
            {
                error = $"방 제목은 {titleMaxLength}자 이하로 입력하세요.";
                return false;
            }

            string maxText = maxPlayersInput != null ? maxPlayersInput.text.Trim() : originalMaxPlayers.ToString();
            if (!int.TryParse(maxText, out maxPlayers))
            {
                error = $"최대 인원을 숫자로 입력하세요. ({CreateRoomPopup.MinPlayers}~{CreateRoomPopup.MaxPlayers}명)";
                return false;
            }
            if (maxPlayers < CreateRoomPopup.MinPlayers || maxPlayers > CreateRoomPopup.MaxPlayers)
            {
                error = $"최대 인원은 {CreateRoomPopup.MinPlayers}~{CreateRoomPopup.MaxPlayers}명이어야 합니다.";
                return false;
            }
            if (maxPlayers < currentPlayerCount)
            {
                error = $"현재 인원({currentPlayerCount}명)보다 적게 설정할 수 없습니다.";
                return false;
            }

            if (privateRoom)
            {
                password = passwordInput != null ? passwordInput.text.Trim() : string.Empty;
                if (string.IsNullOrEmpty(password))
                {
                    if (!wasPrivate)
                    {
                        error = $"비밀방으로 바꾸려면 비밀번호를 입력하세요. ({RoomPasswordRule.Description})";
                        return false;
                    }
                    password = string.Empty; // 기존 비밀번호 유지
                }
                else if (!RoomPasswordRule.IsValid(password))
                {
                    error = $"비밀번호는 {RoomPasswordRule.Description}로 입력하세요.";
                    return false;
                }
            }

            return true;
        }

        private void SetRequesting(bool value)
        {
            isRequesting = value;
            if (saveButton != null) saveButton.interactable = !value;
            if (cancelButton != null) cancelButton.interactable = !value;
            if (titleInput != null) titleInput.interactable = !value;
            if (maxPlayersInput != null) maxPlayersInput.interactable = !value;
            if (privateToggle != null) privateToggle.interactable = !value;
            RefreshPasswordInput();
        }

        private static int PlayerCount(RoomDetailResponse room)
        {
            return room.players != null ? room.players.Count : room.currentPlayers;
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
