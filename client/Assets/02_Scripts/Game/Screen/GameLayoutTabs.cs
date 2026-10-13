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
    /// </summary>
    public sealed class GameLayoutTabs : GameLayoutBody
    {
        private const float StripHeight = 236f, ActionHeight = 140f, InputBar = 140f, Gap = 8f;
        private const float ActionBottom = StripHeight + Gap, ChatBottom = ActionBottom + ActionHeight + Gap;
        private const float DeckAspect = 1672f / 941f; // DayTable/Deck, raised by RoundTableLayout.deckRaise so its sea is behind the HUD
        private const float SecondaryWidth = 170f, PrimaryWidth = 220f, ButtonHeight = 96f;
        private static readonly Color Pick = new Color(1f, 0.82f, 0.3f), Target = new Color(0.92f, 0.3f, 0.24f);
        private static readonly Color Backdrop = new Color(0.05f, 0.04f, 0.03f, 1f);
        private static readonly Color ChatFill = new Color(0.09f, 0.067f, 0.043f, 1f);
        private static readonly Color ActionFill = new Color(0.06f, 0.15f, 0.17f, 1f);

        private sealed class Slot
        {
            public long id;
            public CanvasGroup group;
            public GameObject ring, tally;
            public Image ringLine;
            public TextMeshProUGUI tallyText;
        }

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
        private float laidOutWidth = -1f, laidOutRaise = float.NaN;

        public override bool PlaysDayTransitions => true;

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

        private void Create(bool bake)
        {
            backdrop = BuildBackdrop(ctx.Canvas);
            if (ctx.Chat != null) backdrop.SetSiblingIndex(ctx.Chat.GetSiblingIndex()); // under the chat log

            RectTransform chatBox = NewRect("ChatBox", backdrop);
            Stretch(chatBox, 12f, 10f, 12f, ChatBottom);
            HudShapes.Panel(chatBox, ChatFill, GoldLine, 14f);

            BuildAction(backdrop);
            BuildStrip(backdrop);
            BuildRoleChip();

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
            if (primaryButton != null && primaryButton.button != null) primaryButton.button.onClick.AddListener(OnPrimary);
            if (secondaryButton != null && secondaryButton.button != null) secondaryButton.button.onClick.AddListener(OnSecondary);
            BuildChat(ChatArea);
            if (chatUi != null) chatUi.AddTabRow(); // the tabs and filter at the log's top (made at run time, baked too)
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
        }

        // The chat log between the stage and the input bar (left, top from the top, right, bottom from the bottom).
        private static Vector4 ChatArea()
        {
            return new Vector4(28f, GameHudMetrics.StageBottom + 20f, 28f, ChatBottom + InputBar + 6f);
        }

        /// <summary>
        /// Everything under the stage lies on one dark backdrop with a gold rule on top (the raised deck picture does not
        /// reach the bottom). Stretched over parent from the stage's bottom down. The Edit mode preview uses it too.
        /// </summary>
        public static RectTransform BuildBackdrop(RectTransform parent)
        {
            RectTransform backdrop = NewRect("BottomBackdrop", parent);
            Stretch(backdrop, 0f, GameHudMetrics.StageBottom, 0f, 0f);
            AddImage(backdrop, null, Backdrop);
            RectTransform rule = NewRect("Rule", backdrop);
            rule.anchorMin = new Vector2(0f, 1f);
            rule.anchorMax = new Vector2(1f, 1f);
            rule.pivot = new Vector2(0.5f, 1f);
            rule.sizeDelta = new Vector2(0f, 3f);
            AddImage(rule, null, GoldLine);
            return backdrop;
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
            Transform notice = ctx.Canvas.Find("Notice");
            RectTransform chip = Box(ctx.Canvas, "RoleChip", 16f, GameHudMetrics.HudHeight + 10f, 206f, 66f); // clear of the far seats' names
            if (notice != null) chip.SetSiblingIndex(notice.GetSiblingIndex()); // under the notice
            HudShapes.Panel(chip, Teal, GoldLine, 33f).raycastTarget = true;
            roleChip = chip.gameObject.AddComponent<Button>(); // Wire listens to it
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

        private void BuildAction(RectTransform backdrop)
        {
            RectTransform box = NewRect("ActionLine", backdrop);
            box.anchorMin = new Vector2(0f, 0f);
            box.anchorMax = new Vector2(1f, 0f);
            box.pivot = new Vector2(0.5f, 0f);
            box.offsetMin = new Vector2(12f, ActionBottom);
            box.offsetMax = new Vector2(-12f, ActionBottom + ActionHeight);
            HudShapes.Panel(box, ActionFill, GoldLine, 14f);

            RectTransform chip = Box(box, "Chip", 18f, 16f, 150f, 42f);
            HudShapes.Panel(chip, Parchment, GoldLine, 8f);
            actionChip = NewText(chip, "Text", 23f, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(actionChip.rectTransform, 6f, 2f, 6f, 2f);
            actionChip.enableAutoSizing = true;
            actionChip.fontSizeMin = 14f;
            actionChip.fontSizeMax = 23f;
            float textRight = SecondaryWidth + PrimaryWidth + 44f;
            actionTitle = NewText(box, "Title", 31f, Cream, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Place(actionTitle.rectTransform, 182f, 12f, textRight, 50f);
            actionTitle.enableAutoSizing = true;
            actionTitle.fontSizeMin = 18f;
            actionTitle.fontSizeMax = 31f;
            actionSub = NewText(box, "Sub", 22f, Muted, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            Place(actionSub.rectTransform, 18f, 68f, textRight, 62f);
            actionSub.textWrappingMode = TextWrappingModes.Normal;
            actionSub.enableAutoSizing = true;
            actionSub.fontSizeMin = 15f;
            actionSub.fontSizeMax = 22f;

            primaryButton = NewButton(box, "Primary", 16f, PrimaryWidth, true); // Wire listens to them
            secondaryButton = NewButton(box, "Secondary", 16f + PrimaryWidth + 12f, SecondaryWidth, false);
        }

        private ActionButton NewButton(RectTransform parent, string name, float right, float width, bool primary)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-right, 0f);
            rect.sizeDelta = new Vector2(width, ButtonHeight);
            Button button = TextButton(rect, string.Empty, primary, null);
            return new ActionButton { rect = rect, button = button, label = rect.Find("Label").GetComponent<TextMeshProUGUI>() };
        }

        private void BuildStrip(RectTransform backdrop)
        {
            strip = NewRect("PlayerStrip", backdrop);
            strip.anchorMin = new Vector2(0f, 0f);
            strip.anchorMax = new Vector2(1f, 0f);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.sizeDelta = new Vector2(0f, StripHeight);
            AddImage(strip, null, new Color(0.04f, 0.11f, 0.13f, 1f));
            RectTransform line = NewRect("Rule", strip);
            line.anchorMin = new Vector2(0f, 1f);
            line.anchorMax = new Vector2(1f, 1f);
            line.pivot = new Vector2(0.5f, 1f);
            line.sizeDelta = new Vector2(0f, 3f);
            AddImage(line, null, GoldLine);
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
            string key = (session.Me.roleName ?? "") + "|" + (bust != null ? bust.name : "");
            if (key == chipKey) return;
            chipKey = key;
            if (chipText != null) chipText.text = session.Me.roleName ?? "내 역할";
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
            float slotWidth = Mathf.Min(140f, (stripWidth - 16f) / count);
            float size = Mathf.Min(112f, slotWidth - 18f);
            float left = (stripWidth - slotWidth * count) * 0.5f;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerViewDto p = players[i];
                RectTransform root = Box(strip, "Player" + p.playerId, left + i * slotWidth, 22f, slotWidth, size + 52f);
                AddImage(root, null, new Color(0f, 0f, 0f, 0f)).raycastTarget = true;
                long id = p.playerId;
                root.gameObject.AddComponent<Button>().onClick.AddListener(() => OnSlotClicked(id));
                var slot = new Slot { id = id, group = root.gameObject.AddComponent<CanvasGroup>() };
                float x = (slotWidth - size) * 0.5f;
                Avatar(root, "Avatar", x, 4f, size, p.playerId, p.nickname);
                RectTransform ring = Box(root, "Pick", x - 6f, -2f, size + 12f, size + 12f);
                HudShapes.Panel(ring, new Color(0f, 0f, 0f, 0f), Pick, (size + 12f) * 0.5f);
                slot.ring = ring.gameObject;
                slot.ringLine = ring.Find("Outline").GetComponent<Image>();
                slot.ringLine.pixelsPerUnitMultiplier *= 0.55f; // a thicker line than the avatar's own ring
                ring.gameObject.SetActive(false);
                TextMeshProUGUI name = NewText(root, "Name", 24f, p.playerId == myId ? Gold : Cream, FontStyles.Bold, TextAlignmentOptions.Center);
                RectTransform nameRect = name.rectTransform;
                nameRect.anchorMin = new Vector2(0f, 0f);
                nameRect.anchorMax = new Vector2(1f, 0f);
                nameRect.pivot = new Vector2(0.5f, 0f);
                nameRect.sizeDelta = new Vector2(-4f, 34f);
                name.enableAutoSizing = true;
                name.fontSizeMin = 14f;
                name.fontSizeMax = 24f;
                name.text = p.nickname;
                if (!p.alive)
                {
                    AddImage(Box(root, "Dead", x + size * 0.18f, 4f + size * 0.18f, size * 0.64f, size * 0.64f), Icon("clear"), Target).preserveAspect = true;
                    name.color = Muted;
                }
                if (session.Me != null && session.Me.mafiaTeammateIds.Contains(p.playerId))
                {
                    RectTransform mark = Box(root, "Mate", x + size - 28f, -4f, 36f, 36f);
                    HudShapes.Panel(mark, new Color(0.16f, 0.11f, 0.06f), GoldLine, 18f);
                    AddImage(Box(mark, "Skull", 7f, 7f, 22f, 22f), Icon("skull"), Cream).preserveAspect = true;
                }
                RectTransform badge = Box(root, "Tally", x + size - 30f, size - 26f, 40f, 40f);
                HudShapes.Panel(badge, new Color(0.78f, 0.22f, 0.18f), Cream, 20f);
                slot.tallyText = NewText(badge, "Count", 24f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
                Stretch(slot.tallyText.rectTransform, 0f, 0f, 0f, 0f);
                slot.tally = badge.gameObject;
                badge.gameObject.SetActive(false);
                slots.Add(slot);
            }
            shownPick = -1;
            shownMarks = null;
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

            GameSession session = ctx.Session();
            if (session == null || session.State == null) return;
            if (phase == null) phase = session.State.phase;
            UpdateMarks(session);
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
                    }
                    break;
                }
                case GamePhases.Night:
                {
                    s.chip = "밤 능력";
                    if (me == null) break;
                    AbilityBlock block = session.NightAbility;
                    long shown = ctx.NightPanel != null ? ctx.NightPanel.ShownTarget : session.MyNightTarget;
                    Split(GameScreenText.NightStatus(block, me.actionCode, shown, session.SkippedTonight, state), ref s);
                    if (block != AbilityBlock.None) break;
                    bool picked = ctx.NightPanel != null && ctx.NightPanel.CanConfirm;
                    if (picked) s.sub = "오른쪽 버튼을 누르면 확정됩니다.";
                    else if (shown == 0 && !session.SkippedTonight) s.sub = "아래 명단에서 대상을 누르세요.";
                    s.secondary = session.SkippedTonight ? "넘김" : "넘기기";
                    s.secondaryOn = !session.SkippedTonight;
                    s.primary = ReportFormatter.ActionName(me.actionCode) + (session.MyNightTarget != 0 ? " 변경" : string.Empty);
                    s.primaryOn = picked;
                    break;
                }
                case GamePhases.Vote:
                {
                    s.chip = "투표";
                    if (!session.CanVote)
                    {
                        s.title = "사망한 플레이어는 투표할 수 없습니다.";
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
