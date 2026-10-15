using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.GameUI;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// GameScene3 (B): from the top, the stage (the snug day table or the night room) on the ship's deck, the chat
    /// (always shown), one action line, and a strip of every player at the bottom. Nothing covers the stage.
    /// - Stage: the deck picture is raised so its sea stays behind the top HUD and the table stands on the planks.
    ///   My role chip sits at its top left (tap for my role in the HUD's window).
    /// - Chat: the tabs (전체 / 시스템 / 일반 / 귓속말) and the chat filter on top, the log, and the input bar.
    /// - Action line: only what can be done now, with small buttons: day skip by day, the night ability at night
    ///   (target picked in the strip), the vote (picked in the strip, the card hand stays hidden), otherwise the latest
    ///   announcement. The bottom drawer and its panels are not shown; the line drives the panels instead.
    /// - Strip: each player's bust and name; the dead crossed out, my pirate mates marked (only I see them), the one
    ///   I picked ringed, the vote counts during the execution. Tap to pick a night target or a vote.
    /// GameScene3 has these parts baked in (<see cref="Bake"/>, Tools/Game Scene/게임 화면 굽기): this component is on its
    /// canvas with them, Build only wires them, and nothing here moves them, so they are laid out in the editor. The
    /// stage (deck, table, night room) still follows RoundTableLayout; its Edit mode preview is RoundTablePreview.
    /// The look is the nautical concepts' (<see cref="Dress"/>, <see cref="NauticalHud"/>): the chat window in a frame with
    /// ropes and anchors and a moonlit harbour, the action line and the strip in one frame with the harbour in its head.
    /// </summary>
    public sealed class GameLayoutTabs : GameLayoutBody
    {
        private const float StripHeight = 236f, ActionHeight = 140f, InputBar = 140f, Gap = 8f;
        private const float ActionBottom = StripHeight + Gap, ChatBottom = ActionBottom + ActionHeight + Gap;
        private const float PanelHeight = ActionBottom + ActionHeight; // the night panel's frame: the action line and the strip
        // Over the chat window's foot (its frame's bottom ornaments): the chat log's bottom and the input bar's.
        private const float LogAboveFoot = ChatBottom + InputBar - 20f - PanelHeight, InputAboveFoot = ChatBottom + 4f - PanelHeight;
        private const float DeckAspect = 1672f / 941f; // DayTable/Deck, raised by RoundTableLayout.deckRaise so its sea is behind the HUD
        // The action line on the night panel's head (canvas units from the line's top-left; the panel is 12 wider each side).
        private const float SecondaryWidth = 140f, PrimaryWidth = 214f, ButtonHeight = 84f, ButtonGap = 11f, ButtonRight = 24f, ButtonDrop = 10f;
        private const float TextRight = ButtonRight + PrimaryWidth + ButtonGap + SecondaryWidth + 12f;
        private const float TitleRight = 420f; // the title ends about the moon in the panel's harbour
        // The strip: a portrait ring (with its anchor badge) and a name plate per player, at most this far apart.
        private const float SlotSpacing = 129f, StripMargin = 26f, PlateWidth = 116f, PlateHeight = 39f;
        private const float PlateBelowCentre = 67f; // from the ring's centre to the plate's top
        private const float HeldChatTop = GameHudMetrics.HudHeight + 10f; // held, the chat window's top: just under the top HUD
        private static readonly Color Pick = new Color(1f, 0.82f, 0.3f), Target = new Color(0.92f, 0.3f, 0.24f);

        private sealed class Slot
        {
            public long id;
            public CanvasGroup group;
            public GameObject ring, tally;
            public Image ringLine;
            public TextMeshProUGUI tallyText;
            // The neutral roles' marks (시안 F·G): the kraken's mark, its pulse when the kraken strikes, the siren team's
            // pink ring, and ink over one the kraken dragged away.
            public GameObject krakenMark, krakenGlow, sirenRing, ink;
            public Image krakenFill, krakenIcon, krakenGlowImage;
        }

        private static readonly Color KrakenTeal = new Color(0.18f, 0.83f, 0.77f), KrakenDim = new Color(0.11f, 0.35f, 0.34f);
        private static readonly Color KrakenInk = new Color(0.02f, 0.16f, 0.17f), SirenPink = new Color(0.94f, 0.55f, 0.78f);

        // What the neutral marks were last drawn from: redrawn when these change, only the pulse every frame.
        private MyRoleDto neutralMe;
        private GameStateDto neutralState;
        private bool neutralStruck, neutralMarkedMe;
        private int neutralSlots = -1;
        private readonly HashSet<long> krakenMarked = new HashSet<long>(), sirenTeam = new HashSet<long>();

        [System.Serializable]
        private sealed class ActionButton
        {
            public RectTransform rect;
            public Button button;
            public TextMeshProUGUI label;
        }

        private struct ActionState
        {
            public string chip, title, sub, secondary, primary;
            public bool secondaryOn, primaryOn;
            public string Key => chip + "|" + title + "|" + sub + "|" + secondary + secondaryOn + "|" + primary + primaryOn;
        }

        // Made by Create: at run time, or once in Edit mode by Bake (then saved with the scene and only wired by Build).
        [SerializeField, HideInInspector] private bool baked;
        [SerializeField] private RectTransform backdrop, strip, chipAvatar;
        [SerializeField] private Button roleChip;
        [SerializeField] private TextMeshProUGUI chipText, actionChip, actionTitle, actionSub;
        [SerializeField] private ActionButton secondaryButton, primaryButton;

        private readonly List<Slot> slots = new List<Slot>();
        private readonly Dictionary<long, int> tally = new Dictionary<long, int>();
        private RectTransform background;
        private string phase, rosterKey, chipKey, shownAction, lastNotice, voteSummary;
        private long shownPick = -1;
        private string shownMarks;
        private GameObject titleRule; // the rule under the action line's title (Dress), hidden under a two-line text
        private float laidOutWidth = -1f, laidOutRaise = float.NaN;
        // The chat log held down (RaiseChat): the window grows from the top the scene gives it, by chatRaise.
        private ChatPressHold chatHold;
        private readonly List<Canvas> chatLayers = new List<Canvas>();
        private float chatBoxTop, chatRaise;
        private bool chatRaised;

        public override bool PlaysDayTransitions => true;

        public override RectTransform PlayerSlot(long playerId)
        {
            foreach (Slot slot in slots)
            {
                if (slot.id == playerId && slot.group != null) return (RectTransform)slot.group.transform;
            }
            return null;
        }

        protected override bool NauticalChat => true;

        /// <summary>The parts are in the scene (Bake), laid out there.</summary>
        public bool IsBaked => baked;

        protected override void OnBuild()
        {
            if (!baked) Create(false);
            Wire();
        }

        /// <summary>
        /// Edit mode, once (Tools/Game Scene/게임 화면 굽기): the backdrop with the chat box, action line and strip, my role
        /// chip, the chat log's place, the input bar and the lifted sheet into the scene, to be saved with it. Play then
        /// only wires them (Build), so they can be moved and restyled in the editor.
        /// </summary>
        public void Bake(GameLayoutContext context)
        {
            if (baked) return;
            ctx = context;
            Create(true);
            // Samples to lay out against; the line shows what can be done now once the game runs.
            actionChip.text = "진행";
            actionTitle.text = "지금 할 수 있는 일";
            actionSub.text = "아래 명단에서 대상을 누르세요.";
            primaryButton.label.text = "확정";
            secondaryButton.label.text = "넘기기";
            baked = true;
        }

        /// <summary>
        /// Edit mode (Tools/Game Scene/항해 시안 입히기): a scene baked before the nautical look dressed in it (Dress),
        /// with the chat log moved inside the window's frame and the input bar dressed too.
        /// </summary>
        public void DressBaked()
        {
            GameHudMetrics.Use(HudLayout.Tabs);
            Dress();
            GameHudChat chat = GetComponent<GameHudChat>();
            if (chat != null) chat.DressNautical(ChatArea());
            if (transform.Find("BottomSheetLift") is RectTransform lift) PlaceInputBar(lift);
        }

        private void Create(bool bake)
        {
            backdrop = BuildBackdrop(ctx.Canvas);
            if (ctx.Chat != null) backdrop.SetSiblingIndex(ctx.Chat.GetSiblingIndex()); // under the chat log

            NewRect("ChatBox", backdrop);
            BuildAction(backdrop);
            BuildStrip(backdrop);
            BuildRoleChip();
            Dress();

            if (bake) BakeChat(ChatArea()); // at run time Wire builds the chat (BuildChat), following the stage
            if (ctx.Sheet != null)
            {
                RectTransform lift = LiftSheet(12f, ChatBottom + 4f, 12f);
                lift.SetSiblingIndex(backdrop.GetSiblingIndex() + 1);
                ctx.BottomTab.SetDrawerHeight(0f); // the sheet is the input bar only
            }
        }

        private void Wire()
        {
            if (roleChip != null) roleChip.onClick.AddListener(ShowRoleWindow);
            // [보이스파이프 제거 - 주석 처리] if (roleChip != null && ctx.Hud != null) ctx.Hud.PlaceVoicePipeUnder((RectTransform)roleChip.transform);
            // The notice box right under the [내 역할] chip (the scene may have moved the chip).
            if (roleChip != null && ctx.Hud != null) ctx.Hud.PlaceNoticeUnder((RectTransform)roleChip.transform);
            if (primaryButton != null && primaryButton.button != null) primaryButton.button.onClick.AddListener(OnPrimary);
            if (secondaryButton != null && secondaryButton.button != null) secondaryButton.button.onClick.AddListener(OnSecondary);
            BuildChat(ChatArea);
            if (chatUi != null) chatUi.AddTabRow(); // the tabs and filter at the log's top (made at run time, baked too)
            if (ctx.Chat != null)
            {
                chatHold = ctx.Chat.GetComponent<ChatPressHold>();
                if (chatHold == null) chatHold = ctx.Chat.gameObject.AddComponent<ChatPressHold>();
            }
            if (ctx.Sheet != null) HideDrawer();
            // The card hand keeps the vote but is not shown: the strip picks, the action line confirms.
            if (ctx.VoteHand != null)
            {
                CanvasGroup hand = ctx.VoteHand.gameObject.AddComponent<CanvasGroup>();
                hand.alpha = 0f;
                hand.blocksRaycasts = false;
                hand.interactable = false;
            }
            background = ctx.Canvas.Find("Background") as RectTransform;
            // A scene dressed before the window's backing was added gets it now.
            if (backdrop != null && backdrop.Find("ChatBox") is RectTransform box && box.Find("Inside") == null) Inside(box);
            Transform rule = actionTitle != null ? actionTitle.transform.parent.Find("TitleRule") : null;
            if (rule != null) titleRule = rule.gameObject;
        }

        // The chat window (ChatBox) in the canvas, wherever the scene puts it and however big: its insets from the canvas's
        // left, top, right and bottom. Without one (an older bake), where Dress puts it: the backdrop's top to the night panel.
        private Vector4 ChatWindow()
        {
            var canvas = (RectTransform)transform; // on the game canvas, as GameScreen is
            if (backdrop == null || !(backdrop.Find("ChatBox") is RectTransform box))
            {
                float top = backdrop != null ? -backdrop.offsetMax.y : GameHudMetrics.StageBottom;
                return new Vector4(0f, top, 0f, PanelHeight);
            }
            box.GetWorldCorners(corners);
            Vector3 min = canvas.InverseTransformPoint(corners[0]), max = canvas.InverseTransformPoint(corners[2]);
            Rect r = canvas.rect;
            return new Vector4(min.x - r.xMin, r.yMax - max.y, r.xMax - max.x, min.y - r.yMin);
        }

        // The chat log fills the chat window inside its frame (under its top rail, the tab row first), down to just over the
        // input bar on the window's foot (left, top from the top, right, bottom from the bottom).
        private Vector4 ChatArea()
        {
            Vector4 w = ChatWindow();
            return new Vector4(w.x + 28f, w.y + 40f, w.z + 28f, w.w + LogAboveFoot);
        }

        // The input bar (the lifted bottom sheet) on the chat window's foot, inside its frame.
        private void PlaceInputBar(RectTransform lift)
        {
            Vector4 w = ChatWindow();
            lift.offsetMin = new Vector2(w.x + 12f, w.w + InputAboveFoot);
            lift.offsetMax = new Vector2(-(w.z + 12f), lift.offsetMax.y);
        }

        // The chat log and the input bar follow the chat window: resizing it in the scene gives the chat that room. The
        // drawer's sheet, tab bar and log keep their own places otherwise (the bake tool stopped the sheet pushing the log).
        private void FitChat()
        {
            Vector4 area = ChatArea();
            if (area == fittedArea) return;
            fittedArea = area;
            if (ctx.Chat != null)
            {
                ctx.Chat.offsetMin = new Vector2(area.x, area.w);
                ctx.Chat.offsetMax = new Vector2(-area.z, -area.y);
            }
            if (ctx.Sheet != null && ctx.Sheet.parent is RectTransform lift && lift.name == "BottomSheetLift") PlaceInputBar(lift);
        }

        private Vector4 fittedArea = new Vector4(-1f, -1f, -1f, -1f);

        // Held (ChatPressHold), the chat window grows up over the stage to just under the top HUD, so older lines show
        // above; let go, it shrinks back to the scene's size. Meanwhile the window, its log and the input bar draw over the
        // stage, in their own order: each is on a canvas of its own, sorted over the game canvas only while grown.
        private void RaiseChat()
        {
            float open = chatHold != null ? chatHold.Open : 0f;
            if (open <= 0f && !chatRaised) return;
            if (backdrop == null || !(backdrop.Find("ChatBox") is RectTransform box)) return;
            if (!chatRaised)
            {
                chatBoxTop = box.offsetMax.y;
                chatRaise = Mathf.Max(0f, ChatWindow().y - HeldChatTop);
                SetChatOver(true);
                chatRaised = true;
            }
            box.offsetMax = new Vector2(box.offsetMax.x, chatBoxTop + chatRaise * open);
            if (open > 0f) return;
            SetChatOver(false);
            chatRaised = false;
        }

        private void SetChatOver(bool over)
        {
            if (chatLayers.Count == 0)
            {
                RectTransform lift = ctx.Sheet != null && ctx.Sheet.parent is RectTransform l && l.name == "BottomSheetLift" ? l : null;
                foreach (RectTransform part in new[] { backdrop, ctx.Chat, lift })
                {
                    if (part != null) chatLayers.Add(OwnCanvas(part));
                }
            }
            Canvas root = ctx.Canvas.GetComponent<Canvas>();
            int order = root != null ? root.sortingOrder : 0;
            for (int i = 0; i < chatLayers.Count; i++)
            {
                chatLayers[i].overrideSorting = over;
                chatLayers[i].sortingOrder = order + 1 + i;
            }
        }

        // part on a canvas of its own: unsorted, it still draws in its place among its siblings; still clickable.
        private static Canvas OwnCanvas(RectTransform part)
        {
            Canvas canvas = part.GetComponent<Canvas>();
            if (canvas == null)
            {
                Canvas parent = part.parent != null ? part.parent.GetComponentInParent<Canvas>() : null;
                canvas = part.gameObject.AddComponent<Canvas>();
                if (parent != null) canvas.additionalShaderChannels = parent.rootCanvas.additionalShaderChannels;
            }
            if (part.GetComponent<GraphicRaycaster>() == null) part.gameObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>
        /// Everything under the stage lies on one night-blue backdrop, the colour the nautical frames were drawn on (the
        /// raised deck picture does not reach the bottom). Stretched over parent from the stage's bottom down. The Edit
        /// mode preview uses it too.
        /// </summary>
        public static RectTransform BuildBackdrop(RectTransform parent)
        {
            RectTransform backdrop = NewRect("BottomBackdrop", parent);
            Stretch(backdrop, 0f, GameHudMetrics.StageBottom, 0f, 0f);
            AddImage(backdrop, null, NauticalHud.Backdrop);
            return backdrop;
        }

        // ------------------------------------------------------------------ the nautical look

        /// <summary>
        /// Dresses the parts in the nautical look, made here or baked into the scene before (Tools/Game Scene/항해 시안
        /// 입히기 calls it on GameScene3, then <see cref="GameHudChat.DressNautical"/>): the chat window's frame and harbour
        /// over the backdrop from the stage down to the night panel, the night panel's frame under the action line and the
        /// strip, and the badge, texts and buttons put where the concept has them. Old panels and outlines are taken off.
        /// Dressed once already (the night panel is there), the parts keep the places and sizes the scene gives them; only
        /// the pictures and colours are put on again.
        /// </summary>
        public void Dress()
        {
            if (backdrop == null) return;
            bool place = backdrop.Find("NightPanel") == null;
            Image back = backdrop.GetComponent<Image>();
            if (back != null) back.color = NauticalHud.Backdrop;
            Transform oldRule = backdrop.Find("Rule");
            if (oldRule != null) oldRule.gameObject.SetActive(false);

            // The chat window, as wide as the screen (the frame is drawn 1080 wide), the harbour at its right above the input bar.
            if (backdrop.Find("ChatBox") is RectTransform chatBox)
            {
                TakeOffPanel(chatBox, true);
                Inside(chatBox);
                Image harbor = Part(chatBox, "Harbor", 1);
                NauticalHud.Whole(harbor, "ChatHarbor");
                Image frame = Part(chatBox, "Frame", 2);
                NauticalHud.Sliced(frame, "ChatFrame", NauticalHud.ChatScale);
                if (place)
                {
                    Stretch(chatBox, 0f, 0f, 0f, PanelHeight);
                    RectTransform h = harbor.rectTransform;
                    h.anchorMin = h.anchorMax = h.pivot = new Vector2(1f, 0f);
                    h.sizeDelta = harbor.sprite != null ? harbor.sprite.rect.size / NauticalHud.ChatScale : new Vector2(460f, 301f);
                    h.anchoredPosition = new Vector2(-55f / NauticalHud.ChatScale, 209f / NauticalHud.ChatScale);
                    Stretch(frame.rectTransform, 0f, 0f, 0f, 0f);
                }
            }

            RectTransform line = actionTitle != null ? actionTitle.rectTransform.parent as RectTransform : null;
            if (line != null)
            {
                // The night panel: one frame round the action line and the strip, the harbour in its head.
                Image panel = Part(backdrop, "NightPanel", line.GetSiblingIndex());
                if (place)
                {
                    RectTransform p = panel.rectTransform;
                    p.anchorMin = Vector2.zero;
                    p.anchorMax = new Vector2(1f, 0f);
                    p.pivot = new Vector2(0.5f, 0f);
                    p.offsetMin = Vector2.zero;
                    p.offsetMax = new Vector2(0f, PanelHeight);
                }
                NauticalHud.Sliced(panel, "NightPanel", NauticalHud.NightScale);
                TakeOffPanel(line, true);
            }
            if (actionChip != null && actionChip.rectTransform.parent is RectTransform chip)
            {
                if (place)
                {
                    At(chip, 25f, 33f, 170f, 46f);
                    Stretch(actionChip.rectTransform, 56f, 3f, 16f, 3f);
                }
                Image badge = chip.GetComponent<Image>();
                if (badge != null) NauticalHud.Plate(badge, "Badge", chip.rect.height);
                RemoveChild(chip, "Outline");
                actionChip.color = NauticalHud.Ink;
            }
            // The text sizes stay as they are (set for a phone to read); only places and colours change.
            if (actionTitle != null)
            {
                if (place) Place(actionTitle.rectTransform, 206f, 22f, TitleRight, 52f);
                actionTitle.color = NauticalHud.Title;
            }
            if (line != null)
            {
                // The gold rule and star under the title (the concept's, at its place there); hidden under a two-line text.
                Image rule = Part(line, "TitleRule", 0);
                NauticalHud.Whole(rule, "TitleRule");
                Vector2 size = rule.sprite != null ? rule.sprite.rect.size / NauticalHud.NightScale : new Vector2(457f, 24f);
                if (place) At(rule.rectTransform, 430f / NauticalHud.NightScale - 12f, 148f / NauticalHud.NightScale, size.x, size.y);
            }
            if (actionSub != null)
            {
                // One line sits under the head's rule; a long one takes two lines over it.
                if (place)
                {
                    Place(actionSub.rectTransform, 64f, 74f, TextRight, 64f);
                    actionSub.alignment = TextAlignmentOptions.MidlineLeft;
                }
                actionSub.textWrappingMode = TextWrappingModes.Normal;
                actionSub.color = NauticalHud.Sub;
            }
            DressButton(primaryButton, "ButtonGold", ButtonRight, PrimaryWidth, NauticalHud.Ink, place);
            DressButton(secondaryButton, "ButtonTeal", ButtonRight + PrimaryWidth + ButtonGap, SecondaryWidth, NauticalHud.Title, place);

            if (strip != null)
            {
                TakeOffPanel(strip, false); // the panel's frame shows through; the strip still catches nothing
                RemoveChild(strip, "Rule");
            }
            rosterKey = null; // the strip's people are made again in the new look
        }

        // Night blue inside the chat window's frame lines, so a window made taller over the stage is not see-through.
        private static void Inside(RectTransform chatBox)
        {
            Image inside = Part(chatBox, "Inside", 0);
            inside.sprite = null;
            inside.color = NauticalHud.Backdrop;
            Stretch(inside.rectTransform, 20f, 22f, 20f, 28f);
        }

        private static void DressButton(ActionButton b, string sprite, float right, float width, Color label, bool place)
        {
            if (b == null || b.rect == null) return;
            if (place)
            {
                b.rect.anchorMin = b.rect.anchorMax = b.rect.pivot = new Vector2(1f, 0.5f);
                b.rect.anchoredPosition = new Vector2(-right, -ButtonDrop);
                b.rect.sizeDelta = new Vector2(width, ButtonHeight);
                if (b.label != null) Stretch(b.label.rectTransform, 22f, 6f, 22f, 6f); // clear of the frame's corners
            }
            Image image = b.rect.GetComponent<Image>();
            if (image != null) NauticalHud.Plate(image, sprite, b.rect.rect.height);
            RemoveChild(b.rect, "Outline");
            if (b.label != null) b.label.color = label;
        }

        // A part of the look on parent: the child of that name (made at that sibling index if there is none) with an Image.
        private static Image Part(RectTransform parent, string name, int siblingIndex)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                child = NewRect(name, parent);
                child.SetSiblingIndex(siblingIndex);
            }
            Image image = child.GetComponent<Image>();
            if (image == null) image = child.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        // The rounded panel (HudShapes) of the old look off rect: its outline child, and its fill (or only the fill's colour).
        private static void TakeOffPanel(RectTransform rect, bool removeFill)
        {
            RemoveChild(rect, "Outline");
            Image fill = rect.GetComponent<Image>();
            if (fill == null) return;
            if (removeFill && rect.GetComponent<Selectable>() == null) Remove(fill);
            else fill.color = new Color(0f, 0f, 0f, 0f);
        }

        private static void RemoveChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) Remove(child.gameObject);
        }

        private static void Remove(Object thing)
        {
            if (Application.isPlaying) Destroy(thing);
            else DestroyImmediate(thing);
        }

        // At x, top from the parent's top-left corner, this size.
        private static void At(RectTransform rect, float x, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// The deck picture (the scene's Background): full width, raised so the planks are under the table and the sea
        /// behind the HUD. The Edit mode preview uses it too.
        /// </summary>
        public static void PlaceDeck(RectTransform deck, float canvasWidth)
        {
            deck.anchorMin = new Vector2(0f, 1f);
            deck.anchorMax = new Vector2(1f, 1f);
            deck.pivot = new Vector2(0.5f, 1f);
            deck.sizeDelta = new Vector2(0f, canvasWidth * DeckAspect);
            deck.anchoredPosition = new Vector2(0f, RoundTableLayout.Current.deckRaise * canvasWidth / 1080f);
        }

        // The drawer's panels (keyboard placeholder, vote, night ability, day skip) stay where GameScreen and the bottom
        // sheet switch them, but under an inactive holder: the action line does their work.
        private void HideDrawer()
        {
            RectTransform holder = NewRect("HiddenDrawer", ctx.Canvas);
            holder.gameObject.SetActive(false);
            Transform drawer = ctx.Sheet.Find("Drawer");
            if (drawer != null) drawer.SetParent(holder, false);
            foreach (MonoBehaviour panel in new MonoBehaviour[] { ctx.NightPanel, ctx.DayPanel })
            {
                if (panel != null && !panel.transform.IsChildOf(holder)) panel.transform.SetParent(holder, false);
            }
        }

        // ------------------------------------------------------------------ parts

        private void BuildRoleChip()
        {
            Transform notice = ctx.Canvas.Find("NoticeBubble") ?? ctx.Canvas.Find("Notice"); // the notice box (the old scroll in an older bake)
            RectTransform chip = Box(ctx.Canvas, "RoleChip", 16f, GameHudMetrics.HudHeight + 10f, 206f, 66f); // clear of the far seats' names
            if (notice != null) chip.SetSiblingIndex(notice.GetSiblingIndex()); // under the notice
            HudShapes.Panel(chip, Teal, GoldLine, 33f).raycastTarget = true;
            roleChip = chip.gameObject.AddClickSoundButton(); // Wire listens to it
            chipAvatar = Box(chip, "Avatar", 0f, 0f, 66f, 66f);
            chipText = NewText(chip, "Text", 26f, Cream, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stretch(chipText.rectTransform, 72f, 4f, 32f, 4f);
            chipText.enableAutoSizing = true;
            chipText.fontSizeMin = 16f;
            chipText.fontSizeMax = 26f;
            chipText.text = "내 역할";
            Image more = AddImage(Box(chip, "More", 0f, 0f, 20f, 20f), Icon("arrow_next"), Gold);
            more.rectTransform.anchorMin = more.rectTransform.anchorMax = more.rectTransform.pivot = new Vector2(1f, 0.5f);
            more.rectTransform.anchoredPosition = new Vector2(-12f, 0f);
            more.preserveAspect = true;
        }

        // The action line's parts; Dress gives them their look and places them on the night panel's head.
        private void BuildAction(RectTransform backdrop)
        {
            RectTransform box = NewRect("ActionLine", backdrop);
            box.anchorMin = new Vector2(0f, 0f);
            box.anchorMax = new Vector2(1f, 0f);
            box.pivot = new Vector2(0.5f, 0f);
            box.offsetMin = new Vector2(12f, ActionBottom);
            box.offsetMax = new Vector2(-12f, ActionBottom + ActionHeight);

            RectTransform chip = NewRect("Chip", box);
            AddImage(chip, null, Color.white);
            // Sizes a phone reads: chip 26, title 36, line under it 27, buttons 30 (no smaller than 22, 28, 24, 24).
            actionChip = Sized(NewText(chip, "Text", 26f, Ink, FontStyles.Bold, TextAlignmentOptions.Center), 22f);
            actionTitle = Sized(NewText(box, "Title", 36f, Cream, FontStyles.Bold, TextAlignmentOptions.MidlineLeft), 28f);
            actionSub = Sized(NewText(box, "Sub", 27f, Muted, FontStyles.Normal, TextAlignmentOptions.MidlineLeft), 24f);
            actionSub.textWrappingMode = TextWrappingModes.Normal;

            primaryButton = NewButton(box, "Primary", true); // Wire listens to them
            secondaryButton = NewButton(box, "Secondary", false);
        }

        private ActionButton NewButton(RectTransform parent, string name, bool primary)
        {
            RectTransform rect = NewRect(name, parent);
            Button button = TextButton(rect, string.Empty, primary, null);
            TextMeshProUGUI label = rect.Find("Label").GetComponent<TextMeshProUGUI>();
            label.fontSize = 30f;
            Sized(label, 24f);
            return new ActionButton { rect = rect, button = button, label = label };
        }

        // Shrinks to fit, from its size down to min.
        private static TextMeshProUGUI Sized(TextMeshProUGUI text, float min)
        {
            text.enableAutoSizing = true;
            text.fontSizeMax = text.fontSize;
            text.fontSizeMin = min;
            return text;
        }

        // The strip's place at the bottom; its people are made by RebuildStrip, on the night panel's frame.
        private void BuildStrip(RectTransform backdrop)
        {
            strip = NewRect("PlayerStrip", backdrop);
            strip.anchorMin = new Vector2(0f, 0f);
            strip.anchorMax = new Vector2(1f, 0f);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.sizeDelta = new Vector2(0f, StripHeight);
        }

        // ------------------------------------------------------------------ GameScreen

        public override void OnDrawerMode(BottomTabMode mode)
        {
            // GameScreen opens the drawer at night and by day; here the action line is the drawer, so keep it closed.
            if (mode == BottomTabMode.Vote && ctx.BottomTab != null) ctx.BottomTab.Close();
        }

        public override void OnPhase(string newPhase)
        {
            phase = newPhase;
            if (phase != GamePhases.Execution)
            {
                tally.Clear();
                voteSummary = null;
            }
        }

        public override void OnNotice(string text)
        {
            lastNotice = text;
        }

        public override void OnExecutionResult(ExecutionResultDto result)
        {
            tally.Clear();
            if (result != null && result.votes != null)
            {
                foreach (VoteCountDto vote in result.votes) if (vote != null && vote.count > 0) tally[vote.playerId] = vote.count;
            }
            voteSummary = result != null ? GameScreenText.VoteSummary(result) : null;
        }

        public override void Refresh(GameSession session)
        {
            if (session == null || session.State == null) return;
            if (phase == null) phase = session.State.phase;
            RebuildStrip(session);
            UpdateChip(session);
        }

        private void UpdateChip(GameSession session)
        {
            if (session.Me == null) return;
            Sprite bust = BustOf(session.Me.playerId);
            bool siren = session.Me.IsSirenTeam;
            string key = (session.Me.roleName ?? "") + "|" + (bust != null ? bust.name : "") + (siren ? "|siren" : "");
            if (key == chipKey) return;
            chipKey = key;
            if (chipText != null) chipText.text = session.Me.roleName ?? "내 역할";
            // 세이렌 팀(세이렌, 유혹당한 사람)이면 칩 테두리가 분홍으로 (시안 F ④)
            Transform outline = roleChip != null ? roleChip.transform.Find("Outline") : null;
            Image line = outline != null ? outline.GetComponent<Image>() : null;
            if (line != null) line.color = siren ? SirenPink : GoldLine;
            if (chipAvatar == null) return;
            for (int i = chipAvatar.childCount - 1; i >= 0; i--) Destroy(chipAvatar.GetChild(i).gameObject);
            // Fits the avatar box, inset 6 (a 66 box holds a 54 avatar), so resizing the box in the scene resizes it
            float size = Mathf.Max(8f, Mathf.Min(chipAvatar.rect.width, chipAvatar.rect.height) - 12f);
            Avatar(chipAvatar, "Me", 6f, 6f, size, session.Me.playerId, GameStateQueries.NicknameOf(session.State, session.Me.playerId));
        }

        // ------------------------------------------------------------------ strip

        private void RebuildStrip(GameSession session)
        {
            List<PlayerViewDto> players = session.State.players;
            long myId = session.Me != null ? session.Me.playerId : 0;
            var key = new System.Text.StringBuilder();
            foreach (PlayerViewDto p in players) key.Append(p.playerId).Append(p.alive ? '+' : '-').Append(BustOf(p.playerId) != null ? 'b' : 'n').Append(',');
            if (session.Me != null) foreach (long id in session.Me.mafiaTeammateIds) key.Append('m').Append(id);
            foreach (KeyValuePair<long, string> investigated in session.Investigations) key.Append('i').Append(investigated.Key).Append(investigated.Value);
            if (key.ToString() == rosterKey || strip == null) return;
            rosterKey = key.ToString();

            for (int i = strip.childCount - 1; i >= 0; i--)
            {
                Transform child = strip.GetChild(i);
                if (child.name.StartsWith("Player")) Destroy(child.gameObject);
            }
            slots.Clear();
            int count = Mathf.Max(1, players.Count);
            float stripWidth = strip.rect.width > 0f ? strip.rect.width : 1080f; // as wide as the strip is in the scene
            float stripHeight = strip.rect.height > 0f ? strip.rect.height : StripHeight;
            float spacing = Mathf.Min(SlotSpacing, (stripWidth - StripMargin * 2f) / count);
            Sprite ringArt = NauticalHud.Get("PortraitRing");
            // The ring (sprite) drawn ringWidth wide, smaller for many players; its centre half that from its top.
            float ringWidth = ringArt != null ? ringArt.rect.width / NauticalHud.NightScale : 131f;
            float k = Mathf.Min(1f, (spacing - 4f) / ringWidth);
            float w = ringWidth * k, h = ringArt != null ? w * ringArt.rect.height / ringArt.rect.width : w;
            float face = w * NauticalHud.RingInner * 2f + 2f, outer = w * NauticalHud.RingOuter * 2f;
            float plateW = Mathf.Min(PlateWidth, spacing - 6f), plateH = PlateHeight * Mathf.Max(0.85f, k);
            // Ring and plate together, in the middle of the strip above the frame's foot.
            float block = w * 0.5f + PlateBelowCentre * k + plateH;
            float centre = 6f + Mathf.Max(0f, (stripHeight - 46f - 6f - block) * 0.5f) + w * 0.5f;
            float left = (stripWidth - spacing * count) * 0.5f;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerViewDto p = players[i];
                RectTransform root = Box(strip, "Player" + p.playerId, left + i * spacing, 0f, spacing, stripHeight);
                AddImage(root, null, new Color(0f, 0f, 0f, 0f)).raycastTarget = true;
                long id = p.playerId;
                root.gameObject.AddClickSoundButton().onClick.AddListener(() => OnSlotClicked(id));
                var slot = new Slot { id = id, group = root.gameObject.AddComponent<CanvasGroup>() };
                float mid = spacing * 0.5f;
                Avatar(root, "Avatar", mid - face * 0.5f, centre - face * 0.5f, face, p.playerId, p.nickname, false);
                NauticalHud.Whole(AddImage(Box(root, "Ring", mid - w * 0.5f, centre - w * 0.5f, w, h), null, Color.white), "PortraitRing");
                RectTransform ring = Box(root, "Pick", mid - outer * 0.5f - 7f, centre - outer * 0.5f - 7f, outer + 14f, outer + 14f);
                HudShapes.Panel(ring, new Color(0f, 0f, 0f, 0f), Pick, (outer + 14f) * 0.5f);
                slot.ring = ring.gameObject;
                slot.ringLine = ring.Find("Outline").GetComponent<Image>();
                slot.ringLine.pixelsPerUnitMultiplier *= 0.5f; // a thick line round the gold ring
                ring.gameObject.SetActive(false);

                RectTransform plate = Box(root, "Plate", mid - plateW * 0.5f, centre + PlateBelowCentre * k, plateW, plateH);
                NauticalHud.Plate(AddImage(plate, null, Color.white), "NamePlate", plateH);
                // A long nickname is not shrunk past reading: what does not fit ends in "…" (NewText's overflow).
                TextMeshProUGUI name = NewText(plate, "Name", 24f, p.playerId == myId ? Gold : Cream, FontStyles.Bold, TextAlignmentOptions.Center);
                Stretch(name.rectTransform, 14f, 3f, 14f, 5f); // clear of the plate's studs
                name.enableAutoSizing = true;
                name.fontSizeMax = 24f * Mathf.Max(0.8f, k);
                name.fontSizeMin = name.fontSizeMax * 0.8f;
                name.text = p.nickname;
                if (!p.alive)
                {
                    AddImage(Box(root, "Dead", mid - face * 0.32f, centre - face * 0.32f, face * 0.64f, face * 0.64f), Icon("clear"), Target).preserveAspect = true;
                    name.color = Muted;
                }
                if (session.Me != null && session.Me.mafiaTeammateIds.Contains(p.playerId))
                {
                    RectTransform mark = Box(root, "Mate", mid + outer * 0.5f - 30f, centre - outer * 0.5f - 4f, 36f, 36f);
                    HudShapes.Panel(mark, new Color(0.16f, 0.11f, 0.06f), GoldLine, 18f);
                    AddImage(Box(mark, "Skull", 7f, 7f, 22f, 22f), Icon("skull"), Cream).preserveAspect = true;
                }
                string found;
                if (session.Investigations.TryGetValue(p.playerId, out found))
                {
                    // What my investigation found (captain): top left, red with a skull for a pirate, blue with an anchor for crew.
                    bool pirate = found == Factions.Pirate;
                    RectTransform mark = Box(root, "Investigated", mid - outer * 0.5f - 6f, centre - outer * 0.5f - 4f, 36f, 36f);
                    HudShapes.Panel(mark, pirate ? InvestigationRevealView.PirateRed : InvestigationRevealView.CrewBlue, GoldLine, 18f);
                    AddImage(Box(mark, "Icon", 7f, 7f, 22f, 22f), Icon(pirate ? "skull" : "anchor"), Cream).preserveAspect = true;
                }
                BuildNeutralMarks(slot, root, mid, centre, face, outer);
                RectTransform badge = Box(root, "Tally", mid + outer * 0.5f - 32f, centre + outer * 0.5f - 46f, 40f, 40f);
                HudShapes.Panel(badge, new Color(0.78f, 0.22f, 0.18f), Cream, 20f);
                slot.tallyText = NewText(badge, "Count", 24f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
                Stretch(slot.tallyText.rectTransform, 0f, 0f, 0f, 0f);
                slot.tally = badge.gameObject;
                badge.gameObject.SetActive(false);
                slots.Add(slot);
            }
            shownPick = -1;
            shownMarks = null;
            neutralSlots = -1; // new slots: draw their neutral marks again
        }

        // The neutral roles' parts of one roster slot, all hidden until UpdateNeutralMarks shows them.
        private static void BuildNeutralMarks(Slot slot, RectTransform root, float mid, float centre, float face, float outer)
        {
            // The kraken striking: a teal glow behind the face of each one it marked.
            RectTransform glow = Box(root, "KrakenGlow", mid - outer * 0.75f, centre - outer * 0.75f, outer * 1.5f, outer * 1.5f);
            glow.SetAsFirstSibling();
            slot.krakenGlowImage = AddImage(glow, NightFxSprites.Dot, KrakenTeal);
            slot.krakenGlow = glow.gameObject;
            // The siren team: a pink ring round the portrait (only on the screens of the siren team).
            RectTransform pink = Box(root, "SirenRing", mid - outer * 0.5f - 3f, centre - outer * 0.5f - 3f, outer + 6f, outer + 6f);
            HudShapes.Panel(pink, new Color(0f, 0f, 0f, 0f), SirenPink, (outer + 6f) * 0.5f);
            Image pinkLine = pink.Find("Outline").GetComponent<Image>();
            pinkLine.pixelsPerUnitMultiplier *= 0.6f; // a thick line
            slot.sirenRing = pink.gameObject;
            // Dragged away by the kraken: ink seeping up from the bottom of the face.
            RectTransform ink = Box(root, "KrakenInk", mid - face * 0.5f, centre - face * 0.05f, face, face * 0.55f);
            AddImage(ink, NightFxSprites.Fade, new Color(KrakenInk.r, KrakenInk.g, KrakenInk.b, 0.85f));
            slot.ink = ink.gameObject;
            // The kraken's mark: a curled tentacle in a disc, where the pirates' skull would be (top right).
            RectTransform mark = Box(root, "KrakenMark", mid + outer * 0.5f - 30f, centre - outer * 0.5f - 4f, 36f, 36f);
            slot.krakenFill = HudShapes.Panel(mark, KrakenTeal, new Color(0.9f, 1f, 0.98f), 18f);
            slot.krakenIcon = AddImage(Box(mark, "Tentacle", 5f, 5f, 26f, 26f), NightFxSprites.Tentacle, KrakenInk);
            slot.krakenIcon.preserveAspect = true;
            // A marked one cannot be picked again (dimmed at night), but the badge stays bright.
            CanvasGroup bright = mark.gameObject.AddComponent<CanvasGroup>();
            bright.ignoreParentGroups = true;
            bright.blocksRaycasts = false;
            slot.krakenMark = mark.gameObject;
            foreach (GameObject part in new[] { slot.krakenGlow, slot.sirenRing, slot.ink, slot.krakenMark }) part.SetActive(false);
        }

        /// <summary>
        /// The neutral roles' marks on the roster, from what this player knows:
        /// - the kraken: a tentacle badge on each living one it marked (/me krakenMarkIds); a pulsing glow when it strikes;
        /// - one the kraken marked: a dark badge on their own slot only (KRAKEN_MARKED, remembered by the session);
        /// - the siren team (the siren and the seduced): a pink ring round each of the team (/me sirenTeamIds);
        /// - everyone: ink over the ones the kraken dragged away (the night result's death cause is public).
        /// </summary>
        private void UpdateNeutralMarks(GameSession session)
        {
            MyRoleDto me = session.Me;
            bool struck = session.StruckTonight, markedMe = session.MarkedByKraken;
            if (me != neutralMe || session.State != neutralState || struck != neutralStruck || markedMe != neutralMarkedMe || slots.Count != neutralSlots)
            {
                neutralMe = me;
                neutralState = session.State;
                neutralStruck = struck;
                neutralMarkedMe = markedMe;
                neutralSlots = slots.Count;
                krakenMarked.Clear();
                sirenTeam.Clear();
                if (me != null)
                {
                    foreach (PlayerViewDto p in session.KrakenMarks) krakenMarked.Add(p.playerId);
                    if (me.IsSirenTeam)
                    {
                        sirenTeam.Add(me.playerId);
                        foreach (long id in me.sirenTeamIds) sirenTeam.Add(id);
                    }
                }
                foreach (Slot slot in slots)
                {
                    bool myMark = markedMe && me != null && slot.id == me.playerId;
                    bool marked = krakenMarked.Contains(slot.id);
                    slot.krakenMark.SetActive(marked || myMark);
                    slot.krakenFill.color = myMark ? KrakenDim : KrakenTeal;
                    slot.krakenIcon.color = myMark ? new Color(0.65f, 0.94f, 0.9f) : KrakenInk;
                    slot.krakenGlow.SetActive(struck && marked);
                    slot.sirenRing.SetActive(sirenTeam.Contains(slot.id));
                    slot.ink.SetActive(session.WasDraggedByKraken(slot.id));
                }
            }
            if (!struck) return;
            // [발동]을 고른 밤: 끌려갈 칸이 1.2초마다 맥박처럼 빛난다
            float pulse = 0.35f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI / 1.2f));
            foreach (Slot slot in slots)
            {
                if (slot.krakenGlow.activeSelf) slot.krakenGlowImage.color = new Color(KrakenTeal.r, KrakenTeal.g, KrakenTeal.b, pulse);
            }
        }

        private void OnSlotClicked(long playerId)
        {
            GameSession session = ctx.Session();
            if (session == null) return;
            if (phase == GamePhases.Vote && session.CanVote && ctx.VoteHand != null && ctx.VoteHand.IsShown && !ctx.VoteHand.IsLocked)
            {
                ctx.VoteHand.Choose(playerId); // the same player again takes the vote back (abstain)
            }
            else if (phase == GamePhases.Night && ctx.NightPanel != null && ctx.NightPanel.CanTarget(playerId))
            {
                ctx.NightPanel.Select(playerId);
            }
        }

        // ------------------------------------------------------------------ every frame

        private void LateUpdate()
        {
            if (ctx == null) return;
            float width = CanvasWidth;
            float raise = RoundTableLayout.Current.deckRaise;
            if (background != null && (!Mathf.Approximately(width, laidOutWidth) || raise != laidOutRaise))
            {
                laidOutWidth = width;
                laidOutRaise = raise;
                PlaceDeck(background, width);
            }
            // The stage can be retuned while playing. Baked, the backdrop stays where the scene has it.
            if (!baked && backdrop != null) backdrop.offsetMax = new Vector2(0f, -GameHudMetrics.StageBottom);
            RaiseChat();
            FitChat(); // a taller screen stretches the chat window

            GameSession session = ctx.Session();
            if (session == null || session.State == null) return;
            if (phase == null) phase = session.State.phase;
            UpdateMarks(session);
            UpdateNeutralMarks(session);
            ApplyAction(StateFor(session));
        }

        private float CanvasWidth => ctx.Canvas.rect.width;

        // Who is picked (gold for the vote, red for the night target), who cannot be picked tonight (dimmed), and the
        // vote counts during the execution.
        private void UpdateMarks(GameSession session)
        {
            bool votePick = phase == GamePhases.Vote && ctx.VoteHand != null && ctx.VoteHand.IsShown;
            bool nightPick = phase == GamePhases.Night && ctx.NightPanel != null && session.NightAbility == AbilityBlock.None;
            long pick = votePick ? ctx.VoteHand.DrawnPlayerId : nightPick ? ctx.NightPanel.ShownTarget : 0;
            string marks = (votePick ? "v" : nightPick ? "n" : "-") + tally.Count;
            if (pick == shownPick && marks == shownMarks) return;
            shownPick = pick;
            shownMarks = marks;
            foreach (Slot slot in slots)
            {
                slot.ring.SetActive(pick != 0 && slot.id == pick);
                slot.ringLine.color = nightPick ? Target : Pick;
                slot.group.alpha = nightPick && !ctx.NightPanel.CanTarget(slot.id) ? 0.45f : 1f;
                int votes;
                bool counted = phase == GamePhases.Execution && tally.TryGetValue(slot.id, out votes);
                slot.tally.SetActive(counted);
                if (counted) slot.tallyText.text = tally[slot.id].ToString();
            }
        }

        private ActionState StateFor(GameSession session)
        {
            var s = new ActionState { chip = "진행", title = lastNotice ?? string.Empty, sub = string.Empty };
            GameStateDto state = session.State;
            MyRoleDto me = session.Me;
            switch (phase)
            {
                case GamePhases.Day:
                {
                    s.chip = "낮 토론";
                    bool alive = me != null && GameStateQueries.IsAlive(state, me.playerId);
                    Split(GameScreenText.DayStatus(true, alive, session.SkippedToday, session.DaySkipProgress), ref s);
                    if (alive)
                    {
                        s.primary = session.SkippedToday ? "넘김 완료" : "토론 넘기기";
                        s.primaryOn = session.CanSkipDay;
                        if (session.DayExtendSupported) // 연장 API가 없는 서버면 한 번 눌러 본 뒤로는 버튼을 거둔다
                        {
                            s.secondary = GameScreenText.DayExtendButton(session.ExtendedToday);
                            s.secondaryOn = session.CanExtendDay;
                        }
                    }
                    break;
                }
                case GamePhases.Night:
                {
                    s.chip = "밤 능력";
                    if (me == null) break;
                    AbilityBlock block = session.NightAbility;
                    long shown = ctx.NightPanel != null ? ctx.NightPanel.ShownTarget : session.MyNightTarget;
                    int marks = session.KrakenMarks.Count;
                    Split(GameScreenText.NightStatus(block, me.actionCode, shown, session.SkippedTonight, state, session.StruckTonight, marks), ref s);
                    if (block != AbilityBlock.None) break;
                    bool picked = ctx.NightPanel != null && ctx.NightPanel.CanConfirm;
                    bool strike = ctx.NightPanel != null && ctx.NightPanel.CanStrike; // 크라켄: 대상 없이 누르면 발동
                    if (picked) s.sub = "오른쪽 버튼을 누르면 확정됩니다.";
                    else if (strike) s.sub = "명단에서 표식할 대상을 누르거나, [발동]으로 " + marks + "명을 끌고 갑니다.";
                    else if (shown == 0 && !session.SkippedTonight && !session.StruckTonight) s.sub = "아래 명단에서 대상을 누르세요.";
                    s.secondary = session.SkippedTonight ? "넘김" : "넘기기";
                    s.secondaryOn = !session.SkippedTonight;
                    s.primary = ctx.NightPanel != null ? ctx.NightPanel.ConfirmLabel
                        : ReportFormatter.ActionName(me.actionCode) + (session.MyNightTarget != 0 ? " 변경" : string.Empty);
                    s.primaryOn = picked || strike;
                    break;
                }
                case GamePhases.Vote:
                {
                    s.chip = "투표";
                    if (!session.CanVote)
                    {
                        s.title = session.VoteBannedToday ? ReportFormatter.VoteBannedMessage : "사망한 플레이어는 투표할 수 없습니다.";
                        break;
                    }
                    if (ctx.VoteHand == null || !ctx.VoteHand.IsShown)
                    {
                        s.title = "투표 시간입니다.";
                        break;
                    }
                    long drawn = ctx.VoteHand.DrawnPlayerId;
                    bool locked = ctx.VoteHand.IsLocked;
                    string name = drawn != 0 ? GameStateQueries.NicknameOf(state, drawn) : null;
                    s.title = name != null ? name + "님에게 투표" : "기권";
                    s.sub = locked ? "투표를 마쳤습니다. 결과를 기다리세요."
                        : name != null ? "다시 누르면 기권으로 돌아갑니다." : "처형할 사람을 아래 명단에서 고르세요 · 고르지 않으면 기권";
                    s.secondary = "기권";
                    s.secondaryOn = !locked && drawn != 0;
                    s.primary = locked ? "완료됨" : "투표 완료";
                    s.primaryOn = !locked;
                    break;
                }
                case GamePhases.NightResult:
                    s.chip = "밤 결과";
                    s.sub = "자세한 결과는 채팅에서 볼 수 있습니다.";
                    break;
                case GamePhases.Execution:
                    s.chip = "처형";
                    s.sub = voteSummary ?? string.Empty;
                    break;
                case GamePhases.Ended:
                    s.chip = "게임 종료";
                    break;
            }
            return s;
        }

        private static void Split(string status, ref ActionState s)
        {
            if (string.IsNullOrEmpty(status)) return;
            int line = status.IndexOf('\n');
            s.title = line < 0 ? status : status.Substring(0, line);
            s.sub = line < 0 ? string.Empty : status.Substring(line + 1);
        }

        private void ApplyAction(ActionState s)
        {
            string key = s.Key;
            if (key == shownAction) return;
            shownAction = key;
            SetText(actionChip, s.chip);
            SetText(actionTitle, s.title);
            SetText(actionSub, s.sub);
            SetButton(secondaryButton, s.secondary, s.secondaryOn);
            SetButton(primaryButton, s.primary, s.primaryOn);
            FitLabels();
            ReachButtons(actionTitle);
            ReachButtons(actionSub);
            if (titleRule != null && actionSub != null)
            {
                actionSub.ForceMeshUpdate();
                titleRule.SetActive(actionSub.textInfo.lineCount < 2);
            }
        }

        // The text's right edge goes to just before the leftmost button shown, wherever the scene put them, or to the line's
        // right end when there is none (a dead player's long title is not cut). Its left edge stays where the scene has it.
        private void ReachButtons(TMP_Text text)
        {
            if (text == null || !(text.rectTransform.parent is RectTransform line)) return;
            float right = line.rect.xMax - 24f;
            foreach (ActionButton b in new[] { secondaryButton, primaryButton })
            {
                if (b == null || b.rect == null || !b.rect.gameObject.activeSelf) continue;
                b.rect.GetWorldCorners(corners);
                right = Mathf.Min(right, line.InverseTransformPoint(corners[0]).x - 12f);
            }
            RectTransform r = text.rectTransform;
            r.GetWorldCorners(corners);
            float now = line.InverseTransformPoint(corners[2]).x;
            if (!Mathf.Approximately(now, right)) r.offsetMax += new Vector2(right - now, 0f);
        }

        private readonly Vector3[] corners = new Vector3[4];

        // The chip and the buttons as the scene makes them (measured once): their widths, the title's start after the chip
        // and the gap between the two buttons.
        private bool measured;
        private float chipWidth, primaryWidth, secondaryWidth, titleAfterChip, buttonGap;

        // The chip and the buttons keep the scene's widths, or grow when their text does not fit at its smallest size (it is
        // never cut or shrunk past reading): the chip to the right, pushing the title along; the buttons to the left, the
        // secondary keeping its gap left of the primary.
        private void FitLabels()
        {
            RectTransform line = actionTitle != null ? actionTitle.rectTransform.parent as RectTransform : null;
            RectTransform chip = actionChip != null ? actionChip.rectTransform.parent as RectTransform : null;
            bool hasPrimary = primaryButton != null && primaryButton.rect != null;
            bool hasSecondary = secondaryButton != null && secondaryButton.rect != null;
            if (line == null) return;
            if (!measured)
            {
                measured = true;
                if (chip != null) chipWidth = chip.rect.width;
                if (chip != null) titleAfterChip = Edges(actionTitle.rectTransform, line).x - Edges(chip, line).y;
                if (hasPrimary) primaryWidth = primaryButton.rect.rect.width;
                if (hasSecondary) secondaryWidth = secondaryButton.rect.rect.width;
                if (hasPrimary && hasSecondary) buttonGap = Edges(primaryButton.rect, line).x - Edges(secondaryButton.rect, line).y;
            }
            if (chip != null)
            {
                chip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(chipWidth, Needed(actionChip, chip)));
                float shift = Edges(chip, line).y + titleAfterChip - Edges(actionTitle.rectTransform, line).x;
                actionTitle.rectTransform.offsetMin += new Vector2(shift, 0f);
            }
            if (hasPrimary && primaryButton.label != null)
                primaryButton.rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(primaryWidth, Needed(primaryButton.label, primaryButton.rect)));
            if (hasSecondary && secondaryButton.label != null)
            {
                RectTransform b = secondaryButton.rect;
                b.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(secondaryWidth, Needed(secondaryButton.label, b)));
                if (hasPrimary)
                {
                    float shift = Edges(primaryButton.rect, line).x - buttonGap - Edges(b, line).y;
                    b.anchoredPosition += new Vector2(shift, 0f);
                }
            }
        }

        // How wide rect must be for its text at the text's smallest size (its insets kept).
        private static float Needed(TMP_Text text, RectTransform rect)
        {
            if (string.IsNullOrEmpty(text.text)) return 0f;
            bool auto = text.enableAutoSizing;
            float size = text.fontSize;
            text.enableAutoSizing = false;
            text.fontSize = auto ? text.fontSizeMin : size;
            float width = text.GetPreferredValues(text.text, float.PositiveInfinity, float.PositiveInfinity).x;
            text.fontSize = size;
            text.enableAutoSizing = auto;
            return width + rect.rect.width - text.rectTransform.rect.width + 2f;
        }

        // A rect's left and right edges (x, y) in line's space.
        private Vector2 Edges(RectTransform rect, RectTransform line)
        {
            rect.GetWorldCorners(corners);
            return new Vector2(line.InverseTransformPoint(corners[0]).x, line.InverseTransformPoint(corners[2]).x);
        }

        // A part taken out of the baked scene is simply not shown.
        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        private static void SetButton(ActionButton b, string label, bool on)
        {
            if (b == null || b.rect == null) return;
            b.rect.gameObject.SetActive(!string.IsNullOrEmpty(label));
            SetText(b.label, label ?? string.Empty);
            if (b.button != null) b.button.interactable = on;
        }

        private void OnPrimary()
        {
            switch (phase)
            {
                case GamePhases.Day:
                    if (ctx.DayPanel != null) ctx.DayPanel.Skip();
                    break;
                case GamePhases.Night:
                    if (ctx.NightPanel != null) ctx.NightPanel.Confirm();
                    break;
                case GamePhases.Vote:
                    if (ctx.VoteHand != null) ctx.VoteHand.Confirm();
                    break;
            }
        }

        private void OnSecondary()
        {
            switch (phase)
            {
                case GamePhases.Day:
                    if (ctx.DayPanel != null) ctx.DayPanel.Extend();
                    break;
                case GamePhases.Night:
                    if (ctx.NightPanel != null) ctx.NightPanel.Skip();
                    break;
                case GamePhases.Vote:
                    if (ctx.VoteHand != null && ctx.VoteHand.DrawnPlayerId != 0) ctx.VoteHand.Choose(0);
                    break;
            }
        }

        // A row this high at this top, stretched between these left and right insets.
        private static void Place(RectTransform rect, float left, float top, float right, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
