using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Common;
using WhoisntCitizen.GameUI;

namespace WhoisntCitizen.Lobby
{
    /// <summary>Room 전용 선실 UI. 기존 컨트롤러의 통신/준비/관리 흐름에 새 뷰를 연결한다.</summary>
    public sealed class RoomLobbyLayout : MonoBehaviour
    {
        public TMP_Text Title { get; private set; }
        public TMP_Text MemberCount { get; private set; }
        public RectTransform Content { get; private set; }
        public Button StartButton { get; private set; }
        public Button ReadyButton { get; private set; }
        public Button ExitButton { get; private set; }
        public Button SettingsButton { get; private set; }
        public Button InviteButton { get; private set; }
        public StatusMessageView Status { get; private set; }
        public TMP_Text ReadyLabel { get; private set; }
        public RoomSettingsPanel Settings { get; private set; }

        internal static readonly Color Ink = new Color32(51, 31, 17, 255);
        internal static readonly Color Cream = new Color32(255, 226, 174, 255);
        internal static readonly Color Gold = new Color32(198, 148, 67, 255);
        internal TMP_FontAsset Font { get; private set; }
        internal Sprite Paper { get; private set; }
        internal Sprite Frame { get; private set; }
        internal Sprite ButtonArt { get; private set; }
        internal RectTransform Root { get; private set; }
        private RectTransform safeArea;
        private TMP_Text roomCode, privacy, rosterHeading, hint;
        private ScrollRect rosterScroll;
        private GameObject chatSheet;
        private CanvasGroup statusBackdrop;
        private DesignSpriteCatalog portraits;
        private Sprite cabinArt, helmArt;
        private RoomHelmGraphic privacyIcon;
        private static readonly string[] PortraitIds = { "CREW_CAPTAIN", "CREW_DOCTOR", "PIRATE_RAIDER", "CREW_BOATSWAIN", "CREW_LOOKOUT", "CREW_MONKEY" };

        public void Build(Canvas canvas, TMP_FontAsset font, Transform oldContents, ChatLogView chatLog, ChatInputBar chatInput)
        {
            Font = Resources.Load<TMP_FontAsset>("RoomLobby/CabinFont") ?? font;
            Paper = LoadArt("ParchmentRefined");
            Frame = LoadArt("FrameRefined");
            cabinArt = LoadArt("CabinRefined");
            helmArt = LoadArt("HelmRefined");
            ButtonArt = Resources.Load<Sprite>("RoomLobby/Button");
            portraits = Resources.Load<DesignSpriteCatalog>("GameHud/Portraits");
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = .5f;
            }
            safeArea = canvas.transform.Find("SafeArea") as RectTransform;
            if (safeArea == null) safeArea = (RectTransform)canvas.transform;
            if (oldContents != null) oldContents.gameObject.SetActive(false);
            var oldBackground = canvas.transform.Find("Background");
            if (oldBackground != null) oldBackground.gameObject.SetActive(false);

            var backdrop = Box("CabinBackground", canvas.transform, cabinArt, Color.white);
            backdrop.transform.SetAsFirstSibling();
            Fill(backdrop.rectTransform);
            var aspect = backdrop.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            aspect.aspectRatio = backdrop.sprite != null ? backdrop.sprite.rect.width / backdrop.sprite.rect.height : 9f / 16;
            Root = Rect("CaptainCabinLayout", safeArea);
            Root.sizeDelta = new Vector2(1080, 1920);
            Root.anchorMin = Root.anchorMax = Root.pivot = new Vector2(.5f, .5f);
            Fit();

            var titlePlate = WoodPanel("RoomTitlePlate", Root, 48, 76, 676, 170);
            Panel("TitleParchment", titlePlate, 18, 18, 640, 134, Paper, Color.white);
            Title = Label("RoomTitle", titlePlate, 174,  30, 460,  70, "방 정보를 불러오는 중", 44, Ink);
            Title.fontStyle = FontStyles.Bold;
            Title.richText = false;
            roomCode = Label("RoomNumber", titlePlate, 177, 104, 440, 32, "# —", 26, Ink);
            var helm = Box("Helm", titlePlate, helmArt, Color.white); Place(helm.rectTransform, 10, 11, 150, 150); helm.preserveAspect = true;
            var info = WoodPanel("RoomInfoBar", Root, 58, 254, 660, 66);
            Icon("People", info, 25, 14, 40, RoomHelmGraphic.Symbol.Group, Cream);
            MemberCount = Label("MemberCount", info, 78, 13, 120, 40, "— / —", 25, Cream);
            Panel("Divider1", info, 210, 18, 1, 30, null, Gold);
            Icon("ModeIcon", info, 237, 15, 36, RoomHelmGraphic.Symbol.Helm, Cream);
            Label("GameMode", info, 292, 13, 152, 40, "일반 모드", 25, Cream);
            Panel("Divider2", info, 445, 18, 1, 30, null, Gold);
            privacyIcon = Icon("PrivacyIcon", info, 478, 14, 38, RoomHelmGraphic.Symbol.Lock, Cream);
            privacy = Label("Privacy", info, 534, 13, 112, 40, "확인 중", 25, Cream);

            var board = WoodPanel("CrewBoard", Root, 40, 506, 1000, 1140);
            board.GetComponent<Image>().raycastTarget = true;
            var heading = Panel("RosterTitlePlate", board,  30, 24, 380, 62, Paper, Color.white);
            Icon("RosterIcon", heading, 16, 14, 32, RoomHelmGraphic.Symbol.Helm, Gold);
            rosterHeading = Label("RosterCount", heading, 60, 10, 300, 42, "참가자", 30, Ink);
            rosterHeading.fontStyle = FontStyles.Bold;
            var chat = MakeButton("OpenChat", Root, 800, 532, 204, 60, "채팅", 26);
            AddActionIcon(chat, RoomHelmGraphic.Symbol.Chat);
            chat.onClick.AddListener(() => { if (chatSheet != null) chatSheet.SetActive(!chatSheet.activeSelf); });

            var viewport = Rect("RosterViewport", board); Place(viewport, 30, 104, 920, 958);
            viewport.gameObject.AddComponent<RectMask2D>();
            Content = Rect("Participants", viewport);
            Content.anchorMin = new Vector2(0, 1); Content.anchorMax = Vector2.one;
            Content.pivot = new Vector2(.5f, 1); Content.anchoredPosition = Vector2.zero; Content.sizeDelta = Vector2.zero;
            var list = Content.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 4; list.padding = new RectOffset(0, 0, 6, 6);
            list.childControlWidth = true; list.childControlHeight = true;
            list.childForceExpandWidth = true; list.childForceExpandHeight = false;
            var fit = Content.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            rosterScroll = board.gameObject.AddComponent<ScrollRect>();
            rosterScroll.viewport = viewport; rosterScroll.content = Content;
            rosterScroll.horizontal = false; rosterScroll.vertical = true; rosterScroll.movementType = ScrollRect.MovementType.Clamped;
            rosterScroll.scrollSensitivity = 42;
            var track = Panel("ScrollTrack", board, 963, 116, 6, 940, null, new Color32( 19, 12, 8, 180));
            var handle = Box("Handle", track, null, Gold); Fill(handle.rectTransform);
            track.GetComponent<Image>().raycastTarget = true;
            handle.raycastTarget = true;
            var scrollbar = track.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle.rectTransform;
            scrollbar.direction = Scrollbar.Direction.BottomToTop; scrollbar.targetGraphic = handle;
            rosterScroll.verticalScrollbar = scrollbar;

            Panel("FooterLine", board, 44, 1080, 912, 1, null, new Color32(151, 111, 54, 190));
            hint = Label("ReadyHint", Root, 104, 1598, 872, 36, "선원들이 모이면 항해를 시작할 수 있어요", 22, Cream);
            hint.alignment = TextAlignmentOptions.Center;
            ExitButton = MakeButton("ExitRoom", Root, 48, 1662, 218, 104, "방 나가기", 25);
            SettingsButton = MakeButton("RoomSettings", Root, 274, 1662, 218, 104, "방 설정", 26);
            InviteButton = MakeButton("InviteCrew", Root, 500, 1662, 218, 104, "초대하기", 26);
            StartButton = MakeButton("StartGame", Root, 730, 1662, 302, 104, "게임 시작", 35, true);
            ReadyButton = MakeButton("Ready", Root, 730, 1662, 302, 104, "준비", 35, true);
            ReadyLabel = ReadyButton.GetComponentInChildren<TMP_Text>();
            AddActionIcon(ExitButton, RoomHelmGraphic.Symbol.Exit);
            AddActionIcon(SettingsButton, RoomHelmGraphic.Symbol.Settings);
            AddActionIcon(InviteButton, RoomHelmGraphic.Symbol.Invite);
            AddActionIcon(StartButton, RoomHelmGraphic.Symbol.Helm);
            AddActionIcon(ReadyButton, RoomHelmGraphic.Symbol.Helm);
            var statusBack = Panel("StatusBackdrop", Root, 64, 1790, 952, 90, null, new Color32(23, 16, 11, 210));
            var statusText = Label("Status", statusBack, 20, 8, 912, 74, "", 24, Cream);
            statusText.textWrappingMode = TextWrappingModes.Normal; statusText.alignment = TextAlignmentOptions.Center;
            Status = statusText.gameObject.AddComponent<StatusMessageView>();
            statusBackdrop = statusBack.gameObject.AddComponent<CanvasGroup>();
            statusBackdrop.blocksRaycasts = false; statusBackdrop.alpha = 0;

            BuildChat(chatLog, chatInput);
            Settings = Root.gameObject.AddComponent<RoomSettingsPanel>(); Settings.Build(this);
            var popup = canvas.transform.Find("Popup");
            if (popup != null)
            {
                popup.SetAsLastSibling();
                foreach (var text in popup.GetComponentsInChildren<TMP_Text>(true)) text.font = Font;
            }
        }

        private void LateUpdate()
        {
            Fit();
            if (statusBackdrop != null) statusBackdrop.alpha = string.IsNullOrEmpty(Status.CurrentMessage) ? 0 : 1;
        }

        private void AddActionIcon(Button button, RoomHelmGraphic.Symbol symbol)
        {
            float height = button.GetComponent<RectTransform>().sizeDelta.y;
            Icon("ActionIcon", button.transform, 20, (height - 42) / 2, 42, symbol, Cream);
            var label = button.GetComponentInChildren<TMP_Text>();
            Place(label.rectTransform, 70, 5, button.GetComponent<RectTransform>().sizeDelta.x -  80, height - 10);
        }
        private void Fit()
        {
            if (Root == null || safeArea == null) return;
            float scale = Mathf.Min(safeArea.rect.width / 1080f, safeArea.rect.height / 1920f);
            Root.localScale = Vector3.one * Mathf.Max(.01f, scale);
        }

        public void RenderInfo(RoomDetailResponse room)
        {
            int count = room.players?.Count ?? 0;
            roomCode.text = $"#{room.id}";
            privacy.text = room.privateRoom ? "비공개" : "공개";
            privacyIcon.Shape = room.privateRoom ? RoomHelmGraphic.Symbol.Lock : RoomHelmGraphic.Symbol.Group;
            rosterHeading.text = $"참가자  ({count} / {room.maxPlayers})";
            int ready = 0;
            if (room.players != null) foreach (var p in room.players) if (p.userId != room.hostUserId && p.ready) ready++;
            hint.text = count < 4 ? $"항해 시작까지 {4 - count}명이 더 필요해요" :
                room.AllGuestsReady() ? "모두 준비 완료! 방장이 항해를 시작할 수 있어요" : $"준비 완료 {ready} / {Mathf.Max(0, count - 1)}  ·  선원들의 준비를 기다리는 중";
            Settings.RefreshPermissions(RoomSession.IsHost && !room.IsInGame);
        }

        public RoomPlayerView CreatePlayer(RoomPlayerResponse player, bool isHost, bool isMe)
        {
            var row = Box($"Crew_{player.userId}", Content, Paper, Color.white);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 102;
            var mask = Icon("PortraitMask", row.transform, 19, -1, 104, RoomHelmGraphic.Symbol.Disc, new Color32(45,  30, 19, 255));
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var portrait = Box("CrewPortrait", mask.transform, portraits?.Find(PortraitIds[(int)((ulong)player.userId % (ulong)PortraitIds.Length)]), Color.white);
            Place(portrait.rectTransform, -17, -6, 138, 150); portrait.preserveAspect = true;
            Icon("PortraitMedallion", row.transform, 12, -8, 118, RoomHelmGraphic.Symbol.Medallion, Gold);
            var name = Label("Nickname", row.transform, 184, 24, 404, 54, "", 30, Ink);
            name.richText = false; name.fontStyle = FontStyles.Bold;
            var badge = Panel("HostBadge", row.transform, 184, 10, 104, 25, null, new Color32(133, 38, 27, 255));
            Label("HostLabel", badge, 5, 0, 94, 25, "HOST", 18, Cream).alignment = TextAlignmentOptions.Center;
            var self = Icon("SelfBadge", row.transform, 450, 41, 54, RoomHelmGraphic.Symbol.Pill, new Color32(214, 185, 138, 255));
            Place(self.rectTransform, 450, 41, 54, 32);
            Label("SelfLabel", self.transform, 0, 0, 54, 32, "나", 20, Ink).alignment = TextAlignmentOptions.Center;
            var stateIcon = Icon("StateIcon", row.transform, 650, 31, 36, RoomHelmGraphic.Symbol.Ready, Gold);
            var state = Label("ReadyState", row.transform, 694, 27, 133, 48, "", 23, Ink);
            var manageImage = Box("ManageCrew", row.transform, null, Color.clear);
            Place(manageImage.rectTransform, 838,  20,  60, 62); manageImage.raycastTarget = true;
            var manage = manageImage.gameObject.AddClickSoundButton(); manage.targetGraphic = manageImage;
            Label("Label", manage.transform, 0, 0, 60, 62, "···", 30, new Color32(102, 74,  40, 255)).alignment = TextAlignmentOptions.Center;
            var view = row.gameObject.AddComponent<RoomPlayerView>();
            view.ConfigureCabin(name, badge.gameObject, state, manage, self.gameObject, stateIcon);
            return view;
        }

        public void AddEmptySlots(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var row = Box("OpenBerth", Content, Paper, new Color32( 70,  50, 34, 255));
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 102;
                Icon("OpenPortrait", row.transform, 26, 5,  90, RoomHelmGraphic.Symbol.EmptySlot, new Color32(208, 178, 129, 255));
                Label("Waiting", row.transform, 184, 24, 560, 54, "참가 대기 중...", 25, new Color32(196, 170, 133, 255));
            }
        }

        private void BuildChat(ChatLogView chatLog, ChatInputBar input)
        {
            var sheet = WoodPanel("CrewChat", Root, 68, 988, 944, 650);
            sheet.GetComponent<Image>().raycastTarget = true;
            chatSheet = sheet.gameObject;
            Label("ChatTitle", sheet, 30, 20, 650, 60, "선원들과 대화하기", 30, Cream);
            MakeButton("CloseChat", sheet, 822, 20, 90, 60, "닫기", 24).onClick.AddListener(() => chatSheet.SetActive(false));
            if (chatLog != null)
            {
                var r = chatLog.GetComponent<RectTransform>(); r.SetParent(sheet, false); Place(r, 28, 95, 888, 405);
                chatLog.RowFactory = entry =>
                {
                    string message = entry.Rich ?? (entry.Text ?? "").Replace("<", "<\u200b");
                    var line = Label("ChatMessage", null, 0, 0, 848, 44, message, 27, Cream);
                    line.enableAutoSizing = false; line.textWrappingMode = TextWrappingModes.Normal;
                    line.gameObject.AddComponent<LayoutElement>().preferredHeight = line.GetPreferredValues(message, 848, Mathf.Infinity).y + 12;
                    return line.rectTransform;
                };
            }
            if (input != null)
            {
                var r = input.GetComponent<RectTransform>(); r.SetParent(sheet, false); Place(r, 28, 526, 888, 82);
                foreach (var text in input.GetComponentsInChildren<TMP_Text>(true)) { text.font = Font; text.fontSize = 28; }
                var inputBackground = input.GetComponent<Image>();
                if (inputBackground != null) inputBackground.color = new Color32( 40,  20, 13, 255);
                var field = input.GetComponentInChildren<TMP_InputField>(true);
                if (field != null)
                {
                    var image = field.GetComponent<Image>(); if (image != null) image.color = new Color32(250, 233, 197, 255);
                    field.textComponent.color = Ink;
                }
            }
            var oldChat = transform.root.Find("RoomChat");
            if (oldChat != null && oldChat != sheet) oldChat.gameObject.SetActive(false);
            chatSheet.SetActive(false);
        }

        internal TMP_Text Label(string name, Transform parent, float x, float y, float w, float h, string text, float size, Color color)
        {
            var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            Place(label.rectTransform, x, y, w, h); label.font = Font; label.text = text; label.fontSize = size;
            label.color = color; label.raycastTarget = false; label.enableAutoSizing = true;
            label.fontSizeMin = size * .72f; label.fontSizeMax = size; label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis; label.alignment = TextAlignmentOptions.MidlineLeft;
            return label;
        }

        internal Button MakeButton(string name, Transform parent, float x, float y, float w, float h, string label, float size, bool primary = false)
        {
            var image = Box(name, parent, primary ? null : ButtonArt,
                primary ? new Color32(158, 40, 30, 255) : new Color32(185, 160, 122, 255));
            Place(image.rectTransform, x, y, w, h); image.raycastTarget = true;
            var rim = Box("BrassRim", image.transform, Frame, Color.white); Fill(rim.rectTransform);
            var button = image.gameObject.AddClickSoundButton(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(1.15f, 1.12f, 1.08f);
            colors.pressedColor = new Color(.8f, .8f, .8f); colors.disabledColor = new Color(.65f, .65f, .65f, .85f); button.colors = colors;
            var text = Label("Label", image.transform, 12, 5, w - 24, h - 10, label, size, Cream);
            text.alignment = TextAlignmentOptions.Center; text.fontStyle = FontStyles.Bold;
            return button;
        }

        internal static RectTransform Panel(string name, Transform parent, float x, float y, float w, float h, Sprite sprite, Color color)
        { var box = Box(name, parent, sprite, color); Place(box.rectTransform, x, y, w, h); return box.rectTransform; }
        internal static Image Box(string name, Transform parent, Sprite sprite, Color color)
        {
            var image = Rect(name, parent).gameObject.AddComponent<Image>(); image.sprite = sprite;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = sprite != null && sprite.name.Contains("Refined") ? 4 : 3;
            image.color = color; image.raycastTarget = false; return image;
        }
        internal static RectTransform Rect(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform)); obj.layer = 5;
            var rect = (RectTransform)obj.transform; rect.SetParent(parent, false); return rect;
        }
        internal static void Place(RectTransform r, float x, float y, float w, float h)
        { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); r.localScale = Vector3.one; }
        internal static void Fill(RectTransform r)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        internal RectTransform WoodPanel(string name, Transform parent, float x, float y, float w, float h)
        {
            var panel = Panel(name, parent, x, y, w, h, null, new Color32( 30, 20, 14, 255));
            var wood = Rect("Walnut", panel).gameObject.AddComponent<RawImage>(); Fill(wood.rectTransform);
            wood.texture = cabinArt != null ? cabinArt.texture : null;
            wood.uvRect = new UnityEngine.Rect(.26f, .37f, .45f, .29f);
            wood.color = new Color(.52f, .52f, .52f, 1); wood.raycastTarget = false;
            var edge = Box("WoodBrassFrame", panel, Frame, Color.white); Fill(edge.rectTransform);
            return panel;
        }

        internal static RoomHelmGraphic Icon(string name, Transform parent, float x, float y, float size, RoomHelmGraphic.Symbol shape, Color color)
        {
            var rect = Rect(name, parent); Place(rect, x, y, size, size);
            var icon = rect.gameObject.AddComponent<RoomHelmGraphic>(); icon.Shape = shape; icon.color = color; return icon;
        }

        private static Sprite LoadArt(string name)
        {
            var sprites = Resources.LoadAll<Sprite>("RoomLobby/" + name);
            return sprites.Length > 0 ? sprites[0] : null;
        }
    }
}
