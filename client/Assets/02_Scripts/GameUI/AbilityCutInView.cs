using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// Full-screen ability cut-in: the role's illustration (<see cref="AbilityCutInCatalog"/>) sweeps in over the whole
    /// game screen with a flash and speed lines, holds with the role name and the ability line, then sweeps out.
    /// Tapping skips to the exit. Built at run time on top of the canvas; inactive between plays.
    /// </summary>
    public sealed class AbilityCutInView : MonoBehaviour, IPointerClickHandler
    {
        private const float InEnd = 0.24f, HoldEnd = 1.3f, OutEnd = 1.58f;
        private const int LineCount = 12;

        private RectTransform root;
        private CanvasGroup group;
        private RectTransform artHolder;
        private Image art;
        private Image flash;
        private readonly RectTransform[] lines = new RectTransform[LineCount];
        private RectTransform caption;
        private TextMeshProUGUI title;
        private TextMeshProUGUI line;
        private bool skip;

        public bool IsPlaying => gameObject.activeSelf;

        public static AbilityCutInView Create(RectTransform canvas, TMP_FontAsset font)
        {
            var go = new GameObject("AbilityCutIn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.layer = canvas.gameObject.layer;
            var view = go.AddComponent<AbilityCutInView>();
            view.Build(canvas, font);
            go.SetActive(false);
            return view;
        }

        private void Build(RectTransform canvas, TMP_FontAsset font)
        {
            root = (RectTransform)transform;
            root.SetParent(canvas, false);
            Stretch(root);
            Image dim = GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            dim.raycastTarget = true; // blocks the screen while playing; a tap skips
            group = GetComponent<CanvasGroup>();

            artHolder = NewRect("Art", root);
            Stretch(artHolder);
            art = artHolder.gameObject.AddComponent<Image>();
            art.preserveAspect = true;
            art.raycastTarget = false;

            for (int i = 0; i < LineCount; i++)
            {
                RectTransform rect = NewRect("SpeedLine", root);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                Image image = rect.gameObject.AddComponent<Image>();
                image.sprite = NightFxSprites.Streak;
                image.color = new Color(1f, 1f, 1f, 0.22f);
                image.raycastTarget = false;
                lines[i] = rect;
            }

            caption = NewRect("Caption", root);
            caption.anchorMin = new Vector2(0f, 0f);
            caption.anchorMax = new Vector2(1f, 0f);
            caption.pivot = new Vector2(0.5f, 0f);
            caption.sizeDelta = new Vector2(0f, 380f);
            Image shade = caption.gameObject.AddComponent<Image>();
            shade.sprite = NightFxSprites.Fade;
            shade.color = new Color(0.02f, 0.03f, 0.06f, 0.92f);
            shade.raycastTarget = false;
            title = NewText("Title", caption, font, 76f, new Vector2(0f, 190f));
            line = NewText("Line", caption, font, 44f, new Vector2(0f, 110f));

            flash = NewRect("Flash", root).gameObject.AddComponent<Image>();
            Stretch(flash.rectTransform);
            flash.raycastTarget = false;
        }

        /// <summary>Plays the cut-in once (about 1.6 s, less when tapped). Yield it from a coroutine.</summary>
        public IEnumerator Play(Sprite illustration, string roleTitle, string abilityLine, Color tint)
        {
            gameObject.SetActive(true);
            root.SetAsLastSibling(); // over the drawer, the chat and the result panel
            art.sprite = illustration;
            title.text = roleTitle ?? string.Empty;
            title.color = Color.Lerp(tint, Color.white, 0.35f);
            line.text = abilityLine ?? string.Empty;
            flash.color = Color.Lerp(tint, Color.white, 0.5f);
            for (int i = 0; i < LineCount; i++)
            {
                lines[i].sizeDelta = new Vector2(Mathf.Lerp(420f, 900f, Hash(i, 1)), Mathf.Lerp(5f, 12f, Hash(i, 2)));
            }
            skip = false;
            float start = Time.unscaledTime;
            while (true)
            {
                float t = Time.unscaledTime - start;
                if (skip && t < HoldEnd)
                {
                    start -= HoldEnd - t; // jump to the exit
                    t = HoldEnd;
                }
                skip = false;
                if (t >= OutEnd) break;
                Apply(t);
                yield return null;
            }
            Hide();
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            skip = true;
        }

        private void Apply(float t)
        {
            float width = root.rect.width, height = root.rect.height;
            float enter = EaseOut(Seg(t, 0f, InEnd)), leave = Seg(t, HoldEnd, OutEnd);
            group.alpha = Seg(t, 0f, 0.12f) * (1f - leave);
            float x = Mathf.Lerp(width * 0.55f, 0f, enter) - width * 0.5f * leave * leave;
            artHolder.anchoredPosition = new Vector2(x, 0f);
            artHolder.localScale = Vector3.one * Mathf.Lerp(1.12f, 1.05f, enter) * Mathf.Lerp(1f, 0.952f, Seg(t, InEnd, HoldEnd));
            Color f = flash.color;
            f.a = 0.65f * (1f - Seg(t, 0f, 0.3f));
            flash.color = f;

            float lineAlpha = Seg(t, 0.1f, 0.3f) * (1f - leave);
            float span = width + 1000f;
            for (int i = 0; i < LineCount; i++)
            {
                float speed = Mathf.Lerp(2200f, 3400f, Hash(i, 3));
                float travel = Mathf.Repeat(t * speed + Hash(i, 4) * span, span);
                lines[i].anchoredPosition = new Vector2(width * 0.5f + 500f - travel, (Hash(i, 5) - 0.5f) * height * 0.9f);
                Image image = lines[i].GetComponent<Image>();
                Color c = image.color;
                c.a = 0.22f * lineAlpha;
                image.color = c;
            }

            float rise = EaseOut(Seg(t, 0.12f, 0.38f));
            caption.anchoredPosition = new Vector2(0f, Mathf.Lerp(-140f, 0f, rise));
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI NewText(string name, RectTransform parent, TMP_FontAsset font, float size, Vector2 position)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(-80f, size * 1.4f);
            rect.anchoredPosition = position;
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = false;
            text.raycastTarget = false;
            if (text.font != null)
            {
                text.outlineWidth = 0.2f;
                text.outlineColor = new Color32(0x0A, 0x10, 0x1A, 0xFF);
            }
            return text;
        }

        private static float Seg(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));
        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

        private static float Hash(int i, int salt)
        {
            float v = Mathf.Sin(i * 12.9898f + salt * 78.233f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }
    }
}
