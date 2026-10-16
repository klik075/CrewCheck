using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Chat;
using WhoisntCitizen.GameUI;

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// Lobby 씬 로비 채팅. 씬 파일을 고치지 않도록 버튼·채팅창을 실행할 때 코드로 만든다. (LobbyUIController가 붙이고 Build를 부른다)
    ///
    ///  - [채팅] 버튼: [방 만들기] 오른편에 둔다. 모양은 Room 씬 [채팅] 버튼(RoomLobbyLayout "OpenChat")과 같다:
    ///    버튼 그림 + 황동 테두리 + 크림색 채팅 아이콘 + 크림색 굵은 CabinFont 글자 (204 x 60)
    ///  - 누르면 방 목록 아래쪽 절반 정도에 채팅창이 열리고, 방 목록은 위쪽으로 줄어든다. (Room 씬 채팅창처럼)
    ///    [채팅] 버튼을 다시 누르거나 채팅창의 [닫기]를 누르면 채팅창이 닫히고 방 목록이 원래 크기로 돌아간다.
    ///  - 채팅창 모양은 Room 씬 채팅창(RoomLobbyLayout.BuildChat)과 같다: 호두나무 판 + 황동 테두리, 크림색 글자, CabinFont,
    ///    입력 바(어두운 바 + 크림색 입력칸 + 금색 [전송] 버튼)
    ///  - 서버 통신은 GameChatController(lobbyChat)가 한다. 채팅창이 닫혀 있어도 로비에 들어온 순간부터 계속 받고,
    ///    닫혀 있는 동안 새 메시지가 오면(미확인 채팅이 있으면) 버튼 오른쪽 위에 빨간 점 하나를 찍는다. (NotificationDot)
    ///    몇 개가 와도 점은 하나뿐이고, 채팅창을 열면 사라진다.
    ///  - 로비에 들어온 뒤의 대화만 보인다. 방으로 이동하면 씬과 함께 사라져 더 받지 않는다.
    /// </summary>
    public class LobbyChatPanel : MonoBehaviour
    {
        [Header("Button")]
        [SerializeField] private string chatButtonLabel = "채팅";
        [SerializeField] private float buttonWidth = 204f;  // Room [채팅] 버튼과 같은 크기
        [SerializeField] private float buttonHeight = 60f;
        [SerializeField] private float buttonGap = 16f;

        [Header("Panel")]
        [SerializeField] private string titleText = "항구 사람들과 대화하기";
        [Tooltip("채팅창이 차지할 방 목록 높이의 비율")]
        [Range(0.3f, 0.8f)]
        [SerializeField] private float heightRatio = 0.5f;
        [Tooltip("채팅창과 줄어든 방 목록 사이 간격")]
        [SerializeField] private float gap = 12f;
        [SerializeField] private int maxMessages = 100;

        // Room 채팅창(RoomLobbyLayout)과 같은 색
        private static readonly Color Ink = new Color32(51, 31, 17, 255);
        private static readonly Color Cream = new Color32(255, 226, 174, 255);
        private const float ButtonFontSize = 26f; // Room [채팅] 버튼 글자 크기
        private const float ButtonIconSize = 42f; // Room AddActionIcon 아이콘 크기
        private const float LineFontSize = 27f;
        private const float InputFontSize = 28f;

        private TMP_FontAsset font;
        private Sprite frameArt, buttonArt, cabinArt;

        private Button chatButton;
        private TMP_Text chatButtonText;
        private NotificationDot unreadDot;          // 미확인 채팅 표시 (빨간 점)
        private RectTransform roomList;          // 채팅창이 열리면 위쪽으로 줄이는 방 목록 영역
        private Vector2 roomListOffsetMin;       // 방 목록의 원래 아래쪽 여백
        private RectTransform panel;
        private ChatLogView chatLog;
        private RectTransform logContent;
        private TMP_InputField input;
        private ChatInputBar inputBar;
        private GameChatController controller;

        public bool IsOpen { get; private set; }

        /// <param name="createRoomButton">[방 만들기] 버튼. 이 버튼 오른편에 [채팅] 버튼을 만든다</param>
        /// <param name="roomListArea">방 목록 영역. 채팅창이 열리면 아래쪽 절반에 채팅창을 놓고 이 영역은 위쪽으로 줄인다</param>
        public void Build(Button createRoomButton, RectTransform roomListArea)
        {
            if (panel != null) return;
            if (createRoomButton == null || roomListArea == null)
            {
                Debug.LogError("[LobbyChat] 방 만들기 버튼 또는 방 목록 영역이 없어 로비 채팅을 만들 수 없습니다.");
                return;
            }

            // Room 채팅창과 같은 그림·글꼴 (RoomLobbyLayout.Build와 같은 리소스)
            TMP_Text sampleText = createRoomButton.GetComponentInChildren<TMP_Text>(true);
            font = Resources.Load<TMP_FontAsset>("RoomLobby/CabinFont");
            if (font == null) font = sampleText != null ? sampleText.font : TMP_Settings.defaultFontAsset;
            frameArt = LoadArt("FrameRefined");
            cabinArt = LoadArt("CabinRefined");
            buttonArt = Resources.Load<Sprite>("RoomLobby/Button");

            roomList = roomListArea;
            roomListOffsetMin = roomList.offsetMin;

            Canvas.ForceUpdateCanvases(); // 방 목록·버튼의 실제 크기를 읽기 위해
            BuildButton(createRoomButton, (RectTransform)createRoomButton.transform.parent);
            BuildPanel((RectTransform)roomList.parent);
            BuildController();
            SetOpen(false);
        }

        // ------------------------------------------------------------------
        // 버튼 (디자인은 Room 씬 [채팅] 버튼과 같다: RoomLobbyLayout.MakeButton + AddActionIcon)
        // ------------------------------------------------------------------

        private void BuildButton(Button source, RectTransform parent)
        {
            float width = buttonWidth, height = buttonHeight;
            chatButton = MakeButton("Button_Chat", parent, chatButtonLabel, ButtonFontSize);
            chatButton.onClick.AddListener(Toggle);
            chatButtonText = chatButton.GetComponentInChildren<TMP_Text>(true);

            // 아이콘 + 글자 배치 (RoomLobbyLayout.AddActionIcon과 같다): 왼쪽 20에 아이콘, 70부터 글자
            var iconRt = Rect("ActionIcon", chatButton.transform);
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(20f, 0f);
            iconRt.sizeDelta = new Vector2(ButtonIconSize, ButtonIconSize);
            var icon = iconRt.gameObject.AddComponent<RoomHelmGraphic>();
            icon.Shape = RoomHelmGraphic.Symbol.Chat;
            icon.color = Cream;
            icon.raycastTarget = false;
            Anchor(chatButtonText.rectTransform, Vector2.zero, Vector2.one, new Vector2(70f, 5f), new Vector2(-10f, -5f));
            unreadDot = NotificationDot.AttachTo((RectTransform)chatButton.transform);

            // [방 만들기] 오른편, 세로 가운데에 붙인다. 부모 밖으로 나가면 폭을 줄인다
            var src = (RectTransform)source.transform;
            var rt = (RectTransform)chatButton.transform;
            rt.anchorMin = src.anchorMin;
            rt.anchorMax = src.anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            float srcRight = src.anchoredPosition.x + src.rect.width * (1f - src.pivot.x);
            float srcCenterY = src.anchoredPosition.y + src.rect.height * (0.5f - src.pivot.y);
            float parentRight = parent.rect.width * (1f - src.anchorMin.x); // 앵커 기준 부모 오른쪽 끝
            if (parent.rect.width > 0f && srcRight + buttonGap + width > parentRight - 4f)
                width = Mathf.Max(120f, parentRight - 4f - srcRight - buttonGap);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(srcRight + buttonGap + width * 0.5f, srcCenterY);
            rt.localScale = Vector3.one;
        }

        // ------------------------------------------------------------------
        // 채팅창 (Room 채팅창과 같은 모양)
        // ------------------------------------------------------------------

        private void BuildPanel(RectTransform parent)
        {
            panel = WoodPanel("LobbyChatPanel", parent);
            panel.gameObject.SetActive(false); // 모두 연결한 뒤에 켠다 (입력칸 등이 빈 상태로 OnEnable되지 않게)
            panel.GetComponent<Image>().raycastTarget = true;
            panel.SetSiblingIndex(roomList.GetSiblingIndex() + 1);

            // 제목 + [닫기]
            var title = Label("ChatTitle", panel, titleText, 30f, Cream);
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -80f), new Vector2(-140f, -20f));
            var close = MakeButton("CloseChat", panel, "닫기", 24f);
            Anchor((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-122f, -80f), new Vector2(-32f, -20f));
            close.onClick.AddListener(() => SetOpen(false));

            BuildLog();
            BuildInputBar();
        }

        private void BuildLog()
        {
            // Room의 ChatLogPanel과 같은 구성: 투명한 스크롤 + 아래에 붙는 Content(여백 36·24, 줄 간격 14)
            var log = Rect("ChatLogPanel", panel);
            Anchor(log, Vector2.zero, Vector2.one, new Vector2(28f, 124f), new Vector2(-28f, -95f));
            var scroll = log.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            var viewport = Rect("Viewport", log);
            Anchor(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // 드래그를 받기 위한 투명 그림

            // Content는 Viewport 아래쪽에 붙인다: 메시지가 적으면 아래부터 쌓이고, 최신 메시지가 늘 아래에 보인다 (ChatLogView 참고)
            logContent = Rect("Content", viewport);
            logContent.anchorMin = new Vector2(0f, 0f);
            logContent.anchorMax = new Vector2(1f, 0f);
            logContent.pivot = new Vector2(0.5f, 0f);
            logContent.anchoredPosition = Vector2.zero;
            logContent.sizeDelta = Vector2.zero;
            var vlg = logContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(36, 36, 24, 24);
            vlg.spacing = 14f;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            logContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = logContent;

            // 줄 원본 (RowFactory를 쓰므로 실제로는 복제하지 않지만 ChatLogView 연결용으로 둔다)
            var template = Label("LineTemplate", panel, "", LineFontSize, Cream);
            template.gameObject.SetActive(false);

            chatLog = log.gameObject.AddComponent<ChatLogView>();
            chatLog.Configure(scroll, logContent, (TextMeshProUGUI)template);
            // 줄 모양: Room 채팅창과 같은 크림색 27pt, 줄바꿈 (RoomLobbyLayout.BuildChat의 RowFactory와 같다)
            chatLog.RowFactory = entry =>
            {
                string message = entry.Rich ?? (entry.Text ?? "").Replace("<", "<\u200b");
                float width = Mathf.Max(200f, logContent.rect.width - 72f);
                var line = Label("ChatMessage", null, message, LineFontSize, Cream);
                line.enableAutoSizing = false;
                line.textWrappingMode = TextWrappingModes.Normal;
                line.gameObject.AddComponent<LayoutElement>().preferredHeight =
                    line.GetPreferredValues(message, width, Mathf.Infinity).y + 12f;
                return line.rectTransform;
            };
        }

        private void BuildInputBar()
        {
            // Room 입력 바: 어두운 바 + 크림색 입력칸 + 금색 [전송]
            var bar = Rect("ChatInputBar", panel);
            Anchor(bar, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 26f), new Vector2(-28f, 108f));
            bar.gameObject.AddComponent<Image>().color = new Color32(40, 20, 13, 255);

            Sprite fieldArt = FindLoadedSprite("InputFieldBackground");
            Sprite sendArt = FindLoadedSprite("UISprite");

            // 입력칸
            var fieldRt = Rect("ChatInput", bar);
            Anchor(fieldRt, Vector2.zero, Vector2.one, new Vector2(11f, 11f), new Vector2(-133f, -11f));
            var fieldImage = fieldRt.gameObject.AddComponent<Image>();
            fieldImage.sprite = fieldArt;
            fieldImage.type = fieldArt != null ? Image.Type.Sliced : Image.Type.Simple;
            fieldImage.color = new Color32(250, 233, 197, 255);

            var area = Rect("Text Area", fieldRt);
            Anchor(area, Vector2.zero, Vector2.one, new Vector2(10f, 4f), new Vector2(-10f, -4f));
            area.gameObject.AddComponent<RectMask2D>();

            var placeholder = Label("Placeholder", area, "메시지를 입력하세요", InputFontSize, new Color(0.19607843f, 0.19607843f, 0.19607843f, 0.5f));
            Anchor(placeholder.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var text = Label("Text", area, "", InputFontSize, Ink);
            Anchor(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            text.enableAutoSizing = false;
            text.richText = false;

            input = fieldRt.gameObject.AddComponent<TMP_InputField>();
            #if UNITY_WEBGL && !UNITY_EDITOR
            fieldRt.gameObject.AddComponent<WebGLSupport.WebGLInput>();
            #endif
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.fontAsset = font;
            input.pointSize = InputFontSize;
            input.lineType = TMP_InputField.LineType.SingleLine; // Enter로 보낸다
            input.characterLimit = 1000;                          // 서버 ChatMessage.MAX_MESSAGE_LENGTH
            input.richText = false;
            input.targetGraphic = fieldImage;

            // [전송]
            var sendRt = Rect("SendButton", bar);
            sendRt.anchorMin = sendRt.anchorMax = new Vector2(1f, 0.5f);
            sendRt.pivot = new Vector2(1f, 0.5f);
            sendRt.anchoredPosition = new Vector2(-11f, 0f);
            sendRt.sizeDelta = new Vector2(111f, 58f);
            var sendImage = sendRt.gameObject.AddComponent<Image>();
            sendImage.sprite = sendArt;
            sendImage.type = sendArt != null ? Image.Type.Sliced : Image.Type.Simple;
            sendImage.color = new Color32(242, 193, 78, 255);
            var send = sendRt.gameObject.AddClickSoundButton();
            send.targetGraphic = sendImage;
            var sendLabel = Label("Text (TMP)", sendRt, "전송", InputFontSize, new Color32(27, 30, 38, 255));
            Anchor(sendLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            sendLabel.alignment = TextAlignmentOptions.Center;

            inputBar = bar.gameObject.AddComponent<ChatInputBar>();
            inputBar.Configure(input, send);
        }

        // ------------------------------------------------------------------
        // 서버 연결 (GameChatController를 로비 채팅으로 사용)
        // ------------------------------------------------------------------

        private void BuildController()
        {
            var go = new GameObject("LobbyChat");
            go.SetActive(false); // 연결을 모두 마친 뒤에 켠다 (Awake·Start가 연결된 값으로 돌도록)
            go.transform.SetParent(transform, false);
            var api = go.AddComponent<ChatApiClient>();
            api.lobby = true;
            controller = go.AddComponent<GameChatController>();
            controller.api = api;
            controller.chatLog = chatLog;
            controller.inputBar = inputBar;
            controller.lobbyChat = true;
            controller.autoLogin = false; // 로비에는 로그인해야 들어온다
            controller.maxMessages = maxMessages;
            controller.LiveMessageReceived += OnLiveMessage;
            go.SetActive(true);
        }

        private void OnLiveMessage(ChatMessage message)
        {
            if (IsOpen) return;
            if (unreadDot != null) unreadDot.Show(true);
        }

        // ------------------------------------------------------------------
        // 열기 / 닫기
        // ------------------------------------------------------------------

        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        public void SetOpen(bool open)
        {
            IsOpen = open;
            if (panel == null) return;

            // 방 목록은 원래 크기로 되돌린 뒤 높이를 재서, 아래쪽 heightRatio만큼을 채팅창에 준다
            roomList.offsetMin = roomListOffsetMin;
            if (open)
            {
                Canvas.ForceUpdateCanvases();
                float height = Mathf.Round(roomList.rect.height * heightRatio);
                panel.anchorMin = roomList.anchorMin;
                panel.anchorMax = new Vector2(roomList.anchorMax.x, roomList.anchorMin.y);
                panel.pivot = new Vector2(0.5f, 0f);
                panel.offsetMin = roomListOffsetMin;
                panel.offsetMax = new Vector2(roomList.offsetMax.x, roomListOffsetMin.y + height);
                roomList.offsetMin = new Vector2(roomListOffsetMin.x, roomListOffsetMin.y + height + gap);
            }
            panel.gameObject.SetActive(open);

            if (open)
            {
                if (unreadDot != null) unreadDot.Show(false);
                if (chatLog != null) chatLog.ScrollToLatest();
                if (input != null) input.ActivateInputField();
            }
        }

        private void OnDestroy()
        {
            if (controller != null) controller.LiveMessageReceived -= OnLiveMessage;
        }

        // ------------------------------------------------------------------
        // 모양 도우미 (RoomLobbyLayout의 WoodPanel·Label·MakeButton과 같은 모양)
        // ------------------------------------------------------------------

        /// 호두나무 판 + 황동 테두리
        private RectTransform WoodPanel(string name, Transform parent)
        {
            var root = Rect(name, parent);
            var baseImage = root.gameObject.AddComponent<Image>();
            baseImage.color = new Color32(30, 20, 14, 255);
            baseImage.raycastTarget = false;
            var wood = Rect("Walnut", root).gameObject.AddComponent<RawImage>();
            Fill(wood.rectTransform);
            wood.texture = cabinArt != null ? cabinArt.texture : null;
            wood.uvRect = new UnityEngine.Rect(.26f, .37f, .45f, .29f);
            wood.color = new Color(.52f, .52f, .52f, 1f);
            wood.raycastTarget = false;
            Fill(Box("WoodBrassFrame", root, frameArt, Color.white).rectTransform);
            return root;
        }

        /// 버튼 그림 + 황동 테두리 + 크림색 굵은 글자
        private Button MakeButton(string name, Transform parent, string label, float size)
        {
            var image = Box(name, parent, buttonArt, new Color32(185, 160, 122, 255));
            image.raycastTarget = true;
            Fill(Box("BrassRim", image.transform, frameArt, Color.white).rectTransform);
            var button = image.gameObject.AddClickSoundButton();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.12f, 1.08f);
            colors.pressedColor = new Color(.8f, .8f, .8f);
            colors.disabledColor = new Color(.65f, .65f, .65f, .85f);
            button.colors = colors;
            var text = Label("Label", image.transform, label, size, Cream);
            Anchor(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 5f), new Vector2(-12f, -5f));
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            return button;
        }

        private TMP_Text Label(string name, Transform parent, string text, float size, Color color)
        {
            var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.raycastTarget = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = size * .72f;
            label.fontSizeMax = size;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            return label;
        }

        private static Image Box(string name, Transform parent, Sprite sprite, Color color)
        {
            var image = Rect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = sprite != null && sprite.name.Contains("Refined") ? 4 : 3;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            if (parent != null) rt.SetParent(parent, false);
            return rt;
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void Anchor(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        private static Sprite LoadArt(string name)
        {
            var sprites = Resources.LoadAll<Sprite>("RoomLobby/" + name);
            return sprites.Length > 0 ? sprites[0] : null;
        }

        /// Room 입력 바가 쓰는 Unity 기본 UI 그림(InputFieldBackground, UISprite)은 런타임에 이름으로 불러올 수 없어서,
        /// 로비 씬이 이미 쓰고 있어 메모리에 올라온 것을 찾아 쓴다. 없으면 null (단색으로 그린다)
        private static Sprite FindLoadedSprite(string name)
        {
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sprite != null && string.Equals(sprite.name, name, StringComparison.Ordinal)) return sprite;
            return null;
        }
    }
}
