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
    /// <see cref="UseSnugLayout"/> (GameScene3): as many chairs as players, characters bigger and right against the table.
    /// </summary>
    public sealed class DayRoundTableView : MonoBehaviour
    {
        private const int SeatCount = 12;
        private const float TableHeight = 850f;
        private const float ArrivalDuration = 0.7f;
        private const float BreathingAmount = 0.012f;
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
        // Speech bubbles (table coordinates): right over the speaker, on their name tag when it is over their head. If that
        // place overlaps a bubble already shown, someone's face or a name tag, the nearest free one a little higher or to
        // the side. Who speaks: the speaker's face on the bubble's corner, their name tag turned gold and a soft gold light
        // behind them. The message is as big as it fits: a short line at SpeechSizeMax, a long one down to SpeechSizeMin,
        // then cut.
        private static readonly Vector2 BubbleSize = new Vector2(240f, 112f);
        private const float BubbleGap = 10f, BustSize = 66f;
        private const float SpeechSizeMax = 32f, SpeechSizeMin = 24f;
        private const int SpeechLength = 40; // characters; the whole line stays in the chat
        private static readonly Color SpeakingBack = new Color(0.98f, 0.8f, 0.42f, 0.97f), SpeakingName = new Color(0.2f, 0.12f, 0.04f);

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
            public GameObject nameRoot;     // the label, or its pill (snug)
            public Image nameBack;          // snug: the dark pill behind the name
            public ChibiPose pose;
            public Vector2 anchor;          // chair floor point in table coordinates
            public float figureHeight;      // every skin is drawn this tall here
            public bool occupied;
            public long playerId;
            public bool alive;
            public float arrivedAt;
            public float breathingPhase;
            public ChibiCharacterSkin skin;
            public Sprite restingSprite;
            public Vector2 restingPosition;
            public Vector3 restingScale;
            public Color restingColor;
            public RectTransform bubble;
            public TextMeshProUGUI speechText;
            public CanvasGroup speechGroup;
            public Image speechBust;        // the speaker's face on the bubble's corner (its badge is hidden without one)
            public Image speakingGlow;      // soft gold light behind the speaker
            public Outline speakingOutline;
            public float speechUntil;
            public float speechStartedAt;
        }

        private readonly Seat[] seats = new Seat[SeatCount];
        private readonly RectTransform[] cardSpots = new RectTransform[SeatCount]; // by seat index, on the table rim
        private readonly List<Rect> placedBubbles = new List<Rect>(SeatCount);     // LayoutSpeech's bubbles so far
        private DesignSpriteCatalog portraits;                                       // busts for the bubbles' faces
        private readonly List<PlayerViewDto> players = new List<PlayerViewDto>(SeatCount);
        private readonly Dictionary<long, ChibiCharacterSkin> skinOverrides = new Dictionary<long, ChibiCharacterSkin>();
        private readonly Dictionary<long, ChibiCharacterSkin> wornSkins = new Dictionary<long, ChibiCharacterSkin>();
        private ChibiCharacterSkin[] skins;
        private Func<long, int> seatOrdinalFor;
        private Func<long, int> skinIndexFor;
        private RectTransform tableRoot;
        private RectTransform chatRect;
        private Image background;
        private Sprite originalBackground;
        private Color originalBackgroundColor;
        private DayOceanMotion oceanMotion;
        private Sprite previewSprite;
        private bool initialized;
        private int capacity = SeatCount;
        private float characterScale = 1f;
        private readonly Vector2[] layoutPositions = new Vector2[SeatCount];
        private readonly int[] seatOrder = new int[SeatCount];
        public int Capacity => capacity;
        private GameChatController gameChat;
        private TMP_FontAsset labelFont;
        private Sprite tableSprite;
        private Vector2 tableSize, tableOffset;
        private float depthTopY, depthBottomY;   // the farthest and nearest seat rows (depth scale 0.9 to 1.1)

        // Snug (GameScene3): as many chairs as players, evenly spaced around the table and right against it. Sizes and
        // places come from RoundTableLayout (tuned in the Inspector, live while playing) and the painted table
        // (TableTopCenterPx/RadiusPx, SkirtBottomPx), not from SeatPositions.
        private bool snug, relayingOut;
        private const float SkirtBottomPx = 940f; // the bottom of the table's base in RoundTable.png (y down)
        private static readonly Color NameBack = new Color(0.03f, 0.07f, 0.09f, 0.8f);
        private static RoundTableLayout Snug => RoundTableLayout.Current;
        private float SnugChairHeight => Snug.chairSize * Mathf.Lerp(Snug.fewPlayersScale, 1f, (capacity - 4f) / 8f);
        private readonly float[] seatDepth = new float[SeatCount]; // snug: depth scale of each seat (0.9 far to 1.1 near)

        /// <summary>The seats and card spots are about to be made again (snug layout, the player count changed).</summary>
        public event Action Rebuilding;

        /// <summary>Before Initialize: the snug table (chairs follow the player count, characters right against the table).</summary>
        public void UseSnugLayout()
        {
            if (!initialized) snug = true;
        }

        public void Initialize(Image backgroundImage, RectTransform chat, TMP_FontAsset font, ChibiCharacterSkin[] availableSkins, int maxPlayers = SeatCount)
        {
            if (initialized) return;
            initialized = true;
            background = backgroundImage;
            chatRect = chat;
            labelFont = font;
            skins = availableSkins != null && availableSkins.Length > 0 ? availableSkins : Resources.LoadAll<ChibiCharacterSkin>("DayTable/Skins");
            if (availableSkins == null) Array.Sort(skins, (a, b) => string.CompareOrdinal(a.name, b.name));
            tableSprite = Resources.Load<Sprite>("DayTable/RoundTable");
            previewSprite = Resources.Load<Sprite>("DayTable/PreviewCharacter");
            portraits = Resources.Load<DesignSpriteCatalog>("GameHud/Portraits");
            capacity = Mathf.Clamp(maxPlayers, 4, SeatCount);
            ConfigureLayout();
            if (background != null)
            {
                originalBackground = background.sprite;
                originalBackgroundColor = background.color;
                if (Application.isPlaying)
                {
                    oceanMotion = background.GetComponent<DayOceanMotion>();
                    if (oceanMotion == null) oceanMotion = background.gameObject.AddComponent<DayOceanMotion>();
                }
            }

            var root = new GameObject("DayRoundTable", typeof(RectTransform));
            root.layer = gameObject.layer;
            tableRoot = (RectTransform)root.transform;
            tableRoot.SetParent(transform, false);
            tableRoot.anchorMin = new Vector2(0f, 1f);
            tableRoot.anchorMax = new Vector2(1f, 1f);
            tableRoot.pivot = new Vector2(0.5f, 1f);
            tableRoot.sizeDelta = new Vector2(0f, TableHeight);
            PlaceRoot();
            if (chatRect != null) tableRoot.SetSiblingIndex(chatRect.GetSiblingIndex() + 1);

            BuildTable();
            SetVisible(true);
            ShowPreview();
            BindChat();
            if (snug && Application.isPlaying) RoundTableLayout.Changed += OnLayoutChanged; // the Edit mode preview is made again instead
        }

        private void OnDestroy()
        {
            RoundTableLayout.Changed -= OnLayoutChanged;
        }

        // On the HUD's stage (below the top HUD, above the chat), scaled from the height it is laid out for.
        private void PlaceRoot()
        {
            tableRoot.anchoredPosition = new Vector2(0f, -GameHudMetrics.StageTop);
            tableRoot.localScale = Vector3.one * GameHudMetrics.StageScale;
        }

        // A value changed in the Inspector while playing: the stage and the table are laid out again with everyone seated.
        private void OnLayoutChanged()
        {
            if (!initialized || !snug || tableRoot == null) return;
            GameHudMetrics.Use(GameHudMetrics.Layout);
            PlaceRoot();
            relayingOut = true;
            Rebuild(capacity);
            Refresh();
            relayingOut = false;
        }

        // The chairs, characters, table and card spots of the current capacity (ConfigureLayout first).
        private void BuildTable()
        {
            // Far and side seats sit behind the table. Near seats sit in front of its rim. Each group is drawn back to front.
            var farSeats = new List<int>();
            for (int i = 0; i < capacity; i++) if (IsFarSeat(i)) farSeats.Add(i);
            farSeats.Sort((a, b) => layoutPositions[b].y.CompareTo(layoutPositions[a].y));
            foreach (int i in farSeats) CreateSeat(i, labelFont);
            CreateImage("RoundTable", tableRoot, tableSprite, tableSize, tableOffset);
            // The revised table includes its front rim. The old overlay would restore coins over the clear card band.
            CreateCardSpots(tableSprite, tableSize, tableOffset);
            // Draw the near seats from farthest to closest, on both sides symmetrically.
            var nearSeats = new List<int>();
            for (int i = 0; i < capacity; i++) if (!IsFarSeat(i)) nearSeats.Add(i);
            nearSeats.Sort((a, b) => layoutPositions[b].y.CompareTo(layoutPositions[a].y));
            foreach (int i in nearSeats) CreateSeat(i, labelFont);
            for (int i = 0; i < capacity; i++) CreateSpeech(i, labelFont);
        }

        // Snug: the table made again for this many chairs. Who sits where comes back with the next Refresh.
        private void Rebuild(int seatCount)
        {
            Rebuilding?.Invoke();
            for (int i = tableRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = tableRoot.GetChild(i).gameObject;
                child.SetActive(false); // gone this frame (Destroy waits for the frame's end)
                Destroy(child);
            }
            Array.Clear(seats, 0, SeatCount);
            Array.Clear(cardSpots, 0, SeatCount);
            capacity = seatCount;
            ConfigureLayout();
            BuildTable();
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
            Vector2 radius = TableTopRadiusPx * scale * (snug ? Snug.cardSpotRadius : CardSpotRadius); // snug: clear of the near row's heads
            for (int i = 0; i < capacity; i++)
            {
                float angle = (90f - ClockOf(i) * 30f) * Mathf.Deg2Rad; // 12 o'clock = straight back, clockwise
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
            if (snug && initialized && currentPlayers != null && currentPlayers.Count > 0)
            {
                int seatCount = Mathf.Clamp(currentPlayers.Count, 4, SeatCount);
                if (seatCount != capacity) Rebuild(seatCount);
            }
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
            wornSkins.Clear();
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

        /// <summary>The skin this player wears at the table (null if not seated yet). The night room dresses them the same.</summary>
        public ChibiCharacterSkin SkinOf(long playerId)
        {
            ChibiCharacterSkin skin;
            return wornSkins.TryGetValue(playerId, out skin) ? skin : null;
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
            // Returning from night is a new seating beat. Repeated day/vote refreshes keep the motion running.
            if (visible && !tableRoot.gameObject.activeSelf && Application.isPlaying)
            {
                for (int i = 0; i < capacity; i++)
                {
                    Seat seat = seats[seatOrder[i]];
                    if (seat != null && seat.occupied && seat.alive)
                        seat.arrivedAt = Time.unscaledTime + i * 0.025f;
                }
            }
            tableRoot.gameObject.SetActive(visible);
            if (background != null)
            {
                Sprite deck = visible ? Resources.Load<Sprite>("DayTable/Deck") : null;
                background.sprite = visible && deck != null ? deck : originalBackground;
                background.color = visible && deck != null ? Color.white : originalBackgroundColor;
                background.preserveAspect = false;
                if (oceanMotion != null) oceanMotion.SetVisible(visible && deck != null);
            }
        }

        private void CreateSeat(int index, TMP_FontAsset font)
        {
            Vector2 position = layoutPositions[index];
            float clock = ClockOf(index);
            ChibiPose pose = PoseAtClock(clock);
            PoseLayout layout = LayoutFor(pose);
            float depth = snug ? seatDepth[index] : Mathf.Lerp(FarDepth, NearDepth, Mathf.InverseLerp(depthTopY, depthBottomY, position.y));
            // Fewer players: the seats behind the table grow; the near row keeps its size so its heads stay below
            // the rim (and below the card spots in front of everyone). Snug places the near row by its height instead.
            float chairHeight = (snug ? SnugChairHeight : ChairHeight * (layout.behindChair ? 1f : characterScale)) * depth;
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
            Image nameBack = null;
            if (snug)
            {
                // On the busy deck the name sits on a dark pill (sized to the name in Refresh).
                RectTransform tag = (RectTransform)new GameObject("NameTag", typeof(RectTransform)).transform;
                tag.gameObject.layer = gameObject.layer;
                tag.SetParent(anchor, false);
                tag.anchorMin = tag.anchorMax = new Vector2(0.5f, 0.5f);
                float tagHeight = Snug.nameSize * 1.46f;
                tag.sizeDelta = new Vector2(120f, tagHeight);
                tag.anchoredPosition = labelRect.anchoredPosition + (layout.behindChair ? new Vector2(0f, -4f) : new Vector2(0f, 2f));
                nameBack = HudShapes.Panel(tag, NameBack, new Color(0f, 0f, 0f, 0f), tagHeight * 0.5f);
                labelRect.SetParent(tag, false);
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            }
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.fontSize = snug ? Snug.nameSize : 22f;
            if (snug) label.fontStyle = FontStyles.Bold;
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
            GameObject nameRoot = nameBack != null ? nameBack.gameObject : label.gameObject;
            nameRoot.SetActive(false);

            seats[index] = new Seat
            {
                avatar = avatar,
                name = label,
                nameRoot = nameRoot,
                nameBack = nameBack,
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
                    seat.arrivedAt = relayingOut ? -10f : Time.unscaledTime + ordinal * 0.025f;
                    HideSpeech(seat);
                }
                seat.occupied = true;
                seat.playerId = players[i].playerId;
                seat.alive = players[i].alive;
                if (!seat.alive) HideSpeech(seat);
                seat.avatar.gameObject.SetActive(true);
                seat.nameRoot.SetActive(true);

                ChibiCharacterSkin skin;
                if (!skinOverrides.TryGetValue(players[i].playerId, out skin) || skin == null)
                {
                    int skinIndex = skinIndexFor != null ? skinIndexFor(players[i].playerId) : -1;
                    if (skinIndex < 0) skinIndex = ordinal;
                    skin = skins != null && skins.Length > 0 ? skins[skinIndex % skins.Length] : null;
                }
                wornSkins[players[i].playerId] = skin;
                seat.skin = skin;
                seat.restingSprite = SpriteFor(skin, seat.pose) ?? previewSprite;
                ApplySprite(seat, seat.restingSprite);
                seat.restingColor = seat.alive ? Color.white : new Color(0.55f, 0.6f, 0.65f, 0.6f);
                seat.name.text = players[i].nickname;
                if (seat.nameBack != null)
                {
                    float width = Mathf.Clamp(seat.name.GetPreferredValues(seat.name.text).x + 28f, 64f, 220f);
                    seat.nameBack.rectTransform.sizeDelta = new Vector2(width, seat.nameBack.rectTransform.sizeDelta.y);
                }
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
                seat.avatar.rectTransform.localRotation = Quaternion.identity;
                seat.avatar.gameObject.SetActive(false);
                seat.nameRoot.SetActive(false);
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
            if (gameChat != null) gameChat.LiveMessageReceived -= OnLiveMessage;
            gameChat = null;
            ClearSpeech();
        }

        private void BindChat()
        {
            if (!Application.isPlaying) return;
            foreach (var chat in FindObjectsByType<GameChatController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (chat.gameObject.scene == gameObject.scene && gameChat == null)
                { gameChat = chat; gameChat.LiveMessageReceived += OnLiveMessage; }
        }

        private void OnLiveMessage(ChatMessage message)
        {
            if (message == null || message.IsSystem || message.IsDead || message.nightChat || message.sirenChat) return;
            if (long.TryParse(message.userId, out long userId)) ShowSpeech(userId, message.content);
        }

        public void ShowSpeech(long userId, string message)
        {
            if (tableRoot == null || !tableRoot.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(message)) return;
            foreach (Seat seat in seats)
            {
                if (seat == null || !seat.occupied || !seat.alive || seat.playerId != userId) continue;
                seat.speechText.text = ShortText(message, SpeechLength);
                seat.speechStartedAt = Time.unscaledTime;
                seat.speechUntil = Time.unscaledTime + 4.5f;
                seat.speechGroup.alpha = 1f;
                seat.bubble.gameObject.SetActive(true);
                seat.speakingOutline.enabled = true;
                seat.speakingGlow.color = new Color(1f, 0.82f, 0.35f, 0.8f);
                seat.speakingGlow.gameObject.SetActive(true);
                seat.name.color = new Color(SpeakingName.r, SpeakingName.g, SpeakingName.b, seat.name.alpha);
                if (seat.nameBack != null) seat.nameBack.color = SpeakingBack;
                Sprite bust = BustOf(seat.playerId);
                seat.speechBust.sprite = bust;
                seat.speechBust.transform.parent.parent.gameObject.SetActive(bust != null); // the badge
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
            // A soft gold light behind the speaker (behind their chair too).
            float glow = seat.figureHeight * 1.4f;
            seat.speakingGlow = CreateImage("SpeakingGlow", seat.avatar.transform.parent, NightFxSprites.Dot, new Vector2(glow, glow),
                seat.restingPosition + new Vector2(0f, seat.figureHeight * 0.5f));
            seat.speakingGlow.preserveAspect = false;
            seat.speakingGlow.transform.SetAsFirstSibling();
            Image panel = CreateImage("SpeechBubble_" + index, tableRoot, null, BubbleSize, Vector2.zero);
            HudShapes.Panel(panel.rectTransform, new Color32(255, 244, 216, 255), new Color32(110, 72, 30, 255), 16f);
            seat.bubble = panel.rectTransform;
            seat.speechGroup = panel.gameObject.AddComponent<CanvasGroup>();
            seat.speechGroup.blocksRaycasts = false;
            seat.speechGroup.interactable = false;
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.layer = gameObject.layer;
            var rect = (RectTransform)textObject.transform;
            rect.SetParent(panel.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(BustSize * 0.5f + 12f, 8f); // clear of the face on the top-left corner
            rect.offsetMax = new Vector2(-14f, -8f);
            seat.speechText = textObject.GetComponent<TextMeshProUGUI>();
            if (font != null) seat.speechText.font = font;
            seat.speechText.fontSize = SpeechSizeMax;
            seat.speechText.enableAutoSizing = true;
            seat.speechText.fontSizeMin = SpeechSizeMin;
            seat.speechText.fontSizeMax = SpeechSizeMax;
            seat.speechText.richText = false;
            seat.speechText.textWrappingMode = TextWrappingModes.Normal;
            seat.speechText.overflowMode = TextOverflowModes.Ellipsis;
            seat.speechText.maxVisibleLines = 3;
            seat.speechText.color = new Color32(53, 35, 21, 255);
            seat.speechText.alignment = TextAlignmentOptions.Center;
            seat.speechText.raycastTarget = false;

            // The speaker's face in a gold ring on the bubble's top-left corner (ShowSpeech puts in their bust).
            var badge = (RectTransform)new GameObject("Speaker", typeof(RectTransform)).transform;
            badge.gameObject.layer = gameObject.layer;
            badge.SetParent(panel.transform, false);
            badge.anchorMin = badge.anchorMax = new Vector2(0f, 1f);
            badge.sizeDelta = new Vector2(BustSize, BustSize);
            badge.anchoredPosition = new Vector2(10f, -12f);
            Image disc = CreateImage("Disc", badge, NightFxSprites.Circle, new Vector2(BustSize, BustSize), Vector2.zero);
            disc.color = new Color(0.12f, 0.14f, 0.18f);
            disc.preserveAspect = false;
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            seat.speechBust = CreateImage("Bust", disc.transform, null, Vector2.zero, Vector2.zero);
            RectTransform bust = seat.speechBust.rectTransform;
            bust.anchorMin = Vector2.zero;
            bust.anchorMax = Vector2.one;
            bust.offsetMin = new Vector2(-6f, -10f);
            bust.offsetMax = new Vector2(6f, 2f);
            Image ring = CreateImage("Ring", badge, null, new Vector2(BustSize + 6f, BustSize + 6f), Vector2.zero);
            HudShapes.Panel(ring.rectTransform, new Color(0f, 0f, 0f, 0f), SpeakingBack, (BustSize + 6f) * 0.5f);
            HideSpeech(seat);
        }

        // The bust of the skin this player wears at the table (Resources/GameHud/Portraits), or null.
        private Sprite BustOf(long playerId)
        {
            ChibiCharacterSkin skin = SkinOf(playerId);
            return skin != null && portraits != null ? portraits.Find(skin.DesignId) : null;
        }

        private static void HideSpeech(Seat seat)
        {
            seat.speechUntil = 0f;
            if (seat.bubble != null) seat.bubble.gameObject.SetActive(false);
            if (seat.speakingOutline != null) seat.speakingOutline.enabled = false;
            if (seat.speakingGlow != null) seat.speakingGlow.gameObject.SetActive(false);
            if (seat.name != null) seat.name.color = new Color(1f, 1f, 1f, seat.name.alpha);
            if (seat.nameBack != null) seat.nameBack.color = new Color(NameBack.r, NameBack.g, NameBack.b, NameBack.a * seat.name.alpha);
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
                seat.speakingGlow.color = new Color(1f, 0.82f, 0.35f, 0.8f * alpha);
                seat.speakingOutline.effectColor = new Color(1f, 0.8f, 0.25f, (0.4f + 0.15f * Mathf.Sin(now * 4f)) * alpha);
            }
            if (changed) LayoutSpeech();
        }

        private void LayoutSpeech()
        {
            // Each speaker in turn: their bubble right over them, or the nearest free place a little higher or to the
            // side. Only if none is left does a bubble overlap (a face or a name tag first, then another bubble).
            placedBubbles.Clear();
            Vector2 half = BubbleSize * 0.5f;
            for (int i = 0; i < SeatCount; i++)
            {
                Seat seat = seats[i];
                if (seat == null || seat.speechUntil <= 0f) continue;
                Vector2 home = SpeakerTop(seat) + new Vector2(0f, BubbleGap + half.y);
                Rect bestBounds = default; float bestScore = float.MaxValue;
                for (int up = 0; up < 4; up++)
                {
                    for (int side = 0; side < 9; side++)
                    {
                        float dx = (side + 1) / 2 * 70f * (side % 2 == 1 ? -1f : 1f); // 0, -70, 70, -140, 140, ...
                        Vector2 c = home + new Vector2(dx, up * 60f);
                        c.x = Mathf.Clamp(c.x, -540f + half.x, 540f - half.x); // on the stage
                        c.y = Mathf.Clamp(c.y, -TableHeight * 0.5f + half.y, TableHeight * 0.5f - half.y);
                        var bounds = new Rect(c - half, BubbleSize);
                        float score = (c - home).sqrMagnitude;
                        foreach (Rect placed in placedBubbles) if (bounds.Overlaps(placed)) score += 1e9f;
                        for (int other = 0; other < SeatCount; other++)
                        {
                            if (seats[other] == null || !seats[other].occupied) continue;
                            Vector2 faceSize = new Vector2(0.8f, 0.55f) * seats[other].figureHeight;
                            if (bounds.Overlaps(new Rect(HeadOf(seats[other]) - faceSize * 0.5f, faceSize))) score += 1e6f;
                            if (bounds.Overlaps(TagRect(seats[other]))) score += 1e6f; // keep the name tags readable
                        }
                        if (score < bestScore) { bestScore = score; bestBounds = bounds; }
                    }
                }
                placedBubbles.Add(bestBounds);
                seat.bubble.anchoredPosition = bestBounds.center;
            }
            foreach (Seat seat in seats) if (seat != null && seat.bubble != null) seat.bubble.SetAsLastSibling();
        }

        // A seat's name tag in table coordinates.
        private static Rect TagRect(Seat seat)
        {
            var tag = (RectTransform)seat.nameRoot.transform;
            return new Rect(seat.anchor + tag.anchoredPosition - tag.sizeDelta * 0.5f, tag.sizeDelta);
        }

        // The top of a speaker: their (gold) name tag when it is over their head, else just over the head.
        private static Vector2 SpeakerTop(Seat seat)
        {
            if (LayoutFor(seat.pose).behindChair) return HeadOf(seat) + new Vector2(0f, seat.figureHeight * 0.18f);
            Rect tag = TagRect(seat);
            return new Vector2(tag.center.x, tag.yMax);
        }

        private void AnimateSeats(float now)
        {
            foreach (Seat seat in seats)
                if (seat != null && seat.occupied) ApplySeatVisual(seat, now, true);
        }

        private static void ApplySeatVisual(Seat seat, float now, bool animate)
        {
            bool moving = animate && seat.alive;
            float progress = moving ? Mathf.Clamp01((now - seat.arrivedAt) / ArrivalDuration) : 1f;
            float arrival = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * 3f));
            Color color = seat.restingColor;
            color.a *= arrival;
            seat.avatar.color = color;
            seat.name.alpha = arrival;
            if (seat.nameBack != null)
            {
                Color back = seat.speechUntil > 0f ? SpeakingBack : NameBack; // the speaker's tag stays gold
                seat.nameBack.color = new Color(back.r, back.g, back.b, back.a * arrival);
            }

            float phase = seat.breathingPhase;
            float breath = moving ? Mathf.Sin(now * (1.65f + 0.12f * Mathf.Sin(phase)) + phase) * BreathingAmount * arrival : 0f;
            float sway = moving ? Mathf.Sin(now * 0.85f + phase) * 0.65f * arrival : 0f;
            float speaking = moving && seat.speechUntil > now
                ? Mathf.SmoothStep(0f, 1f, (now - seat.speechStartedAt) / 0.18f) * Mathf.Clamp01((seat.speechUntil - now) / 0.4f)
                : 0f;
            float nod = Mathf.Sin((now - seat.speechStartedAt) * 11f + phase) * speaking;
            // A short downward settle, then one soft compression/recovery. The feet stay on the cushion at rest.
            float drop = (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.45f))) * seat.figureHeight * 0.065f;
            float settle = progress < 0.3f ? 0f : Mathf.Sin((progress - 0.3f) / 0.7f * Mathf.PI * 2f) * (1f - progress) * 0.07f;
            Vector3 scale = seat.restingScale;
            scale.x *= 1f - breath * 0.35f + settle * 0.45f;
            scale.y *= 1f + breath - settle + nod * 0.009f;
            seat.avatar.rectTransform.localScale = scale;
            seat.avatar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, sway + nod * 0.7f);
            seat.avatar.rectTransform.anchoredPosition = seat.restingPosition + Vector2.up * drop;

            // Only front poses have expression frames. Other directions keep their own artwork and gesture in place.
            Sprite sprite = seat.restingSprite;
            if (moving && seat.pose == ChibiPose.SeatedFront && seat.skin != null)
            {
                bool blink = Mathf.Repeat(now + phase, 3.6f + 0.6f * Mathf.Sin(phase)) < 0.13f;
                bool talk = speaking > 0.1f && Mathf.Sin(now * 16f + phase) > 0f;
                if (blink && seat.skin.SeatedFrontBlink != null) sprite = seat.skin.SeatedFrontBlink;
                else if (talk && seat.skin.SeatedFrontTalk != null) sprite = seat.skin.SeatedFrontTalk;
            }
            if (seat.avatar.sprite != sprite) ApplySprite(seat, sprite);
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
        private bool IsFarSeat(int index)
        {
            float clock = ClockOf(index);
            return clock < 3.5f || clock > 8.5f;
        }

        // Where a seat is around the table, in hours clockwise from 12 o'clock (snug: turned by seatRotation).
        private float ClockOf(int index)
        {
            float clock = index * 12f / capacity + (snug ? Snug.seatRotation / 30f : 0f);
            clock %= 12f;
            return clock < 0f ? clock + 12f : clock;
        }

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
            characterScale = Mathf.Lerp(1.25f, 1f, (capacity - 4f) / 8f);
            if (snug)
            {
                tableSize = new Vector2(Snug.tableWidth, Snug.tableWidth / 1.5f);
                tableOffset = new Vector2(0f, Snug.tableY);
                SnugPositions();
                // Every chair is taken: players sit clockwise in the server's order (the same on every screen).
                for (int i = 0; i < capacity; i++) seatOrder[i] = i;
                return;
            }
            else
            {
                tableSize = new Vector2(Mathf.Lerp(840f, 900f, (capacity - 4f) / 8f), 600f);
                tableOffset = new Vector2(0f, -75f);
                for (int i = 0; i < capacity; i++)
                {
                    float clock = i * 12f / capacity;
                    int start = Mathf.FloorToInt(clock);
                    layoutPositions[i] = Vector2.Lerp(SeatPositions[start], SeatPositions[(start + 1) % SeatCount], clock - start);
                    if (capacity < 12) layoutPositions[i] *= Mathf.Lerp(0.97f, 1f, (capacity - 4f) / 8f);
                }
                depthTopY = SeatPositions[0].y;
                depthBottomY = SeatPositions[6].y;
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

        // Snug seats, evenly spaced: a ring (ellipse) around the whole painted table, its middle halfway between the table
        // top's back rim and the bottom of the base, its radii the table's half width and half height plus the padding.
        // Each character's body middle sits on the ring at equal angles, so the far row shows above the table top, the
        // sides overlap its rim and the near row (seen from behind) stands against its base, whatever the count.
        // The chair's floor point is the body middle minus the pose's height to it (feet on the cushion + half a figure).
        private void SnugPositions()
        {
            RoundTableLayout l = Snug;
            Vector2 imageSize = tableSprite != null ? tableSprite.rect.size : new Vector2(1536f, 1024f);
            float scale = Mathf.Min(tableSize.x / imageSize.x, tableSize.y / imageSize.y);
            Vector2 center = tableOffset + new Vector2(TableTopCenterPx.x - imageSize.x * 0.5f, imageSize.y * 0.5f - TableTopCenterPx.y) * scale;
            Vector2 radius = TableTopRadiusPx * scale;
            float backRim = center.y + radius.y;
            float baseBottom = tableOffset.y + (imageSize.y * 0.5f - SkirtBottomPx) * scale;
            var ringCenter = new Vector2(center.x, (backRim + baseBottom) * 0.5f + l.ringOffsetY);
            var ring = new Vector2(radius.x + l.ringPaddingX, (backRim - baseBottom) * 0.5f + l.ringPaddingY);
            float chair = SnugChairHeight;
            for (int i = 0; i < capacity; i++)
            {
                float clock = ClockOf(i);
                float angle = clock * 30f * Mathf.Deg2Rad;
                float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
                Vector2 middle = ringCenter + new Vector2(ring.x * sin, ring.y * cos);
                PoseLayout layout = LayoutFor(PoseAtClock(clock));
                seatDepth[i] = Mathf.Lerp(FarDepth, NearDepth, (1f - cos) * 0.5f); // farther back, a little smaller
                float body = (layout.foot.y + layout.height * 0.5f) * chair * seatDepth[i];
                layoutPositions[i] = new Vector2(middle.x, middle.y - body);
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

