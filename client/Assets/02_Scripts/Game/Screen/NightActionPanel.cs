using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Vote;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 밤 능력 대상 고르기. VotePanelController(feat/vote)와 같은 방식으로 PlayerProfileItem 프리팹을 Grid 아래에 만든다.
    /// 서버 통신은 하지 않는다. 확정·넘기기는 이벤트로 넘기고, GameScreen이 GameController로 보낸다.
    /// 누구를 고를 수 있는지는 AbilityRules(서버 규칙과 같음)가 정한다: 해적은 동료도, 선의·망루지기는 자기 자신도, 주정뱅이는 사망자만.
    /// 하단 시트 Drawer 안에 VotePanel과 나란히 두면, 밤에는 GameScreen이 이 패널을 대신 보여 준다.
    /// </summary>
    public sealed class NightActionPanel : MonoBehaviour
    {
        [Header("Prefab / Layout")]
        [SerializeField] private PlayerProfileItem itemPrefab;
        [SerializeField] private RectTransform gridRoot; // GridLayoutGroup이 붙은 Content

        [Header("UI")]
        [SerializeField] private Button confirmButton;
        [SerializeField] private TextMeshProUGUI confirmButtonText;
        [SerializeField] private Button skipButton;
        [SerializeField] private TextMeshProUGUI statusText;

        private readonly Dictionary<long, PlayerProfileItem> items = new Dictionary<long, PlayerProfileItem>();
        private readonly HashSet<long> targetable = new HashSet<long>();
        private readonly HashSet<long> seen = new HashSet<long>();
        private readonly List<long> toRemove = new List<long>();

        private bool usable;
        private long selectedId;  // 화면에서 고른 대상 (0 = 없음)
        private long chosenId;    // 서버가 받아 준 대상 (0 = 없음)

        /// <summary>확정한 대상 playerId</summary>
        public event Action<long> Confirmed;

        /// <summary>이번 밤 능력을 쓰지 않음</summary>
        public event Action Skipped;

        /// <summary>playerId → 초상화. VotePanelController.PortraitResolver와 같은 것을 넣는다.</summary>
        public Func<long, Sprite> PortraitResolver { get; set; }

        private void Awake()
        {
            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(OnConfirmClicked);
            }
            if (skipButton != null)
            {
                skipButton.onClick.AddListener(OnSkipClicked);
            }
        }

        private void OnDestroy()
        {
            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(OnConfirmClicked);
            }
            if (skipButton != null)
            {
                skipButton.onClick.RemoveListener(OnSkipClicked);
            }
        }

        /// <summary>새 밤(또는 새 게임)이 시작될 때 화면에서 고른 것을 지운다.</summary>
        public void ResetSelection()
        {
            selectedId = 0;
            chosenId = 0;
        }

        /// <summary>최신 상태 반영. GameScreen이 상태·내 역할·접수 결과가 바뀔 때마다 부른다.</summary>
        public void Refresh(GameSession session)
        {
            if (session == null || session.State == null || session.Me == null)
            {
                return;
            }
            GameStateDto state = session.State;
            MyRoleDto me = session.Me;
            AbilityBlock block = session.NightAbility;
            usable = block == AbilityBlock.None;
            chosenId = session.MyNightTarget;

            targetable.Clear();
            foreach (PlayerViewDto p in session.NightTargets)
            {
                targetable.Add(p.playerId);
            }
            SyncItems(state, me.playerId);
            if (selectedId != 0 && !targetable.Contains(selectedId))
            {
                selectedId = 0;
            }

            foreach (PlayerProfileItem item in items.Values)
            {
                long shown = selectedId != 0 ? selectedId : chosenId;
                item.SetSelected(item.PlayerId == shown);
                item.SetClickable(usable && targetable.Contains(item.PlayerId));
            }

            string action = ReportFormatter.ActionName(me.actionCode);
            if (confirmButton != null)
            {
                confirmButton.interactable = usable && selectedId != 0 && selectedId != chosenId;
            }
            if (confirmButtonText != null)
            {
                confirmButtonText.text = chosenId != 0 ? action + " 변경" : action;
            }
            if (skipButton != null)
            {
                skipButton.interactable = usable && !session.SkippedTonight;
            }
            if (statusText != null)
            {
                long shownTarget = selectedId != 0 ? selectedId : chosenId;
                statusText.text = GameScreenText.NoRichText(
                    GameScreenText.NightStatus(block, me.actionCode, shownTarget, session.SkippedTonight, state));
            }
        }

        private void SyncItems(GameStateDto state, long myId)
        {
            seen.Clear();
            foreach (PlayerViewDto p in state.players)
            {
                seen.Add(p.playerId);
                var view = new PlayerView(p.playerId, p.nickname, p.alive);
                PlayerProfileItem item;
                if (items.TryGetValue(p.playerId, out item))
                {
                    item.Refresh(view);
                }
                else
                {
                    item = Instantiate(itemPrefab, gridRoot);
                    Sprite portrait = PortraitResolver != null ? PortraitResolver(p.playerId) : null;
                    item.Setup(view, p.playerId == myId, portrait, OnItemClicked);
                    items.Add(p.playerId, item);
                }
            }
            toRemove.Clear();
            foreach (long id in items.Keys)
            {
                if (!seen.Contains(id))
                {
                    toRemove.Add(id);
                }
            }
            foreach (long id in toRemove)
            {
                Destroy(items[id].gameObject);
                items.Remove(id);
            }
        }

        private void OnItemClicked(long id)
        {
            if (!usable || !targetable.Contains(id))
            {
                return;
            }
            selectedId = selectedId == id ? 0 : id; // 같은 카드를 다시 누르면 선택 해제
            RefreshSelectionOnly();
        }

        private void OnConfirmClicked()
        {
            if (usable && selectedId != 0 && Confirmed != null)
            {
                Confirmed(selectedId);
            }
        }

        private void OnSkipClicked()
        {
            if (usable && Skipped != null)
            {
                Skipped();
            }
        }

        private void RefreshSelectionOnly()
        {
            long shown = selectedId != 0 ? selectedId : chosenId;
            foreach (PlayerProfileItem item in items.Values)
            {
                item.SetSelected(item.PlayerId == shown);
            }
            if (confirmButton != null)
            {
                confirmButton.interactable = usable && selectedId != 0 && selectedId != chosenId;
            }
        }
    }
}
