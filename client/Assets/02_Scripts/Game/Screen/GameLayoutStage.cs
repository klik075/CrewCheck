using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.GameUI;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// GameScene2 (A, 무대 중심): a bigger stage under the top HUD; the rest floats over the deck.
    /// - Role badge at the top left (my bust and role): tap for my role in the HUD's window.
    /// - Chat: a see-through panel over the deck showing the last lines; its header expands it up to the stage's
    ///   bottom edge (or folds it back). The input bar is at the bottom.
    /// - Ability button at the bottom right opens the drawer (night ability, day skip). Opening, the drawer takes the
    ///   whole width and the chat and the button fade out under it; it reaches up to the stage's bottom edge at most.
    /// </summary>
    public sealed class GameLayoutStage : GameLayoutBody
    {
        private const float BarBottom = 16f, InputBar = 140f, ButtonSpace = 186f;
        private const float PanelBottom = BarBottom + InputBar + 8f; // the chat panel's bottom, from the canvas bottom
        private const float PanelHeader = 50f, PeekHeight = PanelHeader + 3f * 58f + 12f; // folded: about three lines

        private RectTransform lift, panel;
        private CanvasGroup panelGroup, buttonGroup;
        private Image badgeBust, tabBar;
        private TextMeshProUGUI badgeRole, foldLabel;
        private Image foldIcon;
        private bool expanded, shownExpanded = true;
        private float panelTop = -1f, laidOutHeight = -1f;

        protected override void OnBuild()
        {
            // The chat panel lies under the chat log (right before it), over the deck.
            panel = NewRect("ChatPanel", ctx.Canvas);
            if (ctx.Chat != null) panel.SetSiblingIndex(ctx.Chat.GetSiblingIndex());
            Stretch(panel, 20f, 0f, 20f, PanelBottom);
            panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            HudShapes.Panel(panel, new Color(0.03f, 0.04f, 0.05f, 0.62f), new Color(GoldLine.r, GoldLine.g, GoldLine.b, 0.55f), 18f);
            RectTransform header = NewRect("Header", panel);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, PanelHeader);
            AddImage(header, null, new Color(0f, 0f, 0f, 0f)).raycastTarget = true;
            header.gameObject.AddClickSoundButton().onClick.AddListener(() => expanded = !expanded);
            AddImage(Box(header, "Icon", 18f, 12f, 26f, 26f), Icon("chat_1"), Gold).preserveAspect = true;
            TextMeshProUGUI title = NewText(header, "Title", 22f, Gold, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stretch(title.rectTransform, 54f, 0f, 200f, 0f);
            title.text = "채팅";
            foldLabel = NewText(header, "Fold", 20f, Cream, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            Stretch(foldLabel.rectTransform, 600f, 0f, 52f, 0f);
            foldIcon = AddImage(Box(header, "FoldIcon", 0f, 13f, 24f, 24f), Icon("arrow_up"), Cream);
            foldIcon.rectTransform.anchorMin = foldIcon.rectTransform.anchorMax = foldIcon.rectTransform.pivot = new Vector2(1f, 1f);
            foldIcon.rectTransform.anchoredPosition = new Vector2(-20f, -13f);
            foldIcon.preserveAspect = true;

            BuildChat(() => new Vector4(34f, panelTop + PanelHeader, 34f, PanelBottom + 6f));
            if (ctx.Sheet != null)
            {
                lift = LiftSheet(0f, BarBottom, ButtonSpace);
                tabBar = TabBarImage();
            }

            // Role badge, over the stage's top left (under the sheet and the windows).
            RectTransform badge = NewRect("RoleBadge", ctx.Canvas);
            if (lift != null) badge.SetSiblingIndex(lift.GetSiblingIndex());
            badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0f, 1f);
            badge.anchoredPosition = new Vector2(18f, -250f);
            badge.sizeDelta = new Vector2(112f, 150f);
            AddImage(badge, null, new Color(0f, 0f, 0f, 0f)).raycastTarget = true;
            badge.gameObject.AddClickSoundButton().onClick.AddListener(ShowRoleWindow);
            RectTransform round = Box(badge, "Portrait", 6f, 0f, 100f, 100f);
            AddImage(round, NightFxSprites.Circle, new Color(0.09f, 0.11f, 0.15f));
            round.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            RectTransform face = NewRect("Bust", round);
            Stretch(face, -8f, -3f, -8f, -12f);
            badgeBust = AddImage(face, null, Color.white);
            badgeBust.preserveAspect = true;
            badgeBust.enabled = false;
            HudShapes.Panel(Box(badge, "Ring", 3f, -3f, 106f, 106f), new Color(0f, 0f, 0f, 0f), Gold, 53f);
            RectTransform chip = Box(badge, "Role", 0f, 104f, 112f, 40f);
            HudShapes.Panel(chip, Teal, GoldLine, 10f);
            badgeRole = NewText(chip, "Text", 22f, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(badgeRole.rectTransform, 6f, 2f, 6f, 2f);
            badgeRole.enableAutoSizing = true;
            badgeRole.fontSizeMin = 14f;
            badgeRole.fontSizeMax = 22f;
            badgeRole.text = "내 역할";

            // Ability button, bottom right beside the input bar.
            RectTransform button = BoxBottomRight(ctx.Canvas, "AbilityButton", 22f, BarBottom + 2f, 150f, 150f);
            if (lift != null) button.SetSiblingIndex(lift.GetSiblingIndex() + 1);
            buttonGroup = button.gameObject.AddComponent<CanvasGroup>();
            HudShapes.Panel(button, new Color(0.08f, 0.2f, 0.22f, 0.97f), Gold, 75f).raycastTarget = true;
            button.gameObject.AddClickSoundButton().onClick.AddListener(ToggleDrawer);
            AddImage(Box(button, "Icon", 45f, 24f, 60f, 60f), Icon("energy"), Gold).preserveAspect = true;
            TextMeshProUGUI label = NewText(button, "Label", 26f, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, 18f);
            labelRect.sizeDelta = new Vector2(120f, 36f);
            label.text = "능력";
        }

        public override void Refresh(GameSession session)
        {
            if (session == null || session.Me == null) return;
            badgeRole.text = session.Me.roleName ?? "내 역할";
            Sprite bust = BustOf(session.Me.playerId);
            badgeBust.sprite = bust;
            badgeBust.enabled = bust != null;
        }

        private void ToggleDrawer()
        {
            if (ctx.BottomTab == null) return;
            if (ctx.BottomTab.Mode == BottomTabMode.Vote) ctx.BottomTab.Close();
            else ctx.BottomTab.OpenVote();
        }

        private void LateUpdate()
        {
            if (ctx == null) return;
            float height = CanvasHeight;
            float open = 0f;
            if (ctx.BottomTab != null && lift != null)
            {
                if (!Mathf.Approximately(height, laidOutHeight))
                {
                    laidOutHeight = height;
                    ctx.BottomTab.SetDrawerHeight(Mathf.Clamp(height - GameHudMetrics.StageBottom - InputBar - BarBottom - 8f,
                        GameHudMetrics.MinDrawerHeight, GameHudMetrics.MaxDrawerHeight));
                }
                float drawer = ctx.BottomTab.DrawerHeight;
                open = drawer > 0f ? Mathf.Clamp01(1f + ctx.Sheet.anchoredPosition.y / drawer) : 0f;
                // Open, the drawer takes the whole width; the button and the chat fade out under it.
                lift.offsetMax = new Vector2(-ButtonSpace * (1f - open), 0f);
                if (tabBar != null) tabBar.color = new Color(DrawerBack.r, DrawerBack.g, DrawerBack.b, DrawerBack.a * open);
            }
            buttonGroup.alpha = 1f - open;
            buttonGroup.blocksRaycasts = open < 0.5f;
            panelGroup.alpha = 1f - open;
            panelGroup.blocksRaycasts = open < 0.5f;
            if (chatUi != null) chatUi.SetLogVisible(open < 0.5f);

            // Folded: the last lines over the deck. Expanded: up to the stage's bottom edge.
            float target = expanded ? GameHudMetrics.StageBottom + 16f : height - PanelBottom - PeekHeight;
            panelTop = panelTop < 0f ? target : Mathf.Lerp(panelTop, target, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            panel.offsetMax = new Vector2(-20f, -panelTop);
            if (shownExpanded != expanded)
            {
                shownExpanded = expanded;
                foldLabel.text = expanded ? "접기" : "펼치기";
                foldIcon.sprite = Icon(expanded ? "arrow_down" : "arrow_up");
            }
        }
    }
}
