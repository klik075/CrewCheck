using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.GameUI;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// Lays the game screen out as the pirate HUD (art: Resources/GameHud, cut by Tools/GameHudArt), at run time over the
    /// scene's own parts, so the scene file does not change. Places: <see cref="GameHudMetrics"/>.
    /// - Top: phase plaque (sun or moon + the phase title), timer panel (time left + bar), settings and map buttons.
    /// - Notice box: each public announcement pops up in a plain green rectangle at the stage's top left, right under
    ///   the top UI, for a few seconds and goes away. (The old notice scroll and the voice pipe are commented out.)
    /// - Stage: the day table and the night room place themselves there.
    /// - Chat: <see cref="GameHudChat"/> (tabs and filter, rows, the input bar in one gold frame). The bottom sheet (chat
    ///   input bar + drawer) is lifted above the role panel. Their behaviour stays with the chat side.
    ///   The drawer is only as tall as the space under the stage: opening, it comes down over the role panel (which
    ///   slides out) instead of rising over the characters.
    /// - Bottom: my role panel (my character's portrait, role name, goal) and the role description card. Tapping the
    ///   role panel opens the action drawer (night ability, day skip).
    /// The old role card of the scene is hidden; GameScreen feeds this instead.
    /// Text boxes are at least 1.33 times their font size tall (the font's line height): a shorter box shows nothing.
    /// A scene can have the top HUD and the notice baked in (<see cref="Bake"/>, Tools/Game Scene/게임 화면 굽기): this
    /// component is then on the canvas with them, Build only wires them, and nothing here moves them.
    /// </summary>
    public sealed class GameHudLayout : MonoBehaviour
    {
        private const string Art = "GameHud/";
        private const string Icons = "Sprites/flaticon/white/128/"; // Artsystack Fantasy RPG GUI, white icons tinted here
        private static readonly Color Ink = new Color(0.27f, 0.16f, 0.07f);      // text on parchment
        private static readonly Color Gold = new Color(0.98f, 0.8f, 0.42f);
        private static readonly Color Cream = new Color(0.99f, 0.93f, 0.78f);
        private static readonly Color Sun = new Color(1f, 0.55f, 0.18f);
        private static readonly Color Moon = new Color(0.2f, 0.25f, 0.48f);    // dark, to show on the parchment
        private static readonly Color DrawerBack = new Color(0.106f, 0.118f, 0.149f, 0.96f); // the drawer panels' own
        // [두루마리 공지 - 주석 처리] private const float NoticeFadeIn = 0.3f, NoticeFadeOut = 0.5f;
        private const float NoticeMinSeconds = 3f, NoticeMaxSeconds = 6f;  // on screen, longer for longer text
        // [보이스파이프 제거 - 주석 처리]
        // // The voice pipe (VoicePipe.png 320x460 at half size) and its mouth, from its top-left corner. The art begins
        // // PipeArtTop down (the bell's top edge); above that the box is empty.
        // private const float PipeWidth = 160f, PipeHeight = 230f, PipeMouthX = 150f, PipeMouthY = 65f, PipeArtTop = 42f;
        // private const float TailWidth = 34f, TailTip = 30f;
        // The notice: a plain green rectangle at the stage's top left, the "공지" tag at its left, one to three lines.
        private const float BubbleLeft = 16f, BubbleMaxWidth = 1048f, BubbleMinWidth = 300f, BubbleMinHeight = 84f, BubbleTextSize = 28f;
        private const float BubblePadLeft = 108f, BubblePadRight = 26f, BubblePadTop = 14f, BubblePadBottom = 14f;
        private const float BubblePop = 0.25f, BubbleFadeOut = 0.4f;
        private static readonly Color BubbleFill = new Color32(26, 78, 52, 242);
        private static readonly Color BubbleBorder = new Color32(118, 196, 144, 255);
        private static readonly Color BubbleText = new Color32(206, 255, 214, 255);  // pale green on the green box
        private static readonly Color TagFill = new Color32(12, 34, 24, 255);
        private static readonly Color TagText = new Color32(150, 255, 178, 255);

        public event Action SettingsClicked;
        public event Action MapClicked;
        public event Action RoleCardClicked;

        // Made by Create: at run time, or once in Edit mode by Bake (then saved with the scene and only wired by Build).
        [SerializeField, HideInInspector] private bool built;
        [SerializeField] private Image phaseIcon;
        [SerializeField] private RectTransform timerFill;
        // [두루마리 공지 → 보이스파이프 말풍선으로 교체 - 주석 처리] 예전에 구운 씬(GameScene3)에 남은 두루마리는 Build에서 끄기만 한다
        [SerializeField] private RectTransform notice;
        // [SerializeField] private CanvasGroup noticeGroup;
        // [SerializeField] private TextMeshProUGUI noticeText;
        // [보이스파이프 제거 - 주석 처리] 보이스파이프(배경)와 그 입의 빛
        // [SerializeField] private RectTransform voicePipe;
        // [SerializeField] private Image pipeGlow;
        // 공지 상자 (녹색 사각형)
        [SerializeField] private RectTransform bubble;
        [SerializeField] private CanvasGroup bubbleGroup;
        [SerializeField] private TextMeshProUGUI bubbleText;
        private float noticeTop = -1f; // PlaceNoticeUnder: from the canvas top (negative: GameHudMetrics.NoticeBoxTop)
        [SerializeField] private Button settingsButton, mapButton;

        private RectTransform canvas, sheet, sheetLift, rolePanel;
        private BottomTabController bottomTab;
        private GameHudChat chatWindow;
        private Image tabBarImage, portrait, portraitShade, factionIcon;
        private RectTransform arrow;
        private TextMeshProUGUI roleName, roleGoal, factionTitle, roleBody, deadMark;
        private TMP_FontAsset font;
        private DesignSpriteCatalog portraits;
        private HudPopup popup;
        // [두루마리 공지 - 주석 처리] private Vector2 noticeRest; // where the notice stays; it drops in from a little above
        private float noticeShownAt = -1f, noticeSeconds;
        private float laidOutHeight = -1f;
        private bool wired;

        /// <summary>The window of the map and settings buttons (made on first use).</summary>
        public HudPopup Popup => popup != null ? popup : (popup = HudPopup.Create(canvas, font));

        /// <param name="phaseText">The top bar's phase title; moved into the phase plaque (GameScreen keeps writing it).</param>
        /// <param name="timerText">The top bar's timer; moved into the timer panel.</param>
        /// <param name="oldRoleCard">The scene's role card, hidden (the role panel replaces it).</param>
        /// <param name="chat">The chat log panel (stretched over the canvas).</param>
        /// <summary>The bust portraits by design id (Resources/GameHud/Portraits).</summary>
        public DesignSpriteCatalog Portraits => portraits;

        /// <param name="bottomSheet">The bottom sheet (tab bar + drawer) that BottomTabController slides.</param>
        /// <param name="frame">The Frame layout (GameScene): also the chat window, the lifted sheet and the role panel.
        /// Other layouts build those themselves (GameLayoutBody); this keeps only the top HUD and the notice.</param>
        public void Build(TextMeshProUGUI phaseText, TextMeshProUGUI timerText, RectTransform oldRoleCard, RectTransform chat,
            RectTransform bottomSheet, TMP_FontAsset labelFont, bool frame = true)
        {
            if (wired) return;
            wired = true;
            if (!built) Create(phaseText, timerText, oldRoleCard, chat, bottomSheet, labelFont, frame);
            canvas = (RectTransform)transform;
            font = labelFont;
            portraits = Resources.Load<DesignSpriteCatalog>(Art + "Portraits");
            if (settingsButton != null) settingsButton.onClick.AddListener(() => SettingsClicked?.Invoke());
            if (mapButton != null) mapButton.onClick.AddListener(() => MapClicked?.Invoke());
            // [두루마리 공지 → 보이스파이프 말풍선으로 교체 - 주석 처리]
            // if (notice != null)
            // {
            //     noticeRest = notice.anchoredPosition;
            //     notice.gameObject.SetActive(false); // until the first announcement
            // }
            if (notice != null) notice.gameObject.SetActive(false); // the scroll baked into an older scene: not used
            // [보이스파이프 제거 - 주석 처리] if (voicePipe == null || bubble == null) BuildVoicePipe(chat);
            if (bubble == null) BuildNoticeBox(chat); // a scene baked without it gets it here
            if (bubble != null) bubble.gameObject.SetActive(false); // until the first announcement
            SetPhase(null);
            if (frame) LateUpdate();
        }

        /// <summary>
        /// Edit mode, once (Tools/Game Scene/게임 화면 굽기): makes the top HUD and the notice in the scene, to be saved
        /// with it, so they can be moved and restyled in the editor. Play then only wires them (Build). Not for Frame.
        /// </summary>
        public void Bake(TextMeshProUGUI phaseText, TextMeshProUGUI timerText, RectTransform oldRoleCard, RectTransform chat,
            TMP_FontAsset labelFont)
        {
            if (built) return;
            Create(phaseText, timerText, oldRoleCard, chat, null, labelFont, false);
            // [두루마리 공지 - 주석 처리] if (noticeText != null) noticeText.text = "공지는 게임 중 여기에 잠깐 나타났다가 사라집니다.";
            if (bubbleText != null) bubbleText.text = "공지는 게임 중 여기에 잠깐 나타났다가 사라집니다."; // seen when turned on in the editor
        }

        private void Create(TextMeshProUGUI phaseText, TextMeshProUGUI timerText, RectTransform oldRoleCard, RectTransform chat,
            RectTransform bottomSheet, TMP_FontAsset labelFont, bool frame)
        {
            built = true;
            canvas = (RectTransform)transform;
            font = labelFont;
            portraits = Resources.Load<DesignSpriteCatalog>(Art + "Portraits");

            RectTransform topBar = phaseText != null ? phaseText.transform.parent as RectTransform : null;
            RectTransform hud = NewRect("GameHud", canvas);
            Stretch(hud, 0f, 0f, 0f, 0f);
            if (topBar != null)
            {
                // The top bar keeps only its disconnected banner, now hanging under the taller HUD.
                Image bar = topBar.GetComponent<Image>();
                if (bar != null) { bar.color = Color.clear; bar.raycastTarget = false; }
                topBar.sizeDelta = new Vector2(topBar.sizeDelta.x, GameHudMetrics.HudHeight);
                hud.SetSiblingIndex(topBar.GetSiblingIndex());
            }
            BuildTop(hud, phaseText, timerText);
            if (oldRoleCard != null) oldRoleCard.gameObject.SetActive(false);
            if (!frame)
            {
                // BuildNotice(chat); // [두루마리 공지 - 주석 처리]
                // BuildVoicePipe(chat); // [보이스파이프 제거 - 주석 처리]
                BuildNoticeBox(chat);
                SetPhase(null);
                return;
            }
            chatWindow = gameObject.AddComponent<GameHudChat>();
            chatWindow.Build(canvas, chat, bottomSheet, bottomSheet != null ? bottomSheet.GetComponent<BottomTabController>() : null,
                font, portraits, GameHudMetrics.ChatTop, GameHudMetrics.RolePanelHeight - 2f);
            // BuildNotice(chat); // [두루마리 공지 - 주석 처리]
            // BuildVoicePipe(chat); // [보이스파이프 제거 - 주석 처리]
            BuildNoticeBox(chat);
            if (bottomSheet != null) BuildInputBar(bottomSheet);
            BuildRolePanel(chat);
        }

        /// <summary>Sun by day, moon at night.</summary>
        public void SetPhase(string phase)
        {
            if (phaseIcon == null) return;
            bool night = phase == GamePhases.Night || phase == GamePhases.NightResult;
            phaseIcon.sprite = Icon(night ? "star_and_moon" : "sun");
            phaseIcon.color = night ? Moon : Sun;
        }

        /// <summary>The bar under the time: what is left of the phase (seconds of total; negative hides it).</summary>
        public void SetTimer(int seconds, int total)
        {
            if (timerFill == null) return;
            float left = seconds < 0 || total <= 0 ? 0f : Mathf.Clamp01((float)seconds / total);
            timerFill.anchorMax = new Vector2(left, 1f);
        }

        // [두루마리 공지 → 보이스파이프 말풍선으로 교체 - 주석 처리] ShowNotice
        // /// <summary>The notice scroll drops in with this text, stays a few seconds and goes away. Empty hides it.</summary>
        // public void ShowNotice(string text)
        // {
        //     if (notice == null) return;
        //     if (noticeText != null) noticeText.text = text ?? string.Empty;
        //     if (string.IsNullOrEmpty(text))
        //     {
        //         noticeShownAt = -1f;
        //         notice.gameObject.SetActive(false);
        //         return;
        //     }
        //     noticeShownAt = Time.unscaledTime;
        //     noticeSeconds = Mathf.Clamp(2f + text.Length * 0.06f, NoticeMinSeconds, NoticeMaxSeconds);
        //     notice.gameObject.SetActive(true);
        //     UpdateNotice();
        // }

        /// <summary>The notice box pops in with this text, stays a few seconds and goes away. Empty hides it.</summary>
        public void ShowNotice(string text)
        {
            if (bubble == null) return;
            if (string.IsNullOrEmpty(text))
            {
                noticeShownAt = -1f;
                bubble.gameObject.SetActive(false);
                return;
            }
            if (bubbleText != null) bubbleText.text = text;
            FitBubble(text);
            noticeShownAt = Time.unscaledTime;
            noticeSeconds = Mathf.Clamp(2f + text.Length * 0.06f, NoticeMinSeconds, NoticeMaxSeconds);
            bubble.gameObject.SetActive(true);
            UpdateNotice();
        }

        /// <summary>The role panel: my portrait (the bust of the skin I wear), role name, goal, faction and description.</summary>
        public void ShowRole(MyRoleDto me, GameStateDto state, string skinDesignId)
        {
            if (roleName == null || me == null) return;
            roleName.text = me.roleName ?? string.Empty;
            roleGoal.text = RoleInfoText.Goal(me.role, me.faction);
            factionTitle.text = RoleInfoText.FactionName(me.faction);
            factionIcon.sprite = Icon(me.faction == Factions.Pirate ? "skull" : me.faction == Factions.Neutral ? "star_and_moon" : "anchor");
            string body = RoleInfoText.Description(me.role);
            if (me.HasAbility) body += "\n능력: " + GameScreenText.AbilityLine(me);
            string mates = ReportFormatter.TeammatesLine(state, me);
            if (!string.IsNullOrEmpty(mates)) body += "\n" + mates;
            roleBody.text = body;
            Sprite bust = portraits != null ? portraits.Find(skinDesignId) : null;
            portrait.sprite = bust;
            portrait.enabled = bust != null;
            bool alive = state != null ? GameStateQueries.IsAlive(state, me.playerId) : me.alive;
            portraitShade.gameObject.SetActive(!alive);
            deadMark.gameObject.SetActive(!alive);
        }

        /// <summary>The arrow on the role panel points down while the drawer is open (the panel slides out under it).</summary>
        public void SetDrawerOpen(bool open)
        {
            if (arrow != null) arrow.localRotation = Quaternion.Euler(0f, 0f, open ? -90f : 90f);
        }

        private void LateUpdate()
        {
            if (!wired) return;
            UpdateNotice();
            LayoutDrawer();
        }

        /// <summary>The skin each player wears, for the busts on the chat's name tags.</summary>
        public void SetChatSkins(Func<long, ChibiCharacterSkin> skinOf)
        {
            if (chatWindow != null) chatWindow.SetSkins(skinOf);
        }

        // [두루마리 공지 → 보이스파이프 말풍선으로 교체 - 주석 처리] UpdateNotice
        // // Fades in while sliding down a little, stays, fades out; then it is off until the next announcement.
        // private void UpdateNotice()
        // {
        //     if (noticeShownAt < 0f) return;
        //     float t = Time.unscaledTime - noticeShownAt;
        //     float shown = Mathf.Clamp01(t / NoticeFadeIn);
        //     float gone = Mathf.Clamp01((t - NoticeFadeIn - noticeSeconds) / NoticeFadeOut);
        //     if (gone >= 1f)
        //     {
        //         noticeShownAt = -1f;
        //         notice.gameObject.SetActive(false);
        //         return;
        //     }
        //     if (noticeGroup != null) noticeGroup.alpha = shown * (1f - gone);
        //     float drop = (1f - shown) * (1f - shown) * 24f;
        //     notice.anchoredPosition = noticeRest + new Vector2(0f, drop);
        // }

        // [보이스파이프 제거 - 주석 처리] 보이스파이프 입에서 튀어나오는 UpdateNotice
        // // Pops out of the pipe's mouth (grows from the tail's tip with a little overshoot), stays, fades out while
        // // shrinking a little; then it is off until the next announcement. The mouth glows while it speaks.
        // private void UpdateNotice()
        // {
        //     if (noticeShownAt < 0f || bubble == null) return;
        //     float t = Time.unscaledTime - noticeShownAt;
        //     float pop = Mathf.Clamp01(t / BubblePop);
        //     float gone = Mathf.Clamp01((t - BubblePop - noticeSeconds) / BubbleFadeOut);
        //     if (gone >= 1f)
        //     {
        //         noticeShownAt = -1f;
        //         bubble.gameObject.SetActive(false);
        //         SetPipeGlow(0f);
        //         return;
        //     }
        //     float scale = Mathf.LerpUnclamped(0.2f, 1f, EaseOutBack(pop)) * (1f - 0.08f * gone);
        //     bubble.localScale = new Vector3(scale, scale, 1f);
        //     float shown = Mathf.Clamp01(pop * 2f) * (1f - gone);
        //     if (bubbleGroup != null) bubbleGroup.alpha = shown;
        //     SetPipeGlow(shown);
        // }

        // Pops in (grows from its top-left corner with a little overshoot), stays, fades out while shrinking a little;
        // then it is off until the next announcement.
        private void UpdateNotice()
        {
            if (noticeShownAt < 0f || bubble == null) return;
            float t = Time.unscaledTime - noticeShownAt;
            float pop = Mathf.Clamp01(t / BubblePop);
            float gone = Mathf.Clamp01((t - BubblePop - noticeSeconds) / BubbleFadeOut);
            if (gone >= 1f)
            {
                noticeShownAt = -1f;
                bubble.gameObject.SetActive(false);
                return;
            }
            float scale = Mathf.LerpUnclamped(0.6f, 1f, EaseOutBack(pop)) * (1f - 0.05f * gone);
            bubble.localScale = new Vector3(scale, scale, 1f);
            if (bubbleGroup != null) bubbleGroup.alpha = Mathf.Clamp01(pop * 2f) * (1f - gone);
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float k = x - 1f;
            return 1f + c3 * k * k * k + c1 * k * k;
        }

        // [보이스파이프 제거 - 주석 처리] SetPipeGlow
        // private void SetPipeGlow(float amount)
        // {
        //     if (pipeGlow == null) return;
        //     Color c = pipeGlow.color;
        //     c.a = 0.55f * amount;
        //     pipeGlow.color = c;
        // }

        // The drawer reaches up to the stage's bottom edge at most (its panels are laid out for MaxDrawerHeight and
        // scroll when it is shorter). Opening, the sheet comes down to the screen's bottom and the role panel slides
        // out under it; closed, the input bar sits on the role panel again.
        private void LayoutDrawer()
        {
            if (bottomTab == null || sheet == null || sheetLift == null) return;
            float height = canvas.rect.height;
            if (!Mathf.Approximately(height, laidOutHeight))
            {
                laidOutHeight = height;
                bottomTab.SetDrawerHeight(Mathf.Clamp(height - GameHudMetrics.ChatTop - GameHudMetrics.InputBarHeight,
                    GameHudMetrics.MinDrawerHeight, GameHudMetrics.MaxDrawerHeight));
            }
            float drawer = bottomTab.DrawerHeight;
            float open = drawer > 0f ? Mathf.Clamp01(1f + sheet.anchoredPosition.y / drawer) : 0f; // 0 closed, 1 open
            sheetLift.offsetMin = new Vector2(0f, GameHudMetrics.RolePanelHeight * (1f - open));
            if (rolePanel != null) rolePanel.anchoredPosition = new Vector2(0f, -GameHudMetrics.RolePanelHeight * open);
            // Open, the input bar sits over the chat log: back it like the drawer under it, and let the chat window fade
            // out (its lines would show through the drawer's panels).
            if (tabBarImage != null) tabBarImage.color = new Color(DrawerBack.r, DrawerBack.g, DrawerBack.b, DrawerBack.a * open);
            if (chatWindow != null) chatWindow.SetOpen(open);
        }

        // ------------------------------------------------------------------ parts

        private void BuildTop(RectTransform hud, TextMeshProUGUI phaseText, TextMeshProUGUI timerText)
        {
            // Inside the plaque's frame: the parchment is x 35–269, y 32–112.
            RectTransform plaque = Box(hud, "PhasePlaque", 18f, 86f, 302f, 140f);
            AddImage(plaque, Load("PhasePlaque"), Color.white);
            phaseIcon = AddImage(Box(plaque, "PhaseIcon", 42f, 49f, 46f, 46f), null, Sun);
            phaseIcon.preserveAspect = true;
            if (phaseText != null)
            {
                phaseText.rectTransform.SetParent(plaque, false);
                Inset(phaseText.rectTransform, 94f, 30f, 40f, 26f);
                Style(phaseText, 38f, Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
                phaseText.textWrappingMode = TextWrappingModes.NoWrap; // "1일차 밤 결과" shrinks instead of wrapping
                phaseText.enableAutoSizing = true;
                phaseText.fontSizeMin = 20f;
                phaseText.fontSizeMax = 38f;
            }

            // Right of the hourglass, the dark board is x 126–422, y 45–157: the label, the time under it, then the bar.
            RectTransform timer = Box(hud, "TimerPanel", 326f, 58f, 456f, 190f);
            AddImage(timer, Load("TimerPanel"), Color.white);
            TextMeshProUGUI label = NewText(timer, "TimeLabel", 19f, new Color(0.86f, 0.8f, 0.62f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            Inset(label.rectTransform, 142f, 56f, 140f, 108f);
            label.text = "남은 시간";
            if (timerText != null)
            {
                timerText.rectTransform.SetParent(timer, false);
                Inset(timerText.rectTransform, 140f, 80f, 40f, 58f);
                Style(timerText, 39f, Cream, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
                timerText.textWrappingMode = TextWrappingModes.NoWrap;
                timerText.enableAutoSizing = true;
                timerText.fontSizeMin = 24f;
                timerText.fontSizeMax = 39f;
            }
            RectTransform bar = Box(timer, "TimeBar", 142f, 136f, 268f, 12f);
            HudShapes.Panel(bar, new Color(0.03f, 0.03f, 0.03f, 0.9f), new Color(0.78f, 0.58f, 0.27f, 0.5f), 7f);
            timerFill = NewRect("Fill", bar);
            Stretch(timerFill, 2f, 2f, 2f, 2f);
            timerFill.anchorMax = new Vector2(0f, 1f);
            HudShapes.Panel(timerFill, new Color(0.86f, 0.2f, 0.15f), new Color(0f, 0f, 0f, 0f), 5f);

            settingsButton = AddButton(Box(hud, "SettingsButton", 800f, 94f, 124f, 124f), Load("ButtonSettings"), null); // Build wires them
            mapButton = AddButton(Box(hud, "MapButton", 936f, 94f, 124f, 124f), Load("ButtonMap"), null);
        }

        // [두루마리 공지 → 보이스파이프 말풍선으로 교체 - 주석 처리] BuildNotice
        // private void BuildNotice(RectTransform chat)
        // {
        //     // Over the top of the stage: right above the chat, so the day table and the night room (which put themselves
        //     // right above the chat later) end up under it, and the bottom sheet and the windows over it.
        //     notice = Box(canvas, "Notice", 120f, GameHudMetrics.NoticeTop, 840f, GameHudMetrics.NoticeHeight);
        //     if (chat != null) notice.SetSiblingIndex(chat.GetSiblingIndex() + 1);
        //     noticeGroup = notice.gameObject.AddComponent<CanvasGroup>();
        //     noticeGroup.blocksRaycasts = false;
        //     noticeGroup.interactable = false;
        //     Image scroll = AddImage(notice, Load("NoticeScroll"), Color.white);
        //     Sliced(scroll, GameHudMetrics.NoticeHeight);
        //     // The parchment between the rods is x 70–770, y 36–140: a faint compass at its right, the text in the middle
        //     // (two lines), and the "공지" tag hanging on the top rail.
        //     AddImage(Box(notice, "Compass", 664f, 46f, 84f, 84f), Icon("compass"), new Color(0.35f, 0.22f, 0.1f, 0.2f)).preserveAspect = true;
        //     noticeText = NewText(notice, "NoticeText", 30f, Ink, FontStyles.Normal, TextAlignmentOptions.Center);
        //     Inset(noticeText.rectTransform, 100f, 44f, 100f, 34f);
        //     noticeText.textWrappingMode = TextWrappingModes.Normal;
        //     noticeText.enableAutoSizing = true;
        //     noticeText.fontSizeMin = 20f;
        //     noticeText.fontSizeMax = 30f;
        //     RectTransform tag = Box(notice, "NoticeTag", 360f, -10f, 120f, 46f);
        //     HudShapes.Panel(tag, new Color(0.07f, 0.17f, 0.19f, 0.97f), new Color(0.78f, 0.58f, 0.27f), 10f);
        //     TextMeshProUGUI title = NewText(tag, "Title", 23f, Gold, FontStyles.Bold, TextAlignmentOptions.Center);
        //     Stretch(title.rectTransform, 0f, 0f, 0f, 0f);
        //     title.text = "공지";
        //     notice.gameObject.SetActive(false);
        // }

        // [보이스파이프 제거 - 주석 처리] 보이스파이프와 말풍선 (BuildVoicePipe, FitBubble, PlaceVoicePipeUnder, MouthOnCanvas)
        // // ------------------------------------------------------------------ voice pipe notice
        //
        // // The voice pipe (a ship's speaking tube) is scenery at the left of the stage, right under the top UI
        // // (GameHudMetrics.VoicePipeTop). Its art: Resources/GameHud/VoicePipe (Tools/GameHudArt/make_voice_pipe.py).
        // // It sits right under the chat log, so the day table and the night room's figures (put right above the chat
        // // later) stand in front of it, while the deck and the night backdrop (right above the screen background) stay
        // // behind it. The bubble goes right above the chat log like the old scroll, so the table and the room end up under it.
        // private void BuildVoicePipe(RectTransform chat)
        // {
        //     float top = GameHudMetrics.VoicePipeTop;
        //     voicePipe = Box(canvas, "VoicePipe", 0f, top, PipeWidth, PipeHeight);
        //     if (chat != null) voicePipe.SetSiblingIndex(chat.GetSiblingIndex());
        //     Image pipe = AddImage(voicePipe, Load("VoicePipe"), Color.white);
        //     pipe.preserveAspect = true;
        //     // A warm light in the mouth while it speaks.
        //     RectTransform glow = Box(voicePipe, "MouthGlow", PipeMouthX - 74f, PipeMouthY - 74f, 148f, 148f);
        //     pipeGlow = AddImage(glow, NightFxSprites.Dot, new Color(1f, 0.78f, 0.42f, 0f));
        //
        //     bubble = Box(canvas, "NoticeBubble", PipeMouthX + TailWidth - 4f, top + PipeMouthY - TailTip, BubbleMaxWidth, BubbleMinHeight);
        //     if (chat != null) bubble.SetSiblingIndex(chat.GetSiblingIndex() + 1);
        //     bubbleGroup = bubble.gameObject.AddComponent<CanvasGroup>();
        //     bubbleGroup.blocksRaycasts = false;
        //     bubbleGroup.interactable = false;
        //     // A square machine plate in green: riveted frame, chamfered corners (Tools/GameHudArt/make_notice_bubble.py).
        //     Image plate = AddImage(bubble, Load("NoticeBubble"), Color.white);
        //     plate.type = Image.Type.Sliced;
        //     plate.pixelsPerUnitMultiplier = 2f; // the 32-px corners at 16 units
        //     // The "공지" plate at the left, the text right of it.
        //     RectTransform tag = Box(bubble, "NoticeTag", 16f, 0f, 78f, 34f);
        //     tag.anchorMin = tag.anchorMax = tag.pivot = new Vector2(0f, 0.5f);
        //     tag.anchoredPosition = new Vector2(16f, 0f);
        //     HudShapes.Panel(tag, TagFill, TagLine, 4f);
        //     TextMeshProUGUI title = NewText(tag, "Title", 21f, TagText, FontStyles.Bold, TextAlignmentOptions.Center);
        //     Stretch(title.rectTransform, 0f, 0f, 0f, 0f);
        //     title.text = "공지";
        //     bubbleText = NewText(bubble, "NoticeText", BubbleTextSize, BubbleText, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        //     Inset(bubbleText.rectTransform, BubblePadLeft, BubblePadTop, BubblePadRight, BubblePadBottom);
        //     bubbleText.textWrappingMode = TextWrappingModes.Normal;
        //     bubbleText.enableAutoSizing = true;
        //     bubbleText.fontSizeMin = 20f;
        //     bubbleText.fontSizeMax = BubbleTextSize;
        //     // The tail points at the mouth; it lies over the bubble's left border (drawn after it), which it opens.
        //     RectTransform tail = Box(bubble, "Tail", -TailWidth + 4f, TailTip - TailWidth * 20f / 64f, TailWidth, TailWidth); // tip at (3, 20) of 64
        //     AddImage(tail, Load("NoticeBubbleTail"), Color.white).preserveAspect = true;
        //     bubble.gameObject.SetActive(false);
        // }
        //
        // // Sizes the bubble to the text (one to three lines, as wide as needed up to the screen's edge) and puts its tail
        // // on the pipe's mouth, wherever the pipe is (a baked scene may have moved it). It grows from the tail's tip.
        // private void FitBubble(string text)
        // {
        //     Vector2 mouth = MouthOnCanvas(); // x from the left, y from the top
        //     float left = mouth.x + TailWidth - 4f;
        //     float maxWidth = Mathf.Min(BubbleMaxWidth, canvas.rect.width - left - 20f);
        //     float width = maxWidth, height = BubbleMinHeight;
        //     if (bubbleText != null)
        //     {
        //         float lineHeight = BubbleTextSize * 1.33f;
        //         Vector2 preferred = bubbleText.GetPreferredValues(text, maxWidth - BubblePadLeft - BubblePadRight, 0f);
        //         width = Mathf.Clamp(preferred.x + BubblePadLeft + BubblePadRight + 4f, BubbleMinWidth, maxWidth);
        //         height = Mathf.Max(BubbleMinHeight, Mathf.Min(preferred.y, lineHeight * 3f) + BubblePadTop + BubblePadBottom);
        //     }
        //     bubble.anchorMin = bubble.anchorMax = new Vector2(0f, 1f);
        //     bubble.sizeDelta = new Vector2(width, height);
        //     bubble.pivot = new Vector2(0f, 1f - TailTip / height);
        //     bubble.anchoredPosition = new Vector2(left, -mouth.y);
        // }
        //
        // /// <summary>
        // /// Moves the voice pipe up or down so its art begins right under this (GameScene3: the [내 역할] chip, wherever the
        // /// scene has it). The bubble follows the pipe's mouth.
        // /// </summary>
        // public void PlaceVoicePipeUnder(RectTransform above, float gap = 4f)
        // {
        //     if (voicePipe == null || above == null || canvas == null) return;
        //     var corners = new Vector3[4];
        //     above.GetWorldCorners(corners); // [0]: bottom left
        //     float bottom = canvas.rect.yMax - canvas.InverseTransformPoint(corners[0]).y; // from the canvas top
        //     voicePipe.anchorMin = voicePipe.anchorMax = voicePipe.pivot = new Vector2(0f, 1f);
        //     voicePipe.anchoredPosition = new Vector2(voicePipe.anchoredPosition.x, -(bottom + gap - PipeArtTop));
        // }
        //
        // private Vector2 MouthOnCanvas()
        // {
        //     if (voicePipe == null) return new Vector2(PipeMouthX, GameHudMetrics.VoicePipeTop + PipeMouthY);
        //     Rect pipe = voicePipe.rect;
        //     Vector3 local = new Vector3(pipe.xMin + PipeMouthX * pipe.width / PipeWidth, pipe.yMax - PipeMouthY * pipe.height / PipeHeight, 0f);
        //     Vector2 onCanvas = canvas.InverseTransformPoint(voicePipe.TransformPoint(local));
        //     Rect area = canvas.rect;
        //     return new Vector2(onCanvas.x - area.xMin, area.yMax - onCanvas.y);
        // }

        // ------------------------------------------------------------------ notice box

        // The notice: a plain green rectangle at the stage's top left, right under the top UI
        // (GameHudMetrics.NoticeBoxTop; GameScene3: right under the [내 역할] chip, PlaceNoticeUnder). The "공지" tag at
        // its left, one to three lines right of it. It goes right above the chat log like the old scroll, so the day table
        // and the night room (put right above the chat later) end up under it.
        private void BuildNoticeBox(RectTransform chat)
        {
            bubble = Box(canvas, "NoticeBubble", BubbleLeft, GameHudMetrics.NoticeBoxTop, BubbleMinWidth, BubbleMinHeight);
            if (chat != null) bubble.SetSiblingIndex(chat.GetSiblingIndex() + 1);
            bubbleGroup = bubble.gameObject.AddComponent<CanvasGroup>();
            bubbleGroup.blocksRaycasts = false;
            bubbleGroup.interactable = false;
            // A plain rectangle: green fill with a thin light green border.
            AddImage(bubble, null, BubbleFill);
            Outline border = bubble.gameObject.AddComponent<Outline>();
            border.effectColor = BubbleBorder;
            border.effectDistance = new Vector2(2f, -2f);
            // The "공지" tag at the left, the text right of it.
            RectTransform tag = Box(bubble, "NoticeTag", 16f, 0f, 78f, 34f);
            tag.anchorMin = tag.anchorMax = tag.pivot = new Vector2(0f, 0.5f);
            tag.anchoredPosition = new Vector2(16f, 0f);
            AddImage(tag, null, TagFill);
            TextMeshProUGUI title = NewText(tag, "Title", 21f, TagText, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(title.rectTransform, 0f, 0f, 0f, 0f);
            title.text = "공지";
            bubbleText = NewText(bubble, "NoticeText", BubbleTextSize, BubbleText, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            Inset(bubbleText.rectTransform, BubblePadLeft, BubblePadTop, BubblePadRight, BubblePadBottom);
            bubbleText.textWrappingMode = TextWrappingModes.Normal;
            bubbleText.enableAutoSizing = true;
            bubbleText.fontSizeMin = 20f;
            bubbleText.fontSizeMax = BubbleTextSize;
            bubble.gameObject.SetActive(false);
        }

        // Sizes the box to the text (one to three lines, as wide as needed up to the screen's right edge) at its place.
        private void FitBubble(string text)
        {
            float top = noticeTop >= 0f ? noticeTop : GameHudMetrics.NoticeBoxTop;
            float maxWidth = Mathf.Min(BubbleMaxWidth, canvas.rect.width - BubbleLeft * 2f);
            float width = maxWidth, height = BubbleMinHeight;
            if (bubbleText != null)
            {
                float lineHeight = BubbleTextSize * 1.33f;
                Vector2 preferred = bubbleText.GetPreferredValues(text, maxWidth - BubblePadLeft - BubblePadRight, 0f);
                width = Mathf.Clamp(preferred.x + BubblePadLeft + BubblePadRight + 4f, BubbleMinWidth, maxWidth);
                height = Mathf.Max(BubbleMinHeight, Mathf.Min(preferred.y, lineHeight * 3f) + BubblePadTop + BubblePadBottom);
            }
            bubble.anchorMin = bubble.anchorMax = bubble.pivot = new Vector2(0f, 1f);
            bubble.sizeDelta = new Vector2(width, height);
            bubble.anchoredPosition = new Vector2(BubbleLeft, -top);
        }

        /// <summary>Puts the notice right under this (GameScene3: the [내 역할] chip, wherever the scene has it).</summary>
        public void PlaceNoticeUnder(RectTransform above, float gap = 6f)
        {
            if (above == null || canvas == null) return;
            var corners = new Vector3[4];
            above.GetWorldCorners(corners); // [0]: bottom left
            noticeTop = canvas.rect.yMax - canvas.InverseTransformPoint(corners[0]).y + gap; // from the canvas top
        }

        private void BuildInputBar(RectTransform sheet)
        {
            // Lift the sheet above the role panel: it slides inside this box exactly as before (LayoutDrawer lowers
            // the box while the drawer opens).
            this.sheet = sheet;
            bottomTab = sheet.GetComponent<BottomTabController>();
            sheetLift = NewRect("BottomSheetLift", canvas);
            sheetLift.SetSiblingIndex(sheet.GetSiblingIndex());
            Stretch(sheetLift, 0f, 0f, 0f, GameHudMetrics.RolePanelHeight);
            sheet.SetParent(sheetLift, false);

            // The field and the buttons are GameHudChat's (the bar is the chat window's bottom row).
            Transform tabBar = sheet.Find("TabBar");
            tabBarImage = tabBar != null ? tabBar.GetComponent<Image>() : null;
            if (tabBarImage != null) tabBarImage.color = Color.clear; // LayoutDrawer backs it while the drawer is open
        }

        private void BuildRolePanel(RectTransform chat)
        {
            RectTransform panel = rolePanel = NewRect("RolePanel", canvas);
            if (sheetLift != null) panel.SetSiblingIndex(sheetLift.GetSiblingIndex() + 1); // over the closed drawer
            else if (chat != null) panel.SetSiblingIndex(chat.GetSiblingIndex() + 1);
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(1f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.sizeDelta = new Vector2(0f, GameHudMetrics.RolePanelHeight);
            AddImage(panel, null, new Color(0.03f, 0.05f, 0.08f, 1f)).raycastTarget = true; // opaque: the closed drawer is under it
            RectTransform rim = NewRect("Rim", panel);
            Stretch(rim, 0f, 0f, 0f, GameHudMetrics.RolePanelHeight - 3f);
            AddImage(rim, null, new Color(0.78f, 0.58f, 0.27f, 0.9f));

            // My role: tap to open the action drawer.
            RectTransform card = BoxBottom(panel, "MyRole", 10f, 18f, 446f, 214f);
            AddButton(card, Load("TealPanel"), () => RoleCardClicked?.Invoke());
            RectTransform round = Box(card, "Portrait", 24f, 30f, 152f, 152f);
            AddImage(round, NightFxSprites.Circle, new Color(0.09f, 0.11f, 0.15f));
            round.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            RectTransform bustRect = NewRect("Bust", round);
            Stretch(bustRect, -8f, -4f, -8f, -12f);
            portrait = AddImage(bustRect, null, Color.white);
            portrait.preserveAspect = true;
            portrait.enabled = false; // until ShowRole knows my skin
            RectTransform shade = NewRect("Dead", round);
            Stretch(shade, 0f, 0f, 0f, 0f);
            portraitShade = AddImage(shade, null, new Color(0.05f, 0.05f, 0.08f, 0.6f));
            shade.gameObject.SetActive(false);
            Sprite ringSprite = Resources.Load<Sprite>("Sprites/components/Avatar_Frame");
            if (ringSprite != null)
            {
                RectTransform ring = Box(card, "Ring", 3f, 15f, 194f, 181f);
                AddImage(ring, ringSprite, Color.white);
            }
            deadMark = NewText(card, "DeadMark", 36f, new Color(0.95f, 0.3f, 0.25f), FontStyles.Bold, TextAlignmentOptions.Center);
            deadMark.rectTransform.anchorMin = deadMark.rectTransform.anchorMax = new Vector2(0f, 1f);
            deadMark.rectTransform.sizeDelta = new Vector2(150f, 50f);
            deadMark.rectTransform.anchoredPosition = new Vector2(100f, -106f);
            deadMark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 14f);
            deadMark.text = "사망";
            deadMark.gameObject.SetActive(false);

            // Right of the portrait, under the frame's top crest (down to y 67 over x 177–268) and left of the compass on
            // the right edge (x 373–446, y 81–139): heading, role name, goal.
            TextMeshProUGUI heading = NewText(Box(card, "Heading", 200f, 64f, 150f, 26f), "Text", 18f, Gold, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stretch(heading.rectTransform, 0f, 0f, 0f, 0f);
            heading.text = "내 역할";
            roleName = NewText(Box(card, "RoleName", 200f, 88f, 134f, 50f), "Text", 36f, Cream, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stretch(roleName.rectTransform, 0f, 0f, 0f, 0f);
            roleName.enableAutoSizing = true;
            roleName.fontSizeMin = 22f;
            roleName.fontSizeMax = 36f;
            roleGoal = NewText(Box(card, "Goal", 200f, 138f, 178f, 40f), "Text", 19f, new Color(0.82f, 0.88f, 0.85f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            Stretch(roleGoal.rectTransform, 0f, 0f, 0f, 0f);
            roleGoal.textWrappingMode = TextWrappingModes.Normal;
            roleGoal.enableAutoSizing = true;
            roleGoal.fontSizeMin = 14f;
            roleGoal.fontSizeMax = 19f;
            arrow = Box(card, "Arrow", 336f, 96f, 30f, 30f);
            arrow.pivot = new Vector2(0.5f, 0.5f);
            arrow.anchoredPosition += new Vector2(15f, -15f);
            AddImage(arrow, Icon("arrow_next"), Gold).preserveAspect = true;
            SetDrawerOpen(false);

            // What the role does. The parchment is y 28–198 and, clear of the diamonds on the side rails, x 50–558;
            // the top crest hangs to y 56 over x 264–351.
            RectTransform info = BoxBottom(panel, "RoleInfo", 462f, 13f, 608f, 226f);
            Sliced(AddImage(info, Load("ParchmentPanel"), Color.white), 226f);
            float scale = Load("ParchmentPanel") != null ? 226f / Load("ParchmentPanel").rect.height : 1f;
            Crest(info, "ParchmentCrestTop", true, 4f * scale, scale);
            Crest(info, "ParchmentCrestBottom", false, 2f * scale, scale);
            factionIcon = AddImage(Box(info, "FactionIcon", 54f, 40f, 34f, 34f), Icon("anchor"), Ink);
            factionIcon.preserveAspect = true;
            factionTitle = NewText(Box(info, "Faction", 96f, 35f, 162f, 44f), "Text", 28f, Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stretch(factionTitle.rectTransform, 0f, 0f, 0f, 0f);
            factionTitle.enableAutoSizing = true;
            factionTitle.fontSizeMin = 18f;
            factionTitle.fontSizeMax = 28f;
            roleBody = NewText(Box(info, "Description", 56f, 82f, 496f, 100f), "Text", 21f, new Color(0.3f, 0.2f, 0.11f), FontStyles.Normal, TextAlignmentOptions.TopLeft);
            Stretch(roleBody.rectTransform, 0f, 0f, 0f, 0f);
            roleBody.textWrappingMode = TextWrappingModes.Normal;
            roleBody.enableAutoSizing = true;
            roleBody.fontSizeMin = 14f;
            roleBody.fontSizeMax = 21f;
        }

        // ------------------------------------------------------------------ helpers

        private static Sprite Load(string name) => Resources.Load<Sprite>(Art + name);
        private static Sprite Icon(string name) => Resources.Load<Sprite>(Icons + name + "_128");

        // A frame stretched to this height keeps its proportions: the 9-slice borders scale with the height.
        private static void Sliced(Image image, float height)
        {
            if (image.sprite == null) return;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = image.sprite.rect.height / height;
        }

        // The ornament cut off a frame's edge (Tools/GameHudArt), put back centred on that edge at the frame's scale.
        private static void Crest(RectTransform frame, string sprite, bool top, float offsetX, float scale)
        {
            Sprite crest = Load(sprite);
            if (crest == null) return;
            RectTransform rect = NewRect(sprite, frame);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rect.sizeDelta = crest.rect.size * scale;
            rect.anchoredPosition = new Vector2(offsetX, 0f);
            AddImage(rect, crest, Color.white);
        }

        private static Image AddImage(RectTransform rect, Sprite sprite, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Button AddButton(RectTransform rect, Sprite sprite, Action onClick)
        {
            Image image = AddImage(rect, sprite, Color.white);
            image.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        private TextMeshProUGUI NewText(RectTransform parent, string name, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            Style(text, size, color, style, alignment);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        private static void Style(TMP_Text text, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
        {
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = alignment;
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

        // Placed from the parent's bottom-left corner (y up), this size.
        private static RectTransform BoxBottom(Transform parent, string name, float x, float bottom, float width, float height)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(x, bottom);
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

        private static void Inset(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            Stretch(rect, left, top, right, bottom);
        }
    }
}
