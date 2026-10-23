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
    /// 크라켄: 대상을 고르면 [표식], 고르지 않았고 표식을 남긴 생존자가 있으면 같은 버튼이 [발동](대상 없음)이 된다.
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
        private bool strikeAvailable; // 크라켄이 발동할 수 있음 (표식을 남긴 생존자가 있음)
        private bool struck;          // 크라켄이 이번 밤 발동을 골랐고 서버가 받아 줌
        private string actionName = string.Empty;
        private GameSession lastSession;

        /// <summary>화면에 고른 것으로 보이는 대상: 지금 고른 대상, 없으면 서버가 받아 준 대상 (0 = 없음).</summary>
        public long ShownTarget
        {
            get { return selectedId != 0 ? selectedId : chosenId; }
        }

        /// <summary>지금 능력을 쓸 수 있고 이 플레이어가 대상이 될 수 있는지.</summary>
        public bool CanTarget(long playerId)
        {
            return usable && targetable.Contains(playerId);
        }

        /// <summary>[확정]을 누를 수 있는지: 능력을 쓸 수 있고, 서버가 받은 대상과 다른 대상을 골랐다.</summary>
        public bool CanConfirm
        {
            get { return usable && selectedId != 0 && selectedId != chosenId; }
        }

        /// <summary>크라켄이 [확정] 자리의 [발동]을 누를 수 있는지: 대상을 고르지 않았고, 표식을 남긴 생존자가 있고, 아직 발동을 고르지 않았다.</summary>
        public bool CanStrike
        {
            get { return usable && strikeAvailable && selectedId == 0 && !struck; }
        }

        /// <summary>
        /// [확정] 자리 버튼의 이름. 크라켄이 대상을 고르지 않았으면 "발동"(골랐으면 "발동함"), 아니면 능력 이름(이미 받아 준 대상이 있으면 "~ 변경").
        /// </summary>
        public string ConfirmLabel
        {
            get
            {
                if (selectedId == 0 && (strikeAvailable || struck))
                {
                    return struck ? "발동함" : ReportFormatter.ActionName(ActionCodes.KrakenStrike);
                }
                return chosenId != 0 || struck ? actionName + " 변경" : actionName;
            }
        }

        /// <summary>[확정]을 누른 것과 같다 (패널 대신 다른 화면의 버튼에서). 크라켄이 대상 없이 누르면 발동.</summary>
        public void Confirm()
        {
            OnConfirmClicked();
        }

        /// <summary>[넘기기]를 누른 것과 같다.</summary>
        public void Skip()
        {
            OnSkipClicked();
        }

        /// <summary>격자 대신 다른 화면(플레이어 명단)에서 대상을 고를 때. 카드를 누른 것과 같다(같은 대상이면 선택 해제).</summary>
        public void Select(long playerId)
        {
            OnItemClicked(playerId);
            if (lastSession != null)
            {
                Refresh(lastSession); // 안내 문구도 고른 대상에 맞춘다
            }
        }

        /// <summary>확정한 대상 playerId</summary>
        public event Action<long> Confirmed;

        /// <summary>이번 밤 능력을 쓰지 않음</summary>
        public event Action Skipped;

        /// <summary>크라켄 발동 (대상 없음)</summary>
        public event Action Struck;

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
            lastSession = session;
            GameStateDto state = session.State;
            MyRoleDto me = session.Me;
            AbilityBlock block = session.NightAbility;
            usable = block == AbilityBlock.None;
            chosenId = session.MyNightTarget;
            strikeAvailable = session.CanStrike;
            struck = session.StruckTonight;
            actionName = ReportFormatter.ActionName(me.actionCode);

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

            RefreshConfirm();
            if (skipButton != null)
            {
                skipButton.interactable = usable && !session.SkippedTonight;
            }
            if (statusText != null)
            {
                long shownTarget = selectedId != 0 ? selectedId : chosenId;
                statusText.text = GameScreenText.NoRichText(GameScreenText.NightStatus(block, me.actionCode, shownTarget,
                    session.SkippedTonight, state, struck, session.KrakenMarks.Count));
            }
        }

        private void RefreshConfirm()
        {
            if (confirmButton != null)
            {
                confirmButton.interactable = CanConfirm || CanStrike;
            }
            if (confirmButtonText != null)
            {
                confirmButtonText.text = ConfirmLabel;
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
            if (usable && selectedId != 0)
            {
                if (Confirmed != null) Confirmed(selectedId);
            }
            else if (CanStrike)
            {
                if (Struck != null) Struck();
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
            RefreshConfirm();
        }
    }
}
