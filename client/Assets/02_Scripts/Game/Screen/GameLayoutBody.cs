using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.GameUI;
using WhoisntCitizen.Vote;

namespace WhoisntCitizen.Game
{
    /// <summary>What a layout body works with: the scene's parts that GameScreen holds.</summary>
    public sealed class GameLayoutContext
    {
        public RectTransform Canvas;
        public TMP_FontAsset Font;
        public GameHudLayout Hud;                // top HUD, notice, popup, busts
        public RectTransform Chat;               // the chat log panel (ChatLogView)
        public RectTransform Sheet;              // the bottom sheet: tab bar (input) + drawer
        public BottomTabController BottomTab;
        public NightActionPanel NightPanel;      // in the drawer; GameScreen shows it at night when the drawer is open
        public DaySkipPanel DayPanel;            // in the drawer; shown by day
        public VoteCardHandController VoteHand;
        public Func<long, ChibiCharacterSkin> SkinOf;
        public Func<GameSession> Session;
        public Action ShowLineup;                // this game's roles (the map button's window)
    }

    /// <summary>
    /// The part of the game screen under the top HUD for one <see cref="HudLayout"/> other than Frame (GameScene's,
    /// which <see cref="GameHudLayout"/> builds): where the chat, the ability drawer, the vote and my role go.
    /// GameScreen makes it after the table and the night room and tells it about phases, refreshes and the drawer.
    /// Built at run time over the scene's parts, like GameHudLayout; game rules stay with the panels and GameScreen.
    /// </summary>
    public abstract class GameLayoutBody : MonoBehaviour
    {
        protected const string Icons = "Sprites/flaticon/white/128/";
        protected static readonly Color Gold = new Color(0.98f, 0.8f, 0.42f);
        protected static readonly Color GoldLine = new Color(0.78f, 0.58f, 0.27f);
        protected static readonly Color Ink = new Color(0.27f, 0.16f, 0.07f);
        protected static readonly Color Parchment = new Color(0.93f, 0.85f, 0.67f);
        protected static readonly Color Teal = new Color(0.07f, 0.17f, 0.19f, 0.97f);
        protected static readonly Color Cream = new Color(0.93f, 0.9f, 0.84f);
        protected static readonly Color Muted = new Color(0.62f, 0.59f, 0.52f);
        protected static readonly Color Panel = new Color(0.06f, 0.05f, 0.04f, 1f); // opaque: in linear colour space even 0.9 shows the deck
        protected static readonly Color DrawerBack = new Color(0.106f, 0.118f, 0.149f, 0.96f);

        protected GameLayoutContext ctx;
        protected GameHudChat chatUi;

        /// <summary>The layout's body on host: the one already there (a scene with the layout baked in), or a new one.</summary>
        public static GameLayoutBody Create(HudLayout layout, GameObject host)
        {
            switch (layout)
            {
                case HudLayout.Stage: return OnHost<GameLayoutStage>(host);
                case HudLayout.Tabs: return OnHost<GameLayoutTabs>(host);
                case HudLayout.Sheet: return OnHost<GameLayoutSheet>(host);
                default: return null;
            }
        }

        private static T OnHost<T>(GameObject host) where T : GameLayoutBody
        {
            T body = host.GetComponent<T>();
            return body != null ? body : host.AddComponent<T>();
        }

        public void Build(GameLayoutContext context)
        {
            ctx = context;
            OnBuild();
        }

        protected abstract void OnBuild();

        /// <summary>A new phase began (after GameScreen opened or closed the drawer for it).</summary>
        public virtual void OnPhase(string phase) { }

        /// <summary>State, my role or an accepted action changed.</summary>
        public virtual void Refresh(GameSession session) { }

        /// <summary>The bottom drawer opened (Vote = night ability or day skip, Chat = keyboard) or closed.</summary>
        public virtual void OnDrawerMode(BottomTabMode mode) { }

        /// <summary>The latest public announcement (what the notice scroll shows): a new phase, the night's or the vote's result.</summary>
        public virtual void OnNotice(string text) { }

        /// <summary>The vote's public result (counts per player), shown until the next phase.</summary>
        public virtual void OnExecutionResult(ExecutionResultDto result) { }

        /// <summary>Whether GameScreen plays the full-screen cut-scenes for the night's result and the execution (DayTransitionView).</summary>
        public virtual bool PlaysDayTransitions => false;

        /// <summary>The player's place on this layout (the roster slot), where a cut-in can fly to. Null if it has none.</summary>
        public virtual RectTransform PlayerSlot(long playerId) => null;

        protected float CanvasHeight => ctx.Canvas.rect.height;

        // ------------------------------------------------------------------ shared parts

        /// <summary>Moves the bottom sheet into a box with these insets (it slides inside it as before); its tab bar clear.</summary>
        protected RectTransform LiftSheet(float left, float bottom, float right)
        {
            RectTransform lift = NewRect("BottomSheetLift", ctx.Canvas);
            lift.SetSiblingIndex(ctx.Sheet.GetSiblingIndex());
            Stretch(lift, left, 0f, right, bottom);
            ctx.Sheet.SetParent(lift, false);
            Image bar = TabBarImage();
            if (bar != null) bar.color = Color.clear;
            return lift;
        }

        protected Image TabBarImage()
        {
            Transform tabBar = ctx.Sheet != null ? ctx.Sheet.Find("TabBar") : null;
            return tabBar != null ? tabBar.GetComponent<Image>() : null;
        }

        /// <summary>The chat rows and the input bar in the nautical look (GameScene3's, <see cref="NauticalHud"/>).</summary>
        protected virtual bool NauticalChat => false;

        /// <summary>The chat rows and the input bar; the log goes where area says (see GameHudChat.BuildBare).</summary>
        protected void BuildChat(Func<Vector4> area)
        {
            chatUi = GetComponent<GameHudChat>(); // baked into the scene (BakeChat): only wired
            if (chatUi == null) chatUi = gameObject.AddComponent<GameHudChat>();
            chatUi.SetNautical(NauticalChat);
            chatUi.BuildBare(ctx.Canvas, ctx.Chat, ctx.Sheet, ctx.BottomTab, ctx.Font, ctx.Hud.Portraits, area);
            chatUi.SetSkins(ctx.SkinOf);
        }

        /// <summary>Edit mode (the bake tool): BuildChat's styles and input bar into the scene, the log put at area once.</summary>
        protected void BakeChat(Vector4 area)
        {
            chatUi = GetComponent<GameHudChat>();
            if (chatUi == null) chatUi = gameObject.AddComponent<GameHudChat>();
            chatUi.SetNautical(NauticalChat);
            chatUi.BakeBare(ctx.Canvas, ctx.Chat, ctx.Sheet, ctx.Font, area);
        }

        /// <summary>My role in the HUD's window: role, goal, faction, what it does, ability and teammates.</summary>
        protected void ShowRoleWindow()
        {
            GameSession session = ctx.Session();
            HudPopup popup = ctx.Hud.Popup;
            if (session == null || session.Me == null)
            {
                popup.Show("내 역할", string.Empty);
                popup.ShowMessage("게임이 시작되면 볼 수 있습니다.");
                return;
            }
            MyRoleDto me = session.Me;
            popup.Show("내 역할", (me.roleName ?? string.Empty) + " · " + RoleInfoText.Goal(me));
            popup.AddRow(me.roleName ?? string.Empty, RoleInfoText.TeamName(me), FactionColor(me.IsSirenTeam ? Factions.Neutral : me.faction),
                RoleText(session));
        }

        /// <summary>What my role does, then the ability and the teammates (one per line).</summary>
        protected static string RoleText(GameSession session)
        {
            MyRoleDto me = session.Me;
            string text = RoleInfoText.Description(me.role);
            if (me.HasAbility) text += "\n능력: " + GameScreenText.AbilityLine(me);
            string mates = ReportFormatter.TeammatesLine(session.State, me);
            if (!string.IsNullOrEmpty(mates)) text += "\n" + mates;
            return text;
        }

        protected static Color FactionColor(string faction)
        {
            switch (faction)
            {
                case Factions.Pirate: return new Color(0.95f, 0.42f, 0.36f);
                case Factions.Crew: return new Color(0.5f, 0.78f, 0.98f);
                default: return new Color(0.78f, 0.64f, 0.98f);
            }
        }

        /// <summary>The bust of the skin this player wears, or null.</summary>
        protected Sprite BustOf(long playerId)
        {
            ChibiCharacterSkin skin = ctx.SkinOf != null && playerId > 0 ? ctx.SkinOf(playerId) : null;
            return skin != null && ctx.Hud.Portraits != null ? ctx.Hud.Portraits.Find(skin.DesignId) : null;
        }

        /// <summary>A round portrait (bust, or the name's initial on a coloured disc) in a gold ring (or none). Returns the disc.</summary>
        protected Image Avatar(RectTransform parent, string name, float x, float top, float size, long playerId, string nickname, bool ringed = true)
        {
            RectTransform round = Box(parent, name, x, top, size, size);
            Sprite bust = BustOf(playerId);
            float hue = string.IsNullOrEmpty(nickname) ? 0f : (nickname.GetHashCode() & 0xFFFF) / 65535f;
            Image disc = AddImage(round, NightFxSprites.Circle, bust != null ? new Color(0.12f, 0.14f, 0.18f) : Color.HSVToRGB(hue, 0.45f, 0.55f));
            round.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            if (bust != null)
            {
                RectTransform face = NewRect("Bust", round);
                Stretch(face, -size * 0.08f, -size * 0.03f, -size * 0.08f, -size * 0.12f);
                AddImage(face, bust, Color.white).preserveAspect = true;
            }
            else
            {
                TextMeshProUGUI initial = NewText(round, "Initial", size * 0.45f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
                Stretch(initial.rectTransform, 0f, 0f, 0f, 0f);
                initial.text = string.IsNullOrEmpty(nickname) ? "?" : nickname.Substring(0, 1);
            }
            if (!ringed) return disc;
            RectTransform ring = Box(parent, name + "Ring", x - 2f, top - 2f, size + 4f, size + 4f);
            HudShapes.Panel(ring, new Color(0f, 0f, 0f, 0f), GoldLine, (size + 4f) * 0.5f);
            return disc;
        }

        protected Button TextButton(RectTransform rect, string label, bool primary, Action onClick)
        {
            Image back = HudShapes.Panel(rect, primary ? new Color(0.85f, 0.66f, 0.25f) : Teal, GoldLine, 10f);
            back.raycastTarget = true;
            TextMeshProUGUI text = NewText(rect, "Label", 26f, primary ? new Color(0.23f, 0.15f, 0.02f) : Cream, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 8f, 4f, 8f, 4f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 16f;
            text.fontSizeMax = 26f;
            text.text = label;
            Button button = rect.gameObject.AddComponent<Button>();
            if (onClick != null) button.onClick.AddListener(() => onClick()); // null: wired later (a baked layout)
            return button;
        }

        // ------------------------------------------------------------------ helpers

        protected static Sprite Icon(string name) => Resources.Load<Sprite>(Icons + name + "_128");

        protected static Image AddImage(RectTransform rect, Sprite sprite, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        protected TextMeshProUGUI NewText(RectTransform parent, string name, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (ctx.Font != null) text.font = ctx.Font;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        protected static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        // Placed from the parent's top-left corner (y down), this size.
        protected static RectTransform Box(Transform parent, string name, float x, float top, float width, float height)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -top);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        // Placed from the parent's bottom-right corner (x left, y up), this size.
        protected static RectTransform BoxBottomRight(Transform parent, string name, float right, float bottom, float width, float height)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-right, bottom);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        // Stretched over the parent with these insets (left, top, right, bottom).
        protected static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
