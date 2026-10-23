using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.GameUI
{
    /// <summary>One of the siren team shown when I am seduced: the skin bust they wear and their nickname (no roles).</summary>
    public sealed class SirenSongMate
    {
        public string Name;     // nickname, or "나"
        public Sprite Portrait; // the bust of the skin they wear at the table
        public bool Me;
    }

    /// <summary>
    /// The siren's song (Docs: 시안 F), only on the screen of the player it concerns:
    /// - Seduced: the song (pink waves at the edges, notes rising) → the siren's picture "이제 당신은 세이렌 팀입니다" →
    ///   the siren team joined by a pink thread, the role kept and the new goal. About 2.4 s.
    ///   After a reconnect only the team part plays.
    /// - The siren's own band: "유혹 성공 · 영희님 / 다음 밤은 쉽니다" in pink, or a grey band when it failed.
    /// Both always play to the end (a tap does not skip them). Built at run time over the canvas; inactive between plays.
    /// Uses unscaled time.
    /// </summary>
    public sealed class SirenSongView : MonoBehaviour
    {
        private const float SongEnd = 0.6f, PictureEnd = 1.5f, TeamEnd = 2.4f, OutEnd = 2.7f;
        private const float BandIn = 0.2f, BandOut = 0.25f;
        private const int WaveCount = 3, NoteCount = 6, MateCount = 6;

        private static readonly Color Pink = new Color(0.94f, 0.55f, 0.78f);
        private static readonly Color PinkLight = new Color(1f, 0.85f, 0.93f);
        private static readonly Color Teal = new Color(0.5f, 0.89f, 0.88f);
        private static readonly Color Plum = new Color(0.1f, 0.03f, 0.1f);
        private static readonly Color Grey = new Color(0.55f, 0.57f, 0.62f);

        private RectTransform root;
        private CanvasGroup group;
        private Image dim, edgeTop, edgeBottom, art, flash;
        private RectTransform artHolder;
        private readonly Image[] waves = new Image[WaveCount];
        private readonly Image[] notes = new Image[NoteCount];
        private TextMeshProUGUI songCaption, pictureLine;
        private RectTransform pictureBand;
        private CanvasGroup teamGroup;
        private RectTransform teamPanel;
        private TextMeshProUGUI teamTitle, teamRole, teamGoal;
        private Image thread;
        private readonly Mate[] mates = new Mate[MateCount];
        private RectTransform band;
        private CanvasGroup bandGroup;
        private Image bandArt, bandShade, bandSolid, bandLineTop, bandLineBottom, bandMoon;
        private TextMeshProUGUI bandTitle, bandSub;
        private TMP_FontAsset font;
        private Coroutine routine;
        private int shownMates;

        private sealed class Mate
        {
            public RectTransform root;
            public CanvasGroup group;
            public Image portrait, ring;
            public TextMeshProUGUI name;
        }

        public bool IsPlaying => routine != null;

        public static SirenSongView Create(RectTransform canvas, TMP_FontAsset font)
        {
            var go = new GameObject("SirenSong", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.layer = canvas.gameObject.layer;
            var view = go.AddComponent<SirenSongView>();
            view.Build(canvas, font);
            go.SetActive(false);
            return view;
        }

        /// <summary>I was seduced: the song, the siren's picture and the team. teamOnly: just the team (after a reconnect).</summary>
        public void PlaySeduced(IList<SirenSongMate> team, string roleName, string goal, bool teamOnly)
        {
            Stop();
            shownMates = Mathf.Min(team != null ? team.Count : 0, MateCount);
            for (int i = 0; i < MateCount; i++)
            {
                SirenSongMate mate = i < shownMates ? team[i] : null;
                mates[i].root.gameObject.SetActive(mate != null);
                if (mate == null) continue;
                mates[i].portrait.sprite = mate.Portrait;
                mates[i].portrait.enabled = mate.Portrait != null;
                mates[i].name.text = mate.Me ? "나" : (string.IsNullOrEmpty(mate.Name) ? "?" : mate.Name);
                mates[i].name.color = mate.Me ? new Color(1f, 0.84f, 0.42f) : PinkLight;
            }
            teamRole.text = string.IsNullOrEmpty(roleName) ? "직업은 그대로입니다" : "직업은 그대로입니다 · " + roleName;
            teamGoal.text = string.IsNullOrEmpty(goal) ? string.Empty : "목표 · " + goal;
            Begin();
            routine = StartCoroutine(RunSeduced(teamOnly ? PictureEnd : 0f));
        }

        /// <summary>The siren's own result: a pink band when the seduction worked (and the next night is a rest), grey when not.</summary>
        public void PlayBand(bool success, string targetName)
        {
            Stop();
            string target = string.IsNullOrEmpty(targetName) ? "?" : targetName;
            bandTitle.text = (success ? "유혹 성공 · " : "유혹 실패 · ") + target + "님";
            bandSub.text = success ? "다음 밤은 쉽니다" : "유혹할 수 없는 사람입니다";
            Color line = success ? Pink : Grey;
            bandLineTop.color = bandLineBottom.color = line;
            bandArt.color = success ? Color.white : new Color(0.45f, 0.45f, 0.48f, 0.55f);
            bandShade.color = success ? new Color(0.16f, 0.02f, 0.12f, 0.86f) : new Color(0.08f, 0.09f, 0.1f, 0.9f);
            bandSolid.color = bandShade.color;
            bandTitle.color = success ? PinkLight : new Color(0.85f, 0.87f, 0.9f);
            bandSub.color = success ? Pink : Grey;
            bandMoon.enabled = success;
            Begin();
            routine = StartCoroutine(RunBand(success ? 1.0f : 0.8f));
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

        private void Begin()
        {
            gameObject.SetActive(true);
            root.SetAsLastSibling(); // over the drawer, the chat and the day's cut-scene
        }

        // ================================================================ seduced

        private IEnumerator RunSeduced(float from)
        {
            float start = Time.unscaledTime - from;
            while (true)
            {
                float t = Time.unscaledTime - start;
                if (t >= OutEnd) break;
                ApplySeduced(t, from);
                yield return null;
            }
            routine = null;
            gameObject.SetActive(false);
        }

        private void ApplySeduced(float t, float from)
        {
            float w = root.rect.width, h = root.rect.height;
            band.gameObject.SetActive(false);
            group.alpha = Seg(t, from, from + 0.15f) * (1f - Seg(t, TeamEnd, OutEnd));

            // 1. The song: the screen dims, pink and teal waves at the edges, notes rising. Nothing is told yet.
            float song = Seg(t, 0f, 0.25f);
            Set(dim, Plum, 0.62f * song);
            float pulse = 0.75f + 0.25f * Mathf.Sin(t * 6f);
            Set(edgeTop, Pink, 0.75f * song * pulse);
            Set(edgeBottom, Pink, 0.75f * song * pulse);
            float waveAlpha = song * (1f - 0.6f * Seg(t, SongEnd, PictureEnd));
            for (int i = 0; i < WaveCount; i++)
            {
                RectTransform r = waves[i].rectTransform;
                r.anchoredPosition = new Vector2(Mathf.Sin(t * 2.2f + i * 1.7f) * 60f, h * (0.12f - i * 0.12f));
                r.sizeDelta = new Vector2(w * 1.3f, 26f - i * 6f);
                r.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.6f + i) * 3f);
                Set(waves[i], i % 2 == 0 ? Pink : Teal, (0.7f - i * 0.15f) * waveAlpha);
            }
            for (int i = 0; i < NoteCount; i++)
            {
                float k = Mathf.Repeat(t * 0.45f + Hash(i, 1), 1f);
                RectTransform r = notes[i].rectTransform;
                r.anchoredPosition = new Vector2((Hash(i, 2) - 0.5f) * w * 0.8f, -h * 0.3f + k * h * 0.6f);
                r.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 3f + i) * 12f);
                Set(notes[i], i % 3 == 0 ? Teal : PinkLight, 0.8f * Mathf.Sin(k * Mathf.PI) * waveAlpha);
            }
            Set(songCaption, PinkLight, Seg(t, 0.12f, 0.3f) * (1f - Seg(t, 0.5f, SongEnd)));

            // 2. The siren's picture sweeps in with a pink flash: "이제 당신은 세이렌 팀입니다".
            float enter = EaseOut(Seg(t, SongEnd, SongEnd + 0.24f));
            float picture = Seg(t, SongEnd, SongEnd + 0.12f) * Mathf.Lerp(1f, 0.25f, Seg(t, PictureEnd - 0.1f, PictureEnd + 0.15f));
            artHolder.anchoredPosition = new Vector2(Mathf.Lerp(w * 0.5f, 0f, enter), 0f);
            artHolder.localScale = Vector3.one * Mathf.Lerp(1.1f, 1.03f, enter);
            art.color = new Color(1f, 1f, 1f, art.sprite != null ? picture : 0f);
            Set(flash, PinkLight, t >= SongEnd ? 0.55f * (1f - Seg(t, SongEnd, SongEnd + 0.25f)) : 0f);
            float line = Seg(t, SongEnd + 0.15f, SongEnd + 0.35f) * (1f - Seg(t, PictureEnd - 0.1f, PictureEnd + 0.05f));
            pictureBand.anchoredPosition = new Vector2(0f, Mathf.Lerp(-120f, 0f, EaseOut(Seg(t, SongEnd + 0.12f, SongEnd + 0.4f))));
            Set(pictureBand.GetComponent<Image>(), Plum, 0.85f * line);
            Set(pictureLine, PinkLight, line);

            // 3. The team: the busts joined by a pink thread, the role kept, the new goal.
            float team = EaseOut(Seg(t, PictureEnd, PictureEnd + 0.25f));
            teamGroup.alpha = team;
            teamPanel.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, team);
            float spacing = Mathf.Min(170f, (teamPanel.rect.width - 80f) / Mathf.Max(1, shownMates));
            for (int i = 0; i < shownMates; i++)
            {
                float pop = EaseOutBack(Seg(t, PictureEnd + 0.08f + i * 0.07f, PictureEnd + 0.32f + i * 0.07f));
                mates[i].root.anchoredPosition = new Vector2((i - (shownMates - 1) * 0.5f) * spacing, 40f);
                mates[i].root.localScale = Vector3.one * Mathf.Max(0f, pop);
                mates[i].group.alpha = Mathf.Clamp01(pop);
                mates[i].ring.rectTransform.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(t * 5f + i));
            }
            float threadWidth = spacing * Mathf.Max(0, shownMates - 1) * EaseOut(Seg(t, PictureEnd + 0.2f, PictureEnd + 0.55f));
            thread.rectTransform.sizeDelta = new Vector2(threadWidth + 40f, 8f);
            Set(thread, Pink, team * 0.9f);
        }

        // ================================================================ the siren's band

        private IEnumerator RunBand(float hold)
        {
            float start = Time.unscaledTime;
            float outAt = BandIn + hold, end = outAt + BandOut;
            while (true)
            {
                float t = Time.unscaledTime - start;
                if (t >= end) break;
                ApplyBand(t, outAt, end);
                yield return null;
            }
            routine = null;
            gameObject.SetActive(false);
        }

        private void ApplyBand(float t, float outAt, float end)
        {
            float w = root.rect.width;
            group.alpha = 1f;
            Set(dim, Plum, 0f);
            Set(edgeTop, Pink, 0f);
            Set(edgeBottom, Pink, 0f);
            foreach (Image wave in waves) Set(wave, Pink, 0f);
            foreach (Image note in notes) Set(note, Pink, 0f);
            Set(songCaption, Pink, 0f);
            art.color = Color.clear;
            Set(flash, Pink, 0f);
            Set(pictureBand.GetComponent<Image>(), Plum, 0f);
            Set(pictureLine, Pink, 0f);
            teamGroup.alpha = 0f;
            band.gameObject.SetActive(true);
            float enter = EaseOut(Seg(t, 0f, BandIn)), leave = Seg(t, outAt, end);
            band.anchoredPosition = new Vector2(Mathf.Lerp(-w, 0f, enter) + w * leave * leave, band.anchoredPosition.y);
            bandGroup.alpha = Seg(t, 0f, 0.1f) * (1f - leave);
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
            blocker.raycastTarget = true; // blocks the screen while playing
            group = GetComponent<CanvasGroup>();

            dim = Img("Dim", root, null, Color.clear);
            Stretch(dim.rectTransform);
            edgeTop = Edge("EdgeTop", true);
            edgeBottom = Edge("EdgeBottom", false);
            for (int i = 0; i < WaveCount; i++) waves[i] = Img("Wave", root, NightFxSprites.Streak, Pink);
            Sprite note = Resources.Load<Sprite>("Sprites/flaticon/white/128/music_128");
            for (int i = 0; i < NoteCount; i++)
            {
                notes[i] = Img("Note", root, note != null ? note : NightFxSprites.Dot, Pink);
                notes[i].preserveAspect = true;
                notes[i].rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(56f, 92f, Hash(i, 3));
            }
            songCaption = Text("SongCaption", root, 52f, PinkLight);
            songCaption.text = "어디선가 노랫소리가 들려옵니다...";
            songCaption.rectTransform.anchoredPosition = new Vector2(0f, -420f);

            artHolder = NewRect("Art", root);
            Stretch(artHolder);
            art = artHolder.gameObject.AddComponent<Image>();
            art.preserveAspect = true;
            art.raycastTarget = false;
            Sprite siren = null;
            AbilityCutInCatalog cutIns = Resources.Load<AbilityCutInCatalog>("NightScene/AbilityCutIns");
            if (cutIns != null) cutIns.TryFind("CONCEPT_SIREN", out siren, out _);
            art.sprite = siren;
            flash = Img("Flash", root, null, Color.clear);
            Stretch(flash.rectTransform);

            pictureBand = Img("PictureBand", root, NightFxSprites.Fade, Color.clear).rectTransform;
            pictureBand.anchorMin = new Vector2(0f, 0f);
            pictureBand.anchorMax = new Vector2(1f, 0f);
            pictureBand.pivot = new Vector2(0.5f, 0f);
            pictureBand.sizeDelta = new Vector2(0f, 420f);
            pictureLine = Text("Line", pictureBand, 76f, PinkLight);
            pictureLine.text = "이제 당신은 세이렌 팀입니다";
            pictureLine.rectTransform.anchorMin = pictureLine.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            pictureLine.rectTransform.anchoredPosition = new Vector2(0f, 170f);
            pictureLine.outlineColor = new Color32(0x5A, 0x12, 0x38, 0xFF);

            BuildTeam();
            BuildBand();
        }

        private void BuildTeam()
        {
            teamPanel = NewRect("SirenTeam", root);
            teamPanel.anchorMin = teamPanel.anchorMax = new Vector2(0.5f, 0.5f);
            teamPanel.sizeDelta = new Vector2(940f, 640f);
            teamGroup = teamPanel.gameObject.AddComponent<CanvasGroup>();
            teamGroup.blocksRaycasts = false;
            Image back = teamPanel.gameObject.AddComponent<Image>();
            back.sprite = Resources.Load<Sprite>("GameHud/HudRoundedFill");
            back.type = back.sprite != null && back.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            back.color = new Color(0.12f, 0.03f, 0.11f, 0.94f);
            back.raycastTarget = false;
            Image rim = Img("Rim", teamPanel, Resources.Load<Sprite>("GameHud/HudRoundedOutline"), Pink);
            Stretch(rim.rectTransform);
            if (rim.sprite != null && rim.sprite.border != Vector4.zero) rim.type = Image.Type.Sliced;
            teamTitle = Text("Title", teamPanel, 72f, Pink);
            teamTitle.text = "세 이 렌  팀";
            teamTitle.rectTransform.anchoredPosition = new Vector2(0f, 240f);
            thread = Img("Thread", teamPanel, NightFxSprites.Streak, Pink);
            thread.rectTransform.anchoredPosition = new Vector2(0f, 40f);
            for (int i = 0; i < MateCount; i++)
            {
                var mate = new Mate { root = NewRect("Mate", teamPanel) };
                mate.root.anchorMin = mate.root.anchorMax = new Vector2(0.5f, 0.5f);
                mate.root.sizeDelta = new Vector2(150f, 200f);
                mate.group = mate.root.gameObject.AddComponent<CanvasGroup>();
                Image disc = Img("Disc", mate.root, NightFxSprites.Circle, new Color(0.11f, 0.14f, 0.2f));
                disc.rectTransform.sizeDelta = Vector2.one * 130f;
                disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                mate.portrait = Img("Portrait", disc.rectTransform, null, Color.white);
                mate.portrait.rectTransform.sizeDelta = Vector2.one * 150f;
                mate.portrait.rectTransform.anchoredPosition = new Vector2(0f, -8f);
                mate.portrait.preserveAspect = true;
                mate.ring = Img("Ring", mate.root, NightFxSprites.Ring, Pink);
                mate.ring.rectTransform.sizeDelta = Vector2.one * 156f;
                mate.name = Text("Name", mate.root, 34f, PinkLight);
                mate.name.rectTransform.anchoredPosition = new Vector2(0f, -100f);
                mates[i] = mate;
            }
            teamRole = Text("Role", teamPanel, 40f, PinkLight);
            teamRole.rectTransform.anchoredPosition = new Vector2(0f, -170f);
            teamGoal = Text("Goal", teamPanel, 38f, Pink);
            teamGoal.rectTransform.anchoredPosition = new Vector2(0f, -235f);
        }

        private void BuildBand()
        {
            band = NewRect("SirenBand", root);
            band.anchorMin = new Vector2(0f, 0.5f);
            band.anchorMax = new Vector2(1f, 0.5f);
            band.sizeDelta = new Vector2(0f, 300f);
            band.anchoredPosition = new Vector2(0f, 320f);
            bandGroup = band.gameObject.AddComponent<CanvasGroup>();
            bandGroup.blocksRaycasts = false;
            band.gameObject.AddComponent<RectMask2D>();
            bandArt = Img("Art", band, null, Color.white);
            bandArt.rectTransform.anchorMin = bandArt.rectTransform.anchorMax = new Vector2(0.28f, 0.5f);
            bandArt.rectTransform.sizeDelta = new Vector2(760f, 1350f);
            bandArt.rectTransform.anchoredPosition = new Vector2(0f, -260f); // the face of the picture in the band
            bandArt.preserveAspect = true;
            bandArt.sprite = art != null ? art.sprite : null;
            bandShade = Img("Shade", band, NightFxSprites.Fade, Color.black);
            bandShade.rectTransform.anchorMin = new Vector2(0.62f, 0.5f);
            bandShade.rectTransform.anchorMax = new Vector2(0.62f, 0.5f);
            bandShade.rectTransform.sizeDelta = new Vector2(300f, 1300f); // turned: clear on the left, solid on the right
            bandShade.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            bandSolid = Img("Solid", band, null, Color.clear); // under the texts, the right of the band stays dark
            bandSolid.rectTransform.anchorMin = new Vector2(0.62f, 0f);
            bandSolid.rectTransform.anchorMax = new Vector2(1f, 1f);
            bandSolid.rectTransform.offsetMin = bandSolid.rectTransform.offsetMax = Vector2.zero;
            bandLineTop = Img("LineTop", band, null, Pink);
            bandLineTop.rectTransform.anchorMin = new Vector2(0f, 1f);
            bandLineTop.rectTransform.anchorMax = new Vector2(1f, 1f);
            bandLineTop.rectTransform.sizeDelta = new Vector2(0f, 6f);
            bandLineTop.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            bandLineBottom = Img("LineBottom", band, null, Pink);
            bandLineBottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            bandLineBottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bandLineBottom.rectTransform.sizeDelta = new Vector2(0f, 6f);
            bandLineBottom.rectTransform.anchoredPosition = new Vector2(0f, 3f);
            bandTitle = Text("Title", band, 64f, PinkLight);
            bandTitle.alignment = TextAlignmentOptions.Right;
            bandTitle.rectTransform.anchorMin = bandTitle.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            bandTitle.rectTransform.pivot = new Vector2(1f, 0.5f);
            bandTitle.rectTransform.anchoredPosition = new Vector2(-60f, 40f);
            bandSub = Text("Sub", band, 40f, Pink);
            bandSub.alignment = TextAlignmentOptions.Right;
            bandSub.rectTransform.anchorMin = bandSub.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            bandSub.rectTransform.pivot = new Vector2(1f, 0.5f);
            bandSub.rectTransform.anchoredPosition = new Vector2(-60f, -50f);
            bandMoon = Img("Moon", band, NightFxSprites.Circle, new Color(1f, 0.86f, 0.55f));
            bandMoon.rectTransform.anchorMin = bandMoon.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            bandMoon.rectTransform.sizeDelta = Vector2.one * 34f;
            bandMoon.rectTransform.anchoredPosition = new Vector2(-400f, -50f);
            band.gameObject.SetActive(false);
        }

        // ================================================================ helpers

        private Image Edge(string name, bool top)
        {
            Image image = Img(name, root, NightFxSprites.Fade, Color.clear);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rect.localScale = new Vector3(1f, top ? -1f : 1f, 1f); // the fade is solid at its bottom: flipped for the top edge
            rect.sizeDelta = new Vector2(0f, 420f);
            rect.anchoredPosition = new Vector2(0f, top ? -210f : 210f);
            return image;
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
            text.richText = false; // nicknames come from players
            text.raycastTarget = false;
            if (text.font != null)
            {
                text.outlineWidth = 0.2f;
                text.outlineColor = new Color32(0x14, 0x05, 0x10, 0xFF);
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

        private static void Set(Graphic graphic, Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            graphic.color = color;
        }

        private static float Seg(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));
        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

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
    }
}
