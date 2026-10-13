using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Common;
using WhoisntCitizen.Game;
using WhoisntCitizen.Chat; // ChatNotice: 내 화면 전용 안내 (채팅창 + 콘솔)
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// 방 만들기 팝업 (Popup_CreateRoom 오브젝트에 부착).
    ///
    /// 동작
    ///   - LobbyUIController가 Button_CreatRoom 클릭 시 Open()을 호출한다.
    ///   - Button_Create (또는 입력칸에서 Enter): 입력 검증 → 방 생성 요청 → 성공하면 Room 씬으로 이동
    ///   - Button_Cancel (또는 ESC): 팝업 닫기
    ///   - 비밀방 Toggle: 켜면 비밀번호 영역(Area_PasswordText)이 나타나고 입력칸에 포커스가 간다.
    ///                    끄면 영역을 숨기고 입력했던 비밀번호를 지운다. (공개방으로 만들 때 비밀번호는 보내지 않음)
    ///
    /// 입력 검증 (서버 규칙과 동일하게 맞춤)
    ///   - 방 제목: 비어 있으면 안 됨, 최대 titleMaxLength자
    ///   - 최대 인원: 숫자, 4~12명
    ///   - 비밀번호(비밀방일 때만): 숫자만, 4자리 이상, 최대 길이 제한 없음 (RoomPasswordRule)
    ///
    /// 비밀방 UI(privateToggle, passwordInput, passwordArea)는 선택이다.
    ///   연결하지 않으면 지금처럼 공개방만 만들 수 있다. (씬 작업 전에도 컴파일·동작에 문제 없도록)
    /// </summary>
    public class CreateRoomPopup : MonoBehaviour
    {
        // 서버 RoleAssigner.MIN_PLAYERS / MAX_PLAYERS 와 같은 값
        public const int MinPlayers = 4;
        public const int MaxPlayers = 12;

        [Header("Input")]
        [SerializeField] private TMP_InputField titleInput;      // Area_Title/InputField (TMP)
        [SerializeField] private TMP_InputField maxPlayersInput; // Area_Member/InputField (TMP) (1)

        [Header("Private Room (선택)")]
        [Tooltip("비밀방 여부 Toggle. 비워 두면 공개방만 만들 수 있다.")]
        [SerializeField] private Toggle privateToggle;           // Area_Private/Toggle
        [Tooltip("비밀번호 입력칸. Content Type은 코드에서 Pin(숫자만 + 가림)으로 바꾼다.")]
        [SerializeField] private TMP_InputField passwordInput;   // Area_PasswordText/InputField (TMP) (1)
        [Tooltip("비밀방일 때만 보이는 비밀번호 영역(라벨 + 입력칸). 비워 두면 입력칸만 활성/비활성으로 바꾼다.")]
        [SerializeField] private GameObject passwordArea;        // Items/Area_PasswordText

        [Header("Buttons")]
        [SerializeField] private Button createButton;            // Button_Create
        [SerializeField] private Button cancelButton;            // Button_Cancel

        private Button editRolesButton;
        private TMP_Text modeInfoText;
        private CreateRoomRoleEditor roleEditor;
        private RoleSetupDto roleSetup;
        private RoleSetupOptionsDto roleOptions;

        [Header("Status")]
        [SerializeField] private StatusMessageView statusMessage; // 팝업 안의 StatusMessageText

        [Header("Rules")]
        [Tooltip("방 제목 최대 글자 수")]
        [SerializeField] private int titleMaxLength = 20;

        private bool isRequesting; // 방 생성 요청 중 (중복 생성 방지)
        private bool initialized;
        private int roleRequestRevision;
        private bool loadingRoleOptions, openRolesWhenReady;

        /// <summary>팝업이 열려 있는지 (LobbyUIController가 자동 새로고침을 멈출 때 사용)</summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <summary>방 생성 요청 중인지</summary>
        public bool IsRequesting => isRequesting;

        // ------------------------------------------------------------------
        // 초기화
        // ------------------------------------------------------------------

        // 팝업은 처음에 꺼져 있어서 Awake가 늦게 불릴 수 있다.
        // 그래서 Open()에서도 호출할 수 있도록 초기화를 따로 뺐다. (한 번만 실행됨)
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;

            if (titleInput != null)
            {
                titleInput.characterLimit = titleMaxLength;
                titleInput.onSubmit.AddListener(_ => OnCreateClicked()); // 입력칸에서 Enter 키
            }

            if (maxPlayersInput != null)
            {
                // 숫자만 입력되도록 설정하고 두 자리로 제한 (최대 12)
                maxPlayersInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                maxPlayersInput.characterLimit = 2;
                maxPlayersInput.ForceLabelUpdate();
                maxPlayersInput.onSubmit.AddListener(_ => OnCreateClicked());
            }

            if (passwordInput != null)
            {
                // Pin: 숫자만 입력되고 화면에는 *로 가려진다. 길이 제한은 두지 않는다. (0 = 무제한)
                passwordInput.contentType = TMP_InputField.ContentType.Pin;
                passwordInput.characterLimit = 0;
                passwordInput.ForceLabelUpdate();
                passwordInput.onSubmit.AddListener(_ => OnCreateClicked());
            }

            // 토글을 켜고 끌 때마다 비밀번호 영역을 보이거나 숨긴다.
            if (privateToggle != null) privateToggle.onValueChanged.AddListener(OnPrivateToggleChanged);

            // 인스펙터 OnClick에 직접 연결한 게 없을 때만 코드로 연결 (두 번 호출 방지)
            if (createButton != null && createButton.onClick.GetPersistentEventCount() == 0)
                createButton.onClick.AddListener(OnCreateClicked);
            if (cancelButton != null && cancelButton.onClick.GetPersistentEventCount() == 0)
                cancelButton.onClick.AddListener(OnCancelClicked);

            // 현재 Lobby 씬은 Items/GameMode가 Background 바로 아래에 있다.
            Transform background = transform.Find("Background");
            editRolesButton = RoleCompositionLayout.BindEntry(background, OpenRoleEditor, out modeInfoText);
            Transform editorRoot = transform.Find("Popup_JobCustom");
            if (editorRoot != null)
            {
                roleEditor = editorRoot.GetComponent<CreateRoomRoleEditor>();
                if (roleEditor == null) roleEditor = editorRoot.gameObject.AddComponent<CreateRoomRoleEditor>();
                roleEditor.Initialize(message => statusMessage?.ShowError(message, keep: true), RefreshRoleMode);
                roleEditor.Close();
            }
        }

        private void Awake()
        {
            Initialize();
        }

        private void Update()
        {
            // ESC로 팝업 닫기 (요청 중에는 닫지 않음)
            if (EscapePressed() && !isRequesting) Close();
        }

        // ------------------------------------------------------------------
        // 열기 / 닫기
        // ------------------------------------------------------------------

        /// <summary>팝업을 열고 입력칸을 초기화한 뒤 제목 입력칸에 포커스를 준다.</summary>
        public void Open()
        {
            Initialize();
            InvalidateRoleRequest();
            gameObject.SetActive(true);

            ResetInputs();
            SetRequesting(false);
            statusMessage?.ShowInfo($"방 제목과 최대 인원({MinPlayers}~{MaxPlayers}명)을 입력하세요."
                + (privateToggle != null ? $"\n비밀방은 비밀번호({RoomPasswordRule.Description})가 필요합니다." : ""), keep: true);
            LoadRoleOptions();

            if (titleInput != null)
            {
                titleInput.Select();
                titleInput.ActivateInputField();
            }
        }

        /// <summary>팝업을 닫는다.</summary>
        public void Close()
        {
            if (roleEditor != null && roleEditor.IsOpen)
            {
                roleEditor.Close();
                return;
            }
            statusMessage?.Clear();
            InvalidateRoleRequest();
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            InvalidateRoleRequest();
            roleEditor?.Close();
        }

        private void InvalidateRoleRequest()
        {
            roleRequestRevision++;
            loadingRoleOptions = openRolesWhenReady = false;
        }

        private void ResetInputs()
        {
            if (titleInput != null) titleInput.text = string.Empty;
            if (maxPlayersInput != null) maxPlayersInput.text = string.Empty;

            // 팝업을 열 때마다 공개방 + 빈 비밀번호로 시작한다. (이전에 입력한 비밀번호가 남지 않도록)
            // SetIsOnWithoutNotify는 onValueChanged를 부르지 않으므로 지우기·숨기기를 직접 한다.
            if (privateToggle != null) privateToggle.SetIsOnWithoutNotify(false); // 토글 값 변경만, onValueChanged는 실행 안 함
            ClearPassword();
            RefreshPasswordInput();
            roleSetup = new RoleSetupDto { mode = RoleSetupModes.Recommended };
            if (roleOptions?.randomCandidates != null)
                roleSetup.randomCandidates.AddRange(roleOptions.randomCandidates);
            roleEditor?.Close();
            RefreshRoleMode();

            // 여기서 isOn = false를 썼다면, 토글이 켜져 있던 경우 OnPrivateToggleChanged(false)가 실행됨 
            // 지금 코드에서는 저렇게 해도 결과는 같지만, 팝업 초기화가 "사용자가 토글을 끈 것"처럼 처리되는 건 의도와 다르고, 
            // 나중에 OnPrivateToggleChanged에 효과음이나 안내 메시지 같은 걸 넣으면 팝업을 열 때마다 그게 실행돼되기 때문에 이벤트 없이 값만 변경 하록 함 
        }

        /// <summary>비밀방 토글이 켜져 있는지 (토글을 연결하지 않았으면 항상 공개방)</summary>
        private bool IsPrivateSelected => privateToggle != null && privateToggle.isOn;

        /// <summary>
        /// 비밀방 Toggle을 사용자가 켜고 끌 때 호출된다. (privateToggle.onValueChanged)
        ///   켜면: 비밀번호 영역을 보여 주고 바로 입력할 수 있게 포커스를 옮긴다.
        ///   끄면: 입력했던 비밀번호를 지우고 영역을 숨긴다. (다시 켜면 빈 칸부터 시작)
        /// </summary>
        private void OnPrivateToggleChanged(bool isOn)
        {
            if (!isOn) ClearPassword();
            RefreshPasswordInput();

            if (isOn && passwordInput != null && passwordInput.interactable && gameObject.activeInHierarchy)
            {
                passwordInput.Select();
                passwordInput.ActivateInputField();
            }
        }

        /// <summary>
        /// 비밀번호 영역 표시와 입력 가능 여부를 현재 상태에 맞춘다.
        ///   - 영역(passwordArea): 비밀방을 선택했을 때만 보인다. (요청 중에도 보이기는 한다)
        ///   - 입력칸: 비밀방 선택 && 요청 중이 아닐 때만 입력할 수 있다.
        /// 포커스는 옮기지 않는다. (요청 실패 후 SetRequesting(false)에서 불려도 커서가 튀지 않도록)
        /// </summary>
        private void RefreshPasswordInput()
        {
            bool visible = IsPrivateSelected;
            if (passwordArea != null && passwordArea.activeSelf != visible) passwordArea.SetActive(visible);

            if (passwordInput != null) passwordInput.interactable = visible && !isRequesting;
        }

        /// <summary>입력한 비밀번호를 지운다. (토글 해제, 팝업 열기 시)</summary>
        private void ClearPassword()
        {
            if (passwordInput == null) return;
            passwordInput.text = string.Empty;
            passwordInput.DeactivateInputField(); // 입력 중(커서 깜빡임) 상태였다면 끝낸다
        }

        // ------------------------------------------------------------------
        // 버튼
        // ------------------------------------------------------------------

        // Button_Cancel 클릭
        public void OnCancelClicked()
        {
            if (isRequesting) return;
            Close();
        }

        // Button_Create 클릭 (또는 입력칸에서 Enter)
        public void OnCreateClicked()
        {
            if (isRequesting || SceneLoader.IsLoading || (roleEditor != null && roleEditor.IsOpen)) return;

            // 1) 클라이언트 1차 검증. 실패하면 서버에 보내지 않는다. (최종 검증은 서버가 다시 한다)
            if (!TryReadInputs(out string title, out int maxPlayers, out bool privateRoom, out string password, out string error))
            {
                statusMessage?.ShowError(error, keep: true);
                return;
            }
            if (roleSetup.mode != RoleSetupModes.Recommended)
            {
                if (roleOptions == null)
                {
                    statusMessage?.ShowError("직업 정보를 불러온 뒤 다시 시도해 주세요.", keep: true);
                    return;
                }
                string problem = RoleSetupRules.FirstCustomProblem(roleSetup, roleOptions);
                if (roleSetup.mode == RoleSetupModes.Custom && problem != null)
                {
                    statusMessage?.ShowError(problem, keep: true);
                    return;
                }
            }

            // 2) 서버에 방 생성 요청
            SetRequesting(true);
            statusMessage?.ShowInfo("방을 만드는 중...", keep: true);

            RoomApi.CreateRoom(title, maxPlayers, privateRoom, password, result =>
            {
                // 응답 전에 씬이 바뀌어 이 오브젝트가 파괴됐으면 아무것도 하지 않는다.
                if (this == null) return;

                if (!result.success)
                {
                    // 401(토큰 만료)은 ApiClient가 타이틀로 보내므로 메시지만 표시한다.
                    SetRequesting(false);
                    statusMessage?.ShowError(result.message, keep: true);
                    return;
                }

                // 방 생성 API와 직업 설정 API가 별도라서, 설정을 저장한 뒤에만 입장한다.
                if (roleSetup.mode == RoleSetupModes.Recommended)
                {
                    EnterCreatedRoom(result.data);
                    return;
                }
                statusMessage?.ShowInfo("직업 구성을 저장하는 중...", keep: true);
                RoomApi.SetRoleSetup(result.data.id, roleSetup.Clone(), saved =>
                {
                    if (this == null) return;
                    if (saved.success) { EnterCreatedRoom(result.data); return; }
                    // 설정 실패 시 생성된 빈 방을 정리해 재시도에서 중복 생성되지 않게 한다.
                    RoomApi.LeaveRoom(result.data.id, leave =>
                    {
                        if (this == null) return;
                        SetRequesting(false);
                        statusMessage?.ShowError("직업 구성 저장에 실패했습니다: " + saved.message
                            + (leave.success ? "" : " (생성된 방을 정리하지 못했습니다.)"), keep: true);
                    });
                });
            });
        }

        private void EnterCreatedRoom(RoomResponse room)
        {
            RoomSession.Set(room);
            statusMessage?.ShowSuccess($"'{room.title}' 방을 만들었습니다. 입장 중...", keep: true);
            Debug.Log($"[Lobby] 방 생성 완료: #{room.id} {room.title} ({room.currentPlayers}/{room.maxPlayers})"
                      + (room.privateRoom ? " [비밀방]" : ""));
            ChatNotice.Post($"'{room.title}' {(room.privateRoom ? "비밀방" : "방")}을 만들었습니다. (최대 {room.maxPlayers}명)");
            if (!SceneLoader.Load(SceneType.Room))
            {
                SetRequesting(false);
                statusMessage?.ShowError("Room 씬으로 이동하지 못했습니다. (Build Settings 확인)", keep: true);
            }
        }

        private void LoadRoleOptions(bool openWhenReady = false)
        {
            if (roleOptions != null) return;
            openRolesWhenReady |= openWhenReady;
            if (loadingRoleOptions) return;
            loadingRoleOptions = true;
            int requestRevision = roleRequestRevision;
            RoomApi.GetRoleSetupOptions(result =>
            {
                if (this == null || !IsOpen || requestRevision != roleRequestRevision) return;
                loadingRoleOptions = false;
                bool openEditor = openRolesWhenReady;
                openRolesWhenReady = false;
                if (!result.success || result.data == null || result.data.roles == null || result.data.roles.Count == 0)
                {
                    statusMessage?.ShowError("직업 정보를 불러오지 못했습니다. ‘직업 구성 수정하기’를 눌러 다시 시도해 주세요. 추천 구성으로 방 만들기는 가능합니다.", keep: true);
                    return;
                }
                roleOptions = result.data;
                roleSetup.randomCandidates = new System.Collections.Generic.List<string>(roleOptions.randomCandidates ?? new System.Collections.Generic.List<string>());
                RefreshRoleMode();
                if (openEditor && !isRequesting && !SceneLoader.IsLoading) OpenRoleEditor();
            });
        }

        private void OpenRoleEditor()
        {
            if (!IsOpen || isRequesting || SceneLoader.IsLoading || (roleEditor != null && roleEditor.IsOpen)) return;
            if (roleEditor == null)
            {
                statusMessage?.ShowError("직업 정보를 불러오지 못해 편집기를 열 수 없습니다.", keep: true);
                return;
            }
            if (roleOptions == null)
            {
                statusMessage?.ShowInfo("직업 구성을 불러오는 중...", keep: true);
                LoadRoleOptions(openWhenReady: true);
                return;
            }
            statusMessage?.Clear();
            int count = int.TryParse(maxPlayersInput?.text, out int selected) ? selected : 8;
            roleEditor.Open(roleSetup, roleOptions, count);
        }

        private void RefreshRoleMode()
        {
            if (roleSetup == null) return;
            if (modeInfoText != null)
                modeInfoText.text = roleSetup.mode == RoleSetupModes.Custom
                    ? $"직접 설정 · {roleSetup.customCompositions.Count}개 인원 구성 수정됨"
                    : roleSetup.mode == RoleSetupModes.Random
                        ? $"랜덤 구성 · 특수 직업 후보 {roleSetup.randomCandidates.Count}개"
                        : "추천 구성 · 4–12명 인원별 편집 / 랜덤 후보 설정";
        }

        // ------------------------------------------------------------------
        // 내부
        // ------------------------------------------------------------------

        /// <summary>입력값을 읽고 검증한다. 실패하면 error에 안내 문구를 넣는다.</summary>
        /// <param name="privateRoom">비밀방 토글 상태</param>
        /// <param name="password">비밀방이면 입력한 비밀번호, 공개방이면 null</param>
        private bool TryReadInputs(out string title, out int maxPlayers, out bool privateRoom, out string password, out string error)
        {
            title = titleInput != null ? titleInput.text.Trim() : string.Empty;
            maxPlayers = 0;
            privateRoom = IsPrivateSelected;
            password = null;
            error = null;

            if (string.IsNullOrEmpty(title))
            {
                error = "방 제목을 입력하세요.";
                ChatNotice.Post("방 제목을 입력해 주세요.", false);
                return false;
            }

            if (title.Length > titleMaxLength)
            {
                error = $"방 제목은 {titleMaxLength}자 이하로 입력하세요.";
                return false;
            }

            string maxText = maxPlayersInput != null ? maxPlayersInput.text.Trim() : string.Empty;
            if (!int.TryParse(maxText, out maxPlayers))
            {
                error = $"최대 인원을 숫자로 입력하세요. ({MinPlayers}~{MaxPlayers}명)";
                return false;
            }

            if (maxPlayers < MinPlayers || maxPlayers > MaxPlayers)
            {
                error = $"최대 인원은 {MinPlayers}~{MaxPlayers}명이어야 합니다.";
                return false;
            }

            // 비밀방일 때만 비밀번호 검사. 공개방이면 입력칸에 뭐가 있든 보내지 않는다.
            if (privateRoom)
            {
                password = passwordInput != null ? passwordInput.text.Trim() : string.Empty;
                if (string.IsNullOrEmpty(password))
                {
                    error = $"비밀번호를 입력하세요. ({RoomPasswordRule.Description})";
                    return false;
                }
                if (!RoomPasswordRule.IsValid(password))
                {
                    error = $"비밀번호는 {RoomPasswordRule.Description}로 입력하세요.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>요청 중에는 버튼과 입력칸을 잠가서 중복 요청을 막는다.</summary>
        private void SetRequesting(bool value)
        {
            isRequesting = value;
            if (createButton != null) createButton.interactable = !value;
            if (cancelButton != null) cancelButton.interactable = !value;
            if (titleInput != null) titleInput.interactable = !value;
            if (maxPlayersInput != null) maxPlayersInput.interactable = !value;
            if (privateToggle != null) privateToggle.interactable = !value;
            if (editRolesButton != null) editRolesButton.interactable = !value;
            RefreshPasswordInput(); // 비밀번호 입력칸은 "비밀방 선택 && 요청 중 아님"일 때만 활성화
        }

        /// <summary>
        /// 이번 프레임에 ESC가 눌렸는지.
        /// 프로젝트가 새 Input System을 쓰면 Keyboard, 아니면 기존 Input 클래스를 사용한다.
        /// </summary>
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
