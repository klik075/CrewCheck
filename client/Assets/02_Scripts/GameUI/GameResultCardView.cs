using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.GameUI
{
    /// <summary>Decorated result header and player cards, using the same cosmetic portrait catalog as the HUD.</summary>
    public sealed class GameResultCardView
    {
        private readonly RectTransform card, header, details, grid;
        private readonly TextMeshProUGUI title, outcome, fallback, summary;
        private readonly CanvasGroup headerGroup, detailsGroup;
        private readonly Image headerGlow, seal, outcomeBorder, headerWood, crest, crestInset, rule;
        private readonly List<RectTransform> rows = new List<RectTransform>();
        private readonly DesignSpriteCatalog portraits;
        private readonly Sprite fill, outline;
        private readonly TMP_FontAsset font;
        private readonly Color gold = new Color(1f, .79f, .36f);
        private readonly Color originalTitleColor, originalOutcomeColor;
        private Color headerAccent;
        private bool showing;

        public GameResultCardView(RectTransform card, TextMeshProUGUI title, TextMeshProUGUI outcome,
            TextMeshProUGUI fallback, Button back)
        {
            this.card = card; this.title = title; this.outcome = outcome; this.fallback = fallback;
            originalTitleColor = title != null ? title.color : Color.white;
            originalOutcomeColor = outcome != null ? outcome.color : Color.white;
            font = title != null ? title.font : null;
            fill = Resources.Load<Sprite>("GameHud/HudRoundedFill");
            outline = Resources.Load<Sprite>("GameHud/HudRoundedOutline");
            portraits = Resources.Load<DesignSpriteCatalog>("GameHud/Portraits");
            var surface = card.GetComponent<Image>();
            if (surface != null)
            {
                Sprite frame = Resources.Load<Sprite>("GameHud/ChatFrame");
                if (frame != null) { surface.sprite = frame; surface.type = Image.Type.Sliced; surface.pixelsPerUnitMultiplier = 1.65f; surface.color = Color.white; }
            }
            header = Rect("ResultHeaderDecor", card); Top(header, 20f, 206f, 42f);
            header.SetAsFirstSibling(); headerGroup = header.gameObject.AddComponent<CanvasGroup>(); headerGroup.blocksRaycasts = false;
            headerWood = Art("HeaderWood", header, fill, new Color(.02f, .085f, .105f, .96f));
            Stretch(headerWood.rectTransform, 0f, 0f);
            headerGlow = Art("VictoryLight", header, NightFxSprites.Dot, gold);
            headerGlow.rectTransform.sizeDelta = new Vector2(630f, 220f);
            seal = Art("NauticalSeal", header, Resources.Load<Sprite>("RoomLobby/HelmRefined"), Color.white);
            seal.preserveAspect = true;
            seal.rectTransform.anchorMin = seal.rectTransform.anchorMax = new Vector2(.13f, .5f);
            seal.rectTransform.sizeDelta = new Vector2(126f, 126f);
            crest = Art("CompassCrest", header, null, gold);
            crest.rectTransform.anchorMin = crest.rectTransform.anchorMax = new Vector2(.5f, 1f);
            crest.rectTransform.sizeDelta = new Vector2(22f, 22f);
            crest.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            crestInset = Art("Inset", crest.transform, null, new Color(.02f, .085f, .105f));
            crestInset.rectTransform.sizeDelta = new Vector2(12f, 12f);
            rule = Art("HeaderRule", header, null, new Color(.8f, .59f, .25f, .8f));
            rule.rectTransform.anchorMin = new Vector2(.08f, 0f); rule.rectTransform.anchorMax = new Vector2(.92f, 0f);
            rule.rectTransform.sizeDelta = new Vector2(0f, 2f);
            outcomeBorder = Art("OutcomeBadge", header, outline, gold);
            outcomeBorder.rectTransform.anchorMin = outcomeBorder.rectTransform.anchorMax = new Vector2(.61f, .3f);
            outcomeBorder.rectTransform.sizeDelta = new Vector2(260f, 44f);

            details = Rect("ResultPlayerCards", card); Stretch(details, 42f, 0f);
            details.offsetMin = new Vector2(42f, 152f); details.offsetMax = new Vector2(-42f, -240f);
            detailsGroup = details.gameObject.AddComponent<CanvasGroup>(); detailsGroup.blocksRaycasts = false;
            var heading = Label("RosterHeading", details, "항해 기록", 27f, gold);
            Top(heading.rectTransform, 0f, 36f, 6f); heading.alignment = TextAlignmentOptions.Left;
            summary = Label("RosterSummary", details, "", 21f, new Color(.82f, .83f, .78f));
            Top(summary.rectTransform, 0f, 36f, 6f); summary.alignment = TextAlignmentOptions.Right;
            grid = Rect("Players", details); Stretch(grid, 0f, 0f); grid.offsetMax = new Vector2(0f, -52f);
            if (back != null)
            {
                var image = back.GetComponent<Image>(); Sprite button = Resources.Load<Sprite>("RoomLobby/Button");
                if (image != null && button != null) { image.sprite = button; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 1.5f; image.color = Color.white; }
                var label = back.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null)
                {
                    label.color = new Color(1f, .88f, .62f);
                    label.fontSizeMax = 28f; label.margin = new Vector4(14f, 8f, 14f, 8f);
                }
            }
            header.gameObject.SetActive(false); details.gameObject.SetActive(false);
        }

        public void Show(GameResultDto result, MyRoleDto me, Func<long, ChibiCharacterSkin> skinOf, bool hasArt)
        {
            ClearRows();
            showing = hasArt && result != null && result.ended && !result.IsCancelled;
            header.gameObject.SetActive(showing); details.gameObject.SetActive(showing);
            if (fallback != null) fallback.gameObject.SetActive(!showing);
            if (!showing)
            {
                if (title != null) { Top(title.rectTransform, 30f, 80f, 32f); title.color = originalTitleColor; }
                if (outcome != null) { Top(outcome.rectTransform, 112f, 64f, 32f); outcome.color = originalOutcomeColor; }
                return;
            }
            headerAccent = GameResultPalette.ForWinner(result.winner);
            headerWood.color = Color.Lerp(new Color(.015f, .025f, .04f), headerAccent, .12f);
            crest.color = rule.color = headerAccent;
            crestInset.color = headerWood.color;
            if (title != null)
            {
                Top(title.rectTransform, 64f, 68f, 0f);
                title.rectTransform.anchorMin = new Vector2(.25f, 1f); title.rectTransform.anchorMax = new Vector2(.91f, 1f);
                title.fontStyle = FontStyles.Bold;
                title.color = headerAccent;
            }
            if (outcome != null)
            {
                Top(outcome.rectTransform, 141f, 44f, 0f);
                outcome.rectTransform.anchorMin = new Vector2(.3f, 1f); outcome.rectTransform.anchorMax = new Vector2(.9f, 1f);
                outcome.fontSizeMax = 29f;
            }
            bool lost = me == null || !GameStateQueries.IsWinner(result, me.playerId, me.faction);
            outcomeBorder.color = lost ? GameResultPalette.Muted : headerAccent;
            if (outcome != null) outcome.color = outcomeBorder.color;
            int alive = 0;
            if (result.players != null)
                foreach (var player in result.players)
                {
                    if (player == null || rows.Count >= 12) continue;
                    if (player.alive) alive++;
                    AddPlayer(player, me, skinOf);
                }
            summary.text = rows.Count == 0 ? "참가자 기록 없음" : $"{rows.Count}명  ·  생존 {alive}  ·  사망 {rows.Count - alive}";
            ResetReveal(); LayoutRows();
        }

        private void AddPlayer(PlayerResultDto player, MyRoleDto me, Func<long, ChibiCharacterSkin> skinOf)
        {
            bool mine = me != null && player.playerId == me.playerId;
            Color accent = GameResultPalette.ForFaction(GameResultPalette.FactionForPlayer(player));
            var rect = Rect("Player_" + player.playerId, grid); rows.Add(rect);
            var bg = Art("CardFill", rect, fill, Color.Lerp(new Color(.025f, .04f, .055f), accent, mine ? .23f : .13f));
            Stretch(bg.rectTransform, 0f, 0f);
            var border = Art("CardBorder", rect, outline, mine ? gold : new Color(accent.r, accent.g, accent.b, .72f)); Stretch(border.rectTransform, 0f, 0f);
            var stripe = Art("FactionStripe", rect, fill, accent);
            stripe.rectTransform.anchorMin = new Vector2(0f, 0f); stripe.rectTransform.anchorMax = new Vector2(0f, 1f);
            stripe.rectTransform.offsetMin = new Vector2(5f, 12f); stripe.rectTransform.offsetMax = new Vector2(9f, -12f);
            var portraitBase = Art("PortraitBase", rect, NightFxSprites.Circle, Color.Lerp(new Color(.025f, .04f, .055f), accent, .25f));
            portraitBase.rectTransform.anchorMin = portraitBase.rectTransform.anchorMax = new Vector2(0f, .5f);
            portraitBase.rectTransform.anchoredPosition = new Vector2(51f, 0f); portraitBase.rectTransform.sizeDelta = new Vector2(80f, 80f);
            ChibiCharacterSkin skin = skinOf != null ? skinOf(player.playerId) : null;
            Sprite sprite = skin != null && portraits != null ? portraits.Find(skin.DesignId) : null;
            if (sprite == null && skin != null) sprite = skin.Standing;
            if (sprite != null)
            {
                var portrait = Art("Portrait", portraitBase.transform, sprite, player.alive ? Color.white : new Color(.65f, .69f, .74f));
                Stretch(portrait.rectTransform, 2f, 2f); portrait.preserveAspect = true;
            }
            else
            {
                var unknown = Label("MissingPortrait", portraitBase.transform, "?", 34f, gold); Stretch(unknown.rectTransform, 0f, 0f);
            }
            string nickname = string.IsNullOrEmpty(player.nickname) ? "플레이어 " + player.playerId : player.nickname;
            var name = Label("Nickname", rect, (mine ? "나 · " : "") + nickname, 25f, mine ? gold : Color.white);
            TextLine(name, 9f, 32f, 108f, 12f); name.fontStyle = FontStyles.Bold;
            name.enableAutoSizing = true; name.fontSizeMin = 20f; name.fontSizeMax = 25f;
            string roleName = string.IsNullOrEmpty(player.roleName) ? player.role : player.roleName;
            var role = Label("Role", rect, string.IsNullOrEmpty(roleName) ? "직업 미확인" : roleName, 23f, Color.Lerp(accent, Color.white, .45f));
            TextLine(role, 39f, 36f, 108f, 90f);
            var faction = Label("Faction", rect, FactionName(player), 19f, accent);
            TextLine(faction, 68f, 30f, 108f, 90f);
            var status = Label("Status", rect, player.alive ? "생존" : "사망", 20f,
                player.alive ? new Color(.55f, .91f, .73f) : new Color(.69f, .71f, .74f));
            status.rectTransform.anchorMin = status.rectTransform.anchorMax = new Vector2(1f, 0f);
            status.rectTransform.pivot = new Vector2(1f, 0f); status.rectTransform.anchoredPosition = new Vector2(-12f, 10f);
            status.rectTransform.sizeDelta = new Vector2(64f, 32f);
        }

        public void Reveal(float time)
        {
            if (!showing) return;
            headerGroup.alpha = Mathf.SmoothStep(0f, 1f, (time - .3f) / .4f);
            detailsGroup.alpha = Mathf.SmoothStep(0f, 1f, (time - 1.6f) / .4f);
            float pulse = .11f + .045f * Mathf.Sin(time * 1.8f);
            headerGlow.color = new Color(headerAccent.r, headerAccent.g, headerAccent.b, pulse);
            seal.rectTransform.localScale = Vector3.one * Mathf.Lerp(.88f, 1f, headerGroup.alpha);
            LayoutRows();
        }

        public void ResetReveal() { headerGroup.alpha = detailsGroup.alpha = 1f; }

        private void LayoutRows()
        {
            int count = Mathf.Max(1, (rows.Count + 1) / 2);
            float height = Mathf.Max(98f, Mathf.Min(114f, (grid.rect.height - (count - 1) * 8f) / count));
            for (int i = 0; i < rows.Count; i++)
            {
                var rect = rows[i]; int col = i % 2;
                rect.anchorMin = new Vector2(col * .5f, 1f); rect.anchorMax = new Vector2((col + 1) * .5f, 1f);
                rect.pivot = new Vector2(.5f, 1f); rect.sizeDelta = new Vector2(-12f, height);
                rect.anchoredPosition = new Vector2(0f, -(i / 2) * (height + 8f));
            }
        }

        private void ClearRows()
        {
            foreach (var row in rows)
            {
                row.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(row.gameObject); else UnityEngine.Object.DestroyImmediate(row.gameObject);
            }
            rows.Clear();
        }

        private static string FactionName(PlayerResultDto player)
        {
            if (player.team == Teams.Siren) return "세이렌 팀"; // the siren and the seduced players (their roles stay)
            switch (GameResultPalette.FactionForRole(player.role))
            {
                case Factions.Crew: return "선원 진영";
                case Factions.Pirate: return "해적 진영";
                case Factions.Neutral: return "개인 진영";
                default: return "진영 미확인";
            }
        }

        private RectTransform Rect(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform)); obj.layer = card.gameObject.layer;
            var rect = (RectTransform)obj.transform; rect.SetParent(parent, false); rect.sizeDelta = Vector2.zero; return rect;
        }
        private Image Art(string name, Transform parent, Sprite sprite, Color color)
        {
            var art = Rect(name, parent).gameObject.AddComponent<Image>(); art.sprite = sprite; art.color = color;
            art.raycastTarget = false; if (sprite != null && sprite.border != Vector4.zero) art.type = Image.Type.Sliced; return art;
        }
        private TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color)
        {
            var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>(); if (font != null) label.font = font;
            label.text = text; label.fontSize = size; label.color = color; label.richText = false; label.raycastTarget = false;
            label.enableAutoSizing = true; label.fontSizeMin = size * .78f; label.fontSizeMax = size;
            label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.Center; return label;
        }
        private static void Stretch(RectTransform rect, float x, float y)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(x, y); rect.offsetMax = new Vector2(-x, -y); }
        private static void Top(RectTransform rect, float top, float height, float inset)
        { rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1f); rect.anchoredPosition = new Vector2(0f, -top); rect.sizeDelta = new Vector2(-inset * 2f, height); }
        private static void TextLine(TextMeshProUGUI label, float top, float height, float left, float right)
        { Top(label.rectTransform, top, height, 0f); label.rectTransform.offsetMin = new Vector2(left, -top - height); label.rectTransform.offsetMax = new Vector2(-right, -top); label.alignment = TextAlignmentOptions.Left; }
    }
}
