using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.GameUI
{
    public enum BottomTabMode
    {
        Closed, // 탭 바만 보임
        Chat,   // 채팅 입력(키보드) 패널이 올라옴
        Vote,   // 투표 패널이 올라옴
    }

    // 화면 하단 탭 바(채팅 입력 / + 버튼 / 전송 버튼)와 그 아래 서랍(Drawer)을 관리한다.
    // 채팅 패널과 투표 패널은 같은 Drawer 영역(같은 크기)을 공유하고, 열리면 탭 바와 함께 위로 올라온다.
    //
    // 구조 (BottomSheet = 이 컴포넌트가 움직이는 RectTransform, 하단 기준 가로 stretch)
    //   BottomSheet
    //   ├─ TabBar   : [+] [채팅 입력..........] [전송]
    //   └─ Drawer   : ChatKeyboardPanel / VotePanel 중 하나만 활성화
    // 닫힘: Drawer 높이만큼 아래로 내려가 탭 바만 보인다. 열림: y = 0.
    public class BottomTabController : MonoBehaviour
    {
        [Header("Sheet")]
        [SerializeField] private RectTransform sheet;
        [SerializeField] private float drawerHeight = 800f;
        [SerializeField] private float slideDuration = 0.2f;

        [Header("Tab Bar")]
        [SerializeField] private TMP_InputField chatInput;
        [SerializeField] private Button plusButton;
        [SerializeField] private Button sendButton;

        [Header("Drawer Contents (같은 크기를 공유)")]
        [SerializeField] private GameObject chatPanel;
        [SerializeField] private GameObject votePanel;

        [Header("Etc")]
        [Tooltip("서랍이 열려 있을 때 바깥(게임 화면)을 누르면 닫히게 하는 반투명 버튼")]
        [SerializeField] private Button dimmer;

        public event Action<string> ChatSubmitted;
        public event Action<BottomTabMode> ModeChanged;

        public BottomTabMode Mode { get; private set; } = BottomTabMode.Closed;

        private Coroutine slideRoutine;

        private void Awake()
        {
            if (sheet == null) sheet = (RectTransform)transform;
            ApplyContent(BottomTabMode.Closed);
            SetSheetY(-drawerHeight);
            if (dimmer != null) dimmer.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (chatInput != null)
            {
                chatInput.onSelect.AddListener(OnChatInputSelected);
                chatInput.onSubmit.AddListener(OnChatInputSubmit);
            }
            if (plusButton != null) plusButton.onClick.AddListener(OnPlusClicked);
            if (sendButton != null) sendButton.onClick.AddListener(OnSendClicked);
            if (dimmer != null) dimmer.onClick.AddListener(Close);
        }

        private void OnDisable()
        {
            if (chatInput != null)
            {
                chatInput.onSelect.RemoveListener(OnChatInputSelected);
                chatInput.onSubmit.RemoveListener(OnChatInputSubmit);
            }
            if (plusButton != null) plusButton.onClick.RemoveListener(OnPlusClicked);
            if (sendButton != null) sendButton.onClick.RemoveListener(OnSendClicked);
            if (dimmer != null) dimmer.onClick.RemoveListener(Close);
        }

        // ---------- 외부에서 호출 ----------

        public void OpenChat() => SetMode(BottomTabMode.Chat);
        public void OpenVote() => SetMode(BottomTabMode.Vote);
        public void Close() => SetMode(BottomTabMode.Closed);

        public void SetMode(BottomTabMode mode)
        {
            if (mode == Mode) return;
            Mode = mode;

            // 투표/닫힘으로 바뀌면 입력 포커스를 해제한다. (모바일에서는 OS 키보드도 내려간다)
            if (mode != BottomTabMode.Chat && chatInput != null && chatInput.isFocused)
                chatInput.DeactivateInputField();

            // 열 때는 내용을 먼저 바꾸고, 닫을 때는 다 내려간 뒤에 끈다.
            if (mode != BottomTabMode.Closed) ApplyContent(mode);
            if (dimmer != null) dimmer.gameObject.SetActive(mode != BottomTabMode.Closed);

            Slide(mode == BottomTabMode.Closed ? -drawerHeight : 0f);
            ModeChanged?.Invoke(mode);
        }

        // ---------- 탭 바 입력 ----------

        private void OnChatInputSelected(string _) => SetMode(BottomTabMode.Chat);

        // + 버튼: 투표 패널 토글 (채팅 중에 누르면 투표 패널로 전환)
        private void OnPlusClicked() =>
            SetMode(Mode == BottomTabMode.Vote ? BottomTabMode.Closed : BottomTabMode.Vote);

        private void OnSendClicked() => Submit(chatInput != null ? chatInput.text : null);

        private void OnChatInputSubmit(string text) => Submit(text);

        private void Submit(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            ChatSubmitted?.Invoke(text);
            chatInput.text = "";
            chatInput.ActivateInputField(); // 연속 입력이 가능하도록 포커스 유지
        }

        // ---------- 연출 ----------

        private void ApplyContent(BottomTabMode mode)
        {
            if (chatPanel != null) chatPanel.SetActive(mode == BottomTabMode.Chat);
            if (votePanel != null) votePanel.SetActive(mode == BottomTabMode.Vote);
        }

        private void Slide(float targetY)
        {
            if (slideRoutine != null) StopCoroutine(slideRoutine);
            if (!isActiveAndEnabled || slideDuration <= 0f)
            {
                SetSheetY(targetY);
                OnSlideFinished();
                return;
            }
            slideRoutine = StartCoroutine(SlideRoutine(targetY));
        }

        private IEnumerator SlideRoutine(float targetY)
        {
            float startY = sheet.anchoredPosition.y;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / slideDuration;
                SetSheetY(Mathf.Lerp(startY, targetY, Mathf.SmoothStep(0f, 1f, t)));
                yield return null;
            }
            SetSheetY(targetY);
            slideRoutine = null;
            OnSlideFinished();
        }

        private void OnSlideFinished()
        {
            if (Mode == BottomTabMode.Closed) ApplyContent(BottomTabMode.Closed);
        }

        private void SetSheetY(float y)
        {
            if (sheet == null) return;
            Vector2 p = sheet.anchoredPosition;
            p.y = y;
            sheet.anchoredPosition = p;
        }
    }
}
