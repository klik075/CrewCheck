using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WhoisntCitizen.GameUI
{
    /// <summary>What the cut-scene tells (<see cref="DayTransitionView"/>).</summary>
    public enum DayTransitionKind
    {
        Killed,      // 밤사이 사망
        Protected,   // 선의 보호로 아무도 죽지 않음
        Quiet,       // 밤사이 아무 일도 없음
        Executed,    // 투표로 처형
        NotExecuted, // 동점·투표 없음
        Dragged,     // 밤사이 크라켄에게 끌려감 (같은 밤 습격으로 죽은 사람도 함께)
    }

    /// <summary>One cut-scene's content. Only what the server made public: never the role of the dead.</summary>
    public sealed class DayTransitionInfo
    {
        public DayTransitionKind Kind;
        public int Day;            // the night's day (the day that comes next), or the vote's day (the next night is Day + 1)
        public string Name;        // the dead player's nickname, without "님"
        public Sprite Portrait;    // the bust of the skin they wore at the table
        public Sprite Figure;      // that skin bound with rope (the plank)
        public int Votes;          // executed: their votes
        public int TotalVotes;     // executed: every vote cast
        public string Reason;      // not executed: why ("동점이라 아무도 처형되지 않았습니다.")
        public bool Mine;          // I am the one who died
        public DayTransitionVictim[] Victims; // dragged: everyone who died that night (mine first); the first four get a poster
        public bool AlsoAttacked;  // dragged: the pirates also killed someone that night (their slashes cut in first)
        public bool GameOver;      // the game ended with this result: no next day or night to announce
    }

    /// <summary>One of the dead of a kraken night (<see cref="DayTransitionKind.Dragged"/>).</summary>
    public sealed class DayTransitionVictim
    {
        public string Name;        // nickname, without "님"
        public Sprite Portrait;    // the bust of the skin they wore at the table
        public bool Mine;          // it is me
        public bool Kraken;        // dragged by the kraken (else the pirates' attack: a red cut instead of ink)
    }

    /// <summary>
    /// Full-screen cut-scene between the night's result and the day, and between the vote and the night
    /// (Docs/UI/DayTransition_*.png). Letterbox bars close, the scene plays, and it stops on the last card ("N일차 낮",
    /// "N일차 밤") until <see cref="Release"/> says the next phase began (at most a few seconds), then the bars open on it.
    /// - Killed: the night lifts, three red slashes, the victim's wanted poster, dawn.
    /// - Protected: the same night and slashes (so the outcome stays hidden), then a gold ring cuts them, dawn.
    /// - Quiet: a calm night sea, then sunrise. No red.
    /// - Executed: the table's tally (the game screen, not covered), the verdict scroll and its seal, the plank
    ///   (DayTransition/ExecutionPlank with the player's skin on it), the splash (DayTransition/ExecutionSplash), night.
    /// - Not executed: the verdict scroll with a void seal that cracks, night.
    /// - Dragged: the night lifts, the sea parts (teal rings, two amber eyes), the kraken's picture lunges in, an ink-soaked
    ///   poster for each of the dead is pulled down into the sea, ink and bubbles at dawn (Docs: 시안 E).
    /// Built at run time over the canvas; tapping skips to the last card. <see cref="Evaluate"/> draws any moment.
    /// </summary>
    public sealed class DayTransitionView : MonoBehaviour, IPointerClickHandler
    {
        private const float DesignOut = 0.5f, MinBar = 120f; // the bars opening, as drawn
        private const float ArtAspect = 576f / 812f; // DayTransition/ExecutionPlank and ExecutionSplash
        private const float Tally = 0.8f;            // execution: the table's tally shows this long before the bars close
        private const int SparkCount = 16, RayCount = 12, SlashCount = 3, SealLineCount = 9;

        // Where things stand in the execution pictures (0..1 from the left, 0..1 from the top)
        private static readonly Vector2 PlankFeet = new Vector2(0.83f, 0.452f);
        private const float FigureHeight = 0.225f;     // of the picture's height
        private static readonly Vector2 SplashBase = new Vector2(0.6f, 0.783f);
        private const float TopBand = 0.059f, BottomBand = 0.926f; // the dark bands for captions

        private static readonly Color Navy = new Color(0.02f, 0.03f, 0.08f);
        private static readonly Color Blood = new Color(0.32f, 0.02f, 0.02f);
        private static readonly Color DeepRed = new Color(0.12f, 0.02f, 0.02f);
        private static readonly Color GoldWash = new Color(0.22f, 0.14f, 0.03f);
        private static readonly Color Gold = new Color(1f, 0.84f, 0.42f);
        private static readonly Color Cream = new Color(0.96f, 0.9f, 0.77f);
        private static readonly Color Ink = new Color(0.23f, 0.11f, 0.05f);
        private static readonly Color Seal = new Color(0.66f, 0.12f, 0.1f);
        private static readonly Color Parchment = new Color(0.93f, 0.85f, 0.66f);
        private static readonly Color DeepTeal = new Color(0.01f, 0.09f, 0.11f);
        private static readonly Color TealGlow = new Color(0.36f, 0.94f, 0.88f);
        private static readonly Color InkTeal = new Color(0.02f, 0.19f, 0.2f);
        private static readonly Color Amber = new Color(1f, 0.62f, 0.2f);
        private const int InkPosterCount = 4; // kraken night: the most posters drawn; more dead show as "+N명"

        public event Action Finished;

        private RectTransform root, stage, frame, barTop, barBottom;
        private CanvasGroup group;
        private Image shade, wash, flash, vignetteTop, vignetteBottom;
        private Art nightArt, dayArt, plankArt, splashArt;
        private Image figure, fin, wake;
        private readonly Image[] ripples = new Image[3];
        private RectTransform sun;
        private Image sunGlow, sunCore;
        private readonly Image[] rays = new Image[RayCount];
        private readonly Image[] slashGlow = new Image[SlashCount], slashCore = new Image[SlashCount];
        private Image ring, innerRing, shieldGlow, dawnSky;
        private readonly Image[] sparks = new Image[SparkCount];
        private Card poster, scroll;
        private Image posterPortrait, posterCutA, posterCutB, posterRibbon, scrollPortrait, scrollPortraitFrame;
        private RectTransform sealRoot, sealCrack;
        private CanvasGroup sealGroup;
        private Image sealCrackImage;
        private TextMeshProUGUI[] fadingTexts;
        private readonly Image[] sealLines = new Image[SealLineCount];
        private TextMeshProUGUI posterHead, posterName, posterLine, scrollTitle, scrollName, scrollVotes, scrollReason, sealText;
        private RectTransform plaque;
        private CanvasGroup plaqueGroup;
        private Image plaqueSun, plaqueMoon, plaqueMoonBite;
        private TextMeshProUGUI plaqueText;
        private Image band;
        private TextMeshProUGUI nightSmall, nightMid, strikeCaption, shieldTitle, shieldSub, calmCaption, dawnCaption,
            nightCaption, plankCaption, splashCaption, splashSub, myLine;
        private TextMeshProUGUI onoChaak, onoDang, onoChaeng, onoSswa, onoDrum, onoDeol, onoKwang, onoCrack;
        private Image myBand;
        private TMP_FontAsset font;

        // The kraken night (Dragged)
        private Art krakenArt;
        private readonly Image[] seaRings = new Image[3];
        private readonly Image[] krakenEyes = new Image[2];
        private readonly InkPoster[] inkPosters = new InkPoster[InkPosterCount];
        private Image inkStain;
        private TextMeshProUGUI onoKugwang, onoChwareuk, victimOverflow;
        private int shownVictims;
        private bool anyVictimMine;

        private DayTransitionInfo info;
        private bool released, skip;
        private Coroutine routine;

        // This play's timing (from the settings when it starts): the beats' real lengths, and the timing they are drawn at.
        private float[] realBeats = new float[0], designBeats = new float[0];
        private int beatCount;
        private float realOut = DesignOut, maxHold = 2.5f;

        /// <summary>The night scenery for the calm sea and the night after the vote (the shared pirate room's picture).</summary>
        public Sprite NightBackdrop { get; set; }

        /// <summary>How long each beat lasts. Read when a scene starts, so edits show on the next one.</summary>
        public DayTransitionSettings Settings { get; set; }

        public bool IsPlaying => routine != null;

        /// <summary>The moment the scene stops on its last card (seconds), and its end after the bars open.</summary>
        public float HoldAt => Sum(realBeats, beatCount);
        public float Duration => HoldAt + realOut;

        private sealed class Art
        {
            public RectTransform rect;
            public Image image;
            public CanvasGroup group;
        }

        private sealed class Card
        {
            public RectTransform rect;
            public CanvasGroup group;
        }

        /// <summary>A kraken night's poster: the bust, the name, soaked in ink from below (or cut red for the pirates' victim).</summary>
        private sealed class InkPoster
        {
            public Card card;
            public Image portrait, ink, cut;
            public TextMeshProUGUI name, line;
            public bool kraken;
        }

        public static DayTransitionView Create(RectTransform canvas, TMP_FontAsset font)
        {
            var go = new GameObject("DayTransition", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.layer = canvas.gameObject.layer;
            var view = go.AddComponent<DayTransitionView>();
            view.Settings = DayTransitionSettings.Load();
            view.Build(canvas, font);
            go.SetActive(false);
            return view;
        }

        /// <summary>Plays one cut-scene from the start (a running one stops). released: the next phase already began.</summary>
        public void Play(DayTransitionInfo content, bool nextPhaseStarted)
        {
            if (content == null) return;
            Stop();
            Prepare(content);
            released = nextPhaseStarted || content.GameOver;
            gameObject.SetActive(true);
            routine = StartCoroutine(Run());
        }

        /// <summary>The next phase began: the scene may leave its last card.</summary>
        public void Release()
        {
            released = true;
        }

        /// <summary>Hides at once (a new game). Does not raise <see cref="Finished"/>.</summary>
        public void Stop()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            skip = true;
        }

        /// <summary>Stops and draws the scene as it is t seconds after it began (a preview: drag through a scene).</summary>
        public void Show(DayTransitionInfo content, float t)
        {
            if (content == null) return;
            Stop();
            Prepare(content);
            Evaluate(t);
        }

        /// <summary>Sets up the content and this play's timing without playing (for previews and checks). Then <see cref="Evaluate"/>.</summary>
        public void Prepare(DayTransitionInfo content)
        {
            info = content;
            DayTransitionSettings settings = Settings != null ? Settings : DayTransitionSettings.Load();
            realBeats = settings.Beats(content.Kind);
            designBeats = DayTransitionSettings.DesignBeats(content.Kind);
            PrepareVictims(content);
            beatCount = content.GameOver ? designBeats.Length - 1 : designBeats.Length; // no next day or night to announce
            realOut = Mathf.Max(DayTransitionSettings.MinBeat, settings.outLength);
            maxHold = Mathf.Max(0f, settings.maxHold);
            gameObject.SetActive(true);
            root.SetAsLastSibling(); // over the HUD, the chat and the result panel
            SetTexts();
            figure.sprite = content.Figure;
            figure.enabled = content.Figure != null;
            posterPortrait.sprite = content.Portrait;
            posterPortrait.enabled = content.Portrait != null;
            scrollPortrait.sprite = content.Portrait;
            scrollPortrait.enabled = content.Portrait != null;
            nightArt.image.sprite = NightBackdrop;
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// A kraken night's posters: the first four of the dead (mine first, as given), the rest as "+N명". From the second
        /// poster on, the dragged beat lasts a little longer (at most 1.8 times) so each name can be read.
        /// </summary>
        private void PrepareVictims(DayTransitionInfo content)
        {
            DayTransitionVictim[] victims = content.Kind == DayTransitionKind.Dragged && content.Victims != null
                ? content.Victims : new DayTransitionVictim[0];
            shownVictims = Mathf.Min(victims.Length, InkPosterCount);
            anyVictimMine = false;
            for (int i = 0; i < InkPosterCount; i++)
            {
                InkPoster poster = inkPosters[i];
                DayTransitionVictim victim = i < shownVictims ? victims[i] : null;
                poster.kraken = victim != null && victim.Kraken;
                poster.portrait.sprite = victim != null ? victim.Portrait : null;
                poster.portrait.enabled = poster.portrait.sprite != null;
                poster.name.text = victim != null ? (string.IsNullOrEmpty(victim.Name) ? "?" : victim.Name) + "님" : string.Empty;
                poster.line.text = poster.kraken ? "크라켄에게 끌려갔습니다" : "밤사이 사망했습니다";
                poster.line.color = poster.kraken ? new Color(0.06f, 0.36f, 0.36f) : new Color(0.55f, 0.1f, 0.06f);
                if (victim != null && victim.Mine) anyVictimMine = true;
            }
            victimOverflow.text = victims.Length > shownVictims ? "+" + (victims.Length - shownVictims) + "명" : string.Empty;
            if (content.Kind == DayTransitionKind.Dragged && shownVictims > 1 && realBeats.Length > 3)
            {
                realBeats[3] *= Mathf.Min(1.8f, 1f + 0.22f * (shownVictims - 1));
            }
        }

        /// <summary>Draws the scene as it is t seconds after it began (no hold).</summary>
        public void Evaluate(float t)
        {
            if (info != null) Apply(DesignTime(t));
        }

        private IEnumerator Run()
        {
            float t = 0f, held = 0f, last = Time.unscaledTime;
            float hold = HoldAt, end = Duration;
            skip = false;
            Evaluate(0f);
            while (t < end)
            {
                yield return null;
                float now = Time.unscaledTime, dt = Mathf.Min(now - last, 0.1f); // a hitch must not skip a beat
                last = now;
                if (skip)
                {
                    skip = false;
                    if (t < hold) t = hold;
                    released = true;
                }
                bool waiting = !released && held < maxHold;
                if (t >= hold && waiting) held += dt;
                else t += dt;
                if (t > hold && waiting) t = hold;
                Evaluate(t);
            }
            routine = null;
            gameObject.SetActive(false);
            Finished?.Invoke();
        }

        // ================================================================ timeline
        // The scenes below are drawn on a fixed timeline (DayTransitionSettings.DesignBeats). A play maps its real time
        // onto it beat by beat, so a beat set longer plays its part slower (and shorter, faster), the rest unchanged.

        /// <summary>Real seconds since the start → seconds on the drawn timeline.</summary>
        private float DesignTime(float t)
        {
            float design = 0f, real = 0f;
            for (int i = 0; i < beatCount; i++)
            {
                if (t <= real + realBeats[i]) return design + Mathf.Max(0f, t - real) / realBeats[i] * designBeats[i];
                design += designBeats[i];
                real += realBeats[i];
            }
            return design + (t - real) / realOut * DesignOut; // the bars opening
        }

        private static float Sum(float[] values, int count)
        {
            float sum = 0f;
            for (int i = 0; i < count && i < values.Length; i++) sum += values[i];
            return sum;
        }

        private void Apply(float t)
        {
            float w = root.rect.width, h = root.rect.height;
            float bar = Mathf.Max(MinBar, (h - w / ArtAspect) * 0.5f);
            float fh = h - bar * 2f;
            frame.sizeDelta = new Vector2(w, fh);
            HideAll();

            bool execution = info.Kind == DayTransitionKind.Executed || info.Kind == DayTransitionKind.NotExecuted;
            float hold = Sum(designBeats, beatCount);
            Vector2 shake = execution ? ApplyExecution(t, w, fh) : ApplyNight(t, w, fh);
            stage.anchoredPosition = shake;

            float close = execution ? EaseOut(Seg(t, Tally, Tally + 0.22f)) : EaseOut(Seg(t, 0f, 0.25f));
            float open = EaseIn(Seg(t, hold, hold + DesignOut));
            float b = bar * close * (1f - open);
            barTop.sizeDelta = new Vector2(0f, b);
            barBottom.sizeDelta = new Vector2(0f, b);
            group.alpha = 1f - EaseIn(Seg(t, hold + 0.05f, hold + DesignOut));
        }

        // ---------------------------------------------------------------- night → day

        private Vector2 ApplyNight(float t, float w, float fh)
        {
            Vector2 shake = Vector2.zero;
            bool quiet = info.Kind == DayTransitionKind.Quiet;
            bool protectedNight = info.Kind == DayTransitionKind.Protected;

            // 1. The night lifts (all three): HUD and chat go dark, "밤이 지나갑니다".
            float darken = Seg(t, 0f, 0.3f);
            SetColor(shade, Navy, 0.62f * darken);
            float open = Seg(t, 0.12f, 0.3f) * (1f - Seg(t, 0.55f, 0.72f));
            Fade(nightSmall, open, 95f);
            Fade(nightMid, open, -20f);

            float dawnAt;
            if (quiet)
            {
                // 2. A calm sea: no red, a slow pan.
                float calm = Seg(t, 0.62f, 0.85f) * (1f - Seg(t, 1.6f, 1.85f));
                ShowArt(nightArt, w, fh, calm, 1.18f, new Vector2(Mathf.Lerp(70f, -70f, Seg(t, 0.62f, 1.85f)), 0f), 1f);
                SetColor(wash, Navy, 0.25f * calm);
                Fade(calmCaption, Seg(t, 0.85f, 1.0f) * (1f - Seg(t, 1.5f, 1.65f)), -fh * 0.3f);
                Pop(onoSswa, t, 0.9f, 1.55f, new Vector2(-w * 0.28f, -fh * 0.18f), -4f);
                dawnAt = 1.6f;
            }
            else if (info.Kind == DayTransitionKind.Dragged)
            {
                shake = ApplyKraken(t, w, fh);
                dawnAt = 3.6f;
            }
            else
            {
                // 2. The strike: red, three slashes, a shake. The same for a death and a saved life.
                float red = Seg(t, 0.68f, 0.8f);
                float pulse = 0.08f * Mathf.Sin((t - 0.7f) * 14f) * Seg(t, 0.8f, 0.9f) * (1f - Seg(t, 1.3f, 1.5f));
                if (t >= 0.68f)
                {
                    SetColor(shade, Color.Lerp(Navy, Blood, red), Mathf.Lerp(0.62f, 0.86f, red) + pulse);
                }
                float strikeEnd = protectedNight ? 1.55f : 1.5f;
                float slashFade = protectedNight ? 1f - Seg(t, 1.55f, 1.75f) : 1f - Seg(t, 1.3f, 1.5f);
                for (int i = 0; i < SlashCount; i++)
                {
                    float start = 0.72f + i * 0.07f;
                    float grow = EaseOut(Seg(t, start, start + 0.13f));
                    float thin = protectedNight ? 1f - Seg(t, 1.55f, 1.72f) : 1f;
                    Slash(i, new Vector2(-40f + i * 70f, 30f - i * 64f), w * 1.35f * grow, thin, t >= start && t < strikeEnd + 0.25f ? slashFade : 0f);
                }
                if (t >= 0.72f && t < 1.0f)
                {
                    float k = 1f - Seg(t, 0.72f, 1.0f);
                    shake = new Vector2(Mathf.Sin(t * 91f), Mathf.Cos(t * 77f)) * 20f * k;
                }
                Pop(onoChaak, t, 0.76f, protectedNight ? 1.5f : 1.45f, new Vector2(w * 0.2f, fh * 0.24f), -14f);
                Fade(strikeCaption, Seg(t, 0.9f, 1.05f) * (1f - Seg(t, 1.35f, 1.5f)), -fh * 0.32f);

                if (protectedNight)
                {
                    shake += ApplyShield(t, w, fh);
                    dawnAt = 2.8f;
                }
                else
                {
                    ApplyVictim(t, w, fh);
                    dawnAt = 3.2f;
                }
            }
            if (!info.GameOver) ApplyDawn(t, w, fh, dawnAt);
            else if (t >= dawnAt) SetColor(shade, Navy, 0.62f);
            return shake;
        }

        private Vector2 ApplyShield(float t, float w, float fh)
        {
            // The slashes stop for a beat, then a gold ring cuts through them. The one who was attacked stays unknown.
            if (t < 1.5f) return Vector2.zero;
            float gold = Seg(t, 1.55f, 1.75f);
            SetColor(shade, Color.Lerp(Blood, GoldWash, gold), Mathf.Lerp(0.86f, 0.9f, gold) * (1f - Seg(t, 2.8f, 3.05f)));
            SetColor(flash, new Color(1f, 0.93f, 0.7f), 0.5f * Seg(t, 1.55f, 1.58f) * (1f - Seg(t, 1.58f, 1.8f)));
            float burst = EaseOut(Seg(t, 1.55f, 1.95f));
            float keep = Mathf.Lerp(1f, 0.65f, Seg(t, 1.95f, 2.2f)) * (1f - Seg(t, 2.6f, 2.8f)); // the ring stays as a shield
            ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.25f, 1.3f, burst);
            ring.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 25f);
            innerRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.15f, 0.92f, EaseOut(Seg(t, 1.62f, 2.02f)));
            SetColor(innerRing, Gold, t >= 1.62f ? 0.5f * keep : 0f);
            SetColor(ring, Gold, t >= 1.55f ? keep : 0f);
            float glow = Seg(t, 1.6f, 1.85f) * (1f - Seg(t, 2.55f, 2.8f));
            shieldGlow.rectTransform.localScale = Vector3.one * (1f + 0.05f * Mathf.Sin(t * 6f));
            SetColor(shieldGlow, Gold, 0.5f * glow);
            Vector2 hit = new Vector2(-w * 0.2f, fh * 0.14f);
            for (int i = 0; i < SparkCount; i++)
            {
                float angle = Hash(i, 1) * Mathf.PI * 2f, dist = Mathf.Lerp(120f, 420f, Hash(i, 2));
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                sparks[i].rectTransform.anchoredPosition = hit + dir * dist * EaseOut(Seg(t, 1.55f, 1.95f));
                sparks[i].rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(18f, 46f, Hash(i, 3));
                SetColor(sparks[i], Gold, t >= 1.55f ? 1f - Seg(t, 1.85f, 2.3f) : 0f);
            }
            Pop(onoChaeng, t, 1.56f, 2.6f, new Vector2(-w * 0.04f, fh * 0.3f), 8f);
            float title = Seg(t, 1.75f, 1.92f) * (1f - Seg(t, 2.6f, 2.8f));
            Fade(shieldTitle, title, -fh * 0.25f);
            Fade(shieldSub, Seg(t, 1.85f, 2.0f) * (1f - Seg(t, 2.6f, 2.8f)), -fh * 0.25f - 110f);
            if (t >= 1.55f && t < 1.75f)
            {
                return new Vector2(Mathf.Sin(t * 97f), Mathf.Cos(t * 83f)) * 10f * (1f - Seg(t, 1.55f, 1.75f));
            }
            return Vector2.zero;
        }

        private void ApplyVictim(float t, float w, float fh)
        {
            // The wanted poster of the dead drops in: the bust they wore, crossed out. No role.
            if (t >= 1.45f)
            {
                SetColor(shade, Color.Lerp(Blood, DeepRed, Seg(t, 1.45f, 1.6f)), 0.94f * (1f - Seg(t, 3.2f, 3.45f)));
            }
            float drop = Seg(t, 1.5f, 1.82f);
            float leave = EaseIn(Seg(t, 3.2f, 3.55f));
            float y = Mathf.Lerp(fh * 0.9f, 30f, EaseOutBack(drop));
            y = Mathf.Lerp(y, -fh * 0.42f, leave);
            poster.rect.anchoredPosition = new Vector2(0f, y);
            poster.rect.localRotation = Quaternion.Euler(0f, 0f, -3f + 4f * Mathf.Sin(Mathf.Clamp01(drop * 1.5f) * Mathf.PI) * (1f - drop));
            poster.rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.32f, leave);
            poster.group.alpha = (t >= 1.5f ? 1f : 0f) * (1f - Seg(t, 3.35f, 3.55f));
            Fade(posterHead, Seg(t, 1.62f, 1.8f) * (1f - Seg(t, 3.1f, 3.25f)), fh * 0.5f - 80f);
            float cut = EaseOut(Seg(t, 1.72f, 1.86f));
            posterCutA.rectTransform.sizeDelta = new Vector2(520f * cut, 12f);
            posterCutB.rectTransform.sizeDelta = new Vector2(470f * EaseOut(Seg(t, 1.78f, 1.92f)), 9f);
            Pop(onoDang, t, 1.9f, 3.1f, new Vector2(w * 0.27f, -fh * 0.33f), -6f);
            if (info.Mine)
            {
                float mine = Seg(t, 2.05f, 2.25f) * (1f - Seg(t, 3.1f, 3.3f));
                myBand.rectTransform.anchoredPosition = new Vector2(0f, -fh * 0.36f);
                SetColor(myBand, new Color(0.62f, 0.06f, 0.05f), 0.92f * mine);
                myLine.alpha = mine;
            }
        }

        // ---------------------------------------------------------------- the kraken night (Dragged)
        // 0.7 the sea parts · 1.3 the tentacles · 2.2 the dragged · 3.6 dawn. The kraken stays nameless; no roles shown.

        private Vector2 ApplyKraken(float t, float w, float fh)
        {
            Vector2 shake = Vector2.zero;

            // 2. The sea parts: the night turns deep teal, rings spread on the water, two amber eyes blink below it.
            //    If the pirates also killed someone, their slashes cut in first, briefly.
            float teal = Seg(t, 0.7f, 0.9f);
            if (t >= 0.7f) SetColor(shade, Color.Lerp(Navy, DeepTeal, teal), Mathf.Lerp(0.62f, 0.86f, teal));
            if (info.AlsoAttacked)
            {
                float slashFade = 1f - Seg(t, 0.92f, 1.05f);
                for (int i = 0; i < SlashCount; i++)
                {
                    float start = 0.72f + i * 0.05f;
                    float grow = EaseOut(Seg(t, start, start + 0.1f));
                    Slash(i, new Vector2(-40f + i * 70f, 30f - i * 64f), w * 1.35f * grow, 1f, t >= start && t < 1.05f ? slashFade : 0f);
                }
                Pop(onoChaak, t, 0.74f, 1.05f, new Vector2(w * 0.2f, fh * 0.24f), -14f);
            }
            Vector2 water = new Vector2(0f, -fh * 0.06f);
            float sea = 1f - Seg(t, 1.3f, 1.42f);
            for (int i = 0; i < seaRings.Length; i++)
            {
                float from = 0.8f + i * 0.1f;
                float k = EaseOut(Seg(t, from, from + 0.5f));
                float width = Mathf.Lerp(160f, 980f, k);
                seaRings[i].rectTransform.anchoredPosition = water;
                seaRings[i].rectTransform.sizeDelta = new Vector2(width, width * 0.24f);
                SetColor(seaRings[i], TealGlow, t >= from ? 0.85f * sea * (1f - 0.6f * k) : 0f);
            }
            float eyes = Seg(t, 0.98f, 1.06f) * (1f - Seg(t, 1.22f, 1.3f));
            for (int i = 0; i < krakenEyes.Length; i++)
            {
                krakenEyes[i].rectTransform.anchoredPosition = water + new Vector2(i == 0 ? -70f : 70f, 6f);
                SetColor(krakenEyes[i], Amber, eyes);
            }
            if (t >= 0.7f && t < 1.3f) shake = new Vector2(Mathf.Sin(t * 41f), Mathf.Cos(t * 37f)) * 5f * Seg(t, 0.7f, 0.85f);
            Fade(strikeCaption, Seg(t, 0.86f, 1.0f) * (1f - Seg(t, 1.2f, 1.32f)), -fh * 0.32f);

            // 3. The tentacles: the kraken's picture lunges in, two hits, a teal pulse at the edges.
            float art = Seg(t, 1.3f, 1.38f) * (1f - Seg(t, 2.05f, 2.2f));
            ShowArt(krakenArt, w, fh, art, Mathf.Lerp(1.18f, 1.02f, EaseOut(Seg(t, 1.3f, 2.2f))), Vector2.zero);
            if (t >= 1.3f && t < 1.55f) SetColor(flash, TealGlow, 0.45f * Seg(t, 1.3f, 1.33f) * (1f - Seg(t, 1.33f, 1.55f)));
            float edge = Seg(t, 1.32f, 1.45f) * (1f - Seg(t, 2.05f, 2.2f));
            float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin((t - 1.32f) * 7f));
            SetColor(vignetteTop, DeepTeal, 0.85f * edge * pulse);
            SetColor(vignetteBottom, DeepTeal, 0.85f * edge * pulse);
            float hit = t >= 1.32f && t < 1.5f ? 1f - Seg(t, 1.32f, 1.5f) : t >= 1.62f && t < 1.8f ? 1f - Seg(t, 1.62f, 1.8f) : 0f;
            if (hit > 0f) shake = new Vector2(Mathf.Sin(t * 97f), Mathf.Cos(t * 83f)) * 18f * hit;
            Pop(onoKugwang, t, 1.34f, 2.1f, new Vector2(w * 0.16f, fh * 0.28f), -9f);

            // 4. The dragged.
            ApplyDragged(t, w, fh);
            return shake;
        }

        private void ApplyDragged(float t, float w, float fh)
        {
            // An ink-soaked poster for each of the dead, fanned out, then pulled down into the sea by the tentacles.
            if (t < 2.2f) return;
            SetColor(shade, DeepTeal, 0.94f);
            Fade(posterHead, Seg(t, 2.3f, 2.45f) * (1f - Seg(t, 3.4f, 3.55f)), fh * 0.5f - 80f);
            ShowArt(krakenArt, w, fh, 0.35f * Seg(t, 2.85f, 3.05f) * (1f - Seg(t, 3.45f, 3.6f)), 1.05f, Vector2.zero, -1f);
            int n = Mathf.Max(1, shownVictims);
            float scale = n <= 1 ? 1f : n == 2 ? 0.92f : 0.78f;
            float spacing = Mathf.Min(w * 0.92f / n, 430f * scale);
            for (int i = 0; i < InkPosterCount; i++)
            {
                InkPoster p = inkPosters[i];
                if (i >= shownVictims)
                {
                    p.card.group.alpha = 0f;
                    continue;
                }
                float at = 2.25f + i * 0.08f;
                float drop = Seg(t, at, at + 0.3f);
                float pull = EaseIn(Seg(t, 3.0f + i * 0.04f, 3.5f + i * 0.04f));
                float side = i % 2 == 0 ? -1f : 1f;
                float y = Mathf.Lerp(fh * 0.8f, 20f, EaseOutBack(drop));
                y = Mathf.Lerp(y, -fh * 0.62f, pull);
                p.card.rect.anchoredPosition = new Vector2((i - (n - 1) * 0.5f) * spacing, y);
                p.card.rect.localRotation = Quaternion.Euler(0f, 0f, side * (5f + 6f * pull));
                p.card.rect.localScale = new Vector3(scale * (1f - 0.12f * pull), scale * (1f + 0.1f * pull), 1f);
                p.card.group.alpha = (t >= at ? 1f : 0f) * (1f - Seg(t, 3.42f + i * 0.04f, 3.58f + i * 0.04f));
                float soak = EaseOut(Seg(t, 2.6f + i * 0.06f, 3.1f + i * 0.06f));
                p.ink.rectTransform.localScale = new Vector3(1f, Mathf.Lerp(0.2f, 1.3f, soak), 1f);
                SetColor(p.ink, InkTeal, p.kraken ? 0.9f * soak : 0f);
                p.cut.rectTransform.sizeDelta = new Vector2(380f * EaseOut(Seg(t, at + 0.2f, at + 0.32f)), 10f);
                SetColor(p.cut, new Color(0.75f, 0.06f, 0.05f), p.kraken ? 0f : 0.95f);
            }
            Fade(victimOverflow, Seg(t, 2.6f, 2.75f) * (1f - Seg(t, 3.3f, 3.45f)), -fh * 0.3f);
            Pop(onoChwareuk, t, 3.0f, 3.5f, new Vector2(-w * 0.26f, -fh * 0.28f), 6f);
            if (anyVictimMine)
            {
                float mine = Seg(t, 2.6f, 2.8f) * (1f - Seg(t, 3.35f, 3.5f));
                myBand.rectTransform.anchoredPosition = new Vector2(0f, -fh * 0.36f);
                SetColor(myBand, new Color(0.62f, 0.06f, 0.05f), 0.92f * mine);
                myLine.alpha = mine;
            }
        }

        private void ApplyInkAtDawn(float t, float fh, float at, float rise)
        {
            // Only an ink stain and a few bubbles are left on the calm water.
            Vector2 stain = new Vector2(0f, -fh * 0.24f);
            inkStain.rectTransform.anchoredPosition = stain;
            inkStain.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(520f, 920f, Seg(t, at, at + 0.8f)), 160f);
            SetColor(inkStain, InkTeal, 0.65f * rise);
            for (int i = 0; i < 8; i++)
            {
                float k = Mathf.Repeat((t - at) * 0.6f + Hash(i, 11), 1f);
                sparks[i].rectTransform.anchoredPosition = stain + new Vector2((Hash(i, 12) - 0.5f) * 520f, 20f + k * 220f);
                sparks[i].rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(10f, 22f, Hash(i, 13));
                SetColor(sparks[i], new Color(0.8f, 1f, 0.97f), 0.7f * rise * Mathf.Sin(k * Mathf.PI));
            }
        }

        private void ApplyDawn(float t, float w, float fh, float at)
        {
            // Sunrise over the deck and the day's plaque. The scene holds here until the day begins.
            float rise = Seg(t, at, at + 0.3f);
            if (rise <= 0f) return;
            ShowArt(dayArt, w, fh, rise, 1.04f, Vector2.zero, 1f);
            SetColor(wash, new Color(0.55f, 0.25f, 0.12f), 0.28f * rise);
            SetEdgeHeight(dawnSky.rectTransform, root.rect.height * 0.5f);
            SetColor(dawnSky, new Color(0.17f, 0.1f, 0.3f), 0.6f * rise); // the morning sky darker above the sun
            if (t >= at) SetColor(shade, Navy, 0.9f);
            sun.anchoredPosition = new Vector2(0f, fh * 0.22f + Mathf.Lerp(-120f, 0f, EaseOut(Seg(t, at, at + 0.6f))));
            sun.localRotation = Quaternion.Euler(0f, 0f, (t - at) * 6f);
            SetColor(sunGlow, new Color(1f, 0.72f, 0.36f), 0.75f * rise);
            SetColor(sunCore, new Color(1f, 0.97f, 0.85f), rise);
            for (int i = 0; i < RayCount; i++)
            {
                SetColor(rays[i], new Color(1f, 0.9f, 0.62f), 0.2f * rise * (0.6f + 0.4f * Hash(i, 7)));
            }
            ShowPlaque(t, at + 0.15f, fh, true);
            if (info.Kind == DayTransitionKind.Dragged) ApplyInkAtDawn(t, fh, at, rise);
            if (info.Kind == DayTransitionKind.Protected || info.Kind == DayTransitionKind.Quiet)
            {
                Fade(dawnCaption, Seg(t, at + 0.35f, at + 0.5f), -fh * 0.12f);
                band.rectTransform.anchoredPosition = new Vector2(0f, -fh * 0.12f);
                SetColor(band, Color.black, 0.55f * Seg(t, at + 0.3f, at + 0.45f));
            }
            if (info.Kind == DayTransitionKind.Protected)
            {
                for (int i = 0; i < SparkCount; i++)
                {
                    float x = (Hash(i, 4) - 0.5f) * w, y0 = (Hash(i, 5) - 0.6f) * fh;
                    sparks[i].rectTransform.anchoredPosition = new Vector2(x + Mathf.Sin(t * 1.5f + i) * 20f, y0 + (t - at) * 60f);
                    sparks[i].rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(10f, 26f, Hash(i, 6));
                    SetColor(sparks[i], Gold, 0.85f * rise);
                }
            }
        }

        // ---------------------------------------------------------------- vote → night

        private Vector2 ApplyExecution(float t, float w, float fh)
        {
            Vector2 shake = Vector2.zero;
            bool executed = info.Kind == DayTransitionKind.Executed;

            // 1. The tally on the table (the game screen shows the piles); only the edges darken.
            float edge = Seg(t, 0.1f, 0.5f);
            SetColor(vignetteTop, Color.black, 0.55f * edge);
            SetColor(vignetteBottom, Color.black, 0.55f * edge);
            Pop(onoDrum, t, 0.15f, Tally + 0.05f, new Vector2(-w * 0.24f, -fh * 0.1f), -6f);

            // 2. The verdict: a scroll unrolls, the seal stamps.
            float dark = Seg(t, Tally, Tally + 0.2f);
            SetColor(shade, new Color(0.03f, 0.02f, 0.02f), 0.88f * dark);
            float verdictEnd = executed ? 1.8f : 2.2f;
            float unroll = EaseOut(Seg(t, Tally + 0.1f, Tally + 0.38f));
            scroll.rect.localScale = new Vector3(1f, Mathf.Max(0.02f, unroll), 1f);
            scroll.group.alpha = (t >= Tally + 0.1f ? 1f : 0f) * (1f - Seg(t, verdictEnd - 0.12f, verdictEnd));
            scrollPortraitFrame.enabled = executed;
            float stampAt = Tally + 0.5f;
            float stamp = Seg(t, stampAt, stampAt + 0.12f);
            sealRoot.localScale = Vector3.one * Mathf.Lerp(2.4f, 1f, stamp * stamp);
            sealGroup.alpha = t >= stampAt ? Mathf.Lerp(0.3f, 1f, stamp) : 0f;
            float hit = t >= stampAt + 0.12f ? 1f - Seg(t, stampAt + 0.12f, stampAt + 0.35f) : 0f;
            if (hit > 0f) shake = new Vector2(Mathf.Sin(t * 101f), Mathf.Cos(t * 89f)) * 16f * hit;
            for (int i = 0; i < SealLineCount; i++)
            {
                float k = EaseOut(Seg(t, stampAt + 0.12f, stampAt + 0.3f));
                sealLines[i].rectTransform.sizeDelta = new Vector2(Mathf.Lerp(20f, 70f, k), 9f);
                sealLines[i].rectTransform.anchoredPosition = Dir(i) * Mathf.Lerp(150f, 200f, k);
                SetColor(sealLines[i], Color.white, hit > 0f ? Mathf.Min(1f, hit * 1.6f) : 0f);
            }
            SetColor(flash, Color.white, 0.35f * hit * hit);
            Pop(onoKwang, t, stampAt + 0.12f, verdictEnd - 0.1f, new Vector2(-w * 0.3f, -fh * 0.3f), -8f);
            if (!executed)
            {
                // The void seal cracks: nobody walks the plank.
                float crack = EaseOut(Seg(t, stampAt + 0.45f, stampAt + 0.6f));
                sealCrack.sizeDelta = new Vector2(14f, 320f * crack);
                sealCrackImage.color = new Color(0.12f, 0.03f, 0.02f, crack > 0f ? 1f : 0f);
                Pop(onoCrack, t, stampAt + 0.45f, verdictEnd - 0.1f, new Vector2(w * 0.3f, fh * 0.05f), 10f);
                if (!info.GameOver) ApplyNightFall(t, w, fh, verdictEnd);
                return shake;
            }

            // 3. The plank: their skin at its end, trembling, then gone.
            float plankIn = Seg(t, 1.8f, 1.92f) * (1f - Seg(t, 3.1f, 3.12f));
            float push = Mathf.Lerp(1f, 1.06f, Seg(t, 1.8f, 3.1f));
            ShowArt(plankArt, w, fh, plankIn, push, Vector2.zero);
            if (plankIn > 0f)
            {
                float fall = Seg(t, 2.72f, 3.1f);
                float tremble = t < 2.72f ? Mathf.Sin(t * 70f) * 4f : 0f;
                RectTransform f = figure.rectTransform;
                float artH = plankArt.rect.sizeDelta.y;
                f.sizeDelta = FigureSize(artH);
                f.anchoredPosition = new Vector2(tremble + fall * 40f, -fall * fall * artH * 0.42f);
                f.localRotation = Quaternion.Euler(0f, 0f, -42f * EaseIn(fall));
                figure.color = new Color(1f, 1f, 1f, figure.sprite != null ? 1f - Seg(t, 2.98f, 3.1f) : 0f);
                float swim = Seg(t, 1.8f, 3.1f);
                fin.rectTransform.anchoredPosition = Vector2.zero;
                fin.rectTransform.anchorMin = fin.rectTransform.anchorMax = new Vector2(Mathf.Lerp(0.98f, 0.7f, swim), 1f - 0.86f);
                fin.color = new Color(0.1f, 0.12f, 0.17f, 1f);
                wake.rectTransform.anchorMin = wake.rectTransform.anchorMax = fin.rectTransform.anchorMin;
                wake.color = new Color(1f, 1f, 1f, 0.75f);
                Fade(plankCaption, Seg(t, 1.9f, 2.05f) * (1f - Seg(t, 2.95f, 3.1f)), 0f);
                Pop(onoDeol, t, 2.0f, 2.72f, Vector2.zero, -10f);
            }

            // 4. The splash. Their role stays hidden.
            float splash = t >= 3.1f ? 1f - Seg(t, 3.9f, 4.05f) : 0f;
            float pop = EaseOut(Seg(t, 3.1f, 3.35f));
            ShowArt(splashArt, w, fh, splash, Mathf.Lerp(1.1f, 1f, pop), Vector2.zero);
            if (splash > 0f)
            {
                SetColor(flash, Color.white, Mathf.Max(flash.color.a, 0.8f * (1f - Seg(t, 3.1f, 3.3f))));
                if (t < 3.35f) shake = new Vector2(Mathf.Sin(t * 95f), Mathf.Cos(t * 85f)) * 18f * (1f - Seg(t, 3.1f, 3.35f));
                for (int i = 0; i < ripples.Length; i++)
                {
                    float k = Seg(t, 3.15f + i * 0.18f, 3.9f + i * 0.18f);
                    float width = splashArt.rect.sizeDelta.x * Mathf.Lerp(0.45f, 1.25f, EaseOut(k));
                    ripples[i].rectTransform.sizeDelta = new Vector2(width, width * 0.2f);
                    ripples[i].color = new Color(1f, 1f, 1f, k > 0f ? 0.8f * (1f - k) : 0f);
                }
                Fade(splashCaption, Seg(t, 3.3f, 3.45f), 0f);
                Fade(splashSub, Seg(t, 3.4f, 3.55f), 0f);
            }
            if (!info.GameOver) ApplyNightFall(t, w, fh, 3.9f);
            return shake;
        }

        private void ApplyNightFall(float t, float w, float fh, float at)
        {
            // Dusk turns to moonlight and the next night's plaque. The scene holds here until the night begins.
            float dusk = Seg(t, at, at + 0.3f);
            if (dusk <= 0f) return;
            ShowArt(nightArt, w, fh, dusk, 1.04f, Vector2.zero, 0.4f);
            SetColor(wash, Navy, 0.35f * dusk);
            ShowPlaque(t, at + 0.1f, fh, false);
            Fade(nightCaption, Seg(t, at + 0.3f, at + 0.45f), -fh * 0.12f);
            band.rectTransform.anchoredPosition = new Vector2(0f, -fh * 0.12f);
            SetColor(band, Color.black, 0.5f * Seg(t, at + 0.25f, at + 0.4f));
        }

        private void ShowPlaque(float t, float at, float fh, bool day)
        {
            float drop = Seg(t, at, at + 0.3f);
            if (drop <= 0f) return;
            plaqueGroup.alpha = Mathf.Min(1f, drop * 3f);
            plaque.anchoredPosition = new Vector2(0f, Mathf.Lerp(fh * 0.45f, fh * 0.06f, EaseOutBack(drop)));
            plaque.localScale = Vector3.one * Mathf.Lerp(1.15f, 1f, drop);
            plaqueSun.enabled = day;
            plaqueMoon.enabled = !day;
            plaqueMoonBite.enabled = !day;
        }

        // ================================================================ parts

        private void HideAll()
        {
            SetColor(shade, Navy, 0f);
            SetColor(wash, Navy, 0f);
            SetColor(flash, Color.white, 0f);
            SetColor(vignetteTop, Color.black, 0f);
            SetColor(vignetteBottom, Color.black, 0f);
            nightArt.group.alpha = dayArt.group.alpha = plankArt.group.alpha = splashArt.group.alpha = 0f;
            SetColor(sunGlow, Color.white, 0f);
            SetColor(sunCore, Color.white, 0f);
            foreach (Image ray in rays) SetColor(ray, Color.white, 0f);
            for (int i = 0; i < SlashCount; i++)
            {
                SetColor(slashGlow[i], Color.white, 0f);
                SetColor(slashCore[i], Color.white, 0f);
            }
            SetColor(ring, Gold, 0f);
            SetColor(innerRing, Gold, 0f);
            SetColor(dawnSky, Navy, 0f);
            SetColor(shieldGlow, Gold, 0f);
            foreach (Image spark in sparks) SetColor(spark, Gold, 0f);
            poster.group.alpha = 0f;
            scroll.group.alpha = 0f;
            plaqueGroup.alpha = 0f;
            SetColor(band, Color.black, 0f);
            SetColor(myBand, Color.black, 0f);
            myLine.alpha = 0f;
            foreach (TextMeshProUGUI text in fadingTexts) text.alpha = 0f;
            foreach (Image r in ripples) SetColor(r, Color.white, 0f);
            sealCrackImage.color = Color.clear;
            krakenArt.group.alpha = 0f;
            foreach (Image r in seaRings) SetColor(r, TealGlow, 0f);
            foreach (Image eye in krakenEyes) SetColor(eye, Amber, 0f);
            foreach (InkPoster p in inkPosters) p.card.group.alpha = 0f;
            SetColor(inkStain, InkTeal, 0f);
        }

        /// <summary>
        /// A picture filling the space between the bars (cropped, never stretched), scaled and moved a little.
        /// alignY: 0 keeps its middle in view, 1 its top edge, -1 its bottom edge.
        /// </summary>
        private static void ShowArt(Art art, float w, float fh, float alpha, float scale, Vector2 offset, float alignY = 0f)
        {
            art.group.alpha = alpha;
            if (alpha <= 0f || art.image.sprite == null)
            {
                art.group.alpha = 0f;
                return;
            }
            Rect r = art.image.sprite.rect;
            float cover = Mathf.Max(w / r.width, fh / r.height) * scale;
            art.rect.sizeDelta = new Vector2(r.width * cover, r.height * cover);
            art.rect.anchoredPosition = offset - new Vector2(0f, alignY * (r.height * cover - fh) * 0.5f);
        }

        private Vector2 FigureSize(float artHeight)
        {
            if (figure.sprite == null) return Vector2.zero;
            Rect r = figure.sprite.rect;
            float height = artHeight * FigureHeight;
            return new Vector2(height * r.width / r.height, height);
        }

        private void Slash(int i, Vector2 centre, float length, float thickness, float alpha)
        {
            Quaternion angle = Quaternion.Euler(0f, 0f, 28f);
            slashGlow[i].rectTransform.anchoredPosition = slashCore[i].rectTransform.anchoredPosition = centre;
            slashGlow[i].rectTransform.localRotation = slashCore[i].rectTransform.localRotation = angle;
            slashGlow[i].rectTransform.sizeDelta = new Vector2(length, 70f * thickness);
            slashCore[i].rectTransform.sizeDelta = new Vector2(length * 0.92f, 18f * thickness);
            SetColor(slashGlow[i], new Color(1f, 0.16f, 0.1f), 0.9f * alpha);
            SetColor(slashCore[i], new Color(1f, 0.93f, 0.88f), alpha);
        }

        /// <summary>A sound word: pops in (overshoot), shakes a little, fades.</summary>
        private static void Pop(TextMeshProUGUI text, float t, float from, float to, Vector2 position, float angle)
        {
            if (t < from || t >= to)
            {
                text.alpha = 0f;
                return;
            }
            float k = Seg(t, from, from + 0.16f);
            text.alpha = Mathf.Min(1f, k * 3f) * (1f - Seg(t, to - 0.15f, to));
            RectTransform rect = text.rectTransform;
            if (position != Vector2.zero) rect.anchoredPosition = position;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle + Mathf.Sin(t * 30f) * 1.5f);
            rect.localScale = Vector3.one * Mathf.LerpUnclamped(0.4f, 1f, EaseOutBack(k));
        }

        private static void Fade(TextMeshProUGUI text, float alpha, float y)
        {
            text.alpha = alpha;
            if (text.rectTransform.anchorMin == new Vector2(0.5f, 0.5f)) text.rectTransform.anchoredPosition = new Vector2(0f, y);
        }

        private static void SetColor(Graphic graphic, Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            graphic.color = color;
        }

        private static Vector2 Dir(int i)
        {
            float a = (-160f + i * (140f / (SealLineCount - 1))) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        private void SetTexts()
        {
            string name = string.IsNullOrEmpty(info.Name) ? "?" : info.Name;
            nightSmall.text = "- " + info.Day + "일차 밤 -";
            posterName.text = name + "님";
            scrollName.text = info.Kind == DayTransitionKind.Executed ? name + "님" : "판결 불발";
            scrollVotes.text = info.Kind == DayTransitionKind.Executed && info.TotalVotes > 0
                ? info.TotalVotes + "표 중 <color=#9C1D17><b>" + info.Votes + "표</b></color>" : string.Empty;
            scrollReason.text = info.Kind == DayTransitionKind.NotExecuted ? TwoLines(info.Reason) : string.Empty;
            sealText.text = info.Kind == DayTransitionKind.Executed ? "처형" : "무효";
            bool night = info.Kind == DayTransitionKind.Executed || info.Kind == DayTransitionKind.NotExecuted;
            plaqueText.text = night ? (info.Day + 1) + "일차 밤" : info.Day + "일차 낮";
            plankCaption.text = info.Mine ? "당신이 널빤지 끝에 섰습니다" : name + "님이 널빤지 끝에 섰습니다";
            splashCaption.text = info.Mine ? "당신은 처형되었습니다" : name + "님이 처형되었습니다";
            myLine.text = "당신은 사망했습니다";
            dawnCaption.text = info.Kind == DayTransitionKind.Protected ? "아무도 죽지 않았습니다" : "밤사이 아무도 죽지 않았습니다";
            bool kraken = info.Kind == DayTransitionKind.Dragged;
            strikeCaption.text = kraken ? "어젯밤, 바다 밑에서 무언가 올라왔습니다" : "어젯밤, 누군가 습격당했습니다";
            posterHead.text = kraken && shownVictims > 1 ? "밤사이 희생자 · " + (info.Victims != null ? info.Victims.Length : shownVictims) + "명" : "밤사이 희생자";
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
            blocker.raycastTarget = true; // blocks the screen while playing; a tap skips to the last card
            group = GetComponent<CanvasGroup>();

            stage = NewRect("Stage", root);
            Stretch(stage);
            shade = Full("Shade", stage, null);
            frame = NewRect("Frame", stage); // between the bars; sized in Apply
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            nightArt = NewArt("NightArt", null);
            dayArt = NewArt("DayArt", Resources.Load<Sprite>("DayTable/Deck"));
            plankArt = NewArt("PlankArt", Resources.Load<Sprite>("DayTransition/ExecutionPlank"));
            splashArt = NewArt("SplashArt", Resources.Load<Sprite>("DayTransition/ExecutionSplash"));
            BuildPlankParts();
            BuildSplashParts();
            wash = Full("Wash", stage, null);
            vignetteTop = Edge("VignetteTop", true);
            vignetteBottom = Edge("VignetteBottom", false);
            dawnSky = Edge("DawnSky", true);
            BuildSun();
            BuildFx();
            BuildPoster();
            BuildKraken();
            BuildScroll();
            BuildPlaque();
            BuildCaptions();
            flash = Full("Flash", stage, null);
            barTop = Bar("BarTop", true);
            barBottom = Bar("BarBottom", false);
            fadingTexts = new[] { nightSmall, nightMid, strikeCaption, shieldTitle, shieldSub, calmCaption, dawnCaption,
                nightCaption, plankCaption, splashCaption, splashSub, posterHead, onoChaak, onoDang, onoChaeng, onoSswa,
                onoDrum, onoDeol, onoKwang, onoCrack, onoKugwang, onoChwareuk, victimOverflow };
        }

        /// <summary>
        /// The kraken night's parts: its picture (the ability cut-in art, NightScene/AbilityCutIns PIRATE_KRAKEN), the rings
        /// and eyes on the water, four ink-soaked posters, the ink stain at dawn and the sound words.
        /// </summary>
        private void BuildKraken()
        {
            Sprite art = null;
            AbilityCutInCatalog cutIns = Resources.Load<AbilityCutInCatalog>("NightScene/AbilityCutIns");
            if (cutIns != null) cutIns.TryFind("PIRATE_KRAKEN", out art, out _);
            krakenArt = NewArt("KrakenArt", art);
            krakenArt.rect.SetAsLastSibling();
            for (int i = 0; i < seaRings.Length; i++) seaRings[i] = Img("SeaRing", stage, NightFxSprites.Ring, TealGlow);
            for (int i = 0; i < krakenEyes.Length; i++)
            {
                krakenEyes[i] = Img("KrakenEye", stage, NightFxSprites.Dot, Amber);
                krakenEyes[i].rectTransform.sizeDelta = new Vector2(46f, 28f);
            }
            inkStain = Img("InkStain", stage, NightFxSprites.Dot, InkTeal);
            for (int i = 0; i < InkPosterCount; i++) inkPosters[i] = NewInkPoster(i);
            victimOverflow = Text("VictimOverflow", stage, 54f, Cream);
            onoKugwang = Sound("Kugwang", stage, "쿠과광!", 140f, new Color(0.03f, 0.3f, 0.3f));
            onoChwareuk = Sound("Chwareuk", stage, "촤르륵", 96f, new Color(0.02f, 0.2f, 0.21f));
        }

        private InkPoster NewInkPoster(int index)
        {
            var poster = new InkPoster { card = NewCard("InkPoster" + index, new Vector2(400f, 540f)) };
            RectTransform window = Box("Window", poster.card.rect, new Vector2(0f, 80f), new Vector2(300f, 290f), new Color(0.16f, 0.11f, 0.08f));
            window.gameObject.AddComponent<RectMask2D>();
            poster.portrait = Img("Portrait", window, null, new Color(0.72f, 0.66f, 0.6f));
            poster.portrait.rectTransform.sizeDelta = new Vector2(340f, 340f);
            poster.portrait.rectTransform.anchoredPosition = new Vector2(0f, -20f);
            poster.portrait.preserveAspect = true;
            poster.cut = Img("Cut", poster.card.rect, NightFxSprites.Streak, Color.clear);
            poster.cut.rectTransform.anchoredPosition = new Vector2(0f, 90f);
            poster.cut.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -58f);
            poster.name = Text("Name", poster.card.rect, 56f, Ink);
            poster.name.outlineWidth = 0f;
            poster.name.rectTransform.anchoredPosition = new Vector2(0f, -110f);
            poster.line = Text("Line", poster.card.rect, 28f, new Color(0.06f, 0.36f, 0.36f));
            poster.line.outlineWidth = 0f;
            poster.line.rectTransform.anchoredPosition = new Vector2(0f, -170f);
            poster.ink = Img("Ink", poster.card.rect, NightFxSprites.Dot, Color.clear);
            poster.ink.rectTransform.pivot = new Vector2(0.5f, 0f);
            poster.ink.rectTransform.anchoredPosition = new Vector2(0f, -300f);
            poster.ink.rectTransform.sizeDelta = new Vector2(470f, 260f);
            return poster;
        }

        private Art NewArt(string name, Sprite sprite)
        {
            RectTransform rect = NewRect(name, frame);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            var art = new Art { rect = rect, image = rect.gameObject.AddComponent<Image>(), group = rect.gameObject.AddComponent<CanvasGroup>() };
            art.image.sprite = sprite;
            art.image.raycastTarget = false;
            art.group.blocksRaycasts = false;
            return art;
        }

        private void BuildPlankParts()
        {
            RectTransform art = plankArt.rect;
            fin = Img("SharkFin", art, Fin, Color.white);
            fin.rectTransform.pivot = new Vector2(0.4f, 0f);
            fin.rectTransform.sizeDelta = new Vector2(96f, 64f);
            wake = Img("Wake", art, NightFxSprites.Streak, Color.white);
            wake.rectTransform.pivot = new Vector2(0.1f, 0.5f);
            wake.rectTransform.sizeDelta = new Vector2(220f, 9f);
            wake.transform.SetSiblingIndex(fin.transform.GetSiblingIndex());
            figure = Img("Figure", art, null, Color.white);
            figure.rectTransform.anchorMin = figure.rectTransform.anchorMax = new Vector2(PlankFeet.x, 1f - PlankFeet.y);
            figure.rectTransform.pivot = new Vector2(0.5f, 0f);
            figure.preserveAspect = true;
            onoDeol = Sound("Deol", art, "덜덜", 64f, new Color(0.16f, 0.1f, 0.05f));
            onoDeol.rectTransform.anchorMin = onoDeol.rectTransform.anchorMax = new Vector2(PlankFeet.x - 0.2f, 1f - PlankFeet.y + 0.16f);
            plankCaption = Text("PlankCaption", art, 50f, Cream);
            plankCaption.rectTransform.anchorMin = plankCaption.rectTransform.anchorMax = new Vector2(0.5f, 1f - TopBand);
        }

        private void BuildSplashParts()
        {
            RectTransform art = splashArt.rect;
            for (int i = 0; i < ripples.Length; i++)
            {
                ripples[i] = Img("Ripple", art, NightFxSprites.Ring, Color.white);
                ripples[i].rectTransform.anchorMin = ripples[i].rectTransform.anchorMax = new Vector2(SplashBase.x, 1f - SplashBase.y);
            }
            splashCaption = Text("SplashCaption", art, 54f, new Color(1f, 0.88f, 0.78f));
            splashCaption.rectTransform.anchorMin = splashCaption.rectTransform.anchorMax = new Vector2(0.5f, 1f - BottomBand + 0.022f);
            splashSub = Text("SplashSub", art, 36f, new Color(0.84f, 0.72f, 0.6f));
            splashSub.fontStyle = FontStyles.Normal;
            splashSub.text = "직업은 공개되지 않습니다";
            splashSub.rectTransform.anchorMin = splashSub.rectTransform.anchorMax = new Vector2(0.5f, 1f - BottomBand - 0.032f);
        }

        private void BuildSun()
        {
            sun = NewRect("Sun", stage);
            sun.anchorMin = sun.anchorMax = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < RayCount; i++)
            {
                rays[i] = Img("Ray", sun, NightFxSprites.Cone, Color.white);
                rays[i].rectTransform.pivot = new Vector2(0f, 0.5f);
                rays[i].rectTransform.sizeDelta = new Vector2(Mathf.Lerp(900f, 1300f, Hash(i, 8)), Mathf.Lerp(150f, 260f, Hash(i, 9)));
                rays[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * (360f / RayCount) + Hash(i, 10) * 12f);
            }
            sunGlow = Img("Glow", sun, NightFxSprites.Dot, Color.white);
            sunGlow.rectTransform.sizeDelta = Vector2.one * 820f;
            sunCore = Img("Core", sun, NightFxSprites.Dot, Color.white);
            sunCore.rectTransform.sizeDelta = Vector2.one * 230f;
        }

        private void BuildFx()
        {
            for (int i = 0; i < SlashCount; i++)
            {
                slashGlow[i] = Img("SlashGlow", stage, NightFxSprites.Streak, Color.white);
                slashCore[i] = Img("Slash", stage, NightFxSprites.Streak, Color.white);
            }
            shieldGlow = Img("ShieldGlow", stage, NightFxSprites.Dot, Gold);
            shieldGlow.rectTransform.sizeDelta = Vector2.one * 760f;
            innerRing = Img("ShieldRingInner", stage, NightFxSprites.Ring, Gold);
            innerRing.rectTransform.sizeDelta = Vector2.one * 700f;
            ring = Img("ShieldRing", stage, NightFxSprites.Ring, Gold);
            ring.rectTransform.sizeDelta = Vector2.one * 700f;
            for (int i = 0; i < SparkCount; i++) sparks[i] = Img("Spark", stage, NightFxSprites.Dot, Gold);
        }

        private void BuildPoster()
        {
            poster = NewCard("Poster", new Vector2(640f, 860f));
            RectTransform window = Box("Window", poster.rect, new Vector2(0f, 120f), new Vector2(540f, 520f), new Color(0.16f, 0.11f, 0.08f));
            window.gameObject.AddComponent<RectMask2D>();
            posterPortrait = Img("Portrait", window, null, new Color(0.7f, 0.62f, 0.55f));
            posterPortrait.rectTransform.sizeDelta = new Vector2(600f, 600f);
            posterPortrait.rectTransform.anchoredPosition = new Vector2(0f, -30f);
            posterPortrait.preserveAspect = true;
            Image age = Img("Age", window, null, new Color(0.35f, 0.22f, 0.12f, 0.35f));
            Stretch(age.rectTransform);
            posterRibbon = Img("Ribbon", window, null, new Color(0.04f, 0.04f, 0.04f, 0.95f));
            posterRibbon.rectTransform.sizeDelta = new Vector2(320f, 46f);
            posterRibbon.rectTransform.anchoredPosition = new Vector2(200f, 230f);
            posterRibbon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -38f);
            posterCutA = Img("Cut", poster.rect, NightFxSprites.Streak, new Color(0.75f, 0.06f, 0.05f, 0.95f));
            posterCutA.rectTransform.anchoredPosition = new Vector2(-10f, 140f);
            posterCutA.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -58f);
            posterCutB = Img("Cut", poster.rect, NightFxSprites.Streak, new Color(0.75f, 0.06f, 0.05f, 0.95f));
            posterCutB.rectTransform.anchoredPosition = new Vector2(40f, 120f);
            posterCutB.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -58f);
            posterName = Text("Name", poster.rect, 84f, Ink);
            posterName.rectTransform.anchoredPosition = new Vector2(0f, -230f);
            posterName.outlineWidth = 0f;
            posterLine = Text("Line", poster.rect, 42f, new Color(0.55f, 0.1f, 0.06f));
            posterLine.rectTransform.anchoredPosition = new Vector2(0f, -320f);
            posterLine.outlineWidth = 0f;
            posterLine.text = "밤사이 사망했습니다";
            posterHead = Text("Head", stage, 44f, new Color(0.95f, 0.65f, 0.55f));
            posterHead.text = "밤사이 희생자";
            posterHead.characterSpacing = 30f;
            myBand = Img("MyBand", stage, null, Color.black);
            myBand.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            myBand.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            myBand.rectTransform.sizeDelta = new Vector2(0f, 110f);
            myLine = Text("MyLine", myBand.rectTransform, 50f, Color.white);
            myLine.rectTransform.anchoredPosition = Vector2.zero;
        }

        private void BuildScroll()
        {
            scroll = NewCard("Verdict", new Vector2(700f, 980f));
            scroll.rect.pivot = new Vector2(0.5f, 1f);
            scroll.rect.anchoredPosition = new Vector2(0f, 500f);
            scrollTitle = Text("Title", scroll.rect, 64f, Ink);
            scrollTitle.text = "판 결";
            scrollTitle.characterSpacing = 40f;
            scrollTitle.outlineWidth = 0f;
            scrollTitle.rectTransform.anchorMin = scrollTitle.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            scrollTitle.rectTransform.anchoredPosition = new Vector2(0f, -100f);
            Image rule = Img("Rule", scroll.rect, null, new Color(0.48f, 0.35f, 0.19f));
            rule.rectTransform.anchorMin = rule.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            rule.rectTransform.sizeDelta = new Vector2(500f, 3f);
            rule.rectTransform.anchoredPosition = new Vector2(0f, -156f);
            RectTransform window = Box("Window", scroll.rect, Vector2.zero, new Vector2(480f, 400f), new Color(0.16f, 0.11f, 0.08f));
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 1f);
            window.anchoredPosition = new Vector2(0f, -385f);
            window.gameObject.AddComponent<RectMask2D>();
            scrollPortraitFrame = window.GetComponent<Image>();
            scrollPortrait = Img("Portrait", window, null, new Color(0.95f, 0.88f, 0.8f));
            scrollPortrait.rectTransform.sizeDelta = new Vector2(540f, 540f);
            scrollPortrait.rectTransform.anchoredPosition = new Vector2(0f, -20f);
            scrollPortrait.preserveAspect = true;
            scrollName = Text("Name", scroll.rect, 80f, Ink);
            scrollName.outlineWidth = 0f;
            scrollName.rectTransform.anchorMin = scrollName.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            scrollName.rectTransform.anchoredPosition = new Vector2(0f, -680f);
            scrollVotes = Text("Votes", scroll.rect, 50f, new Color(0.35f, 0.23f, 0.12f));
            scrollVotes.outlineWidth = 0f;
            scrollVotes.richText = true;
            scrollVotes.rectTransform.anchorMin = scrollVotes.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            scrollVotes.rectTransform.anchoredPosition = new Vector2(0f, -775f);
            scrollReason = Text("Reason", scroll.rect, 42f, new Color(0.35f, 0.23f, 0.12f));
            scrollReason.outlineWidth = 0f;
            scrollReason.lineSpacing = 12f;
            scrollReason.rectTransform.anchorMin = scrollReason.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            scrollReason.rectTransform.sizeDelta = new Vector2(600f, 240f);
            scrollReason.rectTransform.anchoredPosition = new Vector2(0f, -360f);
            scrollReason.fontSize = 46f;

            sealRoot = NewRect("Seal", scroll.rect);
            sealRoot.anchorMin = sealRoot.anchorMax = new Vector2(0.5f, 1f);
            sealRoot.anchoredPosition = new Vector2(205f, -540f);
            sealRoot.localRotation = Quaternion.Euler(0f, 0f, -14f);
            sealGroup = sealRoot.gameObject.AddComponent<CanvasGroup>();
            for (int i = 0; i < SealLineCount; i++)
            {
                sealLines[i] = Img("Burst", sealRoot, NightFxSprites.Streak, Color.white);
                sealLines[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(Dir(i).y, Dir(i).x) * Mathf.Rad2Deg);
            }
            Image wax = Img("Wax", sealRoot, NightFxSprites.Circle, Seal);
            wax.rectTransform.sizeDelta = Vector2.one * 270f;
            Image edge = Img("Edge", sealRoot, NightFxSprites.Ring, new Color(0.42f, 0.05f, 0.04f));
            edge.rectTransform.sizeDelta = Vector2.one * 215f;
            sealText = Text("Text", sealRoot, 72f, new Color(1f, 0.86f, 0.8f));
            sealText.outlineColor = new Color32(0x5A, 0x0C, 0x08, 0xFF);
            sealText.rectTransform.anchoredPosition = new Vector2(0f, 4f);
            sealCrackImage = Img("Crack", sealRoot, null, Color.clear);
            sealCrack = sealCrackImage.rectTransform;
            sealCrack.localRotation = Quaternion.Euler(0f, 0f, 24f);
            onoCrack = Sound("Crack", stage, "뚝", 96f, new Color(0.25f, 0.18f, 0.12f));
        }

        private void BuildPlaque()
        {
            plaque = NewRect("Plaque", stage);
            plaque.anchorMin = plaque.anchorMax = new Vector2(0.5f, 0.5f);
            plaque.sizeDelta = new Vector2(620f, 288f);
            plaqueGroup = plaque.gameObject.AddComponent<CanvasGroup>();
            Image back = plaque.gameObject.AddComponent<Image>();
            back.sprite = Resources.Load<Sprite>("GameHud/PhasePlaque");
            back.color = back.sprite != null ? Color.white : Parchment;
            back.raycastTarget = false;
            plaqueSun = Img("Sun", plaque, NightFxSprites.Dot, new Color(1f, 0.66f, 0.15f));
            plaqueSun.rectTransform.sizeDelta = Vector2.one * 92f;
            plaqueSun.rectTransform.anchoredPosition = new Vector2(-190f, 2f);
            plaqueMoon = Img("Moon", plaque, NightFxSprites.Circle, new Color(0.95f, 0.85f, 0.5f));
            plaqueMoon.rectTransform.sizeDelta = Vector2.one * 74f;
            plaqueMoon.rectTransform.anchoredPosition = new Vector2(-190f, 2f);
            plaqueMoonBite = Img("MoonBite", plaque, NightFxSprites.Circle, Parchment);
            plaqueMoonBite.rectTransform.sizeDelta = Vector2.one * 62f;
            plaqueMoonBite.rectTransform.anchoredPosition = new Vector2(-172f, 14f);
            plaqueText = Text("Text", plaque, 84f, Ink);
            plaqueText.outlineWidth = 0f;
            plaqueText.rectTransform.anchoredPosition = new Vector2(30f, 0f);
        }

        private void BuildCaptions()
        {
            band = Img("CaptionBand", stage, NightFxSprites.Dot, Color.black);
            band.rectTransform.sizeDelta = new Vector2(1500f, 150f);
            band.type = Image.Type.Simple;
            nightSmall = Text("NightDay", stage, 44f, new Color(0.9f, 0.85f, 0.72f));
            nightSmall.characterSpacing = 20f;
            nightMid = Text("NightPass", stage, 66f, Cream);
            nightMid.text = "밤이 지나갑니다...";
            strikeCaption = Text("Strike", stage, 54f, new Color(1f, 0.85f, 0.8f));
            strikeCaption.text = "어젯밤, 누군가 습격당했습니다";
            shieldTitle = Text("ShieldTitle", stage, 108f, Gold);
            shieldTitle.text = "선의의 보호";
            shieldTitle.outlineColor = new Color32(0x4A, 0x2A, 0x06, 0xFF);
            shieldSub = Text("ShieldSub", stage, 46f, Cream);
            shieldSub.text = "습격이 막혔습니다";
            calmCaption = Text("Calm", stage, 62f, Cream);
            calmCaption.text = "고요한 밤이었습니다";
            dawnCaption = Text("DawnCaption", stage, 58f, Color.white);
            nightCaption = Text("NightCaption", stage, 48f, Cream);
            nightCaption.text = "밤이 찾아옵니다. 능력을 사용하세요";
            onoChaak = Sound("Chaak", stage, "촤악!", 140f, new Color(0.62f, 0.07f, 0.05f));
            onoDang = Sound("Dang", stage, "댕-", 96f, new Color(0.1f, 0.07f, 0.05f));
            onoChaeng = Sound("Chaeng", stage, "챙!", 150f, new Color(0.55f, 0.35f, 0.04f));
            onoSswa = Sound("Sswa", stage, "쏴아...", 76f, new Color(0.1f, 0.28f, 0.48f));
            onoDrum = Sound("Drum", stage, "두구두구...", 72f, new Color(0.1f, 0.07f, 0.05f));
            onoKwang = Sound("Kwang", stage, "쾅!", 150f, new Color(0.62f, 0.07f, 0.05f));
        }

        private Card NewCard(string name, Vector2 size)
        {
            RectTransform rect = NewRect(name, stage);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            var card = new Card { rect = rect, group = rect.gameObject.AddComponent<CanvasGroup>() };
            Image back = rect.gameObject.AddComponent<Image>();
            back.sprite = Resources.Load<Sprite>("GameHud/ParchmentPanel"); // 9-sliced in its import settings
            back.type = back.sprite != null && back.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            back.color = back.sprite != null ? Color.white : Parchment;
            back.raycastTarget = false;
            return card;
        }

        private RectTransform Box(string name, RectTransform parent, Vector2 position, Vector2 size, Color color)
        {
            Image image = Img(name, parent, null, color);
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Outline line = image.gameObject.AddComponent<Outline>();
            line.effectColor = new Color(0.36f, 0.24f, 0.13f);
            line.effectDistance = new Vector2(5f, -5f);
            return rect;
        }

        private Image Full(string name, RectTransform parent, Sprite sprite)
        {
            Image image = Img(name, parent, sprite, Color.clear);
            Stretch(image.rectTransform);
            return image;
        }

        private Image Edge(string name, bool top)
        {
            Image image = Img(name, stage, NightFxSprites.Fade, Color.clear);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            // The fade is solid at its bottom: the top edge flips it in place (around its middle, so it stays on screen).
            rect.localScale = new Vector3(1f, top ? -1f : 1f, 1f);
            SetEdgeHeight(rect, 520f);
            return image;
        }

        private static void SetEdgeHeight(RectTransform rect, float height)
        {
            bool top = rect.anchorMin.y > 0.5f;
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, top ? -height * 0.5f : height * 0.5f);
        }

        /// <summary>Breaks a sentence at the space nearest its middle (Korean would otherwise wrap inside a word).</summary>
        private static string TwoLines(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 14) return text ?? string.Empty;
            int best = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ' && (best < 0 || Mathf.Abs(i - text.Length / 2) < Mathf.Abs(best - text.Length / 2))) best = i;
            }
            return best < 0 ? text : text.Substring(0, best) + "\n" + text.Substring(best + 1);
        }

        private RectTransform Bar(string name, bool top)
        {
            Image image = Img(name, root, null, new Color(0.02f, 0.016f, 0.012f, 1f));
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rect.pivot = new Vector2(0.5f, top ? 1f : 0f);
            Image rim = Img("Rim", rect, null, new Color(0.72f, 0.54f, 0.23f, 0.55f));
            rim.rectTransform.anchorMin = new Vector2(0f, top ? 0f : 1f);
            rim.rectTransform.anchorMax = new Vector2(1f, top ? 0f : 1f);
            rim.rectTransform.sizeDelta = new Vector2(0f, 3f);
            return rect;
        }

        private TextMeshProUGUI Text(string name, RectTransform parent, float size, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1000f, size * 1.5f);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            text.raycastTarget = false;
            if (text.font != null)
            {
                text.outlineWidth = 0.22f;
                text.outlineColor = new Color32(0x08, 0x06, 0x05, 0xFF);
            }
            return text;
        }

        private TextMeshProUGUI Sound(string name, RectTransform parent, string word, float size, Color outline)
        {
            TextMeshProUGUI text = Text("Sound" + name, parent, size, Color.white);
            text.text = word;
            if (text.font != null)
            {
                text.outlineWidth = 0.3f;
                text.outlineColor = outline;
            }
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

        // ================================================================ math

        private static float Seg(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));
        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
        private static float EaseIn(float x) => x * x * x;

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        private static float Hash(int i, int salt)
        {
            float v = Mathf.Sin(i * 12.9898f + salt * 78.233f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        // ================================================================ shark fin

        private static Sprite finSprite;

        /// <summary>A white dorsal fin (tinted by the Image): a curved leading edge up to the tip, a concave trailing edge.</summary>
        private static Sprite Fin => finSprite != null ? finSprite : (finSprite = MakeFin());

        private static Sprite MakeFin()
        {
            const int W = 96, H = 64;
            const float Tip = 0.6f;
            var texture = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                name = "FxFin",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                    float front = u < Tip ? Mathf.Pow(u / Tip, 0.85f) : 1f;
                    float back = u <= Tip ? 1f : Mathf.Pow(Mathf.Clamp01(1f - (u - Tip) / 0.32f), 1.8f);
                    float a = Mathf.Clamp01((Mathf.Min(front, back) - v) * H);
                    pixels[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, W, H), new Vector2(0.4f, 0f), 100f);
            sprite.name = "FxFin";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
