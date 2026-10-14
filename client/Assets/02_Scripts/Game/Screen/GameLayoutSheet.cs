using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.GameUI;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// GameScene4 (C, 끌어 올리는 시트): phase steps (낮 · 투표 · 처형 · 밤) and my role chip under the top HUD, a bigger
    /// stage, and a bottom sheet holding 채팅 | 능력.
    /// - The sheet rests low (the last two lines and the input bar) so the stage shows whole; drag or tap its grip to
    ///   raise it half way or almost to the top, and it snaps to the nearest of the three.
    /// - 능력 is the drawer's night ability / day skip panel inside the sheet; GameScreen opening the drawer at night or
    ///   by day switches to it and raises the sheet half way.
    /// - The vote's card hand rises from the screen's bottom over the resting sheet.
    /// - The role chip opens my role in the HUD's window.
    /// </summary>
    public sealed class GameLayoutSheet : GameLayoutBody
    {
        private const float StepsTop = 244f, StepsHeight = 52f, ChipTop = 306f, ChipHeight = 62f;
        private const float Grip = 46f, SegmentHeight = 64f, ContentTop = Grip + SegmentHeight + 8f;
        private const float InputBar = 140f, BarBottom = 14f, Below = 30f; // the sheet reaches this far under the screen
        private const float Peek = 384f;
        private static readonly string[] StepNames = { "낮", "투표", "처형", "밤" };

        private RectTransform sheet, abilityView, lift, chipAvatar;
        private CanvasGroup liftGroup;
        private readonly Image[] stepFills = new Image[4], stepLines = new Image[4];
        private readonly TextMeshProUGUI[] stepLabels = new TextMeshProUGUI[4];
        private Image chatTab, abilityTab;
        private TextMeshProUGUI chatTabLabel, abilityTabLabel, chipText, abilityNote;
        private float height = Peek, target = Peek;
        private bool dragging, abilityShown;
        private string chipKey;

        // Half way: right under the stage, so the characters stay in sight (the ability opens here).
        private float Half => Mathf.Max(Peek, CanvasHeight - GameHudMetrics.StageBottom + 10f);
        private float Full => Mathf.Max(Half, CanvasHeight - 380f); // over the stage, up to the phase steps and the role chip

        protected override void OnBuild()
        {
            Transform notice = ctx.Canvas.Find("NoticeBubble") ?? ctx.Canvas.Find("Notice"); // the notice box
            int top = notice != null ? notice.GetSiblingIndex() : ctx.Canvas.childCount; // under the notice

            RectTransform steps = Box(ctx.Canvas, "PhaseSteps", 24f, StepsTop, 1032f, StepsHeight);
            steps.SetSiblingIndex(top);
            float stepWidth = (1032f - 3f * 8f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                RectTransform step = Box(steps, "Step" + i, i * (stepWidth + 8f), 0f, stepWidth, StepsHeight);
                stepFills[i] = HudShapes.Panel(step, Teal, GoldLine, 10f);
                stepLines[i] = step.Find("Outline").GetComponent<Image>();
                stepLabels[i] = NewText(step, "Label", 26f, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
                Stretch(stepLabels[i].rectTransform, 4f, 2f, 4f, 2f);
                stepLabels[i].text = StepNames[i];
            }

            RectTransform chip = Box(ctx.Canvas, "RoleChip", 24f, ChipTop, 360f, ChipHeight);
            chip.SetSiblingIndex(steps.GetSiblingIndex() + 1);
            HudShapes.Panel(chip, Teal, GoldLine, 12f).raycastTarget = true;
            chip.gameObject.AddClickSoundButton().onClick.AddListener(ShowRoleWindow);
            chipAvatar = Box(chip, "Avatar", 0f, 0f, ChipHeight, ChipHeight);
            chipText = NewText(chip, "Text", 26f, Cream, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stretch(chipText.rectTransform, ChipHeight + 12f, 4f, 44f, 4f);
            chipText.enableAutoSizing = true;
            chipText.fontSizeMin = 16f;
            chipText.fontSizeMax = 26f;
            chipText.text = "내 역할";
            Image more = AddImage(Box(chip, "More", 0f, 0f, 26f, 26f), Icon("arrow_next"), Gold);
            more.rectTransform.anchorMin = more.rectTransform.anchorMax = more.rectTransform.pivot = new Vector2(1f, 0.5f);
            more.rectTransform.anchoredPosition = new Vector2(-12f, 0f);
            more.preserveAspect = true;

            // The sheet lies under the chat log (right before it); the input bar's lift goes over it.
            sheet = NewRect("BottomSheetPanel", ctx.Canvas);
            if (ctx.Chat != null) sheet.SetSiblingIndex(ctx.Chat.GetSiblingIndex());
            sheet.anchorMin = new Vector2(0f, 0f);
            sheet.anchorMax = new Vector2(1f, 0f);
            sheet.pivot = new Vector2(0.5f, 0f);
            sheet.anchoredPosition = new Vector2(0f, -Below);
            HudShapes.Panel(sheet, new Color(0.05f, 0.06f, 0.07f, 1f), GoldLine, 30f).raycastTarget = true; // taps on the sheet stay in it

            RectTransform grip = NewRect("Grip", sheet);
            grip.anchorMin = new Vector2(0f, 1f);
            grip.anchorMax = new Vector2(1f, 1f);
            grip.pivot = new Vector2(0.5f, 1f);
            grip.sizeDelta = new Vector2(0f, Grip);
            AddImage(grip, null, new Color(0f, 0f, 0f, 0f)).raycastTarget = true;
            HudDragHandle handle = grip.gameObject.AddComponent<HudDragHandle>();
            handle.Dragged += OnGripDragged;
            handle.DragEnded += OnGripReleased;
            handle.Tapped += OnGripTapped;
            RectTransform bar = NewRect("Bar", grip);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.sizeDelta = new Vector2(110f, 10f);
            HudShapes.Panel(bar, new Color(GoldLine.r, GoldLine.g, GoldLine.b, 0.8f), new Color(0f, 0f, 0f, 0f), 5f);

            chatTab = Segment("ChatTab", -1, "채팅", out chatTabLabel, () => ShowAbility(false));
            abilityTab = Segment("AbilityTab", 1, "능력", out abilityTabLabel, () => ShowAbility(true));

            abilityView = NewRect("AbilityView", sheet);
            Stretch(abilityView, 12f, ContentTop, 12f, Below + 12f);
            foreach (MonoBehaviour drawerPanel in new MonoBehaviour[] { ctx.NightPanel, ctx.DayPanel })
            {
                if (drawerPanel == null) continue;
                var rect = (RectTransform)drawerPanel.transform;
                rect.SetParent(abilityView, false);
                Stretch(rect, 0f, 0f, 0f, 0f);
            }
            abilityNote = NewText(abilityView, "Note", 28f, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
            Stretch(abilityNote.rectTransform, 20f, 20f, 20f, 20f);
            abilityNote.text = "지금은 쓸 능력이 없습니다.";

            BuildChat(() => new Vector4(30f, CanvasHeight - height + ContentTop, 30f, BarBottom + InputBar + 4f));
            if (ctx.Sheet != null)
            {
                lift = LiftSheet(8f, BarBottom, 8f);
                liftGroup = lift.gameObject.AddComponent<CanvasGroup>();
                ctx.BottomTab.SetDrawerHeight(0f); // the sheet replaces the drawer: the bottom sheet is the input bar only
            }
            if (ctx.VoteHand != null) ctx.VoteHand.SetBottomAnchor(null); // the hand rises from the screen's bottom

            // Raised all the way, the sheet goes over the stage: draw it, the log and the input bar after the day table
            // and the night room.
            Transform table = ctx.Canvas.Find("DayRoundTable"), room = ctx.Canvas.Find("NightRoom");
            Transform stageTop = table == null ? room : room == null ? table : table.GetSiblingIndex() > room.GetSiblingIndex() ? table : room;
            if (stageTop != null)
            {
                PlaceAfter(sheet, stageTop);
                if (ctx.Chat != null) PlaceAfter(ctx.Chat, sheet);
                if (lift != null) PlaceAfter(lift, ctx.Chat != null ? ctx.Chat : sheet);
            }
            ShowAbility(false);
        }

        private static void PlaceAfter(Transform item, Transform reference)
        {
            int at = reference.GetSiblingIndex();
            item.SetSiblingIndex(item.GetSiblingIndex() < at ? at : at + 1);
        }

        private Image Segment(string name, int side, string text, out TextMeshProUGUI label, System.Action onClick)
        {
            RectTransform rect = NewRect(name, sheet);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(side < 0 ? 1f : 0f, 1f);
            rect.anchoredPosition = new Vector2(side * 6f, -Grip);
            rect.sizeDelta = new Vector2(240f, SegmentHeight - 8f);
            Image fill = HudShapes.Panel(rect, Teal, GoldLine, 12f);
            fill.raycastTarget = true;
            label = NewText(rect, "Label", 28f, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, 4f, 2f, 4f, 2f);
            label.text = text;
            rect.gameObject.AddClickSoundButton().onClick.AddListener(() => onClick());
            return fill;
        }

        // 능력 is the drawer: open it for the ability view (and raise the sheet), close it for the chat.
        private void ShowAbility(bool show)
        {
            abilityShown = show;
            if (ctx.BottomTab != null)
            {
                if (show && ctx.BottomTab.Mode != BottomTabMode.Vote) ctx.BottomTab.OpenVote();
                else if (!show && ctx.BottomTab.Mode == BottomTabMode.Vote) ctx.BottomTab.Close();
            }
            if (show && target < Half) target = Half;
            abilityView.gameObject.SetActive(show);
            if (chatUi != null) chatUi.SetLogVisible(!show);
            if (liftGroup != null)
            {
                liftGroup.alpha = show ? 0f : 1f;
                liftGroup.blocksRaycasts = !show;
            }
            chatTab.color = show ? Teal : Parchment;
            chatTabLabel.color = show ? Cream : Ink;
            abilityTab.color = show ? Parchment : Teal;
            abilityTabLabel.color = show ? Ink : Cream;
        }

        public override void OnDrawerMode(BottomTabMode mode)
        {
            if (mode == BottomTabMode.Vote && !abilityShown) ShowAbility(true);
            else if (mode == BottomTabMode.Closed && abilityShown) ShowAbility(false);
        }

        public override void OnPhase(string phase)
        {
            int current = phase == GamePhases.Day ? 0 : phase == GamePhases.Vote ? 1 : phase == GamePhases.Execution ? 2
                : phase == GamePhases.Night || phase == GamePhases.NightResult ? 3 : -1;
            for (int i = 0; i < 4; i++)
            {
                bool now = i == current;
                stepFills[i].color = now ? Parchment : Teal;
                stepLines[i].color = now ? Gold : GoldLine;
                stepLabels[i].color = now ? Ink : Muted;
            }
            if (phase == GamePhases.Vote) target = Peek; // the card hand rises over the resting sheet
        }

        public override void Refresh(GameSession session)
        {
            if (session == null || session.Me == null) return;
            Sprite bust = BustOf(session.Me.playerId);
            string key = (session.Me.roleName ?? "") + "|" + (bust != null ? bust.name : "");
            if (key == chipKey) return;
            chipKey = key;
            chipText.text = "내 역할 · " + (session.Me.roleName ?? "?");
            for (int i = chipAvatar.childCount - 1; i >= 0; i--) Destroy(chipAvatar.GetChild(i).gameObject);
            Avatar(chipAvatar, "Me", 6f, 6f, ChipHeight - 12f, session.Me.playerId,
                session.State != null ? GameStateQueries.NicknameOf(session.State, session.Me.playerId) : null);
        }

        private void OnGripDragged(float dy)
        {
            dragging = true;
            height = Mathf.Clamp(height + dy, Peek, Full);
            target = height;
        }

        private void OnGripReleased()
        {
            dragging = false;
            float best = Peek;
            foreach (float stop in new[] { Half, Full }) if (Mathf.Abs(height - stop) < Mathf.Abs(height - best)) best = stop;
            target = best;
        }

        private void OnGripTapped()
        {
            target = target < Half - 1f ? Half : target < Full - 1f ? Full : Peek;
        }

        private void LateUpdate()
        {
            if (ctx == null) return;
            target = Mathf.Clamp(target, Peek, Full);
            if (!dragging) height = Mathf.Lerp(height, target, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            sheet.sizeDelta = new Vector2(0f, height + Below);
            if (abilityShown)
            {
                bool panelShown = (ctx.NightPanel != null && ctx.NightPanel.gameObject.activeSelf)
                    || (ctx.DayPanel != null && ctx.DayPanel.gameObject.activeSelf);
                abilityNote.gameObject.SetActive(!panelShown);
            }
        }
    }
}
