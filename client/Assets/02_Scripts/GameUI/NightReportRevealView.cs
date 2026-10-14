using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.GameUI
{
    /// <summary>A player on a night report: the nickname and the bust of the skin they wear at the table.</summary>
    public sealed class NightReportFace
    {
        public string Name;     // nickname
        public Sprite Portrait; // the bust of their skin (none: an empty disc)
        public bool Me;         // shown as "나"
    }

    /// <summary>One thing the parrot saw its target do: the action (its icon), the sentence (ReportFormatter) and on whom.</summary>
    public sealed class NightReportAction
    {
        public string ActionCode;
        public string Line;
        public NightReportFace Target; // null: on no one (the kraken's strike)
    }

    /// <summary>
    /// The other private night results, on that player's screen when the day opens (after the night's cut-scene), on the
    /// same gold-framed parchment as the captain's <see cref="InvestigationRevealView"/>:
    /// - Lookout (VISITORS): the watched player's bust in a lantern glow, then the busts of those who came, one by one;
    ///   a lone lantern and "아무도 찾아오지 않았습니다" when no one did.
    /// - Drunk (CORPSE_ROLE): the corpse's greyed bust, a wobbling bottle, and a card that flips from its back to the role's
    ///   picture (its ability cut-in art) and name, in the colour of the role's side.
    /// - Parrot (ACTIONS): the watched player's bust and a line per thing they did (an icon, the bust of whom, the sentence).
    /// - Blocked (BLOCKED): a short grey band, like the siren's failed band.
    /// About 3.5 s (the parrot's longer with more lines); the first tap shows everything, the next one closes it. After a
    /// reconnect it opens with everything shown. Shows only what the server told this player (a monkey's fake result looks
    /// the same). Unscaled time.
    /// </summary>
    public sealed class NightReportRevealView : MonoBehaviour, IPointerClickHandler
    {
        private enum Kind { Visitors, Corpse, Actions, Band }

        private const float RiseEnd = 0.4f, LetterOut = 0.45f;
        private const float BandIn = 0.2f, BandHold = 1.0f, BandOut = 0.25f;
        private const float FlipAt = 1.15f, FlipEnd = 1.5f;
        private const int SlotCount = 6, RowCount = 3;
        private const float FaceRadius = 100f, SlotRadius = 52f, SlotSpacing = 150f, SlotY = -222f;
        private const float RowStep = 102f, RowWidth = 680f, RowHeight = 92f;
        private static readonly Vector2 CardRest = new Vector2(170f, -30f), CardSize = new Vector2(256f, 410f);
        private static readonly Vector2 FeatherOffset = new Vector2(168f, 78f); // by the watched face, upper right

        private static readonly Color Ink = new Color(0.27f, 0.16f, 0.06f);
        private static readonly Color Night = new Color(0f, 0.03f, 0.06f);
        private static readonly Color Gold = new Color(0.85f, 0.65f, 0.3f);
        private static readonly Color Lamp = new Color(1f, 0.76f, 0.36f);
        private static readonly Color Cream = new Color(0.98f, 0.92f, 0.78f);
        private static readonly Color Violet = new Color(0.46f, 0.26f, 0.62f);
        private static readonly Color DeadGrey = new Color(0.66f, 0.67f, 0.7f);
        private static readonly Color Grey = new Color(0.55f, 0.57f, 0.62f);

        private RectTransform root, letter, visitorsPart, corpsePart, actionsPart, card, cardBack, cardFront, band;
        private CanvasGroup group, letterGroup, labelGroup, cardGroup, bandGroup;
        private Image dim, glow, lantern, bottle, roleArt, cardLine, roleBand, burst, feather;
        private TextMeshProUGUI title, day, verdict, note, roleLabel;
        private Face face;
        private readonly Face[] slots = new Face[SlotCount];
        private readonly Row[] rows = new Row[RowCount];
        private TMP_FontAsset font;
        private AbilityCutInCatalog cutIns;
        private Coroutine routine;
        private Kind kind;
        private int shown;                       // visitors or lines on the report
        private float faceScale = 1f, slotScale = 1f;
        private float rowTop;                    // the parrot's first line
        private float doneAt, holdEnd, outTime;  // everything shown; away; how long away takes
        private int taps;

        private sealed class Face
        {
            public RectTransform root;
            public CanvasGroup group;
            public Image bust, ring, dead;
            public TextMeshProUGUI name;
            public bool plated; // the name on a plate: "철수님"
        }

        private sealed class Row
        {
            public RectTransform root;
            public CanvasGroup group;
            public Image icon;
            public Face face;
            public TextMeshProUGUI line;
        }

        public bool IsPlaying => routine != null;

        public static NightReportRevealView Create(RectTransform canvas, TMP_FontAsset font)
        {
            var go = new GameObject("NightReportReveal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.layer = canvas.gameObject.layer;
            var view = go.AddComponent<NightReportRevealView>();
            view.Build(canvas, font);
            go.SetActive(false);
            return view;
        }

        /// <summary>The lookout's report: whom they watched and who came to them.</summary>
        /// <param name="dayNumber">The night the report is from.</param>
        /// <param name="watched">The watched player.</param>
        /// <param name="visitors">Who came, as the server listed them (empty: no one).</param>
        /// <param name="opened">Open with everything shown (a reconnect).</param>
        /// <param name="waitWhile">Wait while this is true before showing (another cut-in on screen).</param>
        public void PlayVisitors(int dayNumber, NightReportFace watched, IList<NightReportFace> visitors, bool opened,
            Func<bool> waitWhile = null)
        {
            Stop();
            Open(Kind.Visitors, "감 시  보 고", dayNumber, "망루지기의 감시");
            SetFace(face, watched, false);
            faceScale = 1.05f;
            face.root.anchoredPosition = new Vector2(0f, 100f);
            glow.rectTransform.anchoredPosition = face.root.anchoredPosition;
            int count = visitors != null ? visitors.Count : 0;
            shown = Mathf.Min(count, SlotCount);
            float spacing = Mathf.Min(SlotSpacing, 680f / Mathf.Max(1, shown));
            slotScale = spacing / SlotSpacing;
            for (int i = 0; i < SlotCount; i++)
            {
                bool on = i < shown;
                slots[i].root.gameObject.SetActive(on);
                if (!on) continue;
                SetFace(slots[i], visitors[i], false);
                slots[i].root.anchoredPosition = new Vector2((i - (shown - 1) * 0.5f) * spacing, SlotY);
            }
            lantern.gameObject.SetActive(shown == 0);
            verdict.text = count == 0 ? "아무도 찾아오지 않았습니다" : Whom(watched) + " " + count + "명이 찾아왔습니다";
            verdict.color = Ink;
            verdict.rectTransform.anchoredPosition = new Vector2(0f, -358f);
            note.text = count > shown ? "외 " + (count - shown) + "명은 채팅 기록에 있습니다" : string.Empty;
            note.rectTransform.anchoredPosition = new Vector2(0f, -408f);
            doneAt = shown > 0 ? VisitorAt(shown - 1) + 0.3f : 1.2f;
            holdEnd = Mathf.Max(3.4f, doneAt + 2.2f);
            Begin(opened, waitWhile);
        }

        /// <summary>The drunk's report: whose corpse and the role it had.</summary>
        /// <param name="dayNumber">The night the report is from.</param>
        /// <param name="corpse">The dead player.</param>
        /// <param name="roleCode">The role the server said (roles.code; picks the picture and the colour).</param>
        /// <param name="roleName">Its name as the server said it (none: the code).</param>
        /// <param name="opened">Open with the card already turned (a reconnect).</param>
        /// <param name="waitWhile">Wait while this is true before showing (another cut-in on screen).</param>
        public void PlayCorpseRole(int dayNumber, NightReportFace corpse, string roleCode, string roleName, bool opened,
            Func<bool> waitWhile = null)
        {
            Stop();
            Open(Kind.Corpse, "시 체  확 인", dayNumber, "주정뱅이의 확인");
            SetFace(face, corpse, true);
            faceScale = 1f;
            face.root.anchoredPosition = new Vector2(-180f, 0f);
            string role = !string.IsNullOrEmpty(roleName) ? roleName : (string.IsNullOrEmpty(roleCode) ? "?" : roleCode);
            Color tone = RoleTone(roleCode);
            Sprite art = null;
            if (cutIns != null) cutIns.TryFind(NightRoomRules.DesignIdFor(roleCode), out art, out _);
            roleArt.sprite = art;
            roleArt.enabled = art != null;
            if (art != null)
            {
                float width = 262f; // a little wider than the window: the face and shoulders fill it
                roleArt.rectTransform.sizeDelta = new Vector2(width, width * art.rect.height / Mathf.Max(1f, art.rect.width));
            }
            roleLabel.text = role;
            cardLine.color = tone;
            roleBand.color = Color.Lerp(tone, Color.black, 0.45f);
            burst.color = Color.clear;
            verdict.text = Whose(corpse) + " 직업은 " + role + "입니다";
            verdict.color = Color.Lerp(tone, Ink, 0.35f);
            verdict.rectTransform.anchoredPosition = new Vector2(0f, -335f);
            note.text = string.Empty;
            doneAt = FlipEnd + 0.05f;
            holdEnd = 3.6f;
            Begin(opened, waitWhile);
        }

        /// <summary>The parrot's report: whom they watched and what that player did.</summary>
        /// <param name="dayNumber">The night the report is from.</param>
        /// <param name="watched">The watched player.</param>
        /// <param name="actions">What they did, as the server listed it (empty: nothing).</param>
        /// <param name="nothingLine">The line when they did nothing (ReportFormatter).</param>
        /// <param name="opened">Open with every line shown (a reconnect).</param>
        /// <param name="waitWhile">Wait while this is true before showing (another cut-in on screen).</param>
        public void PlayActions(int dayNumber, NightReportFace watched, IList<NightReportAction> actions, string nothingLine,
            bool opened, Func<bool> waitWhile = null)
        {
            Stop();
            Open(Kind.Actions, "관 찰  보 고", dayNumber, "앵무새의 관찰");
            SetFace(face, watched, false);
            faceScale = 0.9f;
            int count = actions != null ? actions.Count : 0;
            shown = Mathf.Clamp(count, 1, RowCount); // nothing done is one line too
            // The face and the lines together in the middle of the report: fewer lines, lower face.
            face.root.anchoredPosition = new Vector2(0f, 118f - (RowCount - shown) * RowStep * 0.5f);
            rowTop = face.root.anchoredPosition.y - 230f;
            for (int i = 0; i < RowCount; i++)
            {
                Row row = rows[i];
                bool on = i < shown;
                row.root.gameObject.SetActive(on);
                if (!on) continue;
                NightReportAction action = i < count ? actions[i] : null;
                row.icon.sprite = Icon(action != null ? ActionIcon(action.ActionCode) : "moon");
                row.line.text = action != null ? action.Line : nothingLine;
                bool target = action != null && action.Target != null;
                row.face.root.gameObject.SetActive(target);
                if (target) SetFace(row.face, action.Target, false);
                // Without a bust the sentence starts where the bust would be.
                row.line.rectTransform.anchoredPosition = new Vector2(target ? -180f : -254f, 0f);
                row.line.rectTransform.sizeDelta = new Vector2(target ? 500f : 574f, RowHeight - 8f);
            }
            verdict.text = string.Empty;
            note.text = count > RowCount ? "외 " + (count - RowCount) + "건은 채팅 기록에 있습니다" : string.Empty;
            note.rectTransform.anchoredPosition = new Vector2(0f, -405f);
            doneAt = RowAt(shown - 1) + 0.4f;
            holdEnd = Mathf.Max(3.6f, doneAt + 2.4f + 0.6f * (shown - 1));
            Begin(opened, waitWhile);
        }

        /// <summary>My ability was blocked by the boatswain: a short grey band (only when the result has just come).</summary>
        public void PlayBlocked(Func<bool> waitWhile = null)
        {
            Stop();
            Open(Kind.Band, string.Empty, 0, string.Empty);
            doneAt = 0f;
            holdEnd = BandIn + BandHold;
            outTime = BandOut;
            Begin(false, waitWhile);
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

        public void OnPointerClick(PointerEventData eventData)
        {
            taps++;
        }

        private void Open(Kind what, string heading, int dayNumber, string whose)
        {
            kind = what;
            letter.gameObject.SetActive(what != Kind.Band);
            band.gameObject.SetActive(what == Kind.Band);
            visitorsPart.gameObject.SetActive(what == Kind.Visitors);
            corpsePart.gameObject.SetActive(what == Kind.Corpse);
            actionsPart.gameObject.SetActive(what == Kind.Actions);
            glow.color = Color.clear;
            title.text = heading;
            day.text = dayNumber > 0 ? dayNumber + "일차 밤 · " + whose : whose;
            outTime = LetterOut;
        }

        private void Begin(bool opened, Func<bool> waitWhile)
        {
            // Active (a coroutine needs it) but unseen and not in the way while it waits.
            gameObject.SetActive(true);
            group.alpha = 0f;
            group.blocksRaycasts = false;
            routine = StartCoroutine(Run(opened ? doneAt + 0.05f : 0f, waitWhile));
        }

        private IEnumerator Run(float from, Func<bool> waitWhile)
        {
            while (waitWhile != null && waitWhile()) yield return null;
            group.blocksRaycasts = true;
            root.SetAsLastSibling(); // over the drawer, the chat and the day's cut-scene
            taps = 0;
            float start = Time.unscaledTime - from;
            float outAt = holdEnd;
            while (true)
            {
                float t = Time.unscaledTime - start;
                if (taps > 0)
                {
                    if (t < doneAt) start -= doneAt - t; // first tap: everything shown
                    else if (outAt > t) outAt = t;       // after it: close
                    taps = 0;
                    t = Time.unscaledTime - start;
                }
                if (t >= outAt + outTime) break;
                Apply(t, from, Seg(t, outAt, outAt + outTime));
                yield return null;
            }
            routine = null;
            gameObject.SetActive(false);
        }

        private void Apply(float t, float from, float leave)
        {
            if (kind == Kind.Band)
            {
                ApplyBand(t, leave);
                return;
            }
            float h = root.rect.height;
            group.alpha = Seg(t, from, from + 0.15f) * (1f - leave);
            Set(dim, Night, 0.72f * Seg(t, 0f, 0.25f) * (1f - leave));

            // 1. The report rises from the bottom, a little turned, and settles; away, it sinks a little as it fades.
            float rise = EaseOutBack(Seg(t, 0f, RiseEnd));
            Vector2 rest = new Vector2(0f, h * 0.04f);
            letter.anchoredPosition = Vector2.Lerp(rest - new Vector2(0f, h * 0.7f), rest, rise) - new Vector2(0f, h * 0.05f * EaseIn(leave));
            letter.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(7f, 0f, rise));
            letterGroup.alpha = Seg(t, 0f, 0.12f);

            // 2. The watched player's (or the corpse's) face pops in.
            float pop = EaseOutBack(Seg(t, RiseEnd - 0.05f, RiseEnd + 0.25f));
            face.root.localScale = Vector3.one * faceScale * Mathf.Max(0f, Mathf.Lerp(0.6f, 1f, pop));

            // 3. What the report says.
            switch (kind)
            {
                case Kind.Visitors: ApplyVisitors(t); break;
                case Kind.Corpse: ApplyCorpse(t); break;
                case Kind.Actions: ApplyActions(t); break;
            }

            // 4. It in words.
            float say = Seg(t, doneAt - 0.05f, doneAt + 0.25f);
            verdict.alpha = say;
            note.alpha = say * 0.8f;
        }

        private static float VisitorAt(int i) => 0.85f + i * 0.16f;
        private static float RowAt(int i) => 0.8f + i * 0.22f;

        // The lantern glows behind the watched face; the visitors come up one by one and the glow flares as each arrives.
        private void ApplyVisitors(float t)
        {
            labelGroup.alpha = Seg(t, 0.7f, 0.9f);
            float flare = 0f;
            for (int i = 0; i < shown; i++)
            {
                float at = VisitorAt(i);
                float k = Seg(t, at, at + 0.3f);
                Face slot = slots[i];
                slot.root.localScale = Vector3.one * slotScale * Mathf.Max(0f, EaseOutBack(k));
                slot.group.alpha = Mathf.Clamp01(k * 3f);
                slot.root.anchoredPosition = new Vector2(slot.root.anchoredPosition.x, SlotY - 40f * (1f - EaseOut(k)));
                if (t >= at + 0.15f) flare = Mathf.Max(flare, 1f - Seg(t, at + 0.15f, at + 0.45f));
            }
            Set(glow, Lamp, 0.5f * Seg(t, 0.45f, 0.8f) * (0.85f + 0.15f * Mathf.Sin(t * 3f)) + 0.3f * flare);
            glow.rectTransform.sizeDelta = Vector2.one * (470f + 50f * flare);
            if (shown == 0)
            {
                // No one came: a lone lantern swings where they would stand.
                float k = Seg(t, 0.85f, 1.15f);
                lantern.rectTransform.localScale = Vector3.one * Mathf.Max(0f, EaseOutBack(k));
                lantern.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 2.2f) * 9f);
                Set(lantern, Ink, 0.8f * k);
            }
        }

        // The bottle wobbles and tips towards the card; the card turns from its back to the role.
        private void ApplyCorpse(float t)
        {
            float show = Seg(t, 0.5f, 0.7f);
            float wobble = Mathf.Sin((t - 0.5f) * 14f) * 16f * (1f - Seg(t, 0.6f, FlipAt));
            float pour = Seg(t, 0.95f, FlipAt) * (1f - Seg(t, FlipEnd, FlipEnd + 0.3f));
            bottle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, wobble - 35f * pour);
            Set(bottle, Ink, 0.85f * show);

            float appear = EaseOutBack(Seg(t, 0.5f, 0.8f));
            cardGroup.alpha = Seg(t, 0.5f, 0.65f);
            float k = Seg(t, FlipAt, FlipEnd);
            float turn = Mathf.Abs(Mathf.Cos(k * Mathf.PI));
            float size = Mathf.Max(0f, Mathf.Lerp(0.85f, 1f, appear));
            card.localScale = new Vector3(turn * size, size, 1f);
            card.anchoredPosition = CardRest + new Vector2(0f, 26f * Mathf.Sin(k * Mathf.PI));
            bool front = k >= 0.5f;
            if (cardFront.gameObject.activeSelf != front) cardFront.gameObject.SetActive(front);
            if (cardBack.gameObject.activeSelf == front) cardBack.gameObject.SetActive(!front);

            float ring = Seg(t, FlipEnd, FlipEnd + 0.45f);
            burst.rectTransform.sizeDelta = new Vector2(1f, 1.3f) * Mathf.Lerp(260f, 600f, EaseOut(ring));
            Set(burst, cardLine.color, t < FlipEnd ? 0f : 0.75f * (1f - ring));
        }

        // A feather drifts by the face; the lines slide in one by one.
        private void ApplyActions(float t)
        {
            Set(feather, Ink, 0.7f * Seg(t, 0.55f, 0.85f));
            feather.rectTransform.anchoredPosition = face.root.anchoredPosition + FeatherOffset + new Vector2(Mathf.Sin(t * 1.3f) * 10f, Mathf.Cos(t * 1.1f) * 6f);
            feather.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -20f + Mathf.Sin(t * 1.5f) * 12f);
            for (int i = 0; i < shown; i++)
            {
                float at = RowAt(i);
                Row row = rows[i];
                row.root.anchoredPosition = new Vector2(Mathf.Lerp(80f, 0f, EaseOut(Seg(t, at, at + 0.3f))), rowTop - i * RowStep);
                row.group.alpha = Seg(t, at, at + 0.2f);
                row.icon.rectTransform.localScale = Vector3.one * Mathf.Max(0f, EaseOutBack(Seg(t, at + 0.12f, at + 0.4f)));
            }
        }

        private void ApplyBand(float t, float leave)
        {
            float w = root.rect.width;
            group.alpha = 1f;
            Set(dim, Night, 0f);
            float enter = EaseOut(Seg(t, 0f, BandIn));
            band.anchoredPosition = new Vector2(Mathf.Lerp(-w, 0f, enter) + w * leave * leave, band.anchoredPosition.y);
            bandGroup.alpha = Seg(t, 0f, 0.1f) * (1f - leave);
        }

        // ================================================================ build

        private void Build(RectTransform canvas, TMP_FontAsset labelFont)
        {
            font = labelFont;
            cutIns = Resources.Load<AbilityCutInCatalog>("NightScene/AbilityCutIns");
            root = (RectTransform)transform;
            root.SetParent(canvas, false);
            Stretch(root);
            Image blocker = GetComponent<Image>();
            blocker.color = Color.clear;
            blocker.raycastTarget = true; // blocks the screen while it shows; taps skip and close
            group = GetComponent<CanvasGroup>();
            dim = Img("Dim", root, null, Color.clear);
            Stretch(dim.rectTransform);

            BuildLetter();
            glow = Img("Glow", letter, NightFxSprites.Dot, Color.clear); // under the face
            face = NewFace("Face", letter, FaceRadius, true, true);
            BuildVisitors();
            BuildCorpse();
            BuildActions();
            verdict = Text("Verdict", letter, 46f, Ink);
            verdict.rectTransform.sizeDelta = new Vector2(700f, 70f);
            verdict.enableAutoSizing = true;
            verdict.fontSizeMin = 28f;
            verdict.fontSizeMax = 46f;
            note = Text("Note", letter, 28f, new Color(Ink.r, Ink.g, Ink.b, 0.8f));
            note.fontStyle = FontStyles.Normal;
            BuildBand();
        }

        // The parchment of the captain's report: the frame, its crests, the title, the night and a rule.
        private void BuildLetter()
        {
            letter = NewRect("Report", root);
            letter.anchorMin = letter.anchorMax = new Vector2(0.5f, 0.5f);
            letter.sizeDelta = new Vector2(800f, 1000f);
            letterGroup = letter.gameObject.AddComponent<CanvasGroup>();
            Sprite paper = Resources.Load<Sprite>("GameHud/ParchmentPanel");
            Image sheet = Img("Paper", letter, paper, paper != null ? Color.white : new Color(0.93f, 0.85f, 0.66f));
            Stretch(sheet.rectTransform);
            const float frame = 0.72f; // as heavy as on the captain's report
            if (paper != null)
            {
                sheet.type = Image.Type.Sliced;
                sheet.pixelsPerUnitMultiplier = frame;
                Crest(letter, "ParchmentCrestTop", true, frame);
                Crest(letter, "ParchmentCrestBottom", false, frame);
            }
            title = Text("Title", letter, 60f, Ink);
            title.rectTransform.anchoredPosition = new Vector2(0f, 338f); // under the top crest
            day = Text("Day", letter, 30f, new Color(Ink.r, Ink.g, Ink.b, 0.75f));
            day.fontStyle = FontStyles.Normal;
            day.rectTransform.anchoredPosition = new Vector2(0f, 282f);
            Image rule = Img("Rule", letter, NauticalHud.Get("TitleRule"), new Color(0.55f, 0.38f, 0.16f));
            rule.preserveAspect = true;
            rule.rectTransform.sizeDelta = new Vector2(520f, 36f);
            rule.rectTransform.anchoredPosition = new Vector2(0f, 248f);
        }

        // "찾아온 사람" between footprints, the visitors' places and the lantern for no one.
        private void BuildVisitors()
        {
            visitorsPart = Part("Visitors");
            RectTransform label = NewRect("Label", visitorsPart);
            label.anchorMin = label.anchorMax = new Vector2(0.5f, 0.5f);
            label.sizeDelta = new Vector2(400f, 50f);
            label.anchoredPosition = new Vector2(0f, -140f);
            labelGroup = label.gameObject.AddComponent<CanvasGroup>();
            TextMeshProUGUI words = Text("Words", label, 30f, new Color(Ink.r, Ink.g, Ink.b, 0.8f));
            words.text = "찾아온 사람";
            words.rectTransform.anchoredPosition = Vector2.zero;
            for (int side = -1; side <= 1; side += 2)
            {
                Image steps = Img("Steps", label, Icon("foot_print"), new Color(Ink.r, Ink.g, Ink.b, 0.55f));
                steps.preserveAspect = true;
                steps.rectTransform.sizeDelta = Vector2.one * 40f;
                steps.rectTransform.anchoredPosition = new Vector2(side * 130f, 0f);
                steps.rectTransform.localRotation = Quaternion.Euler(0f, 0f, side * -20f);
            }
            for (int i = 0; i < SlotCount; i++) slots[i] = NewFace("Visitor", visitorsPart, SlotRadius, false, true);
            lantern = Img("Lantern", visitorsPart, Icon("lantern"), Color.clear);
            lantern.preserveAspect = true;
            lantern.rectTransform.pivot = new Vector2(0.5f, 1f); // swings from its handle
            lantern.rectTransform.sizeDelta = Vector2.one * 100f;
            lantern.rectTransform.anchoredPosition = new Vector2(0f, SlotY + 50f);
        }

        // The bottle and the role card: its back, then the role's picture in a window over a band with its name.
        private void BuildCorpse()
        {
            corpsePart = Part("Corpse");
            bottle = Img("Bottle", corpsePart, Icon("bottle"), Color.clear);
            bottle.preserveAspect = true;
            bottle.rectTransform.sizeDelta = Vector2.one * 72f;
            bottle.rectTransform.anchoredPosition = new Vector2(-12f, 10f);
            burst = Img("Burst", corpsePart, NightFxSprites.Ring, Color.clear);
            burst.rectTransform.anchoredPosition = CardRest;

            card = NewRect("Card", corpsePart);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = CardSize;
            card.anchoredPosition = CardRest;
            cardGroup = card.gameObject.AddComponent<CanvasGroup>();
            Sprite backArt = Resources.Load<Sprite>("GameHud/CardBack");
            Image back = Img("Back", card, backArt, backArt != null ? Color.white : new Color(0.1f, 0.25f, 0.3f));
            Stretch(back.rectTransform);
            cardBack = back.rectTransform;

            cardFront = NewRect("Front", card);
            Stretch(cardFront);
            HudShapes.Panel(cardFront, new Color(0.1f, 0.08f, 0.07f), Color.white, 22f);
            cardLine = cardFront.Find("Outline").GetComponent<Image>();
            cardLine.pixelsPerUnitMultiplier *= 0.5f; // a thick frame in the side's colour
            Image window = Img("Window", cardFront, null, new Color(0.05f, 0.06f, 0.09f));
            window.rectTransform.sizeDelta = new Vector2(CardSize.x - 28f, 300f);
            window.rectTransform.anchoredPosition = new Vector2(0f, 41f);
            window.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            roleArt = Img("Art", window.rectTransform, null, Color.white);
            roleArt.preserveAspect = true;
            roleArt.rectTransform.anchoredPosition = new Vector2(0f, -60f); // the top of the picture (face, shoulders) in the window
            roleBand = Img("NameBand", cardFront, null, Color.black);
            roleBand.rectTransform.sizeDelta = new Vector2(CardSize.x - 28f, 70f);
            roleBand.rectTransform.anchoredPosition = new Vector2(0f, -150f);
            roleLabel = Text("Role", roleBand.rectTransform, 40f, Cream);
            roleLabel.rectTransform.anchoredPosition = Vector2.zero;
            roleLabel.rectTransform.sizeDelta = new Vector2(CardSize.x - 48f, 60f);
            roleLabel.enableAutoSizing = true;
            roleLabel.fontSizeMin = 24f;
            roleLabel.fontSizeMax = 40f;
            roleLabel.overflowMode = TextOverflowModes.Ellipsis;
            cardFront.gameObject.SetActive(false);
        }

        // The feather and the lines: an icon, the bust of whom, the sentence.
        private void BuildActions()
        {
            actionsPart = Part("Actions");
            feather = Img("Feather", actionsPart, Icon("feather"), Color.clear);
            feather.preserveAspect = true;
            feather.rectTransform.sizeDelta = Vector2.one * 64f;
            for (int i = 0; i < RowCount; i++)
            {
                var row = new Row { root = NewRect("Line", actionsPart) };
                row.root.anchorMin = row.root.anchorMax = new Vector2(0.5f, 0.5f);
                row.root.sizeDelta = new Vector2(RowWidth, RowHeight);
                row.group = row.root.gameObject.AddComponent<CanvasGroup>();
                HudShapes.Panel(row.root, new Color(0.55f, 0.38f, 0.16f, 0.12f), new Color(0.55f, 0.38f, 0.16f, 0.45f), 18f);
                row.icon = Img("Icon", row.root, null, Ink);
                row.icon.preserveAspect = true;
                row.icon.rectTransform.sizeDelta = Vector2.one * 52f;
                row.icon.rectTransform.anchoredPosition = new Vector2(-296f, 0f);
                row.face = NewFace("Whom", row.root, 34f, false, false);
                row.face.root.anchoredPosition = new Vector2(-224f, 0f);
                row.line = Text("Text", row.root, 30f, Ink);
                row.line.fontStyle = FontStyles.Normal;
                row.line.alignment = TextAlignmentOptions.Left;
                row.line.textWrappingMode = TextWrappingModes.Normal;
                row.line.enableAutoSizing = true;
                row.line.fontSizeMin = 20f;
                row.line.fontSizeMax = 30f;
                row.line.overflowMode = TextOverflowModes.Ellipsis;
                row.line.rectTransform.pivot = new Vector2(0f, 0.5f);
                rows[i] = row;
            }
        }

        // The blocked band, like the siren's failed band: the boatswain's picture in grey on the left, the words on the right.
        private void BuildBand()
        {
            band = NewRect("BlockedBand", root);
            band.anchorMin = new Vector2(0f, 0.5f);
            band.anchorMax = new Vector2(1f, 0.5f);
            band.sizeDelta = new Vector2(0f, 300f);
            band.anchoredPosition = new Vector2(0f, 320f);
            bandGroup = band.gameObject.AddComponent<CanvasGroup>();
            bandGroup.blocksRaycasts = false;
            band.gameObject.AddComponent<RectMask2D>();
            Sprite art = null;
            if (cutIns != null) cutIns.TryFind(RoleCodes.CrewBoatswain, out art, out _);
            Image picture = Img("Art", band, art, new Color(0.45f, 0.45f, 0.48f, 0.55f));
            picture.rectTransform.anchorMin = picture.rectTransform.anchorMax = new Vector2(0.28f, 0.5f);
            picture.rectTransform.sizeDelta = new Vector2(760f, 1350f);
            picture.rectTransform.anchoredPosition = new Vector2(0f, -260f); // the face of the picture in the band
            picture.preserveAspect = true;
            picture.enabled = art != null;
            Color shadeColor = new Color(0.08f, 0.09f, 0.1f, 0.9f);
            Image shade = Img("Shade", band, NightFxSprites.Fade, shadeColor);
            shade.rectTransform.anchorMin = shade.rectTransform.anchorMax = new Vector2(0.62f, 0.5f);
            shade.rectTransform.sizeDelta = new Vector2(300f, 1300f); // turned: clear on the left, solid on the right
            shade.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Image solid = Img("Solid", band, null, shadeColor); // under the words, the right of the band stays dark
            solid.rectTransform.anchorMin = new Vector2(0.62f, 0f);
            solid.rectTransform.anchorMax = new Vector2(1f, 1f);
            solid.rectTransform.offsetMin = solid.rectTransform.offsetMax = Vector2.zero;
            for (int edge = 0; edge < 2; edge++)
            {
                Image line = Img(edge == 0 ? "LineTop" : "LineBottom", band, null, Grey);
                line.rectTransform.anchorMin = new Vector2(0f, edge == 0 ? 1f : 0f);
                line.rectTransform.anchorMax = new Vector2(1f, edge == 0 ? 1f : 0f);
                line.rectTransform.sizeDelta = new Vector2(0f, 6f);
                line.rectTransform.anchoredPosition = new Vector2(0f, edge == 0 ? -3f : 3f);
            }
            Image rope = Img("Rope", band, Icon("rope"), new Color(0.85f, 0.87f, 0.9f, 0.85f));
            rope.preserveAspect = true;
            rope.rectTransform.anchorMin = rope.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            rope.rectTransform.sizeDelta = Vector2.one * 64f;
            rope.rectTransform.anchoredPosition = new Vector2(-690f, 40f);
            TextMeshProUGUI bandTitle = BandText("Title", 64f, new Color(0.85f, 0.87f, 0.9f), 40f);
            bandTitle.text = "능력이 막혔습니다";
            TextMeshProUGUI bandSub = BandText("Sub", 40f, Grey, -50f);
            bandSub.text = "갑판장에게 차단당했습니다";
            band.gameObject.SetActive(false);
        }

        private TextMeshProUGUI BandText(string name, float size, Color color, float y)
        {
            TextMeshProUGUI text = Text(name, band, size, color);
            text.alignment = TextAlignmentOptions.Right;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            text.rectTransform.pivot = new Vector2(1f, 0.5f);
            text.rectTransform.anchoredPosition = new Vector2(-60f, y);
            if (text.font != null)
            {
                text.outlineWidth = 0.2f;
                text.outlineColor = new Color32(0x0A, 0x0B, 0x0D, 0xFF);
            }
            return text;
        }

        /// <summary>
        /// A bust in a dark round window. Big: in the gold portrait ring, the name on a plate under its anchor badge, and a
        /// skull badge for the dead. Small: in a thin gold ring, the name (if any) under it in ink.
        /// </summary>
        private Face NewFace(string name, RectTransform parent, float radius, bool big, bool named)
        {
            var f = new Face { root = NewRect(name, parent) };
            f.root.anchorMin = f.root.anchorMax = new Vector2(0.5f, 0.5f);
            f.root.sizeDelta = Vector2.one * radius * 2f;
            f.group = f.root.gameObject.AddComponent<CanvasGroup>();
            Image disc = Img("Disc", f.root, NightFxSprites.Circle, new Color(0.09f, 0.11f, 0.15f));
            disc.rectTransform.sizeDelta = Vector2.one * radius * 2f;
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            f.bust = Img("Bust", disc.rectTransform, null, Color.white);
            f.bust.rectTransform.sizeDelta = Vector2.one * radius * 2.28f;
            f.bust.rectTransform.anchoredPosition = new Vector2(0f, -radius * 0.1f);
            f.bust.preserveAspect = true;
            Sprite ringArt = big ? NauticalHud.Get("PortraitRing") : null;
            if (ringArt != null)
            {
                // The ring's centre is half its width from its top (its anchor badge hangs below).
                float width = radius / NauticalHud.RingInner;
                f.ring = Img("Ring", f.root, ringArt, Color.white);
                f.ring.rectTransform.pivot = new Vector2(0.5f, 1f);
                f.ring.rectTransform.sizeDelta = new Vector2(width, width * ringArt.rect.height / ringArt.rect.width);
                f.ring.rectTransform.anchoredPosition = new Vector2(0f, width * 0.5f);
            }
            else
            {
                // NightFxSprites.Ring's line is at 0.86 of its half size: just outside the disc.
                f.ring = Img("Ring", f.root, NightFxSprites.Ring, Gold);
                f.ring.rectTransform.sizeDelta = Vector2.one * (radius + 3f) * 2f / 0.86f;
            }
            if (big)
            {
                RectTransform badge = NewRect("Dead", f.root);
                badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 0.5f);
                badge.anchoredPosition = new Vector2(radius * 0.74f, radius * 0.74f);
                badge.sizeDelta = Vector2.one * radius * 0.6f;
                f.dead = badge.gameObject.AddComponent<Image>();
                f.dead.sprite = NightFxSprites.Circle;
                f.dead.color = new Color(0.12f, 0.1f, 0.1f);
                f.dead.raycastTarget = false;
                Image skull = Img("Skull", badge, Icon("skull"), DeadGrey);
                skull.preserveAspect = true;
                skull.rectTransform.sizeDelta = Vector2.one * radius * 0.4f;
            }
            if (!named) return f;
            if (big)
            {
                RectTransform plate = NewRect("NamePlate", f.root);
                plate.anchorMin = plate.anchorMax = new Vector2(0.5f, 0.5f);
                plate.sizeDelta = new Vector2(radius * 3f, radius * 0.56f);
                plate.anchoredPosition = new Vector2(0f, -radius * 1.655f); // under the ring's anchor badge
                Image plateArt = plate.gameObject.AddComponent<Image>();
                plateArt.raycastTarget = false;
                if (NauticalHud.Get("NamePlate") != null) NauticalHud.Plate(plateArt, "NamePlate", plate.sizeDelta.y);
                else plateArt.color = new Color(0.16f, 0.11f, 0.06f);
                f.name = Text("Name", plate, radius * 0.32f, Cream);
                f.name.rectTransform.anchoredPosition = Vector2.zero;
                f.name.rectTransform.sizeDelta = new Vector2(plate.sizeDelta.x - 44f, plate.sizeDelta.y - 8f);
                f.name.fontSizeMin = radius * 0.2f;
                f.plated = true;
            }
            else
            {
                f.name = Text("Name", f.root, 26f, Ink);
                f.name.rectTransform.anchoredPosition = new Vector2(0f, -radius - 26f);
                f.name.rectTransform.sizeDelta = new Vector2(radius * 2.7f, 36f);
                f.name.fontSizeMin = 18f;
            }
            f.name.enableAutoSizing = true;
            f.name.fontSizeMax = f.name.fontSize;
            f.name.overflowMode = TextOverflowModes.Ellipsis;
            return f;
        }

        private static void SetFace(Face f, NightReportFace who, bool dead)
        {
            Sprite portrait = who != null ? who.Portrait : null;
            f.bust.sprite = portrait;
            f.bust.enabled = portrait != null;
            f.bust.color = dead ? DeadGrey : Color.white;
            f.ring.color = dead ? DeadGrey : (f.ring.sprite == NightFxSprites.Ring ? Gold : Color.white);
            if (f.dead != null) f.dead.gameObject.SetActive(dead);
            if (f.name == null) return;
            f.name.text = who != null && who.Me ? "나" : Who(who) + (f.plated ? "님" : string.Empty);
        }

        // Its parts share the report's centre, like everything else on it.
        private RectTransform Part(string name)
        {
            RectTransform part = NewRect(name, letter);
            Stretch(part);
            return part;
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

        private static string Who(NightReportFace who) => who == null || string.IsNullOrEmpty(who.Name) ? "?" : who.Name;
        private static string Whom(NightReportFace who) => who != null && who.Me ? "나를" : Who(who) + "님을";
        private static string Whose(NightReportFace who) => who != null && who.Me ? "나의" : Who(who) + "님의";

        /// <summary>The colour of the side the role belongs to (it comes with the role, so it says no more than the report).</summary>
        private static Color RoleTone(string roleCode)
        {
            if (string.IsNullOrEmpty(roleCode)) return Ink;
            if (roleCode.StartsWith("PIRATE_", StringComparison.Ordinal)) return InvestigationRevealView.PirateRed;
            if (roleCode.StartsWith("NEUTRAL_", StringComparison.Ordinal)) return Violet;
            return InvestigationRevealView.CrewBlue;
        }

        private static string ActionIcon(string actionCode)
        {
            switch (actionCode)
            {
                case ActionCodes.SelectAttackTarget: return "sword_1";
                case ActionCodes.InvestigateFaction: return "compass";
                case ActionCodes.Protect: return "shield";
                case ActionCodes.WatchVisitors: return "lantern";
                case ActionCodes.Block: return "rope";
                case ActionCodes.ReadCorpseRole: return "bottle";
                case ActionCodes.WatchAction: return "feather";
                case ActionCodes.BanVote: return "knife_and_fork";
                case ActionCodes.Seduce: return "music";
                case ActionCodes.KrakenMark: return "target_1";
                case ActionCodes.KrakenStrike: return "wind_wave";
                default: return "mark_question";
            }
        }

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
