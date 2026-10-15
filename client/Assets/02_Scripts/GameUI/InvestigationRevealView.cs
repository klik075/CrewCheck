using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// The captain's investigation report, on the captain's screen when the day opens (after the night's cut-scene):
    /// a gold-framed parchment rises, the target's bust appears in a gold ring while a compass needle spins behind it and
    /// settles, then a stamp slams on it, red "해적" with a skull or blue "선원" with an anchor, and the line under it says
    /// it in words. The report then flies into that player's place in the roster (where the mark stays), or fades.
    /// About 3.5 s, always to the end (a tap does not skip or close it). After a reconnect it opens stamped.
    /// Shows only what the server told this player (a monkey's fake result looks the same). Unscaled time.
    /// </summary>
    public sealed class InvestigationRevealView : MonoBehaviour
    {
        private const float RiseEnd = 0.4f, StampAt = 1.3f, StampEnd = 1.45f, HoldEnd = 3.4f, OutTime = 0.45f;

        public static readonly Color PirateRed = new Color(0.76f, 0.14f, 0.11f);
        public static readonly Color CrewBlue = new Color(0.13f, 0.43f, 0.62f);
        private static readonly Color Ink = new Color(0.27f, 0.16f, 0.06f);
        private static readonly Color Night = new Color(0f, 0.03f, 0.06f);

        private RectTransform root, letter, rose, needle, portraitRoot, stamp;
        private CanvasGroup group, letterGroup, stampGroup;
        private Image dim, bust, stampIcon, stampLine, burst;
        private TextMeshProUGUI day, nameText, stampText, verdict, note;
        private TMP_FontAsset font;
        private Coroutine routine;
        private RectTransform flyTo;

        public bool IsPlaying => routine != null;

        public static InvestigationRevealView Create(RectTransform canvas, TMP_FontAsset font)
        {
            var go = new GameObject("InvestigationReveal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.layer = canvas.gameObject.layer;
            var view = go.AddComponent<InvestigationRevealView>();
            view.Build(canvas, font);
            go.SetActive(false);
            return view;
        }

        /// <param name="dayNumber">The night the report is from.</param>
        /// <param name="targetName">The investigated player's nickname.</param>
        /// <param name="portrait">The bust of the skin they wear at the table (none: an empty disc).</param>
        /// <param name="faction">What the server said: Factions.Pirate or Factions.Crew.</param>
        /// <param name="slot">Their place in the roster, where the report flies at the end (null: it fades).</param>
        /// <param name="stamped">Open with the stamp already on (a reconnect).</param>
        /// <param name="waitWhile">Wait while this is true before showing (another cut-in on screen).</param>
        public void Play(int dayNumber, string targetName, Sprite portrait, string faction, RectTransform slot, bool stamped,
            Func<bool> waitWhile = null)
        {
            Stop();
            bool pirate = faction == Factions.Pirate;
            Color tone = pirate ? PirateRed : CrewBlue;
            string who = string.IsNullOrEmpty(targetName) ? "?" : targetName;
            day.text = dayNumber > 0 ? dayNumber + "일차 밤 · 선장의 조사" : "선장의 조사";
            nameText.text = who + "님";
            bust.sprite = portrait;
            bust.enabled = portrait != null;
            stampText.text = pirate ? "해적" : "선원";
            stampText.color = tone;
            stampIcon.sprite = Icon(pirate ? "skull" : "anchor");
            stampIcon.color = tone;
            stampLine.color = tone;
            verdict.text = who + (pirate ? "님은 해적입니다!" : "님은 선원입니다");
            verdict.color = pirate ? new Color(0.6f, 0.08f, 0.06f) : new Color(0.08f, 0.3f, 0.45f);
            flyTo = slot;
            note.text = slot != null ? "명단에 표시해 두었습니다" : string.Empty;
            // Active (a coroutine needs it) but unseen and not in the way while it waits.
            gameObject.SetActive(true);
            group.alpha = 0f;
            group.blocksRaycasts = false;
            routine = StartCoroutine(Run(stamped ? StampEnd + 0.05f : 0f, waitWhile));
        }

        /// <summary>Hides at once (a new game).</summary>
        public void Stop()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        private IEnumerator Run(float from, Func<bool> waitWhile)
        {
            while (waitWhile != null && waitWhile()) yield return null;
            group.blocksRaycasts = true;
            root.SetAsLastSibling(); // over the drawer, the chat and the day's cut-scene
            float start = Time.unscaledTime - from;
            while (true)
            {
                float t = Time.unscaledTime - start;
                if (t >= HoldEnd + OutTime) break;
                Apply(t, from, Seg(t, HoldEnd, HoldEnd + OutTime));
                yield return null;
            }
            routine = null;
            gameObject.SetActive(false);
        }

        private void Apply(float t, float from, float leave)
        {
            float h = root.rect.height;
            group.alpha = Seg(t, from, from + 0.15f) * (1f - (flyTo != null ? Seg(leave, 0.6f, 1f) : leave));
            Set(dim, Night, 0.72f * Seg(t, 0f, 0.25f) * (1f - leave));

            // 1. The report rises from the bottom, a little turned, and settles.
            float rise = EaseOutBack(Seg(t, 0f, RiseEnd));
            Vector2 rest = new Vector2(0f, h * 0.04f);
            letter.anchoredPosition = Vector2.Lerp(rest - new Vector2(0f, h * 0.7f), rest, rise);
            letter.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(7f, 0f, rise));
            letter.localScale = Vector3.one;
            letterGroup.alpha = Seg(t, 0f, 0.12f);

            // 2. The face pops in; the needle spins behind it and settles pointing at the stamp.
            float pop = EaseOutBack(Seg(t, RiseEnd - 0.05f, RiseEnd + 0.25f));
            portraitRoot.localScale = Vector3.one * Mathf.Max(0f, Mathf.Lerp(0.6f, 1f, pop));
            rose.localRotation = Quaternion.Euler(0f, 0f, -t * 12f);
            float spin = Seg(t, 0.25f, StampAt);
            needle.localRotation = Quaternion.Euler(0f, 0f, -45f - 1080f * (1f - Mathf.Pow(1f - spin, 3f)));

            // 3. The stamp slams down: big and light, then small and solid, a jolt and a ring of its colour.
            float slam = Seg(t, StampAt, StampEnd);
            stamp.localScale = Vector3.one * Mathf.Lerp(2.4f, 1f, slam * slam);
            stampGroup.alpha = t < StampAt ? 0f : Mathf.Lerp(0.35f, 1f, slam);
            float jolt = t < StampEnd ? 0f : 1f - Seg(t, StampEnd, StampEnd + 0.3f);
            letter.anchoredPosition += new Vector2(Mathf.Sin(t * 90f), Mathf.Cos(t * 70f) * 0.6f) * 12f * jolt;
            float ring = Seg(t, StampEnd, StampEnd + 0.45f);
            burst.rectTransform.sizeDelta = new Vector2(1.6f, 1f) * Mathf.Lerp(160f, 460f, EaseOut(ring));
            Set(burst, stampLine.color, t < StampEnd ? 0f : 0.75f * (1f - ring));

            // 4. The verdict in words.
            float say = Seg(t, StampEnd + 0.05f, StampEnd + 0.35f);
            verdict.alpha = say;
            note.alpha = say * 0.8f;

            // 5. Away: into the player's place in the roster, or just fading.
            if (flyTo != null && leave > 0f)
            {
                Vector3 target = letter.parent.InverseTransformPoint(flyTo.TransformPoint(flyTo.rect.center));
                float k = EaseIn(leave);
                letter.anchoredPosition = Vector2.Lerp(letter.anchoredPosition, target, k);
                letter.localScale = Vector3.one * Mathf.Lerp(1f, 0.1f, k);
            }
        }

        // ================================================================ build

        private void Build(RectTransform canvas, TMP_FontAsset labelFont)
        {
            font = labelFont;
            root = (RectTransform)transform;
            root.SetParent(canvas, false);
            Stretch(root);
            Image blocker = GetComponent<Image>();
            blocker.color = Color.clear;
            blocker.raycastTarget = true; // blocks the screen while it shows
            group = GetComponent<CanvasGroup>();
            dim = Img("Dim", root, null, Color.clear);
            Stretch(dim.rectTransform);

            letter = NewRect("Report", root);
            letter.anchorMin = letter.anchorMax = new Vector2(0.5f, 0.5f);
            letter.sizeDelta = new Vector2(800f, 1000f);
            letterGroup = letter.gameObject.AddComponent<CanvasGroup>();
            Sprite paper = Resources.Load<Sprite>("GameHud/ParchmentPanel");
            Image sheet = Img("Paper", letter, paper, paper != null ? Color.white : new Color(0.93f, 0.85f, 0.66f));
            Stretch(sheet.rectTransform);
            const float frame = 0.72f; // the frame drawn a little heavier than on the role card
            if (paper != null)
            {
                sheet.type = Image.Type.Sliced;
                sheet.pixelsPerUnitMultiplier = frame;
                Crest(letter, "ParchmentCrestTop", true, frame);
                Crest(letter, "ParchmentCrestBottom", false, frame);
            }

            TextMeshProUGUI title = Text("Title", letter, 60f, Ink);
            title.rectTransform.anchoredPosition = new Vector2(0f, 338f); // under the top crest
            title.text = "조 사  보 고";
            day = Text("Day", letter, 30f, new Color(Ink.r, Ink.g, Ink.b, 0.75f));
            day.fontStyle = FontStyles.Normal;
            day.rectTransform.anchoredPosition = new Vector2(0f, 282f);
            Image rule = Img("Rule", letter, NauticalHud.Get("TitleRule"), new Color(0.55f, 0.38f, 0.16f));
            rule.preserveAspect = true;
            rule.rectTransform.sizeDelta = new Vector2(520f, 36f);
            rule.rectTransform.anchoredPosition = new Vector2(0f, 248f);

            // The compass behind the face (its letters clear of the rule above): the rose turning slowly, the needle spinning.
            rose = Img("Rose", letter, Resources.Load<Sprite>("Sprites/components/compass_base"), new Color(Ink.r, Ink.g, Ink.b, 0.28f)).rectTransform;
            rose.GetComponent<Image>().preserveAspect = true;
            rose.sizeDelta = Vector2.one * 440f;
            needle = Img("Needle", letter, Resources.Load<Sprite>("Sprites/components/compass_dial"), new Color(1f, 1f, 1f, 0.9f)).rectTransform;
            needle.GetComponent<Image>().preserveAspect = true;
            needle.sizeDelta = Vector2.one * 420f;

            portraitRoot = NewRect("Face", letter);
            portraitRoot.anchorMin = portraitRoot.anchorMax = new Vector2(0.5f, 0.5f);
            Image disc = Img("Disc", portraitRoot, NightFxSprites.Circle, new Color(0.09f, 0.11f, 0.15f));
            disc.rectTransform.sizeDelta = Vector2.one * 290f;
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            bust = Img("Bust", disc.rectTransform, null, Color.white);
            bust.rectTransform.sizeDelta = Vector2.one * 330f;
            bust.rectTransform.anchoredPosition = new Vector2(0f, -14f);
            bust.preserveAspect = true;
            Sprite ringArt = NauticalHud.Get("PortraitRing");
            if (ringArt != null)
            {
                // The ring's centre is half its width from its top (its anchor badge hangs below).
                float width = 145f / NauticalHud.RingInner;
                Image ring = Img("Ring", portraitRoot, ringArt, Color.white);
                ring.rectTransform.pivot = new Vector2(0.5f, 1f);
                ring.rectTransform.sizeDelta = new Vector2(width, width * ringArt.rect.height / ringArt.rect.width);
                ring.rectTransform.anchoredPosition = new Vector2(0f, width * 0.5f);
            }
            else
            {
                Img("Ring", portraitRoot, NightFxSprites.Ring, new Color(0.85f, 0.65f, 0.3f)).rectTransform.sizeDelta = Vector2.one * 312f;
            }
            RectTransform plate = NewRect("NamePlate", letter);
            plate.anchorMin = plate.anchorMax = new Vector2(0.5f, 0.5f);
            plate.sizeDelta = new Vector2(380f, 74f);
            plate.anchoredPosition = new Vector2(0f, -240f); // under the ring's anchor badge
            Image plateArt = plate.gameObject.AddComponent<Image>();
            plateArt.raycastTarget = false;
            if (NauticalHud.Get("NamePlate") != null) NauticalHud.Plate(plateArt, "NamePlate", 74f);
            else plateArt.color = new Color(0.16f, 0.11f, 0.06f);
            nameText = Text("Name", plate, 36f, new Color(0.98f, 0.92f, 0.78f));
            nameText.rectTransform.anchoredPosition = Vector2.zero;
            nameText.rectTransform.sizeDelta = new Vector2(330f, 60f);
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 22f;
            nameText.fontSizeMax = 36f;
            nameText.overflowMode = TextOverflowModes.Ellipsis;

            // The stamp, over the lower right of the face, a little turned; a ring of its colour spreads under it.
            Vector2 stampAt = new Vector2(150f, -95f);
            burst = Img("Burst", letter, NightFxSprites.Ring, Color.clear);
            burst.rectTransform.anchoredPosition = stampAt;
            burst.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -12f);
            stamp = NewRect("Stamp", letter);
            stamp.anchorMin = stamp.anchorMax = new Vector2(0.5f, 0.5f);
            stamp.sizeDelta = new Vector2(340f, 136f);
            stamp.anchoredPosition = stampAt;
            stamp.localRotation = Quaternion.Euler(0f, 0f, -12f);
            stampGroup = stamp.gameObject.AddComponent<CanvasGroup>();
            Image paperUnder = HudShapes.Panel(stamp, new Color(0.96f, 0.9f, 0.76f, 0.55f), Color.white, 18f);
            stampLine = paperUnder.transform.Find("Outline").GetComponent<Image>();
            stampLine.pixelsPerUnitMultiplier *= 0.35f; // a thick stamped border
            stampIcon = Img("Icon", stamp, null, Color.white);
            stampIcon.preserveAspect = true;
            stampIcon.rectTransform.sizeDelta = Vector2.one * 76f;
            stampIcon.rectTransform.anchoredPosition = new Vector2(-108f, 0f);
            stampText = Text("Word", stamp, 72f, Color.white);
            stampText.rectTransform.anchoredPosition = new Vector2(46f, 2f);
            stampText.rectTransform.sizeDelta = new Vector2(220f, 110f);
            stampText.characterSpacing = 4f;

            verdict = Text("Verdict", letter, 50f, Ink);
            verdict.rectTransform.anchoredPosition = new Vector2(0f, -330f);
            verdict.rectTransform.sizeDelta = new Vector2(700f, 70f);
            verdict.enableAutoSizing = true;
            verdict.fontSizeMin = 30f;
            verdict.fontSizeMax = 50f;
            note = Text("Note", letter, 28f, new Color(Ink.r, Ink.g, Ink.b, 0.8f));
            note.fontStyle = FontStyles.Normal;
            note.rectTransform.anchoredPosition = new Vector2(0f, -388f);
        }

        // The ornament cut off the parchment's edge (Tools/GameHudArt), back on that edge at the frame's scale.
        private static void Crest(RectTransform frame, string sprite, bool top, float multiplier)
        {
            Sprite crest = Resources.Load<Sprite>("GameHud/" + sprite);
            if (crest == null) return;
            Image image = Img(sprite, frame, crest, Color.white);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rect.sizeDelta = crest.rect.size / multiplier;
            rect.anchoredPosition = Vector2.zero;
        }

        // ================================================================ helpers

        private static Sprite Icon(string name) => Resources.Load<Sprite>("Sprites/flaticon/white/128/" + name + "_128");

        private TextMeshProUGUI Text(string name, RectTransform parent, float size, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(720f, size * 1.5f);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false; // nicknames come from players
            text.raycastTarget = false;
            return text;
        }

        private static Image Img(string name, RectTransform parent, Sprite sprite, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
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

        private static void Set(Graphic graphic, Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            graphic.color = color;
        }

        private static float Seg(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));
        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
        private static float EaseIn(float x) => x * x * x;

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
