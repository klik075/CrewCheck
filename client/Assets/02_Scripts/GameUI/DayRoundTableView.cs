using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Game;
using WhoisntCitizen.Chat;
using System.Globalization;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// Draws the day table and seated characters inside the screen-space game canvas.
    /// Characters are skins: any <see cref="ChibiCharacterSkin"/> fits any seat at the same size and spot, because every
    /// pose sprite is trimmed to the character with its pivot under it (Tools/ChibiSkinFit/FitSkins.ps1). A seat places
    /// that pivot on its chair and scales the sprite to one height, so swapping skins never moves or resizes anyone.
    /// </summary>
    public sealed class DayRoundTableView : MonoBehaviour
    {
        private const int SeatCount = 12;
        private const float TableHeight = 850f;
        private const float ChatTop = -1080f;
        private const float ArrivalDuration = 0.45f;
        private const float BreathingAmount = 0.006f;
        // Chair floor point (bottom centre of the chair), clockwise from 12 o'clock, in table coordinates (y up).
        // Tuned to the painted table: the far row and the 3/9 o'clock seats sit behind its rim (the table hides their
        // laps, so nobody sits on the table top); the near row sits in front of its skirt.
        private static readonly Vector2[] SeatPositions =
        {
            new Vector2(0f, 105f), new Vector2(215f, 82f), new Vector2(370f, 20f),
            new Vector2(455f, -60f), new Vector2(370f, -317f), new Vector2(215f, -370f),
            new Vector2(0f, -385f), new Vector2(-215f, -370f), new Vector2(-370f, -317f),
            new Vector2(-455f, -60f), new Vector2(-370f, 20f), new Vector2(-215f, 82f)
        };
        // The painted table top (an ellipse) in RoundTable.png pixels, top-left origin. Each seat's card spot lies on its
        // outer band, across from the seat; a redrawn table must keep that band free of props.
        private static readonly Vector2 TableTopCenterPx = new Vector2(769f, 430f), TableTopRadiusPx = new Vector2(719f, 270f);
        private const float CardSpotRadius = 0.86f;
        // Footprint of a card lying on the table (foreshortened like the table top), with 12 seats.
        private static readonly Vector2 CardSpotSize = new Vector2(52f, 34f);
        // Chair height with 12 seats; fewer seats scale it up (characterScale). Farther seats are a little smaller.
        private const float ChairHeight = 130f;
        private const float FarDepth = 0.9f, NearDepth = 1.1f;

        /// <summary>
        /// How a pose sits on its chair, in chair heights from the chair's floor point: where the character's pivot
        /// (its feet / seat bottom) goes, how tall the character is, and whether the chair back is drawn over it.
        /// Chairs face right/down-left as painted; seats on the other side mirror the chair and these x offsets.
        /// </summary>
        private struct PoseLayout
        {
            public string chair;
            public Vector2 foot;
            public float height;
            public bool behindChair;

            public PoseLayout(string chair, float footX, float footY, float height, bool behindChair)
            {
                this.chair = chair; foot = new Vector2(footX, footY); this.height = height; this.behindChair = behindChair;
            }
        }

        private static PoseLayout LayoutFor(ChibiPose pose)
        {
            switch (pose)
            {
                case ChibiPose.SeatedFront: return new PoseLayout("ChairFront", 0f, 0.12f, 1.12f, false);
                case ChibiPose.Seated1OClock:
                case ChibiPose.Seated11OClock: return new PoseLayout("ChairDiagonalFront", -0.04f, 0.13f, 1.12f, false);
                case ChibiPose.SeatedSide: return new PoseLayout("ChairSide", 0.10f, 0.12f, 1.12f, false);
                case ChibiPose.Seated5OClock:
                case ChibiPose.Seated7OClock: return new PoseLayout("ChairDiagonalBack", 0.10f, 0.30f, 1.12f, true);
                case ChibiPose.SeatedBack: return new PoseLayout("ChairBack", 0f, 0.30f, 1.12f, true);
                default: return new PoseLayout("ChairFront", 0f, 0f, 1.25f, false); // standing beside the chair
            }
        }

        private sealed class Seat
        {
            public Image avatar;
            public TextMeshProUGUI name;
            public ChibiPose pose;
            public Vector2 anchor;          // chair floor point in table coordinates
            public float figureHeight;      // every skin is drawn this tall here
            public bool occupied;
            public long playerId;
            public bool alive;
            public float arrivedAt;
            public float breathingPhase;
            public Vector2 restingPosition;
            public Vector3 restingScale;
            public Color restingColor;
            public RectTransform bubble;
            public TextMeshProUGUI speechText;
            public CanvasGroup speechGroup;
            public Image speechLeader;
            public Outline speakingOutline;
            public float speechUntil;
        }

        private readonly Seat[] seats = new Seat[SeatCount];
        private readonly RectTransform[] cardSpots = new RectTransform[SeatCount]; // by seat index, on the table rim
        private readonly List<PlayerViewDto> players = new List<PlayerViewDto>(SeatCount);
        private readonly Dictionary<long, ChibiCharacterSkin> skinOverrides = new Dictionary<long, ChibiCharacterSkin>();
        private ChibiCharacterSkin[] skins;
        private Func<long, int> seatOrdinalFor;
        private Func<long, int> skinIndexFor;
        private RectTransform tableRoot;
        private RectTransform chatRect;
        private Vector2 originalChatMax;
        private Image background;
        private Sprite originalBackground;
        private Color originalBackgroundColor;
        private Sprite previewSprite;
        private bool initialized;
        private int capacity = SeatCount;
        private float characterScale = 1f;
        private readonly Vector2[] layoutPositions = new Vector2[SeatCount];
        private readonly int[] seatOrder = new int[SeatCount];
        public int Capacity => capacity;
        private ChatUIController roomChat;
        private GameChatController gameChat;

        public void Initialize(Image backgroundImage, RectTransform chat, TMP_FontAsset font, ChibiCharacterSkin[] availableSkins, int maxPlayers = SeatCount)
        {
            if (initialized) return;
            initialized = true;
            background = backgroundImage;
            chatRect = chat;
            skins = availableSkins != null && availableSkins.Length > 0 ? availableSkins : Resources.LoadAll<ChibiCharacterSkin>("DayTable/Skins");
            if (availableSkins == null) Array.Sort(skins, (a, b) => string.CompareOrdinal(a.name, b.name));
            capacity = Mathf.Clamp(maxPlayers, 4, SeatCount);
            characterScale = Mathf.Lerp(1.25f, 1f, (capacity - 4f) / 8f);
            ConfigureLayout();
            if (background != null)
            {
                originalBackground = background.sprite;
                originalBackgroundColor = background.color;
            }
            if (chatRect != null) originalChatMax = chatRect.offsetMax;

            var root = new GameObject("DayRoundTable", typeof(RectTransform));
            root.layer = gameObject.layer;
            tableRoot = (RectTransform)root.transform;
            tableRoot.SetParent(transform, false);
            tableRoot.anchorMin = new Vector2(0f, 1f);
            tableRoot.anchorMax = new Vector2(1f, 1f);
            tableRoot.pivot = new Vector2(0.5f, 1f);
            tableRoot.anchoredPosition = new Vector2(0f, -230f);
            tableRoot.sizeDelta = new Vector2(0f, TableHeight);
            if (chatRect != null) tableRoot.SetSiblingIndex(chatRect.GetSiblingIndex() + 1);

            Sprite table = Resources.Load<Sprite>("DayTable/RoundTable");
            previewSprite = Resources.Load<Sprite>("DayTable/PreviewCharacter");

            // Far and side seats sit behind the table. Near seats sit in front of its rim. Each group is drawn back to front.
            var farSeats = new List<int>();
            for (int i = 0; i < capacity; i++) if (IsFarSeat(i)) farSeats.Add(i);
            farSeats.Sort((a, b) => layoutPositions[b].y.CompareTo(layoutPositions[a].y));
            foreach (int i in farSeats) CreateSeat(i, font);
            Vector2 tableSize = new Vector2(Mathf.Lerp(840f, 900f, (capacity - 4f) / 8f), 600f);
            CreateImage("RoundTable", tableRoot, table, tableSize, new Vector2(0f, -75f));
            // The revised table includes its front rim. The old overlay would restore coins over the clear card band.
            CreateCardSpots(table, tableSize, new Vector2(0f, -75f));
            // Draw the near seats from farthest to closest, on both sides symmetrically.
            var nearSeats = new List<int>();
            for (int i = 0; i < capacity; i++) if (!IsFarSeat(i)) nearSeats.Add(i);
            nearSeats.Sort((a, b) => layoutPositions[b].y.CompareTo(layoutPositions[a].y));
            foreach (int i in nearSeats) CreateSeat(i, font);
            for (int i = 0; i < capacity; i++) CreateSpeech(i, font);

            SetVisible(true);
            ShowPreview();
            BindChat();
        }

        public void SetPhase(string phase)
        {
            // Execution too: the vote tally stays on the table while the result is shown (DayTableVoteCards).
            SetVisible(phase == GamePhases.Day || phase == GamePhases.Vote || phase == GamePhases.Execution);
        }

        /// <summary>
        /// Where cards for this player go: an empty spot on the table rim right in front of their seat, drawn above the
        /// table and below the near row (put card images under it). Null if the player is not seated.
        /// </summary>
        public RectTransform CardSpotFor(long playerId)
        {
            for (int i = 0; i < SeatCount; i++)
                if (seats[i] != null && seats[i].occupied && seats[i].playerId == playerId) return cardSpots[i];
            return null;
        }

        // One spot per seat on the table top's outer band, at the seat's clock angle (evenly spaced around the rim).
        private void CreateCardSpots(Sprite table, Vector2 tableSize, Vector2 tableOffset)
        {
            var layer = new GameObject("CardSpots", typeof(RectTransform));
            layer.layer = gameObject.layer;
            var layerRect = (RectTransform)layer.transform;
            layerRect.SetParent(tableRoot, false);
            layerRect.anchorMin = layerRect.anchorMax = new Vector2(0.5f, 0.5f);
            layerRect.sizeDelta = Vector2.zero;
            Vector2 imageSize = table != null ? table.rect.size : new Vector2(1536f, 1024f);
            float scale = Mathf.Min(tableSize.x / imageSize.x, tableSize.y / imageSize.y); // preserveAspect fit
            Vector2 center = tableOffset + new Vector2(TableTopCenterPx.x - imageSize.x * 0.5f, imageSize.y * 0.5f - TableTopCenterPx.y) * scale;
            Vector2 radius = TableTopRadiusPx * scale * CardSpotRadius;
            for (int i = 0; i < capacity; i++)
            {
                float angle = (90f - i * 360f / capacity) * Mathf.Deg2Rad; // 12 o'clock = straight back, clockwise
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                var spot = new GameObject("CardSpot_" + (i + 1), typeof(RectTransform));
                spot.layer = gameObject.layer;
                var rect = (RectTransform)spot.transform;
                rect.SetParent(layerRect, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = CardSpotSize * (scale / 0.5859f);
                rect.anchoredPosition = center + new Vector2(radius.x * cos, radius.y * sin);
                cardSpots[i] = rect;
            }
        }

        public void SetPlayers(List<PlayerViewDto> currentPlayers,
            Func<long, int> seatOrdinalResolver = null, Func<long, int> skinIndexResolver = null)
        {
            seatOrdinalFor = seatOrdinalResolver;
            skinIndexFor = skinIndexResolver;
            players.Clear();
            if (currentPlayers != null)
            {
                for (int i = 0; i < currentPlayers.Count && i < capacity; i++)
                    players.Add(currentPlayers[i]);
            }
            Refresh();
        }

        public void Clear()
        {
            players.Clear();
            Refresh();
        }

        /// <summary>All skins that can be worn, in a stable order (the default skin of a seat is picked from this list).</summary>
        public IReadOnlyList<ChibiCharacterSkin> Skins => skins;

        /// <summary>The skin with this design id, or null.</summary>
        public ChibiCharacterSkin FindSkin(string designId)
        {
            if (skins == null || string.IsNullOrEmpty(designId)) return null;
            foreach (ChibiCharacterSkin skin in skins)
                if (skin != null && string.Equals(skin.DesignId, designId, StringComparison.OrdinalIgnoreCase)) return skin;
            return null;
        }

        /// <summary>Dresses a player in this skin (null = back to the seat's default). Size and seat stay the same.</summary>
        public void SetSkin(long playerId, ChibiCharacterSkin skin)
        {
            if (skin == null) skinOverrides.Remove(playerId);
            else skinOverrides[playerId] = skin;
            Refresh();
        }

        private void ShowPreview()
        {
            if (Application.isPlaying) return;
            if ((skins == null || skins.Length == 0) && previewSprite == null) return;
            players.Add(new PlayerViewDto { playerId = -1, nickname = "미리보기", alive = true });
            Refresh();
        }

        private void SetVisible(bool visible)
        {
            if (!initialized || tableRoot == null) return;
            if (!visible) ClearSpeech();
            tableRoot.gameObject.SetActive(visible);
            if (background != null)
            {
                Sprite deck = visible ? Resources.Load<Sprite>("DayTable/Deck") : null;
                background.sprite = visible && deck != null ? deck : originalBackground;
                background.color = visible && deck != null ? Color.white : originalBackgroundColor;
                background.preserveAspect = false;
            }
            if (chatRect != null)
            {
                Vector2 max = originalChatMax;
                if (visible) max.y = ChatTop;
                chatRect.offsetMax = max;
            }
        }

        private void CreateSeat(int index, TMP_FontAsset font)
        {
            Vector2 position = layoutPositions[index];
            float clock = index * 12f / capacity;
            ChibiPose pose = PoseAtClock(clock);
            PoseLayout layout = LayoutFor(pose);
            float depth = Mathf.Lerp(FarDepth, NearDepth, Mathf.InverseLerp(SeatPositions[0].y, SeatPositions[6].y, position.y));
            // Fewer players: the seats behind the table grow; the near row keeps its size so its heads stay below
            // the rim (and below the card spots in front of everyone).
            float chairHeight = ChairHeight * (layout.behindChair ? 1f : characterScale) * depth;
            bool mirrorChair = pose == ChibiPose.Seated11OClock || pose == ChibiPose.Seated5OClock || (pose == ChibiPose.SeatedSide && position.x > 0f);
            var holder = new GameObject("Seat_" + (index + 1), typeof(RectTransform));
            holder.layer = gameObject.layer;
            RectTransform anchor = (RectTransform)holder.transform;
            anchor.SetParent(tableRoot, false);
            anchor.anchorMin = anchor.anchorMax = new Vector2(0.5f, 0.5f);
            anchor.sizeDelta = Vector2.zero;
            anchor.anchoredPosition = position;

            // The chair stands on the seat point; the character's pivot (feet / seat bottom) goes on its cushion.
            Sprite chair = Resources.Load<Sprite>("DayTable/" + layout.chair);
            float chairAspect = chair != null ? chair.rect.width / chair.rect.height : 0.8f;
            Image chairImage = CreateImage("Chair", anchor, chair, new Vector2(chairHeight * chairAspect, chairHeight), Vector2.zero);
            chairImage.preserveAspect = false;
            chairImage.rectTransform.pivot = new Vector2(0.5f, 0f);
            chairImage.rectTransform.localScale = new Vector3(mirrorChair ? -1f : 1f, 1f, 1f);
            Vector2 foot = new Vector2(mirrorChair ? -layout.foot.x : layout.foot.x, layout.foot.y) * chairHeight;
            Image avatar = CreateImage("Character", anchor, null, Vector2.zero, foot);
            avatar.preserveAspect = false;
            // Seen from behind (4–8 o'clock) the backrest faces the camera and covers the seated body.
            if (layout.behindChair) chairImage.transform.SetAsLastSibling();
            float figureHeight = layout.height * chairHeight;

            var labelObject = new GameObject("PlayerName", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.layer = gameObject.layer;
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(anchor, false);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(150f, 32f);
            // Above the head; for the near row (seen from behind) under the chair, where the deck is free.
            labelRect.anchoredPosition = layout.behindChair ? new Vector2(0f, -18f) : foot + new Vector2(0f, figureHeight + 20f);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.fontSize = 22f;
            label.alignment = TextAlignmentOptions.Center;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.color = Color.white;
            if (label.font != null)
            {
                label.outlineWidth = 0.2f;
                label.outlineColor = new Color32(0x0A, 0x16, 0x23, 0xFF);
            }
            label.raycastTarget = false;
            avatar.gameObject.SetActive(false);
            label.gameObject.SetActive(false);

            seats[index] = new Seat
            {
                avatar = avatar,
                name = label,
                pose = pose,
                anchor = position,
                figureHeight = figureHeight,
                restingPosition = foot,
                // Side poses are painted facing right; the right-hand side seat looks left, at the table.
                restingScale = new Vector3(pose == ChibiPose.SeatedSide && position.x > 0f ? -1f : 1f, 1f, 1f),
                breathingPhase = index * 2.39996f
            };
        }

        /// <summary>Puts a skin's sprite on the seat: its pivot on the cushion, scaled to the seat's figure height.</summary>
        private static void ApplySprite(Seat seat, Sprite sprite)
        {
            seat.avatar.sprite = sprite;
            seat.avatar.enabled = sprite != null;
            if (sprite == null) return;
            Rect rect = sprite.rect;
            RectTransform rt = seat.avatar.rectTransform;
            rt.pivot = new Vector2(sprite.pivot.x / rect.width, sprite.pivot.y / rect.height);
            rt.sizeDelta = new Vector2(rect.width * seat.figureHeight / rect.height, seat.figureHeight);
        }

        /// <summary>Head of the seated character in table coordinates (for speech bubbles).</summary>
        private static Vector2 HeadOf(Seat seat) => seat.anchor + seat.restingPosition + new Vector2(0f, seat.figureHeight * 0.78f);

        private void Refresh()
        {
            var occupied = new bool[SeatCount];
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == null) continue;
                int ordinal = seatOrdinalFor != null ? seatOrdinalFor(players[i].playerId) : -1;
                if (ordinal < 0 || ordinal >= capacity || occupied[ordinal])
                {
                    ordinal = 0;
                    while (ordinal < capacity && occupied[ordinal]) ordinal++;
                }
                if (ordinal >= capacity) break;
                occupied[ordinal] = true;
                Seat seat = seats[seatOrder[ordinal]];
                if (seat == null) continue;
                // Polling the same roster must not restart the arrival or breathing motion.
                if (!seat.occupied || seat.playerId != players[i].playerId)
                {
                    seat.arrivedAt = Time.unscaledTime;
                    HideSpeech(seat);
                }
                seat.occupied = true;
                seat.playerId = players[i].playerId;
                seat.alive = players[i].alive;
                if (!seat.alive) HideSpeech(seat);
                seat.avatar.gameObject.SetActive(true);
                seat.name.gameObject.SetActive(true);

                ChibiCharacterSkin skin;
                if (!skinOverrides.TryGetValue(players[i].playerId, out skin) || skin == null)
                {
                    int skinIndex = skinIndexFor != null ? skinIndexFor(players[i].playerId) : -1;
                    if (skinIndex < 0) skinIndex = ordinal;
                    skin = skins != null && skins.Length > 0 ? skins[skinIndex % skins.Length] : null;
                }
                ApplySprite(seat, SpriteFor(skin, seat.pose) ?? previewSprite);
                seat.restingColor = seat.alive ? Color.white : new Color(0.55f, 0.6f, 0.65f, 0.6f);
                seat.name.text = players[i].nickname;
                ApplySeatVisual(seat, Time.unscaledTime, Application.isPlaying);
            }
            for (int ordinal = 0; ordinal < capacity; ordinal++)
            {
                if (occupied[ordinal]) continue;
                Seat seat = seats[seatOrder[ordinal]];
                if (seat == null) continue;
                seat.occupied = false;
                HideSpeech(seat);
                seat.avatar.rectTransform.anchoredPosition = seat.restingPosition;
                seat.avatar.rectTransform.localScale = seat.restingScale;
                seat.avatar.gameObject.SetActive(false);
                seat.name.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || tableRoot == null || !tableRoot.gameObject.activeInHierarchy) return;
            AnimateSeats(Time.unscaledTime);
            UpdateSpeech(Time.unscaledTime);
        }

        private void OnEnable() { if (initialized) BindChat(); }

        private void OnDisable()
        {
            if (roomChat != null) roomChat.LiveMessageReceived -= OnLiveMessage;
            if (gameChat != null) gameChat.LiveMessageReceived -= OnLiveMessage;
            roomChat = null;
            gameChat = null;
            ClearSpeech();
        }

        private void BindChat()
        {
            if (!Application.isPlaying) return;
            foreach (var chat in FindObjectsByType<ChatUIController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (chat.gameObject.scene == gameObject.scene && roomChat == null)
                { roomChat = chat; roomChat.LiveMessageReceived += OnLiveMessage; }
            foreach (var chat in FindObjectsByType<GameChatController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (chat.gameObject.scene == gameObject.scene && gameChat == null)
                { gameChat = chat; gameChat.LiveMessageReceived += OnLiveMessage; }
        }

        private void OnLiveMessage(ChatMessage message)
        {
            if (message == null || message.IsSystem || message.IsDead || message.nightChat) return;
            if (long.TryParse(message.userId, out long userId)) ShowSpeech(userId, message.content);
        }

        public void ShowSpeech(long userId, string message)
        {
            if (tableRoot == null || !tableRoot.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(message)) return;
            foreach (Seat seat in seats)
            {
                if (seat == null || !seat.occupied || !seat.alive || seat.playerId != userId) continue;
                seat.speechText.text = ShortText(seat.name.text, 10) + "\n" + ShortText(message, 40);
                seat.speechUntil = Time.unscaledTime + 4.5f;
                seat.speechGroup.alpha = 1f;
                seat.bubble.gameObject.SetActive(true);
                seat.speechLeader.gameObject.SetActive(true);
                seat.speakingOutline.enabled = true;
                LayoutSpeech();
                return;
            }
        }

        private static string ShortText(string text, int limit)
        {
            string clean = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            var info = new StringInfo(clean);
            return info.LengthInTextElements > limit ? info.SubstringByTextElements(0, limit) + "…" : clean;
        }

        private void CreateSpeech(int index, TMP_FontAsset font)
        {
            Seat seat = seats[index];
            seat.speakingOutline = seat.avatar.gameObject.AddComponent<Outline>();
            seat.speakingOutline.effectDistance = new Vector2(2f, -2f);
            seat.speakingOutline.effectColor = new Color(1f, 0.8f, 0.25f, 0.6f);
            seat.speakingOutline.enabled = false;
            seat.speechLeader = CreateImage("SpeechLeader_" + index, tableRoot, null, new Vector2(2f, 20f), Vector2.zero);
            seat.speechLeader.color = new Color(0.95f, 0.76f, 0.38f, 0.85f);
            Image panel = CreateImage("SpeechBubble_" + index, tableRoot, null, new Vector2(188f, 90f), Vector2.zero);
            panel.color = new Color32(255, 244, 216, 255);
            seat.bubble = panel.rectTransform;
            seat.speechGroup = panel.gameObject.AddComponent<CanvasGroup>();
            seat.speechGroup.blocksRaycasts = false;
            seat.speechGroup.interactable = false;
            Outline border = panel.gameObject.AddComponent<Outline>();
            border.effectColor = new Color32(110, 72, 30, 255);
            border.effectDistance = new Vector2(2f, -2f);
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.layer = gameObject.layer;
            var rect = (RectTransform)textObject.transform;
            rect.SetParent(panel.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 7f);
            rect.offsetMax = new Vector2(-10f, -7f);
            seat.speechText = textObject.GetComponent<TextMeshProUGUI>();
            if (font != null) seat.speechText.font = font;
            seat.speechText.fontSize = 20f;
            seat.speechText.richText = false;
            seat.speechText.textWrappingMode = TextWrappingModes.Normal;
            seat.speechText.overflowMode = TextOverflowModes.Ellipsis;
            seat.speechText.maxVisibleLines = 3;
            seat.speechText.color = new Color32(53, 35, 21, 255);
            seat.speechText.alignment = TextAlignmentOptions.Center;
            seat.speechText.raycastTarget = false;
            HideSpeech(seat);
        }

        private static void HideSpeech(Seat seat)
        {
            seat.speechUntil = 0f;
            if (seat.bubble != null) seat.bubble.gameObject.SetActive(false);
            if (seat.speechLeader != null) seat.speechLeader.gameObject.SetActive(false);
            if (seat.speakingOutline != null) seat.speakingOutline.enabled = false;
        }

        private void ClearSpeech()
        {
            foreach (Seat seat in seats) if (seat != null) HideSpeech(seat);
        }

        private void UpdateSpeech(float now)
        {
            bool changed = false;
            foreach (Seat seat in seats)
            {
                if (seat == null || seat.speechUntil <= 0f) continue;
                float remaining = seat.speechUntil - now;
                if (remaining <= 0f) { HideSpeech(seat); changed = true; continue; }
                float alpha = Mathf.Clamp01(remaining / 0.4f);
                seat.speechGroup.alpha = alpha;
                Color line = seat.speechLeader.color; line.a = 0.85f * alpha; seat.speechLeader.color = line;
                seat.speakingOutline.effectColor = new Color(1f, 0.8f, 0.25f, (0.4f + 0.15f * Mathf.Sin(now * 4f)) * alpha);
            }
            if (changed) LayoutSpeech();
        }

        private void LayoutSpeech()
        {
            // Reserve disjoint cells nearest each speaker; even twelve simultaneous bubbles cannot overlap.
            var used = new bool[30];
            for (int i = 0; i < SeatCount; i++)
            {
                Seat seat = seats[i];
                if (seat == null || seat.speechUntil <= 0f) continue;
                Vector2 head = HeadOf(seat);
                Vector2 preferred = head + new Vector2(0f, 100f);
                int best = -1; float distance = float.MaxValue;
                for (int cell = 0; cell < used.Length; cell++)
                {
                    if (used[cell]) continue;
                    Vector2 candidate = new Vector2((cell % 5 - 2) * 200f, 355f - (cell / 5) * 110f);
                    float score = (candidate - preferred).sqrMagnitude;
                    Rect bounds = new Rect(candidate - new Vector2(96f, 47f), new Vector2(192f, 94f));
                    for (int other = 0; other < SeatCount; other++)
                    {
                        if (seats[other] == null || !seats[other].occupied) continue;
                        Vector2 faceSize = new Vector2(0.8f, 0.55f) * seats[other].figureHeight;
                        Rect face = new Rect(HeadOf(seats[other]) - faceSize * 0.5f, faceSize);
                        if (bounds.Overlaps(face)) score += 1000000f;
                    }
                    if (score < distance) { distance = score; best = cell; }
                }
                used[best] = true;
                Vector2 position = new Vector2((best % 5 - 2) * 200f, 355f - (best / 5) * 110f);
                seat.bubble.anchoredPosition = position;
                Vector2 end = position - new Vector2(0f, 45f);
                Vector2 delta = end - head;
                seat.speechLeader.rectTransform.anchoredPosition = (head + end) * 0.5f;
                seat.speechLeader.rectTransform.sizeDelta = new Vector2(delta.magnitude, 2f);
                seat.speechLeader.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            }
            // Draw every bubble above every connector.
            foreach (Seat seat in seats) if (seat != null && seat.bubble != null) seat.bubble.SetAsLastSibling();
        }

        private void AnimateSeats(float now)
        {
            foreach (Seat seat in seats)
                if (seat != null && seat.occupied) ApplySeatVisual(seat, now, true);
        }

        private static void ApplySeatVisual(Seat seat, float now, bool animate)
        {
            float arrival = animate ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((now - seat.arrivedAt) / ArrivalDuration)) : 1f;
            Color color = seat.restingColor;
            color.a *= arrival;
            seat.avatar.color = color;
            seat.name.alpha = arrival;

            float stretch = animate && seat.alive
                ? Mathf.Sin(now * 1.7f + seat.breathingPhase) * BreathingAmount * arrival
                : 0f;
            Vector3 scale = seat.restingScale;
            scale.y *= 1f + stretch;
            // The pivot is where the character sits, so breathing stretches upward and never leaves the cushion.
            seat.avatar.rectTransform.localScale = scale;
            seat.avatar.rectTransform.anchoredPosition = seat.restingPosition;
        }

        private static Sprite SpriteFor(ChibiCharacterSkin skin, ChibiPose pose)
        {
            if (skin == null) return null;
            Sprite sprite;
            switch (pose)
            {
                case ChibiPose.SeatedFront: sprite = skin.SeatedFront; break;
                case ChibiPose.SeatedBack: sprite = skin.SeatedBack; break;
                case ChibiPose.SeatedSide: sprite = skin.SeatedSide; break;
                case ChibiPose.Seated1OClock: sprite = skin.Seated1OClock; break;
                case ChibiPose.Seated5OClock: sprite = skin.Seated5OClock; break;
                case ChibiPose.Seated7OClock: sprite = skin.Seated7OClock; break;
                case ChibiPose.Seated11OClock: sprite = skin.Seated11OClock; break;
                default: sprite = skin.Standing; break;
            }
            return sprite != null ? sprite : skin.Standing;
        }

        // Drawn before the table: the far row and the 3/9 o'clock side seats, whose laps the table rim hides.
        private bool IsFarSeat(int index) => index * 12f / capacity < 3.5f || index * 12f / capacity > 8.5f;

        private static ChibiPose PoseAtClock(float clock)
        {
            if (clock < 0.5f || clock > 11.5f) return ChibiPose.SeatedFront;
            if (clock < 2.5f) return ChibiPose.Seated1OClock;
            if (clock < 3.5f) return ChibiPose.SeatedSide;
            if (clock < 5.5f) return ChibiPose.Seated5OClock;
            if (clock < 6.5f) return ChibiPose.SeatedBack;
            if (clock < 8.5f) return ChibiPose.Seated7OClock;
            if (clock < 9.5f) return ChibiPose.SeatedSide;
            return ChibiPose.Seated11OClock;
        }

        private void ConfigureLayout()
        {
            for (int i = 0; i < capacity; i++)
            {
                float clock = i * 12f / capacity;
                int start = Mathf.FloorToInt(clock);
                layoutPositions[i] = Vector2.Lerp(SeatPositions[start], SeatPositions[(start + 1) % SeatCount], clock - start);
                if (capacity < 12) layoutPositions[i] *= Mathf.Lerp(0.97f, 1f, (capacity - 4f) / 8f);
            }
            var used = new bool[capacity];
            for (int ordinal = 0; ordinal < capacity; ordinal++)
            {
                int best = 0; int largestGap = -1;
                for (int candidate = 0; candidate < capacity; candidate++)
                {
                    if (used[candidate]) continue;
                    int gap = capacity;
                    for (int prior = 0; prior < ordinal; prior++)
                    {
                        int distance = Mathf.Abs(candidate - seatOrder[prior]);
                        gap = Mathf.Min(gap, Mathf.Min(distance, capacity - distance));
                    }
                    if (gap > largestGap) { largestGap = gap; best = candidate; }
                }
                seatOrder[ordinal] = best; used[best] = true;
            }
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite, Vector2 size, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }
    }
}

