using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WhoisntCitizen.Common;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// A framed window over the game screen (the HUD's map and settings buttons): title, subtitle and a scrolling list of
    /// text rows, or of control rows (settings: headings, volume rows, the window size drop-down). Tapping outside the
    /// window or the close mark closes it. Built at run time; inactive while closed.
    /// </summary>
    public sealed class HudPopup : MonoBehaviour, IPointerClickHandler
    {
        private static readonly Color TitleColor = new Color(1f, 0.86f, 0.5f);
        private static readonly Color TextColor = new Color(0.93f, 0.9f, 0.82f);
        private static readonly Color LineColor = new Color(0.85f, 0.65f, 0.3f, 0.35f);
        // Control rows: dark fields with a gold edge, gold fill and check, a cream slider knob.
        private static readonly Color FieldFill = new Color(0.06f, 0.05f, 0.04f, 0.85f);
        private static readonly Color FieldLine = new Color(0.85f, 0.65f, 0.3f, 0.8f);
        private static readonly Color ListFill = new Color(0.1f, 0.08f, 0.07f, 0.98f);
        private static readonly Color Accent = new Color(0.98f, 0.8f, 0.42f);
        private static readonly Color Knob = new Color(0.99f, 0.93f, 0.78f);
        private const string ArrowIcon = "Sprites/flaticon/white/128/arrow_down_128";
        private const float ControlRowHeight = 84f, ControlGap = 20f, LabelWidth = 210f, ToggleSize = 56f;
        private const float TrackHeight = 14f, HandleSize = 44f, FieldHeight = 72f, ItemHeight = 72f, ListRows = 4f;

        private RectTransform root, window, content;
        private TextMeshProUGUI title, subtitle, message;
        private ScrollRect scroll;
        private TMP_FontAsset font;

        public static HudPopup Create(RectTransform canvas, TMP_FontAsset font)
        {
            var go = new GameObject("HudPopup", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = canvas.gameObject.layer;
            var popup = go.AddComponent<HudPopup>();
            popup.Build(canvas, font);
            go.SetActive(false);
            return popup;
        }

        private void Build(RectTransform canvas, TMP_FontAsset labelFont)
        {
            font = labelFont;
            root = (RectTransform)transform;
            root.SetParent(canvas, false);
            Stretch(root, 0f);
            Image dim = GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.66f);
            dim.raycastTarget = true; // a tap outside the window closes it

            window = NewRect("Window", root);
            window.anchorMin = new Vector2(0.06f, 0.16f);
            window.anchorMax = new Vector2(0.94f, 0.84f);
            window.offsetMin = window.offsetMax = Vector2.zero;
            Image frame = window.gameObject.AddComponent<Image>();
            frame.sprite = Resources.Load<Sprite>("GameHud/ChatFrame");
            frame.type = Image.Type.Sliced;
            frame.color = frame.sprite != null ? Color.white : new Color(0.12f, 0.1f, 0.09f, 0.97f);
            frame.raycastTarget = true; // taps on the window stay in it
            Sprite crestSprite = Resources.Load<Sprite>("GameHud/ChatFrameCrest");
            if (crestSprite != null)
            {
                RectTransform crest = NewRect("Crest", window);
                crest.anchorMin = crest.anchorMax = new Vector2(0.5f, 1f);
                crest.pivot = new Vector2(0.5f, 1f);
                crest.sizeDelta = crestSprite.rect.size;
                crest.anchoredPosition = new Vector2(1.5f, 0f);
                Image image = crest.gameObject.AddComponent<Image>();
                image.sprite = crestSprite;
                image.raycastTarget = false;
            }

            title = NewText("Title", window, 46f, TitleColor, FontStyles.Bold);
            Place(title.rectTransform, 70f, 64f, 70f, 60f);
            subtitle = NewText("Subtitle", window, 26f, new Color(0.8f, 0.76f, 0.66f), FontStyles.Normal);
            Place(subtitle.rectTransform, 70f, 124f, 70f, 36f);

            Button close = NewText("Close", window, 48f, TitleColor, FontStyles.Bold).gameObject.AddClickSoundButton();
            var closeText = close.GetComponent<TextMeshProUGUI>();
            closeText.text = "X"; // plain letter: the game fonts may not have the multiplication sign
            closeText.raycastTarget = true;
            RectTransform closeRect = closeText.rectTransform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(80f, 80f);
            closeRect.anchoredPosition = new Vector2(-34f, -40f);
            close.onClick.AddListener(Hide);

            RectTransform viewport = NewRect("Viewport", window);
            Place(viewport, 56f, 176f, 56f, 60f, true);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // drag area
            content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 22f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            message = NewText("Message", window, 30f, TextColor, FontStyles.Normal);
            Place(message.rectTransform, 70f, 200f, 70f, 120f, true);
            message.alignment = TextAlignmentOptions.Top;
        }

        /// <summary>Opens the window empty, with this title.</summary>
        public void Show(string titleText, string subtitleText)
        {
            gameObject.SetActive(true);
            root.SetAsLastSibling();
            title.text = titleText ?? string.Empty;
            subtitle.text = subtitleText ?? string.Empty;
            for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
            message.text = string.Empty;
            scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>A line of text in the window instead of rows (loading, failure, nothing to show).</summary>
        public void ShowMessage(string text)
        {
            message.text = text ?? string.Empty;
        }

        /// <summary>One row: a heading with a coloured tag after it and a body under it. Text is plain (no rich-text tags).</summary>
        public void AddRow(string heading, string tag, Color tagColor, string body)
        {
            TextMeshProUGUI row = NewText("Row", content, 26f, TextColor, FontStyles.Normal);
            row.textWrappingMode = TextWrappingModes.Normal;
            row.overflowMode = TextOverflowModes.Overflow;
            row.alignment = TextAlignmentOptions.TopLeft;
            row.richText = true;
            string tagHex = ColorUtility.ToHtmlStringRGB(tagColor);
            row.text = "<size=34><b><color=#FFDB80>" + Plain(heading) + "</color></b></size>   <color=#" + tagHex + ">" + Plain(tag)
                + "</color>\n" + Plain(body);
            AddLine();
        }

        /// <summary>A section heading over control rows, with a rule under it.</summary>
        public void AddHeading(string text)
        {
            TextMeshProUGUI heading = NewText("Heading", content, 34f, TitleColor, FontStyles.Bold);
            heading.alignment = TextAlignmentOptions.BottomLeft;
            heading.text = text ?? string.Empty;
            heading.gameObject.AddComponent<LayoutElement>().minHeight = 60f;
            AddLine();
        }

        /// <summary>
        /// A volume row as in the title's settings popup: sound on/off box, name, slider and percentage. Runs on
        /// <see cref="AudioMuteToggle"/> and <see cref="AudioVolumeSlider"/>: applies at once, saves, follows the saved values.
        /// </summary>
        public void AddVolumeRow(string label, AudioVolumeTarget target)
        {
            RectTransform row = NewControlRow("Volume" + target, 0f);
            Toggle toggle = NewToggle(row);
            AddLabel(row, label);
            Slider slider = NewSlider(row);
            TextMeshProUGUI percent = NewText("Percent", row, 28f, TextColor, FontStyles.Normal);
            percent.alignment = TextAlignmentOptions.MidlineRight;
            percent.gameObject.AddComponent<LayoutElement>().preferredWidth = 100f;

            AudioVolumeSlider volume = slider.gameObject.AddComponent<AudioVolumeSlider>();
            volume.Target = target;
            volume.valueText = percent;
            AudioMuteToggle mute = toggle.gameObject.AddComponent<AudioMuteToggle>();
            mute.Target = target;
            mute.volumeSlider = slider;
            row.gameObject.SetActive(true); // now they wake up with their target set
        }

        /// <summary>
        /// The window size row as in the title's settings popup: name and a drop-down of the sizes that fit this monitor.
        /// Runs on <see cref="ResolutionOptionView"/>: a choice applies and saves at once.
        /// </summary>
        public void AddResolutionRow(string label)
        {
            RectTransform row = NewControlRow("Resolution", ToggleSize + ControlGap); // name under the volume names
            AddLabel(row, label);
            NewDropdown(row).gameObject.AddComponent<ResolutionOptionView>();
            row.gameObject.SetActive(true);
        }

        private void AddLine()
        {
            Image line = NewRect("Line", content).gameObject.AddComponent<Image>();
            line.color = LineColor;
            line.raycastTarget = false;
            line.gameObject.AddComponent<LayoutElement>().preferredHeight = 2f;
        }

        // Controls left to right. Made inactive: the caller turns it on once the parts are wired (they read it on waking).
        private RectTransform NewControlRow(string name, float left)
        {
            RectTransform row = NewRect(name, content);
            row.gameObject.SetActive(false);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(Mathf.RoundToInt(left), 0, 0, 0);
            layout.spacing = ControlGap;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = ControlRowHeight;
            return row;
        }

        private void AddLabel(Transform row, string text)
        {
            TextMeshProUGUI label = NewText("Label", row, 30f, TextColor, FontStyles.Normal);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.text = text ?? string.Empty;
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = LabelWidth;
        }

        // Checked = sound on. A gold square inside a dark box.
        private Toggle NewToggle(Transform row)
        {
            RectTransform box = NewRect("Toggle", row);
            var size = box.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = size.preferredHeight = ToggleSize;
            Image back = HudShapes.Panel(box, FieldFill, FieldLine, 12f);
            back.raycastTarget = true;
            RectTransform mark = NewRect("Checkmark", box);
            Stretch(mark, 12f);
            Image check = HudShapes.Panel(mark, Accent, Color.clear, 6f);
            Toggle toggle = box.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = back;
            toggle.graphic = check;
            return toggle;
        }

        // 0..1, a rounded track with a gold fill and a round knob. The whole row height takes taps (jump to that value).
        private Slider NewSlider(Transform row)
        {
            RectTransform rect = NewRect("Slider", row);
            var size = rect.gameObject.AddComponent<LayoutElement>();
            size.flexibleWidth = 1f;
            size.preferredHeight = ControlRowHeight;
            rect.gameObject.AddComponent<Image>().color = Color.clear;

            RectTransform track = NewRect("Background", rect);
            Band(track, 0f, TrackHeight);
            HudShapes.Panel(track, FieldFill, FieldLine, TrackHeight * 0.5f);
            RectTransform fillArea = NewRect("Fill Area", rect);
            Band(fillArea, 0f, TrackHeight);
            RectTransform fill = NewRect("Fill", fillArea);
            fill.anchorMin = Vector2.zero; // the slider moves the right edge
            fill.anchorMax = Vector2.one;
            fill.sizeDelta = Vector2.zero;
            HudShapes.Panel(fill, Accent, Color.clear, TrackHeight * 0.5f);

            RectTransform handleArea = NewRect("Handle Slide Area", rect);
            Band(handleArea, HandleSize * 0.5f, HandleSize); // the knob stays inside the track's ends
            RectTransform handle = NewRect("Handle", handleArea);
            handle.anchorMin = new Vector2(0.5f, 0f); // the slider moves it across
            handle.anchorMax = new Vector2(0.5f, 1f);
            handle.sizeDelta = new Vector2(HandleSize, 0f);
            Image knob = handle.gameObject.AddComponent<Image>();
            knob.sprite = NightFxSprites.Circle;
            knob.color = Knob;

            Slider slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = knob;
            return slider;
        }

        // A box with the chosen item and an arrow; the list (a copy of Template, made by TMP_Dropdown on a canvas of its
        // own over everything) opens under it, or above it when there is no room below.
        private TMP_Dropdown NewDropdown(Transform row)
        {
            RectTransform rect = NewRect("Dropdown", row);
            var size = rect.gameObject.AddComponent<LayoutElement>();
            size.flexibleWidth = 1f;
            size.preferredHeight = FieldHeight;
            Image back = HudShapes.Panel(rect, FieldFill, FieldLine, 14f);
            back.raycastTarget = true;
            TextMeshProUGUI caption = NewText("Label", rect, 28f, TextColor, FontStyles.Normal);
            caption.alignment = TextAlignmentOptions.MidlineLeft;
            Inset(caption.rectTransform, 24f, 72f);
            RectTransform arrow = NewRect("Arrow", rect);
            arrow.anchorMin = arrow.anchorMax = new Vector2(1f, 0.5f);
            arrow.sizeDelta = new Vector2(32f, 32f);
            arrow.anchoredPosition = new Vector2(-38f, 0f);
            Image arrowImage = arrow.gameObject.AddComponent<Image>();
            arrowImage.sprite = Resources.Load<Sprite>(ArrowIcon);
            arrowImage.color = Accent;
            arrowImage.raycastTarget = false;

            RectTransform template = NewRect("Template", rect);
            template.anchorMin = Vector2.zero;
            template.anchorMax = Vector2.right;
            template.pivot = new Vector2(0.5f, 1f);
            template.sizeDelta = new Vector2(0f, ItemHeight * ListRows); // shrunk to the items when fewer
            template.anchoredPosition = new Vector2(0f, -6f);
            HudShapes.Panel(template, ListFill, FieldLine, 14f).raycastTarget = true;
            // No padding above or below the items: TMP_Dropdown fits the list to them and would leave them a little scroll.
            RectTransform viewport = NewRect("Viewport", template);
            Inset(viewport, 6f, 6f);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform list = NewRect("Content", viewport);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = Vector2.one;
            list.pivot = new Vector2(0.5f, 1f);
            list.sizeDelta = new Vector2(0f, ItemHeight);
            var listScroll = template.gameObject.AddComponent<ScrollRect>();
            listScroll.content = list;
            listScroll.viewport = viewport;
            listScroll.horizontal = false;
            listScroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform item = NewRect("Item", list);
            item.anchorMin = new Vector2(0f, 0.5f);
            item.anchorMax = new Vector2(1f, 0.5f);
            item.sizeDelta = new Vector2(0f, ItemHeight);
            RectTransform itemBack = NewRect("Item Background", item);
            Stretch(itemBack, 0f);
            Image highlight = HudShapes.Panel(itemBack, Accent, Color.clear, 10f);
            highlight.raycastTarget = true;
            RectTransform dot = NewRect("Item Checkmark", item);
            dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
            dot.sizeDelta = new Vector2(16f, 16f);
            dot.anchoredPosition = new Vector2(30f, 0f);
            Image dotImage = dot.gameObject.AddComponent<Image>();
            dotImage.sprite = NightFxSprites.Circle;
            dotImage.color = Accent;
            dotImage.raycastTarget = false;
            TextMeshProUGUI itemLabel = NewText("Item Label", item, 28f, TextColor, FontStyles.Normal);
            itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
            Inset(itemLabel.rectTransform, 56f, 16f);
            Toggle itemToggle = item.gameObject.AddComponent<Toggle>();
            itemToggle.targetGraphic = highlight;
            itemToggle.graphic = dotImage;
            ColorBlock tint = itemToggle.colors; // the gold shows only under the pointer
            tint.normalColor = tint.selectedColor = new Color(1f, 1f, 1f, 0f);
            tint.highlightedColor = new Color(1f, 1f, 1f, 0.22f);
            tint.pressedColor = new Color(1f, 1f, 1f, 0.38f);
            itemToggle.colors = tint;
            template.gameObject.SetActive(false);

            TMP_Dropdown dropdown = rect.gameObject.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = back;
            dropdown.template = template;
            dropdown.captionText = caption;
            dropdown.itemText = itemLabel;
            return dropdown;
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        // Taps that reach the dim background (not the window) close the popup.
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.pointerPressRaycast.gameObject == gameObject) Hide();
        }

        private static string Plain(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : text.Replace("<", "(").Replace(">", ")");
        }

        private TextMeshProUGUI NewText(string name, Transform parent, float size, Color color, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
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

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        // Full height, these insets at the left and right.
        private static void Inset(RectTransform rect, float left, float right)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
        }

        // Across the parent less this inset at both ends, centred, this tall.
        private static void Band(RectTransform rect, float inset, float height)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(inset, -height * 0.5f);
            rect.offsetMax = new Vector2(-inset, height * 0.5f);
        }

        // Inside the window: left/right insets, top inset and height (or, with fillDown, the bottom inset).
        private static void Place(RectTransform rect, float left, float top, float right, float heightOrBottom, bool fillDown = false)
        {
            if (fillDown)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(left, heightOrBottom);
                rect.offsetMax = new Vector2(-right, -top);
                return;
            }
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - heightOrBottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
