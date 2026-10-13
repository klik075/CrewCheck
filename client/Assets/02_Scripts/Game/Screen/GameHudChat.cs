using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.GameUI;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// The chat window of the pirate HUD, laid out at run time over the scene's chat log and bottom sheet (like
    /// <see cref="GameHudLayout"/>, which makes it). One gold frame holds, from the top:
    /// - tabs (전체 / 시스템 / 일반 / 귓속말) and the chat filter (only one player's chat, on 전체 and 일반), which filter
    ///   the chat log;
    /// - the chat log: each line a row with the sender's bust on a name tag and the message (no time: the message gets
    ///   the width, at a size a phone reads);
    /// - the input bar (the bottom sheet's tab bar): a parchment pill with a chat mark (a down arrow closing the drawer
    ///   while it is open) and the send button. The [+] button is hidden: the role panel opens the action drawer.
    /// What the chat says and sends stays with the chat side (<see cref="ChatLogView"/>, GameChatController).
    /// The bare form (no window) can be baked into a scene (<see cref="BakeBare"/>): the log's place and the input bar's
    /// look are then the scene's, and BuildBare only wires them.
    /// </summary>
    public sealed class GameHudChat : MonoBehaviour
    {
        private const string Icons = "Sprites/flaticon/white/128/";
        // In the window (canvas units from its top-left): the tab row, then the log.
        private const float TabTop = 32f, TabHeight = 56f, LogTop = 100f; // the tabs sit under the frame's top rail
        // AddTabRow (bare chat): the tab row inside the log's top edge; the rows scroll under it.
        private const float BareTabTop = 6f, BareTabInset = 8f, BareLogTop = BareTabTop + TabHeight + 8f;
        // A row: name tag on the left, then the message.
        private const float RowPad = 6f, TagWidth = 216f, TagHeight = 52f, BodyLeft = 232f, BodyRight = 14f, BodyPad = 12f;
        // Text sizes in canvas units: the canvas is 1080 wide, so on a phone one unit is about 0.37 pt (30 ≈ 11 pt).
        private const float NameSize = 26f, BodySize = 30f;
        private const float BodyDilate = 0.12f; // the regular weight is thin on a phone: the message is drawn a little heavier

        private static readonly Color Gold = new Color(0.98f, 0.8f, 0.42f);
        private static readonly Color GoldLine = new Color(0.78f, 0.58f, 0.27f);
        private static readonly Color Ink = new Color(0.27f, 0.16f, 0.07f);
        private static readonly Color Parchment = new Color(0.93f, 0.85f, 0.67f);
        private static readonly Color Teal = new Color(0.07f, 0.17f, 0.19f, 0.97f);
        private static readonly Color Cream = new Color(0.93f, 0.9f, 0.84f);
        private static readonly Color Muted = new Color(0.62f, 0.59f, 0.52f);
        // Message text on the dark band, a little lighter than the window's own for the small size.
        private static readonly Color BodyText = new Color(0.97f, 0.95f, 0.9f);
        private static readonly Color SystemText = new Color(0.62f, 0.95f, 0.67f);

        // Made by BuildBare at run time, or once in Edit mode by BakeBare (then saved with the scene and only wired).
        [SerializeField, HideInInspector] private bool baked;
        [SerializeField] private RectTransform chat;
        [SerializeField] private CanvasGroup chatGroup;
        [SerializeField] private ChatLogView log;
        [SerializeField] private TMP_InputField input;
        [SerializeField] private Image chatMark;
        [SerializeField] private Button chatMarkButton;

        private RectTransform canvas, window, dropdown, dropdownList, dropdownBlocker, filterButton;
        private CanvasGroup windowGroup;
        private BottomTabController bottomTab;
        private TextMeshProUGUI filterLabel, emptyLabel;
        private TMP_FontAsset font;
        private DesignSpriteCatalog portraits;
        private Func<long, ChibiCharacterSkin> skinOf;
        private readonly Dictionary<ChatTab, Tab> tabs = new Dictionary<ChatTab, Tab>();
        private Func<Vector4> logArea; // BuildBare: the log's insets (left, top from the top, right, bottom from the bottom)
        private bool drawerOpen;

        private sealed class Tab
        {
            public Image fill, outline, icon;
            public TextMeshProUGUI label;
        }

        /// <param name="chatPanel">The chat log panel (stretched over the canvas; kept in the window's middle).</param>
        /// <param name="sheet">The bottom sheet: its tab bar becomes the window's input bar.</param>
        /// <param name="bottom">Where the window ends, from the canvas bottom (the role panel's top).</param>
        public void Build(RectTransform canvasRect, RectTransform chatPanel, RectTransform sheet, BottomTabController tabController,
            TMP_FontAsset labelFont, DesignSpriteCatalog busts, float top, float bottom)
        {
            canvas = canvasRect;
            chat = chatPanel;
            bottomTab = tabController;
            font = labelFont;
            portraits = busts;
            log = chat != null ? chat.GetComponent<ChatLogView>() : null;

            window = NewRect("ChatWindow", canvas);
            if (chat != null) window.SetSiblingIndex(chat.GetSiblingIndex()); // right under the chat log
            Stretch(window, 10f, top, 10f, bottom);
            windowGroup = window.gameObject.AddComponent<CanvasGroup>();
            Image frame = AddImage(window, Resources.Load<Sprite>("GameHud/ChatFrame"), Color.white);
            frame.type = Image.Type.Sliced;
            Sprite crest = Resources.Load<Sprite>("GameHud/ChatFrameCrest");
            if (crest != null)
            {
                RectTransform crestRect = NewRect("ChatFrameCrest", window);
                crestRect.anchorMin = crestRect.anchorMax = crestRect.pivot = new Vector2(0.5f, 1f);
                crestRect.sizeDelta = crest.rect.size;
                crestRect.anchoredPosition = new Vector2(1.5f, 0f);
                AddImage(crestRect, crest, Color.white);
            }
            // The log lies on a darker band between the tabs and the input bar.
            RectTransform band = NewRect("LogBand", window);
            Stretch(band, 14f, LogTop - 4f, 14f, GameHudMetrics.InputBarHeight - 2f);
            HudShapes.Panel(band, new Color(0.04f, 0.035f, 0.03f, 0.72f), new Color(GoldLine.r, GoldLine.g, GoldLine.b, 0.35f), 10f);
            emptyLabel = NewText(band, "Empty", 24f, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
            Stretch(emptyLabel.rectTransform, 20f, 20f, 20f, 20f);
            emptyLabel.textWrappingMode = TextWrappingModes.Normal;

            BuildTabs(window, TabTop, 24f);
            if (chat != null)
            {
                StyleChat();
                ListenToChat();
            }
            if (sheet != null) BuildInputBar(sheet);
            WireInputBar();
            UpdateEmpty();
        }

        /// <summary>
        /// Without the window (no frame, tabs or filter): the rows and the input bar only, for layouts that place the chat
        /// themselves. area gives the log's insets in the canvas every frame (left, top from the top, right, bottom from
        /// the bottom), so it can follow a moving panel. Baked (BakeBare): only wires; the log stays where the scene has it.
        /// </summary>
        public void BuildBare(RectTransform canvasRect, RectTransform chatPanel, RectTransform sheet, BottomTabController tabController,
            TMP_FontAsset labelFont, DesignSpriteCatalog busts, Func<Vector4> area)
        {
            canvas = canvasRect;
            bottomTab = tabController;
            font = labelFont;
            portraits = busts;
            if (!baked)
            {
                chat = chatPanel;
                logArea = area;
                log = chat != null ? chat.GetComponent<ChatLogView>() : null;
                if (chat != null) StyleChat();
                if (sheet != null) BuildInputBar(sheet);
            }
            ListenToChat();
            WireInputBar();
        }

        /// <summary>
        /// After BuildBare (GameScene3): the tabs (전체 / 시스템 / 일반 / 귓속말) and the chat filter at the top of the chat
        /// log, as in the framed window. They are made at run time inside the log (baked scenes too), and the log's viewport
        /// starts under them, so the log keeps the place the layout or the scene gives it.
        /// </summary>
        public void AddTabRow()
        {
            if (chat == null || log == null || tabs.Count > 0) return;
            ScrollRect scroll = chat.GetComponentInChildren<ScrollRect>(true);
            RectTransform viewport = scroll != null ? scroll.viewport : null;
            if (viewport != null) viewport.offsetMax = new Vector2(viewport.offsetMax.x, viewport.offsetMax.y - BareLogTop);

            emptyLabel = NewText(chat, "Empty", 24f, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
            Stretch(emptyLabel.rectTransform, 20f, BareLogTop + 12f, 20f, 12f);
            emptyLabel.textWrappingMode = TextWrappingModes.Normal;

            BuildTabs(chat, BareTabTop, BareTabInset);
            UpdateEmpty();
        }

        /// <summary>
        /// Edit mode, once (Tools/Game Scene/게임 화면 굽기): BuildBare's styles and input bar in the scene, to be saved with
        /// it, and the log put at area once (left, top from the top, right, bottom from the bottom) instead of every frame.
        /// </summary>
        public void BakeBare(RectTransform canvasRect, RectTransform chatPanel, RectTransform sheet, TMP_FontAsset labelFont, Vector4 area)
        {
            canvas = canvasRect;
            font = labelFont;
            chat = chatPanel;
            log = chat != null ? chat.GetComponent<ChatLogView>() : null;
            if (chat != null)
            {
                StyleChat();
                PlaceLog(area);
            }
            if (sheet != null) BuildInputBar(sheet);
            baked = true;
        }

        /// <summary>Shows or hides the chat log (it keeps receiving lines).</summary>
        public void SetLogVisible(bool visible)
        {
            if (chatGroup == null) return;
            chatGroup.alpha = visible ? 1f : 0f;
            chatGroup.blocksRaycasts = visible;
        }

        /// <summary>The skin each player wears (for the bust on their name tag). Rows made before this have no bust.</summary>
        public void SetSkins(Func<long, ChibiCharacterSkin> skinFor)
        {
            skinOf = skinFor;
        }

        /// <summary>How far the bottom drawer is open (0–1): it covers the window, so the window fades out under it.</summary>
        public void SetOpen(float open)
        {
            if (windowGroup != null) windowGroup.alpha = 1f - open;
            if (chatGroup != null) chatGroup.alpha = 1f - open;
            bool nowOpen = open > 0.5f;
            if (chatMark != null && nowOpen != drawerOpen) chatMark.sprite = Icon(nowOpen ? "arrow_down" : "chat_1");
            drawerOpen = nowOpen;
            if (nowOpen) CloseFilter();
        }

        private void LateUpdate()
        {
            // BottomTabController moves the chat's bottom edge with the sheet; the log stays on its band instead.
            // Baked, the scene places the log (the bake tool also stops the sheet pushing it).
            if (chat != null && !baked)
            {
                PlaceLog(logArea != null ? logArea()
                    : new Vector4(34f, GameHudMetrics.ChatTop + LogTop, 34f, GameHudMetrics.ChatBottom + 4f));
            }
        }

        // Insets in the canvas: left, top from the top, right, bottom from the bottom.
        private void PlaceLog(Vector4 a)
        {
            chat.offsetMin = new Vector2(a.x, a.w);
            chat.offsetMax = new Vector2(-a.z, -a.y);
        }

        private void OnDestroy()
        {
            if (log != null)
            {
                log.RowsChanged -= UpdateEmpty;
                log.RowFactory = null;
            }
            if (bodyMaterial != null) Destroy(bodyMaterial);
        }

        // ------------------------------------------------------------------ tabs and filter

        // The tab row in parent: top from its top, inset from its left and right (the filter at the right end).
        private void BuildTabs(RectTransform parent, float top, float inset)
        {
            AddTab(parent, top, ChatTab.All, "전체", "flag_1", inset, 168f);
            AddTab(parent, top, ChatTab.System, "시스템", "notification", inset + 174f, 150f);
            AddTab(parent, top, ChatTab.General, "일반", "chat_1", inset + 330f, 150f);
            AddTab(parent, top, ChatTab.Whisper, "귓속말", "friend", inset + 486f, 150f);

            RectTransform filter = Box(parent, "ChatFilter", 0f, top, 206f, TabHeight);
            filter.anchorMin = filter.anchorMax = filter.pivot = new Vector2(1f, 1f);
            filter.anchoredPosition = new Vector2(-inset, -top);
            filterButton = filter;
            HudShapes.Panel(filter, Teal, GoldLine, 12f).raycastTarget = true;
            filter.gameObject.AddComponent<Button>().onClick.AddListener(ToggleFilter);
            filterLabel = NewText(filter, "Label", 23f, Gold, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(filterLabel.rectTransform, 14f, 6f, 44f, 6f);
            filterLabel.text = "채팅 필터";
            Image arrow = AddImage(Box(filter, "Arrow", 0f, 0f, 24f, 24f), Icon("arrow_down"), Gold);
            arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
            arrow.rectTransform.anchoredPosition = new Vector2(-16f, 0f);
            arrow.preserveAspect = true;
            ShowTab(ChatTab.All);
        }

        private void AddTab(RectTransform parent, float top, ChatTab kind, string text, string icon, float x, float width)
        {
            RectTransform rect = Box(parent, "Tab" + kind, x, top, width, TabHeight);
            var tab = new Tab { fill = HudShapes.Panel(rect, Teal, GoldLine, 12f) };
            tab.fill.raycastTarget = true;
            tab.outline = rect.Find("Outline").GetComponent<Image>();
            tab.icon = AddImage(Box(rect, "Icon", 16f, 14f, 28f, 28f), Icon(icon), Cream);
            tab.icon.preserveAspect = true;
            tab.label = NewText(rect, "Label", 24f, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(tab.label.rectTransform, 46f, 6f, 10f, 6f);
            tab.label.text = text;
            rect.gameObject.AddComponent<Button>().onClick.AddListener(() => ShowTab(kind));
            tabs[kind] = tab;
        }

        private void ShowTab(ChatTab kind)
        {
            foreach (KeyValuePair<ChatTab, Tab> pair in tabs)
            {
                bool active = pair.Key == kind;
                pair.Value.fill.color = active ? Parchment : Teal;
                pair.Value.outline.color = active ? Gold : GoldLine;
                pair.Value.label.color = active ? Ink : Cream;
                pair.Value.icon.color = active ? new Color(0.09f, 0.3f, 0.32f) : Cream;
            }
            // 전체 ↔ 일반을 오갈 때는 고른 플레이어를 그대로 둔다 (시스템·귓속말 탭에서는 풀림)
            if (log != null) log.SetFilter(kind, ChatLogView.UsesSpeakerFilter(kind) ? log.SpeakerFilter : null);
            UpdateFilterLabel();
        }

        private void ToggleFilter()
        {
            if (dropdown != null && dropdown.gameObject.activeSelf) CloseFilter();
            else OpenFilter();
        }

        // The filter: every player who has chatted, or everyone. Picking one shows only their chat on the 일반 tab if that
        // is open, else on the 전체 tab.
        private void OpenFilter()
        {
            if (log == null) return;
            if (dropdown == null) BuildDropdown();
            PlaceDropdown();
            for (int i = dropdownList.childCount - 1; i >= 0; i--) Destroy(dropdownList.GetChild(i).gameObject);
            AddFilterRow("모든 플레이어", null);
            List<string> speakers = log.Speakers();
            foreach (string name in speakers) AddFilterRow(name, name);
            if (speakers.Count == 0) AddFilterRow("아직 채팅한 사람이 없습니다", null, false);
            float rows = Mathf.Min(speakers.Count + 1 + (speakers.Count == 0 ? 1 : 0), 7);
            dropdown.sizeDelta = new Vector2(dropdown.sizeDelta.x, rows * 54f + 16f);
            dropdownBlocker.gameObject.SetActive(true);
            dropdown.gameObject.SetActive(true);
            dropdownBlocker.SetAsLastSibling();
            dropdown.SetAsLastSibling();
        }

        private void CloseFilter()
        {
            if (dropdown == null) return;
            dropdown.gameObject.SetActive(false);
            dropdownBlocker.gameObject.SetActive(false);
        }

        private void BuildDropdown()
        {
            dropdownBlocker = NewRect("ChatFilterBlocker", canvas);
            Stretch(dropdownBlocker, 0f, 0f, 0f, 0f);
            AddImage(dropdownBlocker, null, new Color(0f, 0f, 0f, 0f)).raycastTarget = true; // a tap outside closes it
            dropdownBlocker.gameObject.AddComponent<Button>().onClick.AddListener(CloseFilter);

            // Hangs from the filter button, right-aligned with it (PlaceDropdown).
            dropdown = Box(canvas, "ChatFilterList", 0f, 0f, 320f, 100f);
            dropdown.anchorMin = dropdown.anchorMax = dropdown.pivot = new Vector2(1f, 1f);
            HudShapes.Panel(dropdown, new Color(0.06f, 0.12f, 0.13f, 0.98f), GoldLine, 12f).raycastTarget = true;
            RectTransform viewport = NewRect("Viewport", dropdown);
            Stretch(viewport, 8f, 8f, 8f, 8f);
            viewport.gameObject.AddComponent<RectMask2D>();
            AddImage(viewport, null, new Color(0f, 0f, 0f, 0f)).raycastTarget = true; // drag area
            dropdownList = NewRect("List", viewport);
            dropdownList.anchorMin = new Vector2(0f, 1f);
            dropdownList.anchorMax = new Vector2(1f, 1f);
            dropdownList.pivot = new Vector2(0.5f, 1f);
            dropdownList.sizeDelta = Vector2.zero;
            var layout = dropdownList.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            dropdownList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = dropdownList;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
        }

        // Right under the filter button, right edges lined up (the button's place depends on the layout).
        private void PlaceDropdown()
        {
            if (filterButton == null) return;
            var corners = new Vector3[4];
            filterButton.GetWorldCorners(corners); // [3]: bottom right
            Vector2 corner = canvas.InverseTransformPoint(corners[3]);
            Rect area = canvas.rect;
            dropdown.anchoredPosition = new Vector2(corner.x - area.xMax, corner.y - area.yMax - 6f);
        }

        private void AddFilterRow(string text, string speaker, bool pickable = true)
        {
            RectTransform row = NewRect("Option", dropdownList);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;
            bool chosen = pickable && ChatLogView.UsesSpeakerFilter(log.Tab) && log.SpeakerFilter == speaker;
            Image back = AddImage(row, HudShapes.Fill, chosen ? new Color(0.98f, 0.8f, 0.42f, 0.25f) : new Color(1f, 1f, 1f, 0f));
            back.type = Image.Type.Sliced;
            back.pixelsPerUnitMultiplier = 2f;
            back.raycastTarget = pickable;
            TextMeshProUGUI label = NewText(row, "Label", 24f, pickable ? Cream : Muted, chosen ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            Stretch(label.rectTransform, 16f, 0f, 16f, 0f);
            label.text = text;
            if (!pickable) return;
            row.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                CloseFilter();
                ChatTab target = log.Tab == ChatTab.General ? ChatTab.General : ChatTab.All;
                log.SetFilter(target, speaker);
                ShowTab(target);
            });
        }

        private void UpdateFilterLabel()
        {
            if (filterLabel == null) return;
            string speaker = log != null ? log.SpeakerFilter : null;
            filterLabel.text = string.IsNullOrEmpty(speaker) ? "채팅 필터" : speaker;
            UpdateEmpty();
        }

        private void UpdateEmpty()
        {
            if (emptyLabel == null || log == null) return;
            bool empty = log.VisibleCount == 0;
            emptyLabel.gameObject.SetActive(empty);
            if (!empty) return;
            switch (log.Tab)
            {
                case ChatTab.System: emptyLabel.text = "시스템 메시지가 없습니다."; break;
                case ChatTab.General:
                    emptyLabel.text = string.IsNullOrEmpty(log.SpeakerFilter) ? "아직 플레이어 채팅이 없습니다." : log.SpeakerFilter + "님의 채팅이 없습니다.";
                    break;
                case ChatTab.Whisper: emptyLabel.text = "받은 귓속말이 없습니다."; break;
                default:
                    emptyLabel.text = string.IsNullOrEmpty(log.SpeakerFilter) ? "아직 메시지가 없습니다." : log.SpeakerFilter + "님의 채팅이 없습니다.";
                    break;
            }
        }

        // ------------------------------------------------------------------ log rows

        private void StyleChat()
        {
            // The log's own dark backings would hide the band: let it show through (they still catch drags).
            ScrollRect scroll = chat.GetComponentInChildren<ScrollRect>(true);
            foreach (Image backing in new[] { chat.GetComponent<Image>(), scroll != null ? scroll.GetComponent<Image>() : null,
                                              scroll != null && scroll.viewport != null ? scroll.viewport.GetComponent<Image>() : null })
            {
                if (backing != null) backing.color = new Color(0f, 0f, 0f, 0f);
            }
            if (scroll != null && scroll.content != null)
            {
                var layout = scroll.content.GetComponent<VerticalLayoutGroup>();
                if (layout != null)
                {
                    layout.padding = new RectOffset(14, 14, 6, 6);
                    layout.spacing = 0f;
                }
            }
            chatGroup = chat.GetComponent<CanvasGroup>();
            if (chatGroup == null) chatGroup = chat.gameObject.AddComponent<CanvasGroup>();
        }

        private void ListenToChat()
        {
            if (log == null) return;
            log.RowFactory = BuildRow;
            log.RowsChanged += UpdateEmpty;
        }

        private RectTransform BuildRow(ChatEntry e)
        {
            RectTransform row = NewRect("ChatRow", canvas);

            // Name tag: the sender's bust and name, or the kind of line (시스템, 안내, 알림) with its mark.
            RectTransform tag = Box(row, "Tag", 0f, RowPad, TagWidth, TagHeight);
            Color tagFill, tagLine, nameColor;
            switch (e.Kind)
            {
                case ChatEntryKind.System: tagFill = Teal; tagLine = GoldLine; nameColor = Gold; break;
                case ChatEntryKind.Notice: tagFill = new Color(0.16f, 0.12f, 0.09f); tagLine = GoldLine; nameColor = e.TextColor ?? Cream; break;
                case ChatEntryKind.Whisper: tagFill = new Color(0.26f, 0.17f, 0.32f); tagLine = GoldLine; nameColor = Cream; break;
                default:
                    tagFill = e.Dead ? new Color(0.5f, 0.48f, 0.45f) : e.Mine ? new Color(0.99f, 0.88f, 0.56f) : Parchment;
                    tagLine = e.Mine ? Gold : new Color(0.6f, 0.42f, 0.2f);
                    nameColor = e.Dead ? new Color(0.25f, 0.24f, 0.22f) : Ink;
                    break;
            }
            HudShapes.Panel(tag, tagFill, tagLine, 12f);
            if (e.Kind == ChatEntryKind.Player || e.Kind == ChatEntryKind.Whisper) AddAvatar(tag, e);
            else
            {
                Image mark = AddImage(Box(tag, "Mark", 12f, 11f, 30f, 30f),
                    Icon(e.Kind == ChatEntryKind.System ? "notification" : e.Nickname == "알림" ? "exclamation" : "info"), nameColor);
                mark.preserveAspect = true;
            }
            TextMeshProUGUI name = NewText(tag, "Name", NameSize, nameColor, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stretch(name.rectTransform, 62f, 2f, 10f, 2f);
            name.enableAutoSizing = true; // a long nickname shrinks, but not below what a phone still reads
            name.fontSizeMin = 22f;
            name.fontSizeMax = NameSize;
            name.text = string.IsNullOrEmpty(e.Nickname) ? "안내" : e.Nickname;

            // The message, wrapped right of the tag; the row grows with it (ChatRowLayout).
            Color bodyColor = e.TextColor ?? (e.Kind == ChatEntryKind.System ? SystemText : BodyText);
            TextMeshProUGUI body = NewText(row, "Message", BodySize, bodyColor, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            body.fontSharedMaterial = BodyMaterial(body);
            Stretch(body.rectTransform, BodyLeft, BodyPad, BodyRight, BodyPad);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.overflowMode = TextOverflowModes.Overflow;
            body.richText = string.IsNullOrEmpty(e.Text); // a plain line from AddLine keeps its tags
            body.text = string.IsNullOrEmpty(e.Text) ? e.Rich ?? string.Empty : e.Text;

            RectTransform rule = NewRect("Rule", row);
            rule.anchorMin = new Vector2(0f, 0f);
            rule.anchorMax = new Vector2(1f, 0f);
            rule.pivot = new Vector2(0.5f, 0f);
            rule.sizeDelta = new Vector2(0f, 2f);
            AddImage(rule, null, new Color(GoldLine.r, GoldLine.g, GoldLine.b, 0.18f));

            row.gameObject.AddComponent<ChatRowLayout>().Setup(body, BodyLeft, BodyRight, BodyPad, BodyPad, TagHeight + RowPad * 2f);
            return row;
        }

        // The font's own material drawn a little heavier (BodyDilate), shared by every message. Made once, on first use.
        private Material bodyMaterial;

        private Material BodyMaterial(TMP_Text text)
        {
            if (bodyMaterial == null && text.fontSharedMaterial != null)
            {
                bodyMaterial = new Material(text.fontSharedMaterial) { name = text.fontSharedMaterial.name + " (Chat)" };
                bodyMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, BodyDilate);
            }
            return bodyMaterial != null ? bodyMaterial : text.fontSharedMaterial;
        }

        // The sender's bust in a ring at the tag's left end; without a known skin, their initial on a coloured disc.
        private void AddAvatar(RectTransform tag, ChatEntry e)
        {
            RectTransform round = Box(tag, "Avatar", 3f, 2f, 48f, 48f);
            ChibiCharacterSkin skin = skinOf != null && e.SenderId > 0 ? skinOf(e.SenderId) : null;
            Sprite bust = skin != null && portraits != null ? portraits.Find(skin.DesignId) : null;
            float hue = string.IsNullOrEmpty(e.Nickname) ? 0f : (e.Nickname.GetHashCode() & 0xFFFF) / 65535f;
            AddImage(round, NightFxSprites.Circle, bust != null ? new Color(0.12f, 0.14f, 0.18f) : Color.HSVToRGB(hue, 0.45f, 0.55f));
            round.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            if (bust != null)
            {
                RectTransform face = NewRect("Bust", round);
                Stretch(face, -4f, -1f, -4f, -6f);
                AddImage(face, bust, e.Dead ? new Color(0.6f, 0.6f, 0.6f) : Color.white).preserveAspect = true;
            }
            else
            {
                TextMeshProUGUI initial = NewText(round, "Initial", 26f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
                Stretch(initial.rectTransform, 0f, 0f, 0f, 0f);
                initial.text = string.IsNullOrEmpty(e.Nickname) ? "?" : e.Nickname.Substring(0, 1);
            }
            RectTransform ring = Box(tag, "Ring", 1f, 0f, 52f, 52f);
            HudShapes.Panel(ring, new Color(0f, 0f, 0f, 0f), e.Mine ? Gold : GoldLine, 26f); // a rounded square this round is a circle
        }

        // ------------------------------------------------------------------ input bar

        private void BuildInputBar(RectTransform sheet)
        {
            Transform tabBar = sheet.Find("TabBar");
            if (tabBar == null) return;
            Transform plus = tabBar.Find("PlusButton");
            if (plus != null) plus.gameObject.SetActive(false);

            input = tabBar.GetComponentInChildren<TMP_InputField>(true);
            if (input != null)
            {
                var field = (RectTransform)input.transform;
                field.anchorMin = Vector2.zero;
                field.anchorMax = Vector2.one;
                field.offsetMin = new Vector2(34f, 28f);
                field.offsetMax = new Vector2(-176f, -28f);
                HudShapes.Panel(field, Parchment, GoldLine, 38f).raycastTarget = true;
                if (input.textViewport != null)
                {
                    input.textViewport.offsetMin = new Vector2(92f, input.textViewport.offsetMin.y);
                    input.textViewport.offsetMax = new Vector2(-66f, input.textViewport.offsetMax.y);
                }
                if (input.textComponent != null)
                {
                    input.textComponent.color = Ink;
                    input.textComponent.fontSize = 28f;
                }
                if (input.placeholder is TMP_Text hint)
                {
                    hint.text = "메시지를 입력하세요...";
                    hint.color = new Color(0.45f, 0.36f, 0.25f, 0.85f);
                    hint.fontSize = 28f;
                }

                // Chat mark on a dark disc, then a thin rule; the smiley at the other end is only a mark (no emoji yet).
                RectTransform disc = Box(field, "ChatMark", 10f, 0f, 58f, 58f);
                disc.anchorMin = disc.anchorMax = disc.pivot = new Vector2(0f, 0.5f);
                disc.anchoredPosition = new Vector2(10f, 0f);
                Image discImage = AddImage(disc, NightFxSprites.Circle, new Color(0.17f, 0.13f, 0.09f));
                discImage.raycastTarget = true;
                chatMarkButton = disc.gameObject.AddComponent<Button>(); // WireInputBar listens to it
                chatMark = AddImage(Box(disc, "Icon", 14f, 14f, 30f, 30f), Icon("chat_1"), Cream);
                chatMark.preserveAspect = true;
                RectTransform rule = Box(field, "Rule", 78f, 0f, 2f, 46f);
                rule.anchorMin = rule.anchorMax = rule.pivot = new Vector2(0f, 0.5f);
                rule.anchoredPosition = new Vector2(78f, 0f);
                AddImage(rule, null, new Color(0.55f, 0.4f, 0.22f, 0.6f));
                RectTransform smile = Box(field, "Smiley", 0f, 0f, 40f, 40f);
                smile.anchorMin = smile.anchorMax = smile.pivot = new Vector2(1f, 0.5f);
                smile.anchoredPosition = new Vector2(-16f, 0f);
                AddImage(smile, Icon("emoji_smiley"), new Color(0.3f, 0.22f, 0.13f)).preserveAspect = true;
            }

            Transform send = tabBar.Find("SendButton");
            Image sendImage = send != null ? send.GetComponent<Image>() : null;
            if (sendImage != null)
            {
                var sendRect = (RectTransform)send;
                sendRect.anchorMin = sendRect.anchorMax = sendRect.pivot = new Vector2(1f, 0.5f);
                sendRect.anchoredPosition = new Vector2(-34f, 0f);
                sendRect.sizeDelta = new Vector2(126f, 96f);
                sendImage.sprite = Resources.Load<Sprite>("GameHud/ButtonSend");
                sendImage.color = Color.white;
                sendImage.type = Image.Type.Simple;
                sendImage.preserveAspect = true;
                foreach (TMP_Text text in send.GetComponentsInChildren<TMP_Text>(true)) text.gameObject.SetActive(false); // the art has the plane
            }
        }

        private void WireInputBar()
        {
            if (chatMarkButton != null) chatMarkButton.onClick.AddListener(OnChatMarkClicked);
        }

        // Closed: like tapping the field. Open (any drawer): closes it.
        private void OnChatMarkClicked()
        {
            if (bottomTab != null && bottomTab.Mode != BottomTabMode.Closed)
            {
                bottomTab.Close();
                return;
            }
            if (input != null) input.ActivateInputField();
        }

        // ------------------------------------------------------------------ helpers

        private static Sprite Icon(string name) => Resources.Load<Sprite>(Icons + name + "_128");

        private static Image AddImage(RectTransform rect, Sprite sprite, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private TextMeshProUGUI NewText(RectTransform parent, string name, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        // Placed from the parent's top-left corner (y down), this size.
        private static RectTransform Box(Transform parent, string name, float x, float top, float width, float height)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -top);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        // Stretched over the parent with these insets (left, top, right, bottom).
        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
