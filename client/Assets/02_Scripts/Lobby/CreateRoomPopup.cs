using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Common;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using WhoisntCitizen.Chat; // ChatNotice: 내 화면 전용 안내 (채팅창 + 콘솔)
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
    ///
    /// 입력 검증 (서버 규칙과 동일하게 맞춤)
    ///   - 방 제목: 비어 있으면 안 됨, 최대 titleMaxLength자
    ///   - 최대 인원: 숫자, 4~12명
    /// </summary>
    public class CreateRoomPopup : MonoBehaviour
    {
        // 서버 RoleAssigner.MIN_PLAYERS / MAX_PLAYERS 와 같은 값
        public const int MinPlayers = 4;
        public const int MaxPlayers = 12;

        [Header("Input")]
        [SerializeField] private TMP_InputField titleInput;      // Area_Title/InputField (TMP)
        [SerializeField] private TMP_InputField maxPlayersInput; // Area_Member/InputField (TMP) (1)

        [Header("Buttons")]
        [SerializeField] private Button createButton;            // Button_Create
        [SerializeField] private Button cancelButton;            // Button_Cancel

        [Header("Status")]
        [SerializeField] private StatusMessageView statusMessage; // 팝업 안의 StatusMessageText

        [Header("Rules")]
        [Tooltip("방 제목 최대 글자 수")]
        [SerializeField] private int titleMaxLength = 20;

        private bool isRequesting; // 방 생성 요청 중 (중복 생성 방지)
        private bool initialized;

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

            // 인스펙터 OnClick에 직접 연결한 게 없을 때만 코드로 연결 (두 번 호출 방지)
            if (createButton != null && createButton.onClick.GetPersistentEventCount() == 0)
                createButton.onClick.AddListener(OnCreateClicked);
            if (cancelButton != null && cancelButton.onClick.GetPersistentEventCount() == 0)
                cancelButton.onClick.AddListener(OnCancelClicked);
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
            gameObject.SetActive(true);

            ResetInputs();
            SetRequesting(false);
            statusMessage?.ShowInfo($"방 제목과 최대 인원({MinPlayers}~{MaxPlayers}명)을 입력하세요.", keep: true);

            if (titleInput != null)
            {
                titleInput.Select();
                titleInput.ActivateInputField();
            }
        }

        /// <summary>팝업을 닫는다.</summary>
        public void Close()
        {
            statusMessage?.Clear();
            gameObject.SetActive(false);
        }

        private void ResetInputs()
        {
            if (titleInput != null) titleInput.text = string.Empty;
            if (maxPlayersInput != null) maxPlayersInput.text = string.Empty;
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
            if (isRequesting || SceneLoader.IsLoading) return;

            // 1) 클라이언트 1차 검증. 실패하면 서버에 보내지 않는다. (최종 검증은 서버가 다시 한다)
            if (!TryReadInputs(out string title, out int maxPlayers, out string error))
            {
                statusMessage?.ShowError(error, keep: true);
                return;
            }

            // 2) 서버에 방 생성 요청
            SetRequesting(true);
            statusMessage?.ShowInfo("방을 만드는 중...", keep: true);

            RoomApi.CreateRoom(title, maxPlayers, result =>
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

                // 3) 성공: 만든 사람은 방장으로 자동 입장된 상태 → 방 정보를 저장하고 Room 씬으로 이동
                RoomSession.Set(result.data);
                statusMessage?.ShowSuccess($"'{result.data.title}' 방을 만들었습니다. 입장 중...", keep: true);
                Debug.Log($"[Lobby] 방 생성 완료: #{result.data.id} {result.data.title} ({result.data.currentPlayers}/{result.data.maxPlayers})");
                ChatNotice.Post($"'{result.data.title}' 방을 만들었습니다. (최대 {result.data.maxPlayers}명)");

                // 씬 이동이 시작되지 않으면(Build Settings 누락 등) 다시 누를 수 있게 잠금을 푼다.
                if (!SceneLoader.Load(SceneType.Room))
                {
                    SetRequesting(false);
                    statusMessage?.ShowError("Room 씬으로 이동하지 못했습니다. (Build Settings 확인)", keep: true);
                }
            });
        }

        // ------------------------------------------------------------------
        // 내부
        // ------------------------------------------------------------------

        /// <summary>입력값을 읽고 검증한다. 실패하면 error에 안내 문구를 넣는다.</summary>
        private bool TryReadInputs(out string title, out int maxPlayers, out string error)
        {
            title = titleInput != null ? titleInput.text.Trim() : string.Empty;
            maxPlayers = 0;
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
