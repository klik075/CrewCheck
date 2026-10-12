using UnityEngine;

namespace WhoisntCitizen.GameUI
{
    /// <summary>How the game screen is laid out (GameScreen's choice, one per scene).</summary>
    public enum HudLayout
    {
        Frame, // GameScene: stage, then the gold chat frame and the role panel under it
        Stage, // GameScene2 (A): a bigger stage; chat, ability button and role badge float over the deck
        Tabs,  // GameScene3 (B): the snug table on the deck, the chat always shown, one action line, the player strip
        Sheet, // GameScene4 (C): phase steps on top, the stage, and a bottom sheet dragged up for chat or abilities
    }

    /// <summary>
    /// Where the parts of the game screen sit, in canvas units (the canvas is 1080 wide; taller screens give the chat
    /// the extra height). From the top: the HUD (phase, timer, buttons), the stage (day table or night room), then the
    /// chat log. From the bottom: the role panel and, above it, the chat input bar.
    /// The notice scroll is not part of the column: it shows over the top of the stage for a moment and goes away.
    /// The bottom drawer (night ability, day skip, chat keyboard) opens over the chat and the role panel, never higher
    /// than the stage's bottom edge, so nothing covers the characters.
    /// The stage depends on the layout (<see cref="Use"/>, before the table and the night room are made); the rest of
    /// the constants are the Frame layout's. <see cref="WhoisntCitizen.Game.GameHudLayout"/> lays the screen out with these.
    /// </summary>
    public static class GameHudMetrics
    {
        public const float HudHeight = 236f;
        public const float NoticeHeight = 176f; // over the stage's top for a moment ("공지" tag above it)
        public static float NoticeTop { get; private set; } = 256f; // the Sheet layout keeps its phase steps and role chip clear
        /// <summary>The table and the night room are laid out for this stage height and scaled to fit the stage.</summary>
        public const float DesignStageHeight = 850f;

        public static HudLayout Layout { get; private set; } = HudLayout.Frame;
        public static float StageTop { get; private set; } = 240f;
        public static float StageHeight { get; private set; } = 734f;
        public static float StageBottom => StageTop + StageHeight;
        public static float StageScale => StageHeight / DesignStageHeight;

        /// <summary>Picks the stage of this layout. The table's seats reach the screen's sides at a scale of about 1.05.</summary>
        public static void Use(HudLayout layout)
        {
            Layout = layout;
            NoticeTop = layout == HudLayout.Sheet ? 392f : 256f;
            switch (layout)
            {
                case HudLayout.Stage: StageTop = 236f; StageHeight = 850f; break;
                case HudLayout.Tabs: StageTop = RoundTableLayout.Current.stageTop; StageHeight = Mathf.Max(100f, RoundTableLayout.Current.stageHeight); break;
                case HudLayout.Sheet: StageTop = 380f; StageHeight = 880f; break;
                default: StageTop = 240f; StageHeight = 734f; break;
            }
        }

        // ---- Frame layout
        public const float ChatTop = 978f;            // the chat frame (just under the stage), down to the input bar
        public const float RolePanelHeight = 252f;    // at the bottom
        public const float InputBarHeight = 140f;     // the bottom sheet's tab bar, right above the role panel
        public const float ChatBottom = RolePanelHeight + InputBarHeight; // from the bottom
        /// <summary>The drawer's panels are laid out for this height; a shorter screen gives them less (they scroll).</summary>
        public const float MaxDrawerHeight = 800f, MinDrawerHeight = 360f;
    }
}
