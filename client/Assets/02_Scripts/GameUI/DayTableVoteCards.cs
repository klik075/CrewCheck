using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.GameUI
{
    /// <summary>
    /// Vote cards on the day table's card spots (<see cref="DayRoundTableView.CardSpotFor"/>).
    /// - My vote (only on my screen): when the server accepts it, a face-down card drops onto the table in front of me and
    ///   is thrown onto the target's spot. Voting again throws it on to the new target; if the vote is lost (the target
    ///   left the game) the card is lifted away.
    /// - Execution: the public tally (<see cref="ExecutionResultDto.votes"/>) becomes a pile of face-down cards on every
    ///   target's spot, with the count beside it.
    /// Who voted for whom is never drawn: other players' cards only appear as the public counts at the execution.
    /// Cards live in the table's card spot layer: above the table top, below the near row, hidden with the table.
    /// </summary>
    public sealed class DayTableVoteCards : MonoBehaviour
    {
        private const float Flatten = 0.62f;                               // a card lying on the table, at the table's angle
        private static readonly Vector2 CardSize = new Vector2(40f, 56f);  // upright card at the 12-seat table size
        private const float SpotWidthAt12 = 52f;                           // DayRoundTableView.CardSpotSize.x
        private const float StackStep = 4f;                                // each card in a pile lies this much higher
        private const int MaxPileCards = 6;                                // more votes only raise the number
        private static readonly Color Edge = new Color32(70, 42, 20, 255);
        private static readonly Color Back = new Color32(26, 74, 92, 255);
        private static readonly Color Gold = new Color32(226, 180, 99, 255);

        private sealed class Pile
        {
            public long playerId;
            public RectTransform root;          // flattened, at the spot
            public readonly List<RectTransform> cards = new List<RectTransform>();
            public TextMeshProUGUI count;
        }

        private readonly Dictionary<long, Pile> piles = new Dictionary<long, Pile>();
        private DayRoundTableView table;
        private TMP_FontAsset font;
        private RectTransform myCard;           // holder (flattened) with the card face; lying on myTarget's spot or flying
        private long myTarget;
        private Coroutine myRoutine;

        public void Bind(DayRoundTableView view, TMP_FontAsset labelFont)
        {
            table = view;
            font = labelFont;
        }

        /// <summary>
        /// The vote the server holds for me (0 = none). Throws my card when it changes; calling again with the same
        /// target does nothing, so it can follow every poll.
        /// </summary>
        public void ShowMyVote(long myId, long targetId)
        {
            if (table == null || targetId == myTarget)
            {
                return;
            }
            RectTransform to = targetId != 0 ? table.CardSpotFor(targetId) : null;
            if (myRoutine != null)
            {
                StopCoroutine(myRoutine);
                myRoutine = null;
            }
            if (to == null)
            {
                myTarget = 0;
                if (myCard != null)
                {
                    StartCoroutine(LiftAway(myCard));
                    myCard = null;
                }
                return;
            }
            myTarget = targetId;
            bool animate = Application.isPlaying && isActiveAndEnabled;
            float unit = UnitOf(to);
            if (myCard == null)
            {
                RectTransform mine = table.CardSpotFor(myId);
                myCard = NewHolder("MyVoteCard", to.parent, unit);
                CreateFace("Face", myCard);
                myCard.anchoredPosition = (mine != null ? mine : to).anchoredPosition;
                if (animate)
                {
                    myRoutine = StartCoroutine(LayDownAndThrow(myCard, mine != null, to, targetId, unit));
                    return;
                }
            }
            else if (animate)
            {
                myRoutine = StartCoroutine(Throw(myCard, to, targetId, unit));
                return;
            }
            Rest(myCard, to, targetId, unit);
        }

        /// <summary>The execution's public tally: a pile of face-down cards on each target's spot. My card tops my target's pile.</summary>
        public void ShowTally(IList<VoteCountDto> votes)
        {
            ClearPiles();
            if (table == null || votes == null)
            {
                return;
            }
            bool myVoteCounted = false;
            bool animate = Application.isPlaying && isActiveAndEnabled;
            foreach (VoteCountDto vote in votes)
            {
                RectTransform spot = vote != null && vote.count > 0 ? table.CardSpotFor(vote.playerId) : null;
                if (spot == null)
                {
                    continue;
                }
                bool mine = myCard != null && myTarget == vote.playerId;
                myVoteCounted |= mine;
                Pile pile = NewPile(vote.playerId, spot);
                int cards = Mathf.Min(MaxPileCards, vote.count - (mine ? 1 : 0));
                for (int i = 0; i < cards; i++)
                {
                    RectTransform face = CreateFace("Vote_" + i, pile.root);
                    face.anchoredPosition = new Vector2((i % 2 == 0 ? 1f : -1f) * i * 2.5f, i * StackStep / Flatten);
                    face.localRotation = Quaternion.Euler(0f, 0f, (i * 37 % 21) - 10f);
                    pile.cards.Add(face);
                    if (animate)
                    {
                        StartCoroutine(DropIn(face, i * 0.09f));
                    }
                }
                pile.count.text = vote.count + "표";
            }
            if (myCard != null && !myVoteCounted)
            {
                StartCoroutine(LiftAway(myCard)); // the server did not count it (the target left the game)
                myCard = null;
                myTarget = 0;
            }
            if (myCard != null)
            {
                myCard.SetAsLastSibling();
            }
        }

        /// <summary>Removes every card (a new vote round, a new game).</summary>
        public void Clear()
        {
            StopAllCoroutines();
            myRoutine = null;
            ClearPiles();
            if (myCard != null)
            {
                Destroy(myCard.gameObject);
            }
            myCard = null;
            myTarget = 0;
        }

        private void LateUpdate()
        {
            if (table == null)
            {
                return;
            }
            // Follow the spots (seats can be rearranged when the roster changes).
            foreach (Pile pile in piles.Values)
            {
                RectTransform spot = table.CardSpotFor(pile.playerId);
                if (spot == null)
                {
                    continue;
                }
                pile.root.anchoredPosition = spot.anchoredPosition;
                pile.count.rectTransform.anchoredPosition = spot.anchoredPosition + new Vector2(34f, -4f) * UnitOf(spot);
            }
            if (myCard != null && myRoutine == null && myTarget != 0)
            {
                RectTransform spot = table.CardSpotFor(myTarget);
                if (spot != null)
                {
                    myCard.anchoredPosition = RestPosition(spot, myTarget);
                }
            }
        }

        // ================================================================ animation

        private IEnumerator LayDownAndThrow(RectTransform card, bool fromMySeat, RectTransform to, long targetId, float unit)
        {
            if (fromMySeat)
            {
                // The card drops onto the table in front of me, then a short beat before the throw.
                Vector2 rest = card.anchoredPosition;
                yield return Move(card, rest + Vector2.up * 46f * unit, rest, 0.25f, 0f, 1f, 0f, 0f, 0f, 0f, 0f);
                yield return new WaitForSecondsRealtime(0.15f);
            }
            yield return Throw(card, to, targetId, unit);
        }

        private IEnumerator Throw(RectTransform card, RectTransform to, long targetId, float unit)
        {
            Vector2 start = card.anchoredPosition;
            float startAngle = FaceAngle(card);
            float endAngle = RestAngle(targetId);
            float spin = Mathf.DeltaAngle(startAngle, endAngle) - 360f; // one full turn in the air
            yield return Move(card, start, RestPosition(to, targetId), 0.55f, 80f * unit, 0f, 0f, 0.55f,
                startAngle, startAngle + spin, 1f);
            card.SetAsLastSibling();
            myRoutine = null;
        }

        /// <param name="lift">0 = lying flat, 1 = upright; midLift is added at the middle of the move.</param>
        private IEnumerator Move(RectTransform card, Vector2 from, Vector2 to, float seconds, float arc,
            float fromLift, float toLift, float midLift, float fromAngle, float toAngle, float fromAlpha)
        {
            CanvasGroup group = card.GetComponentInChildren<CanvasGroup>();
            RectTransform face = card.childCount > 0 ? (RectTransform)card.GetChild(0) : null;
            float unit = card.localScale.x;
            for (float t = 0f; t < 1f && card != null; t += Time.unscaledDeltaTime / seconds)
            {
                float e = Mathf.SmoothStep(0f, 1f, t);
                float hump = Mathf.Sin(t * Mathf.PI);
                card.anchoredPosition = Vector2.Lerp(from, to, e) + Vector2.up * arc * hump;
                SetLift(card, unit, Mathf.Clamp01(Mathf.Lerp(fromLift, toLift, e) + midLift * hump));
                if (face != null)
                {
                    face.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(fromAngle, toAngle, e));
                    face.localScale = Vector3.one * (1f + 0.15f * hump);
                }
                if (group != null)
                {
                    group.alpha = Mathf.Lerp(fromAlpha, 1f, Mathf.Clamp01(t * 3f));
                }
                yield return null;
            }
            if (card == null)
            {
                yield break;
            }
            card.anchoredPosition = to;
            SetLift(card, unit, toLift);
            if (face != null)
            {
                face.localRotation = Quaternion.Euler(0f, 0f, toAngle);
                face.localScale = Vector3.one;
            }
            if (group != null)
            {
                group.alpha = 1f;
            }
        }

        private static IEnumerator DropIn(RectTransform face, float delay)
        {
            CanvasGroup group = face.GetComponent<CanvasGroup>();
            Vector2 end = face.anchoredPosition;
            group.alpha = 0f;
            yield return new WaitForSecondsRealtime(delay);
            for (float t = 0f; t < 1f && face != null; t += Time.unscaledDeltaTime / 0.3f)
            {
                float e = 1f - (1f - t) * (1f - t);
                face.anchoredPosition = end + Vector2.up * (1f - e) * 90f;
                group.alpha = e;
                yield return null;
            }
            if (face == null)
            {
                yield break;
            }
            face.anchoredPosition = end;
            group.alpha = 1f;
        }

        private static IEnumerator LiftAway(RectTransform card)
        {
            CanvasGroup group = card.GetComponentInChildren<CanvasGroup>();
            Vector2 start = card.anchoredPosition;
            for (float t = 0f; t < 1f && card != null; t += Time.unscaledDeltaTime / 0.35f)
            {
                card.anchoredPosition = start + Vector2.up * 50f * t;
                if (group != null)
                {
                    group.alpha = 1f - t;
                }
                yield return null;
            }
            if (card != null)
            {
                Destroy(card.gameObject);
            }
        }

        // ================================================================ layout

        private void Rest(RectTransform card, RectTransform spot, long targetId, float unit)
        {
            card.anchoredPosition = RestPosition(spot, targetId);
            SetLift(card, unit, 0f);
            if (card.childCount > 0)
            {
                card.GetChild(0).localRotation = Quaternion.Euler(0f, 0f, RestAngle(targetId));
            }
            card.SetAsLastSibling();
        }

        // On top of the target's pile, if the tally has already been laid out.
        private Vector2 RestPosition(RectTransform spot, long targetId)
        {
            int below = piles.TryGetValue(targetId, out Pile pile) ? pile.cards.Count : 0;
            return spot.anchoredPosition + Vector2.up * below * StackStep * UnitOf(spot);
        }

        private static float RestAngle(long targetId) => (targetId % 7) * 4f - 12f;

        private static float FaceAngle(RectTransform card) =>
            card.childCount > 0 ? card.GetChild(0).localEulerAngles.z : 0f;

        private static float UnitOf(RectTransform spot) => spot.sizeDelta.x / SpotWidthAt12;

        private static void SetLift(RectTransform holder, float unit, float lift) =>
            holder.localScale = new Vector3(unit, unit * Mathf.Lerp(Flatten, 1f, lift), 1f);

        private Pile NewPile(long playerId, RectTransform spot)
        {
            float unit = UnitOf(spot);
            var pile = new Pile { playerId = playerId, root = NewHolder("VotePile_" + playerId, spot.parent, unit) };
            pile.root.anchoredPosition = spot.anchoredPosition;
            SetLift(pile.root, unit, 0f);
            var label = new GameObject("VoteCount_" + playerId, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            label.layer = gameObject.layer;
            var rect = (RectTransform)label.transform;
            rect.SetParent(spot.parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(60f, 28f);
            rect.anchoredPosition = spot.anchoredPosition + new Vector2(34f, -4f) * unit;
            pile.count = label.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                pile.count.font = font;
                pile.count.outlineWidth = 0.25f;
                pile.count.outlineColor = new Color32(20, 12, 6, 255);
            }
            pile.count.fontSize = 20f * unit;
            pile.count.fontStyle = FontStyles.Bold;
            pile.count.color = Gold;
            pile.count.alignment = TextAlignmentOptions.MidlineLeft;
            pile.count.raycastTarget = false;
            piles.Add(playerId, pile);
            return pile;
        }

        private void ClearPiles()
        {
            foreach (Pile pile in piles.Values)
            {
                if (pile.root != null) Destroy(pile.root.gameObject);
                if (pile.count != null) Destroy(pile.count.gameObject);
            }
            piles.Clear();
        }

        private RectTransform NewHolder(string name, Transform parent, float unit)
        {
            RectTransform holder = NewRect(name, parent);
            holder.sizeDelta = CardSize;
            SetLift(holder, unit, 0f);
            return holder;
        }

        // A face-down vote card drawn in the table's colours: brown edge, gold frame, teal back, gold emblem.
        private RectTransform CreateFace(string name, Transform parent)
        {
            RectTransform face = NewRect(name, parent);
            face.sizeDelta = CardSize;
            face.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            AddImage(face, Edge);
            AddImage(Inset("Frame", face, 2.5f), Gold);
            AddImage(Inset("Back", face, 4.5f), Back);
            RectTransform emblem = NewRect("Emblem", face);
            emblem.sizeDelta = new Vector2(11f, 11f);
            emblem.localRotation = Quaternion.Euler(0f, 0f, 45f);
            AddImage(emblem, Gold);
            return face;
        }

        private RectTransform Inset(string name, RectTransform parent, float inset)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        private static void AddImage(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            return rect;
        }
    }
}
