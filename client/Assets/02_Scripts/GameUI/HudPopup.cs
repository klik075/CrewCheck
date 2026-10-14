using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// A framed window over the game screen (the HUD's map and settings buttons): title, subtitle and a scrolling list of
    /// text rows. Tapping outside the window or the close mark closes it. Built at run time; inactive while closed.
    /// </summary>
    public sealed class HudPopup : MonoBehaviour, IPointerClickHandler
    {
        private static readonly Color TitleColor = new Color(1f, 0.86f, 0.5f);
        private static readonly Color TextColor = new Color(0.93f, 0.9f, 0.82f);

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
            Image line = NewRect("Line", content).gameObject.AddComponent<Image>();
            line.color = new Color(0.85f, 0.65f, 0.3f, 0.35f);
            line.raycastTarget = false;
            line.gameObject.AddComponent<LayoutElement>().preferredHeight = 2f;
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
