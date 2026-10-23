using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.GameUI
{
    /// <summary>A two-second result reveal that always plays to the end (a tap does not skip it). Reuses the players' worn skins and unscaled time.</summary>
    public sealed class GameResultSequence : MonoBehaviour
    {
        public const float Duration = 2f;
        private sealed class Figure
        {
            public RectTransform root;
            public CanvasGroup group;
            public Image avatar;
            public Image shadow;
            public Vector2 position;
            public float delay;
        }
        private readonly List<Figure> figures = new List<Figure>();
        private readonly List<Image> particles = new List<Image>();
        private RectTransform root, stage, fx;
        private Image curtain, blocker, surface, glow;
        private Button back;
        private CanvasGroup titleGroup, outcomeGroup, rosterGroup, backGroup;
        private TextMeshProUGUI title;
        private TMP_FontAsset font;
        private GameResultCardView cardView;
        private Color surfaceColor, accent;
        private Action completed;
        private bool initialized, active, defeat, personal; // personal: a neutral win (its motes rise like bubbles, pearls or will-o'-the-wisps)
        private float elapsed, layoutWidth, layoutHeight;
        public bool IsPlaying { get; private set; }

        public void Initialize(TextMeshProUGUI winner, TextMeshProUGUI outcome, TextMeshProUGUI roster, Button backButton, GameResultCardView details = null)
        {
            if (initialized) return;
            initialized = true;
            cardView = details;
            root = (RectTransform)transform;
            title = winner;
            font = winner != null ? winner.font : null;
            titleGroup = Group(winner); outcomeGroup = Group(outcome); rosterGroup = Group(roster);
            back = backButton; backGroup = Group(backButton);
            Transform card = transform.Find("Card");
            surface = card != null ? card.GetComponent<Image>() : null;
            surfaceColor = surface != null ? surface.color : Color.clear;

            fx = NewRect("ResultEffects", root); Stretch(fx);
            fx.SetSiblingIndex(1); // Above the backdrop, below the result card.
            stage = NewRect("ResultCharacters", root);
            stage.anchorMin = new Vector2(.05f, .615f); stage.anchorMax = new Vector2(.95f, .94f);
            stage.offsetMin = stage.offsetMax = Vector2.zero;
            stage.SetSiblingIndex(2);
            glow = Image("ResultGlow", fx, NightFxSprites.Dot);
            glow.rectTransform.anchorMin = glow.rectTransform.anchorMax = new Vector2(.5f, .72f);
            glow.rectTransform.sizeDelta = new Vector2(750f, 440f);
            for (int i = 0; i < 28; i++) particles.Add(Image("Mote_" + i, fx, NightFxSprites.Dot));

            curtain = Image("ResultFade", root, null); Stretch(curtain.rectTransform);
            blocker = Image("ResultIntroBlocker", root, null); Stretch(blocker.rectTransform);
            blocker.color = Color.clear; blocker.raycastTarget = true; // Blocks the result screen until the reveal ends.
            Stop();
        }

        public void Play(GameResultDto result, MyRoleDto me, Func<long, ChibiCharacterSkin> skinOf, bool hasArt, Action onCompleted)
        {
            Stop();
            completed = onCompleted;
            string background = GameResultPanel.BackgroundName(result, me);
            if (!hasArt || background == null) { FinishImmediately(); return; }
            defeat = background == "Defeat";
            personal = background == "PersonalVictory";
            accent = defeat ? GameResultPalette.Muted : GameResultPalette.ForWinner(result.winner);
            // TMP initializes its renderer on activation, before outline materials are configured.
            stage.gameObject.SetActive(true);
            BuildFigures(result, me, skinOf);
            for (int i = 0; i < particles.Count; i++)
            {
                particles[i].sprite = defeat ? NightFxSprites.Streak : NightFxSprites.Dot;
                particles[i].rectTransform.sizeDelta = defeat ? new Vector2(45f + i % 4 * 8f, 2f) : Vector2.one * (personal ? 8f + i % 5 * 3f : 5f + i % 5 * 2f);
                particles[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, defeat ? -72f : 0f);
            }
            active = IsPlaying = true; elapsed = 0f;
            fx.gameObject.SetActive(true); stage.gameObject.SetActive(true);
            curtain.gameObject.SetActive(true); blocker.gameObject.SetActive(true);
            blocker.transform.SetAsLastSibling();
            LayoutFigures(); Apply(0f);
        }

        private void BuildFigures(GameResultDto result, MyRoleDto me, Func<long, ChibiCharacterSkin> skinOf)
        {
            if (skinOf == null || me == null) return;
            // Defeat shows only me; a victory shows everyone who won with me (the whole team, the siren team, or only me
            // for a solo win). Winners come from the server (winnerIds), not from the role's faction.
            var ids = new HashSet<long>();
            if (result.players != null)
                foreach (PlayerResultDto player in result.players)
                {
                    if (player == null || figures.Count >= 12 || !ids.Add(player.playerId)) continue;
                    bool mine = player.playerId == me.playerId;
                    bool won = GameStateQueries.IsWinner(result, player.playerId, mine ? me.faction : GameResultPalette.FactionForRole(player.role));
                    if (defeat ? !mine : !mine && !won) continue;
                    AddFigure(player.playerId, player.nickname, skinOf);
                }
            if (figures.Count == 0) AddFigure(me.playerId, "나", skinOf);
        }

        private void AddFigure(long id, string nickname, Func<long, ChibiCharacterSkin> skinOf)
        {
            ChibiCharacterSkin skin = skinOf(id);
            Sprite sprite = skin != null ? skin.Standing : null;
            if (sprite == null) return; // Missing art must not show a white rectangle or a different player's outfit.
            RectTransform holder = NewRect("Winner_" + id, stage);
            holder.anchorMin = holder.anchorMax = new Vector2(.5f, 0f);
            var group = holder.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false;
            var shadow = Image("FootShadow", holder, NightFxSprites.Dot);
            shadow.color = new Color(0f, 0f, 0f, .4f);
            shadow.rectTransform.sizeDelta = new Vector2(120f, 24f);
            var avatar = Image("Character", holder, sprite);
            avatar.rectTransform.pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            if (defeat) avatar.color = new Color(.65f, .73f, .82f);
            var namePlate = Image("NamePlate", holder, null);
            namePlate.color = new Color(.015f, .035f, .06f, .8f);
            namePlate.rectTransform.anchoredPosition = new Vector2(0f, -24f);
            namePlate.rectTransform.sizeDelta = new Vector2(154f, 36f);
            var name = Label("Name", holder, string.IsNullOrEmpty(nickname) ? "플레이어" : nickname);
            name.rectTransform.anchoredPosition = new Vector2(0f, -24f);
            name.rectTransform.sizeDelta = new Vector2(145f, 34f);
            name.outlineWidth = .2f; name.outlineColor = new Color32(8, 15, 25, 255);
            figures.Add(new Figure { root = holder, group = group, avatar = avatar, shadow = shadow, delay = .65f + figures.Count * .045f });
        }

        private void LayoutFigures()
        {
            layoutWidth = root.rect.width; layoutHeight = root.rect.height;
            float width = Mathf.Max(300f, layoutWidth * .9f);
            int rows = figures.Count > 6 ? 2 : 1;
            float height = Mathf.Min(rows == 1 ? 310f : 205f, Mathf.Max(110f, layoutHeight * .28f / rows - 45f));
            int columns = Mathf.Min(6, Mathf.Max(1, figures.Count));
            float slot = Mathf.Min(230f, width / columns);
            // Standing sprite rects are already trimmed by ChibiSkinFit. Fit the entire
            // lineup to ONE height: fitting each image to its slot made wide skins smaller.
            foreach (Figure figure in figures)
            {
                Rect sprite = figure.avatar.sprite.rect;
                float pivotX = figure.avatar.rectTransform.pivot.x;
                float spanFromFeet = sprite.width * Mathf.Max(pivotX, 1f - pivotX) * 2f;
                height = Mathf.Min(height, slot * .88f * sprite.height / spanFromFeet);
            }
            for (int i = 0; i < figures.Count; i++)
            {
                int row = i / 6, count = Mathf.Min(6, figures.Count - row * 6);
                Figure figure = figures[i];
                figure.position = new Vector2((i % 6 - (count - 1) * .5f) * slot, 38f + (rows - 1 - row) * (height + 48f));
                Rect sprite = figure.avatar.sprite.rect;
                figure.avatar.rectTransform.sizeDelta = new Vector2(height * sprite.width / sprite.height, height);
                figure.shadow.rectTransform.sizeDelta = new Vector2(height * .55f, height * .11f);
            }
        }

        private void Update() { if (active) Step(Time.unscaledDeltaTime); }

        private void Step(float delta)
        {
            if (!active) return;
            elapsed += Mathf.Max(0f, delta);
            if (!Mathf.Approximately(layoutWidth, root.rect.width) || !Mathf.Approximately(layoutHeight, root.rect.height)) LayoutFigures();
            Apply(elapsed);
            if (IsPlaying && elapsed >= Duration) Complete();
        }

        private void Apply(float time)
        {
            cardView?.Reveal(time);
            curtain.color = new Color(0f, 0f, 0f, (1f - Ease(time / .38f)) * .97f);
            float titleK = Ease((time - .3f) / .4f);
            Alpha(titleGroup, titleK); Alpha(outcomeGroup, Ease((time - .5f) / .32f));
            if (title != null) title.rectTransform.localScale = Vector3.one * Mathf.Lerp(.88f, 1f, titleK);
            float infoK = Ease((time - 1.6f) / .4f);
            Alpha(rosterGroup, infoK); Alpha(backGroup, infoK);
            if (back != null) back.interactable = !IsPlaying;
            if (surface != null) surface.color = new Color(surfaceColor.r, surfaceColor.g, surfaceColor.b, surfaceColor.a * Ease((time - .22f) / .35f));
            foreach (Figure figure in figures)
            {
                float k = Ease((time - figure.delay) / .34f);
                figure.group.alpha = k;
                figure.root.anchoredPosition = figure.position + new Vector2(0f, -28f * (1f - k));
                figure.avatar.rectTransform.localScale = Vector3.one * Mathf.Lerp(.95f, 1f, k);
                figure.avatar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, defeat ? -5f * k : 0f);
            }
            float fade = Ease((time - .4f) / .5f);
            glow.color = new Color(accent.r, accent.g, accent.b, defeat ? .025f * fade : .12f * fade);
            for (int i = 0; i < particles.Count; i++)
            {
                float x = Mathf.Repeat(i * .618034f + (defeat ? -time * .018f : Mathf.Sin(time * .7f + i) * .012f), 1f);
                float y = Mathf.Repeat(i * .381966f + time * (defeat ? -.48f : personal ? .12f : .035f), 1f);
                particles[i].rectTransform.anchorMin = particles[i].rectTransform.anchorMax = new Vector2(x, .4f + y * .59f);
                particles[i].color = new Color(accent.r, accent.g, accent.b, fade * (defeat ? .22f : personal ? .55f : .45f) * Mathf.Sin(y * Mathf.PI));
            }
        }

        private void Complete()
        {
            IsPlaying = false;
            curtain.gameObject.SetActive(false); blocker.gameObject.SetActive(false);
            if (back != null) back.interactable = true;
            Action done = completed; completed = null; done?.Invoke();
        }

        private void FinishImmediately() { IsPlaying = false; Action done = completed; completed = null; done?.Invoke(); }

        public void Stop()
        {
            active = IsPlaying = false; completed = null;
            if (!initialized) return;
            foreach (Figure figure in figures)
            {
                figure.root.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(figure.root.gameObject); else DestroyImmediate(figure.root.gameObject);
            }
            figures.Clear();
            fx.gameObject.SetActive(false); stage.gameObject.SetActive(false);
            curtain.gameObject.SetActive(false); blocker.gameObject.SetActive(false);
            Alpha(titleGroup, 1f); Alpha(outcomeGroup, 1f); Alpha(rosterGroup, 1f); Alpha(backGroup, 1f);
            if (title != null) title.rectTransform.localScale = Vector3.one;
            if (surface != null) surface.color = surfaceColor;
            cardView?.ResetReveal();
            if (back != null) back.interactable = true;
        }

        private void OnDisable() { Stop(); }
        private static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        private static void Alpha(CanvasGroup group, float value) { if (group != null) group.alpha = value; }
        private static CanvasGroup Group(Component target)
        {
            if (target == null) return null;
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            return group != null ? group : target.gameObject.AddComponent<CanvasGroup>();
        }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private RectTransform NewRect(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform)); obj.layer = gameObject.layer;
            var rect = (RectTransform)obj.transform; rect.SetParent(parent, false); rect.sizeDelta = Vector2.zero; return rect;
        }
        private Image Image(string name, Transform parent, Sprite sprite)
        {
            var image = NewRect(name, parent).gameObject.AddComponent<Image>(); image.sprite = sprite; image.raycastTarget = false; return image;
        }
        private TextMeshProUGUI Label(string name, Transform parent, string value)
        {
            var text = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.text = value; text.richText = false; text.fontSize = 22f;
            text.alignment = TextAlignmentOptions.Center; text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false;
            return text;
        }
    }
}
