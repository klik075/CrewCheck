using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.Vote
{
    /// <summary>
    /// 부채꼴 카드패 투표 UI. (기존 하단 서랍의 VotePanelController를 대신한다)
    ///
    /// 연출
    ///  - Show    : 투표할 수 있는 플레이어 카드가 부채꼴로 펼쳐진 카드패가 아래에서 올라온다.
    ///  - 마우스   : 카드에 올리면 카드가 살짝 위로 올라간다. (VoteCardView)
    ///  - 클릭     : 그 카드가 카드패에서 위로 빠져나와 카드패 위에 단독으로 놓인다 → VoteRequested(playerId)
    ///  - 재투표   : 뽑아 둔 카드가 있을 때 카드패의 다른 카드를 누르면, 뽑아 둔 카드가 카드패로 돌아가고 이어서 누른 카드가 뽑힌다.
    ///  - 넘기기   : 뽑아 둔 카드를 다시 누르면 카드패로 돌아간다 → VoteRequested(0). 아무 카드도 뽑지 않은 상태 = 기권(넘기기).
    ///               (별도 기권 버튼 없음. 이 상태로 시간이 끝나거나 투표 완료를 누르면 기권으로 처리된다)
    ///  - 투표 완료 : 투표 중에는 오른쪽 하단에 [투표 완료] 버튼이 뜬다. 누르면 지금 상태(뽑은 카드 또는 기권)로 고정되고
    ///               → VoteConfirmed(playerId, 기권이면 0), 카드패는 아래로 내려간다. 뽑아 둔 카드는 투표 시간이 끝날 때까지 남는다.
    ///  - Hide    : (투표 여부와 관계없이) 투표 시간이 끝나면 카드패는 아래로 내려가고, 뽑아 둔 카드는 점점 투명해지며 사라진다.
    ///
    /// 화면 구성은 코드로 만든다(프리팹·씬 수정 불필요). 이 오브젝트는 캔버스 아래 전체 화면 크기로 두면 된다.
    /// 자체 Canvas(overrideSorting)로 다른 UI 위에 그린다. 카드 밖은 클릭을 막지 않으므로 채팅 기록 등은 그대로 쓸 수 있다.
    /// 서버 통신은 하지 않는다. VoteRequested·VoteConfirmed를 받은 쪽(GameScreen)이 POST /votes를 보내고,
    /// 서버가 거절하면 SetDrawnSilently(서버가 가진 내 표)·Unlock으로 화면을 되돌린다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class VoteCardHandController : MonoBehaviour
    {
        [Header("모양 (비우면 기본 사각형 / TMP 기본 글꼴)")]
        [SerializeField] private Sprite cardSprite;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private int sortingOrderOffset = 20;

        [Header("카드")]
        [SerializeField] private Vector2 cardSize = new Vector2(200f, 290f);
        [SerializeField] private float hoverLift = 48f;
        [SerializeField] private float hoverScale = 1.06f;

        [Header("부채꼴")]
        [Tooltip("부채꼴 원의 반지름. 클수록 덜 휜다.")]
        [SerializeField] private float fanRadius = 1300f;
        [Tooltip("카드 사이 최대 각도(도). 카드가 적을 때 너무 벌어지지 않게 한다.")]
        [SerializeField] private float maxStepAngle = 9f;
        [Tooltip("부채꼴 전체 최대 각도(도).")]
        [SerializeField] private float maxSpreadAngle = 48f;
        [SerializeField] private float sideMargin = 24f;
        [SerializeField] private float bottomPadding = 16f;

        [Header("뽑은 카드")]
        [SerializeField] private float drawnScale = 1.3f;
        [Tooltip("카드패(안내 문구 포함) 윗변과 뽑은 카드 아랫변 사이 간격")]
        [SerializeField] private float drawnGap = 40f;
        [Tooltip("재투표 시 뽑아 둔 카드가 돌아가기 시작한 뒤 새 카드를 뽑기까지의 시간")]
        [SerializeField] private float swapDelay = 0.18f;

        [Header("등장 / 퇴장")]
        [SerializeField] private float showDuration = 0.55f;
        [SerializeField] private float hideDuration = 0.45f;
        [SerializeField] private float drawnFadeDuration = 0.9f;

        [Header("안내 문구")]
        [SerializeField] private string pickHint = "처형할 플레이어의 카드를 뽑으세요 · 뽑지 않으면 기권";
        [SerializeField] private string votedHintFormat = "{0}님 선택 · 다시 누르면 취소(기권)";
        [SerializeField] private string confirmedFormat = "{0}님에게 투표 완료";
        [SerializeField] private string confirmedAbstainText = "기권으로 투표 완료";

        [Header("투표 완료 버튼 (오른쪽 하단, 카드패 바로 위)")]
        [SerializeField] private string confirmLabel = "투표 완료";
        [SerializeField] private Vector2 confirmButtonSize = new Vector2(240f, 92f);
        [SerializeField] private float confirmRightMargin = 28f;
        [Tooltip("카드패(안내 문구 포함) 윗변에서 버튼 아랫변까지 간격")]
        [SerializeField] private float confirmGap = 8f;
        [SerializeField] private Color confirmColor = new Color(0.95f, 0.76f, 0.30f, 1f);
        [SerializeField] private Color confirmTextColor = new Color(0.10f, 0.08f, 0.04f, 1f);

        /// <summary>
        /// 선택이 바뀌었다(임시 선택). 인자는 뽑은 카드의 playerId, 뽑은 카드를 다시 눌러 카드패로 돌려보냈으면 0(기권).
        /// 시간이 끝나면 마지막 선택이 그대로 집계된다(0이면 기권).
        /// </summary>
        public event Action<long> VoteRequested;

        /// <summary>[투표 완료]를 눌렀다. 인자는 고정된 대상 playerId, 아무 카드도 뽑지 않았으면 0(기권).</summary>
        public event Action<long> VoteConfirmed;

        /// <summary>카드패가 화면에 올라와 있는지 (올라오는 중 포함, 내려가는 중 제외). 투표 완료로 카드패만 내려간 동안도 true.</summary>
        public bool IsShown { get; private set; }

        /// <summary>[투표 완료]를 눌러 지금 상태가 고정되었는지.</summary>
        public bool IsLocked { get; private set; }

        /// <summary>뽑아 둔 카드의 playerId. 없으면 0.</summary>
        public long DrawnPlayerId
        {
            get { return drawn != null ? drawn.PlayerId : 0; }
        }

        private readonly List<VoteCardView> handCards = new List<VoteCardView>(); // 카드패에 있는 카드 (HandIndex 순)
        private readonly List<VoteCardView> allCards = new List<VoteCardView>();  // 뽑은 카드 포함
        private readonly HashSet<long> aliveTargets = new HashSet<long>();

        private RectTransform root;
        private RectTransform handArea;   // 카드패 (아래에서 올라오고 내려간다)
        private RectTransform drawnLayer; // 뽑은 카드가 놓이는 층 (카드패 위에 그린다)
        private TextMeshProUGUI hintText;
        private RectTransform confirmButtonRt; // 카드패(handArea) 안에 있어 카드패와 함께 오르내린다
        private Button confirmButton;
        private TextMeshProUGUI confirmText;
        private TextMeshProUGUI lockedText;     // 투표 완료 뒤 카드패가 내려간 자리에 남는 안내
        private Canvas canvas;
        private bool built;

        private RectTransform bottomAnchor; // 이 RectTransform의 윗변 위에 카드패를 놓는다 (하단 탭 바 등). 없으면 화면 아래
        private VoteCardView drawn;
        private VoteCardView hovered;
        private bool interactable;
        private bool busy;                // 재투표 연출 중
        private float slideOffset = -4000f;
        private float contentHeight;      // 카드패 높이 (카드 + 마우스 올림 여유)
        private float laidOutWidth = -1f; // 마지막으로 배치할 때의 화면 폭
        private Coroutine slideRoutine;
        private Coroutine swapRoutine;

        // ================================================================ Unity

        private void Awake()
        {
            EnsureBuilt();
        }

        private void LateUpdate()
        {
            if (!built)
            {
                return;
            }
            if (IsShown && !Mathf.Approximately(root.rect.width, laidOutWidth))
            {
                Relayout(false); // 화면 크기(캔버스 스케일러)가 바뀌면 부채꼴을 다시 편다
            }
            float baseY = BaseY();
            handArea.anchoredPosition = new Vector2(0f, baseY + slideOffset);
            if (drawn != null && IsShown)
            {
                drawn.SetTarget(DrawnSlot(baseY), 0f, drawnScale);
            }
            lockedText.rectTransform.anchoredPosition = new Vector2(0f, baseY + contentHeight + 4f);
        }

        // ================================================================ 설정

        /// <summary>안내 문구·닉네임 글꼴 (한글이 나오는 글꼴을 넣는다).</summary>
        public void SetFont(TMP_FontAsset value)
        {
            if (value == null)
            {
                return;
            }
            font = value;
            if (hintText != null)
            {
                hintText.font = value;
            }
            if (confirmText != null)
            {
                confirmText.font = value;
            }
            if (lockedText != null)
            {
                lockedText.font = value;
            }
        }

        /// <summary>카드패를 이 RectTransform의 윗변 위에 놓는다. (하단 시트를 넘기면 탭 바를 가리지 않고, 시트가 올라가면 따라 올라간다)</summary>
        public void SetBottomAnchor(RectTransform anchor)
        {
            bottomAnchor = anchor;
        }

        // ================================================================ 외부 API

        /// <summary>
        /// 투표 시작: 카드패를 새로 만들어 아래에서 올린다.
        /// targets = 투표할 수 있는 플레이어(살아 있는 다른 플레이어). preDrawnId가 0이 아니면 그 카드를 뽑아 둔 상태로 시작한다(재접속 등).
        /// preConfirmed면 이미 투표 완료한 상태로 시작한다(카드패는 올리지 않고 뽑은 카드·완료 안내만 보인다. 이벤트 없음).
        /// </summary>
        public void Show(IList<PlayerView> targets, Func<long, Sprite> portraitResolver, long preDrawnId, bool preConfirmed = false)
        {
            EnsureBuilt();
            StopRoutines();
            ClearCards();
            transform.SetAsLastSibling();

            aliveTargets.Clear();
            for (int i = 0; i < targets.Count; i++)
            {
                PlayerView p = targets[i];
                aliveTargets.Add(p.playerId);
                VoteCardView card = VoteCardView.Create(handArea, p.playerId, p.nickname,
                    portraitResolver != null ? portraitResolver(p.playerId) : null, cardSprite, font, cardSize);
                card.HoverLift = hoverLift;
                card.HoverScale = hoverScale;
                card.HandIndex = i;
                card.Clicked += OnCardClicked;
                card.HoverChanged += OnCardHoverChanged;
                handCards.Add(card);
                allCards.Add(card);
            }

            Relayout(true);
            IsLocked = false;
            interactable = true;
            SetCardsInteractable(true);
            SetConfirmVisible(true);
            SetLockedText(null);
            IsShown = true;
            UpdateHint();

            slideOffset = HiddenOffset();
            LateUpdate();
            slideRoutine = StartCoroutine(Slide(slideOffset, 0f, showDuration, EaseOutBack));

            if (preDrawnId != 0)
            {
                VoteCardView card = Find(preDrawnId);
                if (card != null)
                {
                    Draw(card);
                }
            }
            if (preConfirmed)
            {
                Lock(false);
            }
        }

        /// <summary>투표 종료: 카드패는 아래로 내려가고, 뽑아 둔 카드는 점점 투명해지며 사라진다.</summary>
        public void Hide()
        {
            if (!built || !IsShown)
            {
                return;
            }
            IsShown = false;
            IsLocked = false;
            interactable = false;
            SetCardsInteractable(false);
            SetConfirmInteractable(false);
            StopRoutines();
            slideRoutine = StartCoroutine(HideRoutine());
        }

        /// <summary>연출 없이 바로 치운다 (새 게임·재접속).</summary>
        public void HideImmediate()
        {
            if (!built)
            {
                return;
            }
            IsShown = false;
            IsLocked = false;
            interactable = false;
            StopRoutines();
            ClearCards();
            SetConfirmVisible(false);
            SetLockedText(null);
            slideOffset = HiddenOffset();
        }

        /// <summary>
        /// [투표 완료]: 지금 상태(뽑은 카드, 없으면 기권)로 고정하고 카드패를 내린다 → VoteConfirmed.
        /// 버튼이 부르며, 테스트 등에서 직접 불러도 된다.
        /// </summary>
        public void Confirm()
        {
            Lock(true);
        }

        private void Lock(bool notify)
        {
            if (!IsShown || IsLocked)
            {
                return;
            }
            if (swapRoutine != null)
            {
                // 재투표 연출 도중: 앞 카드는 이미 돌아갔고 다음 카드는 아직 안 뽑혔다 → 화면에 보이는 그대로(기권) 고정한다
                StopCoroutine(swapRoutine);
                swapRoutine = null;
                busy = false;
            }
            IsLocked = true;
            interactable = false;
            SetCardsInteractable(false);
            if (drawn != null)
            {
                drawn.SetInteractable(false);
            }
            SetConfirmInteractable(false);
            long target = DrawnPlayerId;
            SetLockedText(drawn != null ? string.Format(confirmedFormat, drawn.Nickname) : confirmedAbstainText);
            SlideTo(HiddenOffset(), hideDuration, EaseInBack); // 카드패(버튼 포함)만 내려간다. 뽑은 카드는 그 자리에 남는다
            if (notify)
            {
                VoteConfirmed?.Invoke(target);
            }
        }

        /// <summary>투표 완료를 되돌린다 (서버가 거절했거나, 고정한 표의 대상이 나가 서버가 그 표를 지웠을 때). 카드패가 다시 올라온다.</summary>
        public void Unlock()
        {
            if (!IsShown || !IsLocked)
            {
                return;
            }
            IsLocked = false;
            interactable = true;
            SetCardsInteractable(true);
            if (drawn != null)
            {
                drawn.SetInteractable(true);
            }
            SetConfirmInteractable(true);
            SetLockedText(null);
            UpdateHint();
            SlideTo(0f, showDuration, EaseOutBack);
        }

        /// <summary>
        /// 폴링으로 받은 최신 플레이어 목록 반영. 투표 도중 죽은(연결 끊김) 플레이어의 카드를 뺀다.
        /// 뽑아 둔 카드의 주인이 빠지면(서버가 그 표를 지운다) 그 카드는 흐려지며 사라진다.
        /// </summary>
        public void SyncPlayers(IList<PlayerView> targets)
        {
            if (!IsShown)
            {
                return;
            }
            aliveTargets.Clear();
            foreach (PlayerView p in targets)
            {
                aliveTargets.Add(p.playerId);
            }
            bool changed = false;
            for (int i = allCards.Count - 1; i >= 0; i--)
            {
                VoteCardView card = allCards[i];
                if (aliveTargets.Contains(card.PlayerId))
                {
                    continue;
                }
                changed = true;
                allCards.RemoveAt(i);
                handCards.Remove(card);
                if (card == hovered)
                {
                    hovered = null;
                }
                if (card == drawn)
                {
                    drawn = null;
                }
                card.SetInteractable(false);
                StartCoroutine(FadeAndDestroy(card, drawnFadeDuration * 0.6f, 0f, false));
            }
            if (changed)
            {
                Relayout(false);
                UpdateHint();
            }
        }

        /// <summary>
        /// 이벤트 없이 뽑은 카드를 맞춘다 (서버가 실제로 가진 내 표와 화면을 맞출 때). 0이면 뽑은 카드를 카드패로 돌려놓는다.
        /// </summary>
        public void SetDrawnSilently(long playerId)
        {
            if (!IsShown || DrawnPlayerId == playerId)
            {
                return;
            }
            if (swapRoutine != null)
            {
                StopCoroutine(swapRoutine);
                swapRoutine = null;
                busy = false;
            }
            if (drawn != null)
            {
                ReturnToHand(drawn);
            }
            VoteCardView card = playerId != 0 ? Find(playerId) : null;
            if (card != null && !card.IsDrawn)
            {
                Draw(card);
            }
            UpdateHint();
            if (IsLocked)
            {
                SetLockedText(drawn != null ? string.Format(confirmedFormat, drawn.Nickname) : confirmedAbstainText);
            }
        }

        // ================================================================ 입력

        private void OnCardClicked(VoteCardView card)
        {
            if (!interactable || busy || IsLocked)
            {
                return;
            }
            if (card.IsDrawn)
            {
                // 뽑아 둔 카드를 다시 누름 → 카드패로 돌려보낸다. 아무 카드도 없으면 기권(넘기기)
                if (card != drawn)
                {
                    return;
                }
                ReturnToHand(card);
                UpdateHint();
                VoteRequested?.Invoke(0);
                return;
            }
            if (!handCards.Contains(card))
            {
                return;
            }
            if (drawn == null)
            {
                Draw(card);
                UpdateHint();
                VoteRequested?.Invoke(card.PlayerId);
                return;
            }
            // 재투표: 뽑아 둔 카드를 카드패로 돌려보내고, 이어서 누른 카드를 뽑는다
            swapRoutine = StartCoroutine(SwapRoutine(card));
        }

        private void OnCardHoverChanged(VoteCardView card, bool isHovered)
        {
            if (isHovered)
            {
                hovered = card;
                card.Rect.SetAsLastSibling(); // 올라온 카드가 이웃 카드에 가려지지 않게
            }
            else if (hovered == card)
            {
                hovered = null;
                SortSiblings();
            }
        }

        // ================================================================ 뽑기 / 되돌리기

        private void Draw(VoteCardView card)
        {
            handCards.Remove(card);
            if (hovered == card)
            {
                hovered = null;
            }
            card.Rect.SetParent(drawnLayer, true); // 화면상 위치는 그대로 두고 층만 옮긴다 → 그 자리에서 위로 빠져나간다
            card.SetDrawn(true);
            card.SetInteractable(interactable && !IsLocked); // 뽑은 카드를 다시 누르면 카드패로 돌아간다 (넘기기)
            card.FollowSpeed = 10f;
            card.SetTarget(DrawnSlot(BaseY()), 0f, drawnScale);
            drawn = card;
            Relayout(false);
        }

        private void ReturnToHand(VoteCardView card)
        {
            card.Rect.SetParent(handArea, true);
            card.SetDrawn(false);
            card.SetInteractable(interactable);
            card.FollowSpeed = 12f;
            int insertAt = 0;
            while (insertAt < handCards.Count && handCards[insertAt].HandIndex < card.HandIndex)
            {
                insertAt++;
            }
            handCards.Insert(insertAt, card);
            if (drawn == card)
            {
                drawn = null;
            }
            Relayout(false);
        }

        private IEnumerator SwapRoutine(VoteCardView next)
        {
            busy = true;
            ReturnToHand(drawn);
            UpdateHint();
            yield return new WaitForSecondsRealtime(swapDelay);
            busy = false;
            swapRoutine = null;
            if (!IsShown || !handCards.Contains(next))
            {
                yield break; // 그사이 투표 시간이 끝났거나 그 플레이어가 나갔다
            }
            Draw(next);
            UpdateHint();
            VoteRequested?.Invoke(next.PlayerId);
        }

        // ================================================================ 배치

        /// <summary>카드패에 남은 카드를 부채꼴로 다시 놓는다. snap이면 바로, 아니면 부드럽게 이동.</summary>
        private void Relayout(bool snap)
        {
            int n = handCards.Count;
            laidOutWidth = root.rect.width;
            float halfWidth = Mathf.Max(200f, root.rect.width * 0.5f - sideMargin);
            float w = cardSize.x;
            float h = cardSize.y;
            float r = Mathf.Max(100f, fanRadius);

            float step = n > 1 ? Mathf.Min(maxStepAngle, maxSpreadAngle / (n - 1)) : 0f;
            // 화면 폭을 넘으면 각도를 줄인다 (카드가 더 겹친다)
            while (step > 0.5f && HalfExtentX(r, step * (n - 1) * 0.5f, w, h) > halfWidth)
            {
                step -= 0.25f;
            }
            float half = step * (n - 1) * 0.5f;
            float halfRad = half * Mathf.Deg2Rad;

            // 가장 바깥 카드의 아래 모서리까지 카드패 안에 들어오도록 기준 높이를 잡는다
            float lowest = r * (1f - Mathf.Cos(halfRad)) + h * 0.5f * Mathf.Cos(halfRad) + w * 0.5f * Mathf.Sin(halfRad);
            float baseY = lowest + bottomPadding;
            contentHeight = baseY + h * 0.5f + hoverLift;
            handArea.sizeDelta = new Vector2(root.rect.width, contentHeight);
            hintText.rectTransform.anchoredPosition = new Vector2(0f, contentHeight + 4f);
            float hintHeight = hintText.preferredHeight > 0f ? hintText.preferredHeight : 40f;
            confirmButtonRt.anchoredPosition = new Vector2(
                root.rect.width * 0.5f - confirmRightMargin - confirmButtonSize.x * 0.5f,
                contentHeight + 4f + hintHeight + confirmGap);

            for (int i = 0; i < n; i++)
            {
                float a = n > 1 ? -half + step * i : 0f;
                float rad = a * Mathf.Deg2Rad;
                var pos = new Vector2(r * Mathf.Sin(rad), baseY - r * (1f - Mathf.Cos(rad)));
                VoteCardView card = handCards[i];
                card.SetTarget(pos, -a, 1f); // 오른쪽 카드는 시계 방향(-z)으로 기운다
                if (snap)
                {
                    card.SnapToTarget();
                }
            }
            SortSiblings();
        }

        private static float HalfExtentX(float r, float halfDeg, float w, float h)
        {
            float rad = halfDeg * Mathf.Deg2Rad;
            return r * Mathf.Sin(rad) + w * 0.5f * Mathf.Cos(rad) + h * 0.5f * Mathf.Sin(rad);
        }

        private void SortSiblings()
        {
            for (int i = 0; i < handCards.Count; i++)
            {
                handCards[i].Rect.SetSiblingIndex(i + 1); // 0번은 안내 문구
            }
            if (confirmButtonRt != null)
            {
                confirmButtonRt.SetAsLastSibling(); // 버튼이 카드에 가려지지 않게
            }
            if (hovered != null && handCards.Contains(hovered))
            {
                hovered.Rect.SetAsLastSibling();
            }
        }

        /// <summary>카드패 바닥 높이 (화면 아래 기준). 아래 기준 RectTransform이 있으면 그 윗변.</summary>
        private float BaseY()
        {
            if (bottomAnchor == null || !bottomAnchor.gameObject.activeInHierarchy)
            {
                return 0f;
            }
            var corners = new Vector3[4];
            bottomAnchor.GetWorldCorners(corners);
            Vector3 topLeft = root.InverseTransformPoint(corners[1]);
            return Mathf.Max(0f, topLeft.y - root.rect.yMin);
        }

        private Vector2 DrawnSlot(float baseY)
        {
            float hintHeight = hintText.preferredHeight > 0f ? hintText.preferredHeight : 40f;
            return new Vector2(0f, baseY + contentHeight + hintHeight + drawnGap + cardSize.y * drawnScale * 0.5f);
        }

        private float HiddenOffset()
        {
            return -(BaseY() + contentHeight + 80f);
        }

        // ================================================================ 등장 / 퇴장

        private IEnumerator Slide(float from, float to, float duration, Func<float, float> ease)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                slideOffset = Mathf.LerpUnclamped(from, to, ease(Mathf.Clamp01(t / duration)));
                yield return null;
            }
            slideOffset = to;
            slideRoutine = null;
        }

        private void SlideTo(float to, float duration, Func<float, float> ease)
        {
            if (slideRoutine != null)
            {
                StopCoroutine(slideRoutine);
            }
            slideRoutine = StartCoroutine(Slide(slideOffset, to, duration, ease));
        }

        private IEnumerator HideRoutine()
        {
            if (lockedText.gameObject.activeSelf)
            {
                StartCoroutine(FadeLockedText(drawnFadeDuration));
            }
            VoteCardView fading = drawn;
            drawn = null;
            if (fading != null)
            {
                // 카드패와 따로: 제자리에서 살짝 떠오르며 점점 투명해진다
                StartCoroutine(FadeAndDestroy(fading, drawnFadeDuration, 30f, true));
                allCards.Remove(fading);
            }
            yield return Slide(slideOffset, HiddenOffset(), hideDuration, EaseInBack);
            ClearCards(false); // 뽑은 카드는 아직 흐려지는 중이다. 다 흐려지면 FadeAndDestroy가 지운다
            SetConfirmVisible(false);
        }

        private IEnumerator FadeLockedText(float duration)
        {
            float start = lockedText.alpha;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                lockedText.alpha = Mathf.Lerp(start, 0f, Mathf.Clamp01(t / duration));
                yield return null;
            }
            SetLockedText(null);
        }

        /// <param name="atDrawnSlot">뽑은 카드면 true: 아직 날아가는 중이었어도 뽑은 카드 자리에서 흐려진다.</param>
        private IEnumerator FadeAndDestroy(VoteCardView card, float duration, float rise, bool atDrawnSlot)
        {
            if (card == null)
            {
                yield break;
            }
            card.BeginFadeOut();
            Vector2 start = atDrawnSlot ? DrawnSlot(BaseY()) : card.Rect.anchoredPosition;
            float angle = atDrawnSlot ? 0f : card.Rect.localEulerAngles.z;
            float scale = atDrawnSlot ? drawnScale : card.Rect.localScale.x;
            float startAlpha = card.Alpha;
            float t = 0f;
            while (t < duration && card != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float a = Mathf.Lerp(startAlpha, 0f, k * k * (3f - 2f * k)); // smoothstep
                card.Alpha = a;
                card.SetTargetAlpha(a);
                card.SetTarget(start + new Vector2(0f, rise * k), angle, scale);
                yield return null;
            }
            if (card != null)
            {
                Destroy(card.gameObject);
            }
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.25f;
            const float c3 = c1 + 1f;
            float p = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }

        private static float EaseInBack(float x)
        {
            const float c1 = 1.2f;
            const float c3 = c1 + 1f;
            return c3 * x * x * x - c1 * x * x;
        }

        // ================================================================ 도우미

        private void EnsureBuilt()
        {
            if (built)
            {
                return;
            }
            built = true;
            root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            // 다른 UI(하단 시트, 채팅 기록) 위에 그린다
            canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            Canvas parentCanvas = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
            canvas.overrideSorting = true;
            canvas.sortingOrder = (parentCanvas != null ? parentCanvas.sortingOrder : 0) + sortingOrderOffset;
            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            handArea = NewRect("Hand", root);
            handArea.anchorMin = handArea.anchorMax = new Vector2(0.5f, 0f);
            handArea.pivot = new Vector2(0.5f, 0f);

            var hintRt = NewRect("Hint", handArea);
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 0f);
            hintRt.pivot = new Vector2(0.5f, 0f);
            hintRt.sizeDelta = new Vector2(1000f, 50f);
            hintText = hintRt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                hintText.font = font;
            }
            hintText.alignment = TextAlignmentOptions.Center;
            hintText.fontSize = 30f;
            hintText.color = new Color(1f, 0.92f, 0.7f, 1f);
            hintText.richText = false;
            hintText.raycastTarget = false;
            hintText.text = string.Empty;

            // [투표 완료] 버튼: 카드패 안 오른쪽, 카드패 바로 위. 카드패와 함께 오르내린다
            confirmButtonRt = NewRect("ConfirmButton", handArea);
            confirmButtonRt.anchorMin = confirmButtonRt.anchorMax = new Vector2(0.5f, 0f);
            confirmButtonRt.pivot = new Vector2(0.5f, 0f);
            confirmButtonRt.sizeDelta = confirmButtonSize;
            var confirmImage = confirmButtonRt.gameObject.AddComponent<Image>();
            confirmImage.color = Color.white; // 색은 Button의 colors로 칠한다
            confirmImage.raycastTarget = true;
            confirmButton = confirmButtonRt.gameObject.AddComponent<Button>();
            confirmButton.targetGraphic = confirmImage;
            confirmButton.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = confirmButton.colors;
            colors.normalColor = confirmColor;
            colors.highlightedColor = Color.Lerp(confirmColor, Color.white, 0.25f);
            colors.pressedColor = Color.Lerp(confirmColor, Color.black, 0.2f);
            colors.selectedColor = confirmColor;
            colors.disabledColor = new Color(confirmColor.r * 0.5f, confirmColor.g * 0.5f, confirmColor.b * 0.5f, 0.6f);
            confirmButton.colors = colors;
            confirmButton.onClick.AddListener(Confirm);
            var confirmOutline = confirmButtonRt.gameObject.AddComponent<Outline>();
            confirmOutline.effectColor = new Color(0f, 0f, 0f, 0.45f);
            confirmOutline.effectDistance = new Vector2(2f, -2f);

            var confirmLabelRt = NewRect("Label", confirmButtonRt);
            confirmLabelRt.anchorMin = Vector2.zero;
            confirmLabelRt.anchorMax = Vector2.one;
            confirmLabelRt.offsetMin = Vector2.zero;
            confirmLabelRt.offsetMax = Vector2.zero;
            confirmText = confirmLabelRt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                confirmText.font = font;
            }
            confirmText.alignment = TextAlignmentOptions.Center;
            confirmText.fontSize = 34f;
            confirmText.fontStyle = FontStyles.Bold;
            confirmText.color = confirmTextColor;
            confirmText.richText = false;
            confirmText.raycastTarget = false;
            confirmText.text = confirmLabel;
            confirmButtonRt.gameObject.SetActive(false);

            drawnLayer = NewRect("Drawn", root);
            drawnLayer.anchorMin = Vector2.zero;
            drawnLayer.anchorMax = Vector2.one;
            drawnLayer.offsetMin = Vector2.zero;
            drawnLayer.offsetMax = Vector2.zero;

            // 투표 완료 뒤 안내 (카드패가 내려간 자리, 뽑은 카드 아래)
            var lockedRt = NewRect("LockedHint", root);
            lockedRt.anchorMin = lockedRt.anchorMax = new Vector2(0.5f, 0f);
            lockedRt.pivot = new Vector2(0.5f, 0f);
            lockedRt.sizeDelta = new Vector2(1000f, 50f);
            lockedText = lockedRt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                lockedText.font = font;
            }
            lockedText.alignment = TextAlignmentOptions.Center;
            lockedText.fontSize = 32f;
            lockedText.color = new Color(1f, 0.92f, 0.7f, 1f);
            lockedText.richText = false;
            lockedText.raycastTarget = false;
            lockedText.text = string.Empty;
            lockedRt.gameObject.SetActive(false);

            slideOffset = -4000f;
        }

        private void SetConfirmVisible(bool visible)
        {
            if (confirmButtonRt == null)
            {
                return;
            }
            confirmButtonRt.gameObject.SetActive(visible);
            SetConfirmInteractable(visible);
        }

        private void SetConfirmInteractable(bool value)
        {
            if (confirmButton != null)
            {
                confirmButton.interactable = value;
            }
        }

        /// <summary>투표 완료 안내. null이면 숨긴다.</summary>
        private void SetLockedText(string text)
        {
            if (lockedText == null)
            {
                return;
            }
            bool show = !string.IsNullOrEmpty(text);
            lockedText.text = show ? text : string.Empty;
            lockedText.alpha = 1f;
            lockedText.gameObject.SetActive(show);
        }

        private void UpdateHint()
        {
            if (hintText == null)
            {
                return;
            }
            hintText.text = drawn != null ? string.Format(votedHintFormat, drawn.Nickname) : pickHint;
        }

        private VoteCardView Find(long playerId)
        {
            foreach (VoteCardView card in allCards)
            {
                if (card != null && card.PlayerId == playerId)
                {
                    return card;
                }
            }
            return null;
        }

        private void SetCardsInteractable(bool value)
        {
            foreach (VoteCardView card in allCards)
            {
                if (card != null && !card.IsDrawn)
                {
                    card.SetInteractable(value);
                }
            }
        }

        private void StopRoutines()
        {
            StopAllCoroutines(); // 슬라이드·재투표·흐려지기 (흐려지던 카드는 ClearCards가 지운다)
            slideRoutine = null;
            swapRoutine = null;
            busy = false;
        }

        private void ClearCards(bool includeDrawnLayer = true)
        {
            foreach (VoteCardView card in allCards)
            {
                if (card != null)
                {
                    Destroy(card.gameObject);
                }
            }
            allCards.Clear();
            handCards.Clear();
            drawn = null;
            hovered = null;
            // 흐려지다 멈춘 카드(StopAllCoroutines)도 정리한다
            if (includeDrawnLayer)
            {
                for (int i = drawnLayer.childCount - 1; i >= 0; i--)
                {
                    Destroy(drawnLayer.GetChild(i).gameObject);
                }
            }
            for (int i = handArea.childCount - 1; i >= 0; i--)
            {
                Transform child = handArea.GetChild(i);
                if (child != hintText.transform && child != confirmButtonRt)
                {
                    Destroy(child.gameObject);
                }
            }
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }
    }
}
