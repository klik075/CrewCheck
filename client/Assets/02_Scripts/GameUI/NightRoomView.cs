using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// The night scene: the room of my role (<see cref="NightSceneBackgroundCatalog"/>) with my character standing in it,
    /// on the stage the day table uses (below the role card, above the chat). Pirates who know each other stand
    /// together in the pirates' room; who is in the room comes from <see cref="NightRoomRules"/>.
    /// - The room changes with a crossfade (the parrot's room turns into the pirates' room when it makes contact) and a
    ///   member who joins a room already on screen walks in from the side (the pirates see the parrot arrive).
    /// - <see cref="PlayAbility"/>: when my night action is accepted, the role's cut-in (<see cref="AbilityCutInView"/>)
    ///   and then the role's motion on my character (<see cref="NightAbilityMotion"/>). Room changes wait until it ends.
    /// Characters wear the same skins as at the day table (pivot at the feet, see Tools/ChibiSkinFit), drawn one height.
    /// </summary>
    public sealed class NightRoomView : MonoBehaviour
    {
        // The stage (canvas units from the top): the same strip the day table uses. The open night drawer covers its
        // bottom 100 px, so feet stand above that.
        private const float StageTop = 230f, StageHeight = 850f;
        // Feet in stage coordinates (centre origin, y up): me in front, the others a little behind.
        private const float FrontFeetY = -255f, BackFeetY = -205f;
        // The night art is drawn full width and moved up so that its deck (about 60% down the picture) is under the feet.
        private const float DeckRow = 0.6f;
        private const float SoloHeight = 360f, GroupHeight = 330f, OtherScale = 0.92f;
        private const float MaxSpacing = 270f, GroupWidth = 780f;
        private const float AppearSeconds = 0.6f, WalkSeconds = 1.2f, MoveSeconds = 0.7f, LeaveSeconds = 0.5f, CrossfadeSeconds = 0.8f;
        private const float BreathingAmount = 0.006f;
        private const string FallbackDesign = "CREW_SAILOR"; // a plain deck for a role without its own room
        private static readonly Color Shade = new Color(0.02f, 0.04f, 0.07f, 0.85f);
        private static readonly Color MyName = new Color(1f, 0.84f, 0.4f);
        private static readonly Color Ghost = new Color(0.7f, 0.82f, 1f, 0.45f);

        private sealed class Figure
        {
            public long playerId;
            public RectTransform holder;     // at the feet; moves between slots
            public Image avatar;             // pivot at the feet, local rest (0,0); the ability motion drives it
            public Image shadow;
            public TextMeshProUGUI label;
            public ChibiCharacterSkin skin;
            public float height;
            public Vector2 from, to;
            public float moveStart, moveSeconds;
            public bool walk;                // walking in from the side (bobs)
            public float appearStart;
            public float leaveStart = -1f;
            public bool alive = true, mine;
            public float phase;
        }

        private readonly List<Figure> figures = new List<Figure>();
        private readonly List<Vector2> slotPositions = new List<Vector2>();
        private readonly List<float> slotHeights = new List<float>();
        private readonly List<PlayerViewDto> pendingMembers = new List<PlayerViewDto>();
        private RectTransform canvas;
        private RectTransform stage, content, backFx, figureLayer, frontFx;
        private Image backdrop, oldBackdrop, shadeFade, shadeSolid;
        private NightSceneBackgroundCatalog backgrounds;
        private AbilityCutInCatalog cutIns;
        private AbilityCutInView cutIn;
        private NightAbilityMotion motion;
        private TMP_FontAsset font;
        private Func<long, ChibiCharacterSkin> skinOf;
        private Sprite previewSprite;
        private bool initialized, visible, roomBuilt, motionBusy, hasPending, abilityRunning;
        private string currentDesign, pendingDesign;
        private long myId;
        private float crossfadeStart = -1f;
        private float laidOutWidth = -1f;
        private Coroutine abilityRoutine;

        /// <param name="chat">The chat panel; the stage is drawn just above it, like the day table.</param>
        /// <param name="skinFor">The skin each player wears (the day table's, so nobody changes clothes at night).</param>
        public void Initialize(RectTransform chat, TMP_FontAsset labelFont, Func<long, ChibiCharacterSkin> skinFor)
        {
            if (initialized) return;
            initialized = true;
            canvas = (RectTransform)transform;
            font = labelFont;
            skinOf = skinFor;
            backgrounds = Resources.Load<NightSceneBackgroundCatalog>("NightScene/NightSceneBackgrounds");
            cutIns = Resources.Load<AbilityCutInCatalog>("NightScene/AbilityCutIns");
            previewSprite = Resources.Load<Sprite>("DayTable/PreviewCharacter");

            // Backdrop and shade go right above the screen background, under every panel.
            oldBackdrop = CreateBackdrop("NightBackdropPrevious");
            backdrop = CreateBackdrop("NightBackdrop");
            shadeFade = CreateImage("NightShadeFade", canvas, NightFxSprites.Fade, Shade);
            shadeFade.rectTransform.anchorMin = new Vector2(0f, 1f);
            shadeFade.rectTransform.anchorMax = new Vector2(1f, 1f);
            shadeFade.rectTransform.pivot = new Vector2(0.5f, 1f);
            shadeFade.rectTransform.anchoredPosition = new Vector2(0f, -(StageTop + StageHeight - 120f));
            shadeFade.rectTransform.sizeDelta = new Vector2(0f, 280f);
            shadeSolid = CreateImage("NightShade", canvas, null, Shade);
            shadeSolid.rectTransform.anchorMin = Vector2.zero;
            shadeSolid.rectTransform.anchorMax = Vector2.one;
            shadeSolid.rectTransform.offsetMin = Vector2.zero;
            shadeSolid.rectTransform.offsetMax = new Vector2(0f, -(StageTop + StageHeight + 159f));
            Transform screenBackground = canvas.Find("Background");
            int under = screenBackground != null ? screenBackground.GetSiblingIndex() + 1 : 0;
            oldBackdrop.transform.SetSiblingIndex(under);
            backdrop.transform.SetSiblingIndex(under + 1);
            shadeFade.transform.SetSiblingIndex(under + 2);
            shadeSolid.transform.SetSiblingIndex(under + 3);

            stage = NewRect("NightRoom", canvas);
            stage.anchorMin = new Vector2(0f, 1f);
            stage.anchorMax = new Vector2(1f, 1f);
            stage.pivot = new Vector2(0.5f, 1f);
            stage.anchoredPosition = new Vector2(0f, -StageTop);
            stage.sizeDelta = new Vector2(0f, StageHeight);
            if (chat != null) stage.SetSiblingIndex(chat.GetSiblingIndex() + 1);
            content = NewCentred("Content", stage);
            backFx = NewCentred("EffectsBehind", content);
            figureLayer = NewCentred("Figures", content);
            frontFx = NewCentred("EffectsFront", content);
            motion = new NightAbilityMotion(backFx, frontFx, content);
            SetVisible(false);
        }

        /// <summary>Shown at night and while the night's result is read; hidden otherwise.</summary>
        public void SetPhase(string phase)
        {
            bool show = phase == GamePhases.Night || phase == GamePhases.NightResult;
            if (initialized && show != visible) SetVisible(show);
        }

        /// <summary>
        /// The room and who is in it (me first, see <see cref="NightRoomRules.RoomFor"/>). Same values do nothing, so it
        /// can follow every poll. While an ability motion plays the change waits for its end.
        /// </summary>
        public void SetRoom(string designId, IList<PlayerViewDto> members, long myPlayerId)
        {
            if (!initialized || members == null) return;
            myId = myPlayerId;
            if (abilityRunning)
            {
                hasPending = true;
                pendingDesign = designId;
                pendingMembers.Clear();
                pendingMembers.AddRange(members);
                return;
            }
            ApplyRoom(designId, members);
        }

        /// <summary>
        /// My night action was accepted: the role's cut-in (only when withCutIn, e.g. the first choice of the night), then
        /// the ability's motion on my character. A new call cuts the previous one short.
        /// </summary>
        public void PlayAbility(string designId, string actionCode, string roleTitle, string abilityLine, bool withCutIn)
        {
            if (!initialized || !visible || !isActiveAndEnabled) return;
            StopAbility();
            Sprite art = null;
            bool hasGameplayAbility;
            if (withCutIn && cutIns != null) cutIns.TryFind(designId, out art, out hasGameplayAbility);
            abilityRunning = true;
            abilityRoutine = StartCoroutine(Flatten(AbilityRoutine(art, actionCode, roleTitle, abilityLine)));
            if (!abilityRunning) abilityRoutine = null; // nothing to play: it already ended in StartCoroutine
        }

        /// <summary>New game: nobody in the room, hidden.</summary>
        public void Clear()
        {
            if (!initialized) return;
            StopAbility();
            hasPending = false;
            foreach (Figure f in figures) Destroy(f.holder.gameObject);
            figures.Clear();
            currentDesign = null;
            roomBuilt = false;
            SetVisible(false);
        }

        private IEnumerator AbilityRoutine(Sprite art, string actionCode, string roleTitle, string abilityLine)
        {
            if (art != null)
            {
                if (cutIn == null) cutIn = AbilityCutInView.Create(canvas, font);
                yield return cutIn.Play(art, roleTitle, abilityLine, NightAbilityMotion.TintFor(actionCode));
            }
            Figure me = FindFigure(myId);
            if (me != null && me.alive && me.leaveStart < 0f)
            {
                motionBusy = true;
                yield return motion.Play(actionCode, me.avatar.rectTransform, me.holder.anchoredPosition, me.height);
                motionBusy = false;
            }
            abilityRunning = false;
            abilityRoutine = null;
            ApplyPending();
        }

        private void StopAbility()
        {
            if (abilityRoutine != null)
            {
                StopCoroutine(abilityRoutine);
                abilityRoutine = null;
            }
            abilityRunning = false;
            if (motion != null) motion.Reset();
            motionBusy = false;
            if (cutIn != null) cutIn.Hide();
            ApplyPending();
        }

        private void ApplyPending()
        {
            if (!hasPending) return;
            hasPending = false;
            ApplyRoom(pendingDesign, pendingMembers);
        }

        // Runs nested enumerators inside one coroutine, so StopCoroutine stops the cut-in and the motion with it
        // (yielding an IEnumerator to Unity would start a separate coroutine).
        private static IEnumerator Flatten(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                yield return top.Current;
            }
        }

        private void OnDisable()
        {
            StopAbility();
        }

        // ------------------------------------------------------------------ room

        private void ApplyRoom(string designId, IList<PlayerViewDto> members)
        {
            float now = Time.unscaledTime;
            string design = string.IsNullOrEmpty(designId) ? FallbackDesign : designId;
            bool newRoom = design != currentDesign;
            if (newRoom)
            {
                ShowBackdrop(design, visible && currentDesign != null);
                currentDesign = design;
            }
            LayoutSlots(members.Count);
            var present = new HashSet<long>();
            for (int i = 0; i < members.Count; i++)
            {
                PlayerViewDto member = members[i];
                if (member == null || !present.Add(member.playerId)) continue;
                Vector2 slot = slotPositions[i];
                Figure f = FindFigure(member.playerId);
                if (f == null)
                {
                    f = CreateFigure(member.playerId);
                    f.from = f.to = slot;
                    if (visible && roomBuilt && !newRoom)
                    {
                        // Joins a room already on screen: walks in from the nearer side.
                        float side = slot.x < 0f ? -1f : 1f;
                        f.from = new Vector2(side * (canvas.rect.width * 0.5f + 160f), slot.y);
                        f.walk = true;
                        f.moveStart = now;
                        f.moveSeconds = WalkSeconds;
                        f.appearStart = now - AppearSeconds;
                    }
                    else
                    {
                        f.appearStart = now;
                    }
                }
                else if (f.leaveStart >= 0f)
                {
                    f.leaveStart = -1f;
                    f.appearStart = now;
                }
                if (f.to != slot)
                {
                    f.from = PositionOf(f, now);
                    f.to = slot;
                    f.moveStart = now;
                    f.moveSeconds = MoveSeconds;
                    f.walk = false;
                }
                f.alive = member.alive;
                f.mine = member.playerId == myId;
                ChibiCharacterSkin skin = skinOf != null ? skinOf(member.playerId) : null;
                if (skin != f.skin || !Mathf.Approximately(f.height, slotHeights[i]) || f.avatar.sprite == null)
                {
                    f.skin = skin;
                    f.height = slotHeights[i];
                    ApplySprite(f);
                }
                f.label.text = member.nickname;
                f.label.color = f.mine ? MyName : Color.white;
            }
            foreach (Figure f in figures)
            {
                if (!present.Contains(f.playerId) && f.leaveStart < 0f) f.leaveStart = now;
            }
            // Draw back to front.
            figures.Sort((a, b) =>
            {
                int depth = b.to.y.CompareTo(a.to.y);
                return depth != 0 ? depth : a.playerId.CompareTo(b.playerId);
            });
            for (int i = 0; i < figures.Count; i++) figures[i].holder.SetSiblingIndex(i);
            roomBuilt = true;
        }

        // Me in the middle and in front; the others spread to both sides, one step back.
        private void LayoutSlots(int count)
        {
            slotPositions.Clear();
            slotHeights.Clear();
            if (count <= 1)
            {
                slotPositions.Add(new Vector2(0f, FrontFeetY));
                slotHeights.Add(SoloHeight);
                return;
            }
            float spacing = Mathf.Min(MaxSpacing, GroupWidth / (count - 1));
            int middle = count / 2;
            for (int member = 0; member < count; member++)
            {
                // member 0 (me) takes the middle slot; the rest fill the others from left to right.
                int slot = member == 0 ? middle : (member - 1 < middle ? member - 1 : member);
                float x = (slot - (count - 1) * 0.5f) * spacing;
                int distance = Mathf.Abs(slot - middle);
                float y = distance == 0 ? FrontFeetY : (distance % 2 == 1 ? BackFeetY : (FrontFeetY + BackFeetY) * 0.5f);
                slotPositions.Add(new Vector2(x, y));
                slotHeights.Add(distance == 0 ? GroupHeight : GroupHeight * OtherScale);
            }
        }

        private void SetVisible(bool show)
        {
            visible = show;
            if (!show) StopAbility();
            stage.gameObject.SetActive(show);
            backdrop.gameObject.SetActive(show && backdrop.sprite != null);
            oldBackdrop.gameObject.SetActive(false);
            crossfadeStart = -1f;
            backdrop.color = Color.white;
            shadeFade.gameObject.SetActive(show);
            shadeSolid.gameObject.SetActive(show);
            float now = Time.unscaledTime;
            for (int i = figures.Count - 1; i >= 0; i--)
            {
                Figure f = figures[i];
                if (f.leaveStart >= 0f)
                {
                    Destroy(f.holder.gameObject);
                    figures.RemoveAt(i);
                    continue;
                }
                // Each night starts with everyone already in place, fading in.
                f.from = f.to;
                f.walk = false;
                f.moveSeconds = 0f;
                f.appearStart = now;
            }
            if (show) laidOutWidth = -1f;
        }

        private void ShowBackdrop(string design, bool crossfade)
        {
            Sprite sprite = SpriteFor(design);
            if (crossfade && backdrop.sprite != null && sprite != backdrop.sprite)
            {
                oldBackdrop.sprite = backdrop.sprite;
                oldBackdrop.color = Color.white;
                oldBackdrop.gameObject.SetActive(true);
                LayoutBackdrop(oldBackdrop);
                crossfadeStart = Time.unscaledTime;
                backdrop.color = new Color(1f, 1f, 1f, 0f);
            }
            else
            {
                crossfadeStart = -1f;
                oldBackdrop.gameObject.SetActive(false);
                backdrop.color = Color.white;
            }
            backdrop.sprite = sprite;
            backdrop.gameObject.SetActive(visible && sprite != null);
            LayoutBackdrop(backdrop);
        }

        private Sprite SpriteFor(string design)
        {
            if (backgrounds == null) return null;
            if (design == NightRoomRules.PirateShared) return backgrounds.PirateShared;
            Sprite sprite = backgrounds.Find(design);
            return sprite != null ? sprite : backgrounds.Find(FallbackDesign);
        }

        // Full width, moved up so the art's deck row is where the front feet stand.
        private void LayoutBackdrop(Image image)
        {
            Sprite sprite = image.sprite;
            if (sprite == null) return;
            float width = canvas.rect.width;
            float height = width * sprite.rect.height / sprite.rect.width;
            float feetFromTop = StageTop + StageHeight * 0.5f - FrontFeetY;
            image.rectTransform.sizeDelta = new Vector2(0f, height);
            image.rectTransform.anchoredPosition = new Vector2(0f, DeckRow * height - feetFromTop);
        }

        // ------------------------------------------------------------------ figures

        private Figure CreateFigure(long playerId)
        {
            RectTransform holder = NewCentred("Player_" + playerId, figureLayer);
            Image shadow = CreateImage("Shadow", holder, NightFxSprites.Dot, new Color(0f, 0f, 0f, 0.5f));
            Image avatar = CreateImage("Character", holder, null, Color.white);
            avatar.rectTransform.anchorMin = avatar.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            var labelObject = new GameObject("PlayerName", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.layer = holder.gameObject.layer;
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(holder, false);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(240f, 36f);
            labelRect.anchoredPosition = new Vector2(0f, -24f);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.fontSize = 26f;
            label.alignment = TextAlignmentOptions.Center;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.richText = false;
            label.raycastTarget = false;
            if (label.font != null)
            {
                label.outlineWidth = 0.2f;
                label.outlineColor = new Color32(0x0A, 0x16, 0x23, 0xFF);
            }
            var f = new Figure
            {
                playerId = playerId,
                holder = holder,
                avatar = avatar,
                shadow = shadow,
                label = label,
                phase = figures.Count * 2.39996f
            };
            figures.Add(f);
            return f;
        }

        /// <summary>The standing pose of the skin: its pivot (feet) on the holder, scaled to the figure's height.</summary>
        private void ApplySprite(Figure f)
        {
            Sprite sprite = f.skin != null ? (f.skin.Standing != null ? f.skin.Standing : f.skin.SeatedFront) : previewSprite;
            f.avatar.sprite = sprite;
            f.avatar.enabled = sprite != null;
            f.shadow.rectTransform.sizeDelta = new Vector2(f.height * 0.46f, f.height * 0.08f);
            if (sprite == null) return;
            Rect rect = sprite.rect;
            RectTransform rt = f.avatar.rectTransform;
            rt.pivot = new Vector2(sprite.pivot.x / rect.width, sprite.pivot.y / rect.height);
            rt.sizeDelta = new Vector2(rect.width * f.height / rect.height, f.height);
        }

        private static Vector2 PositionOf(Figure f, float now)
        {
            float k = f.moveSeconds > 0f ? Mathf.Clamp01((now - f.moveStart) / f.moveSeconds) : 1f;
            Vector2 p = Vector2.Lerp(f.from, f.to, k * k * (3f - 2f * k));
            if (f.walk && k < 1f) p.y += Mathf.Abs(Mathf.Sin(k * Mathf.PI * 5f)) * 14f; // steps
            return p;
        }

        private Figure FindFigure(long playerId)
        {
            foreach (Figure f in figures)
            {
                if (f.playerId == playerId) return f;
            }
            return null;
        }

        private void Update()
        {
            if (!visible) return;
            float now = Time.unscaledTime;
            if (!Mathf.Approximately(laidOutWidth, canvas.rect.width))
            {
                laidOutWidth = canvas.rect.width;
                LayoutBackdrop(backdrop);
                if (oldBackdrop.gameObject.activeSelf) LayoutBackdrop(oldBackdrop);
            }
            if (crossfadeStart >= 0f)
            {
                float k = Mathf.Clamp01((now - crossfadeStart) / CrossfadeSeconds);
                backdrop.color = new Color(1f, 1f, 1f, k);
                if (k >= 1f)
                {
                    crossfadeStart = -1f;
                    oldBackdrop.gameObject.SetActive(false);
                }
            }
            for (int i = figures.Count - 1; i >= 0; i--)
            {
                Figure f = figures[i];
                float alpha = Mathf.Clamp01((now - f.appearStart) / AppearSeconds);
                if (f.leaveStart >= 0f)
                {
                    float gone = (now - f.leaveStart) / LeaveSeconds;
                    if (gone >= 1f)
                    {
                        Destroy(f.holder.gameObject);
                        figures.RemoveAt(i);
                        continue;
                    }
                    alpha *= 1f - gone;
                }
                Vector2 position = PositionOf(f, now);
                if (!f.alive) position.y += 6f + 6f * Mathf.Sin(now * 2f + f.phase); // a ghost floats
                f.holder.anchoredPosition = position;
                Color tint = f.alive ? Color.white : Ghost;
                tint.a *= alpha;
                f.avatar.color = tint;
                f.shadow.color = new Color(0f, 0f, 0f, (f.alive ? 0.5f : 0.15f) * alpha);
                f.label.alpha = alpha;
                if (f.mine && motionBusy) continue; // the ability motion drives my avatar
                float breath = f.alive ? Mathf.Sin(now * 1.7f + f.phase) * BreathingAmount : 0f;
                f.avatar.rectTransform.localScale = new Vector3(1f, 1f + breath, 1f);
            }
        }

        // ------------------------------------------------------------------ building

        private Image CreateBackdrop(string name)
        {
            Image image = CreateImage(name, canvas, null, Color.white);
            image.rectTransform.anchorMin = new Vector2(0f, 1f);
            image.rectTransform.anchorMax = new Vector2(1f, 1f);
            image.rectTransform.pivot = new Vector2(0.5f, 1f);
            image.preserveAspect = false;
            image.gameObject.SetActive(false);
            return image;
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        // An empty point at the parent's centre: children are placed in stage coordinates.
        private static RectTransform NewCentred(string name, Transform parent)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            return rect;
        }
    }
}
