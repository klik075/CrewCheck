using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.Vote
{
    // 투표 패널: Grid Layout 아래에 PlayerProfileItem을 동적으로 만들고, 선택/확정을 관리한다.
    // 서버 통신은 하지 않는다. 확정 시 VoteConfirmed 이벤트로 targetId를 넘기면 바깥에서 POST /votes를 보낸다.
    //
    // 서버 규칙 (Game.recordVote)
    //  - 살아있는 플레이어만 투표할 수 있고, 살아있는 플레이어에게만 투표할 수 있다.
    //  - 재투표하면 덮어쓴다. (그래서 확정 후에도 선택을 바꿀 수 있게 한다)
    //  - 자기 자신 투표는 서버가 막지 않는다. → allowSelfVote로 UI에서 결정
    public class VotePanelController : MonoBehaviour
    {
        [Header("Prefab / Layout")]
        [SerializeField] private PlayerProfileItem itemPrefab;
        [SerializeField] private RectTransform gridRoot; // GridLayoutGroup이 붙은 Content

        [Header("UI")]
        [SerializeField] private Button confirmButton;
        [SerializeField] private TextMeshProUGUI confirmButtonText;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Rule")]
        [SerializeField] private bool allowSelfVote = false;

        // 확정한 대상 playerId
        public event Action<long> VoteConfirmed;

        // playerId → 초상화. 직업/아바타 매핑이 정해지면 바깥에서 주입한다.
        public Func<long, Sprite> PortraitResolver { get; set; }

        public long? SelectedPlayerId => selectedId;
        public long? SubmittedPlayerId => submittedId;

        private readonly Dictionary<long, PlayerProfileItem> items = new Dictionary<long, PlayerProfileItem>();
        private readonly HashSet<long> seen = new HashSet<long>();
        private readonly List<long> toRemove = new List<long>();

        private long myPlayerId;
        private bool amAlive = true;
        private bool votingOpen = true;
        private long? selectedId;
        private long? submittedId;

        private void Awake()
        {
            if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmClicked);
            RefreshUI();
        }

        private void OnDestroy()
        {
            if (confirmButton != null) confirmButton.onClick.RemoveListener(OnConfirmClicked);
        }

        // ---------- 외부 API ----------

        // 플레이어 목록 반영. 처음엔 아이템을 생성하고, 이후 호출(폴링)에서는 playerId 기준으로 갱신만 한다.
        public void SetPlayers(IList<PlayerView> players, long myId)
        {
            myPlayerId = myId;
            amAlive = false;
            seen.Clear();

            foreach (PlayerView p in players)
            {
                seen.Add(p.playerId);
                if (p.playerId == myId) amAlive = p.alive;

                if (items.TryGetValue(p.playerId, out PlayerProfileItem item))
                {
                    item.Refresh(p);
                }
                else
                {
                    item = Instantiate(itemPrefab, gridRoot);
                    item.Setup(p, p.playerId == myId, PortraitResolver?.Invoke(p.playerId), OnItemClicked);
                    items.Add(p.playerId, item);
                }
            }

            // 목록에서 빠진 플레이어 제거
            toRemove.Clear();
            foreach (long id in items.Keys)
                if (!seen.Contains(id)) toRemove.Add(id);
            foreach (long id in toRemove)
            {
                Destroy(items[id].gameObject);
                items.Remove(id);
            }

            // 선택했던 대상이 죽었거나 사라졌으면 선택 해제.
            // 투표한 대상이 투표 도중 죽으면(연결 끊김) 서버가 그 표를 지우므로 "투표했습니다" 표시도 지운다.
            if (selectedId.HasValue && !IsTargetable(selectedId.Value)) selectedId = null;
            if (submittedId.HasValue && !IsTargetable(submittedId.Value)) submittedId = null;

            RefreshUI();
        }

        // 투표 페이즈가 아닐 때 false (패널은 보이되 조작 불가)
        public void SetVotingOpen(bool open)
        {
            votingOpen = open;
            RefreshUI();
        }

        // 득표 수 표시 (서버 votes 집계가 생기면 연결). null이면 모두 0.
        public void SetVoteCounts(IDictionary<long, int> counts)
        {
            foreach (KeyValuePair<long, PlayerProfileItem> pair in items)
            {
                int c = 0;
                if (counts != null) counts.TryGetValue(pair.Key, out c);
                pair.Value.SetVoteCount(c);
            }
        }

        // 새 투표 라운드 시작 시 호출
        public void ResetVote()
        {
            selectedId = null;
            submittedId = null;
            SetVoteCounts(null);
            RefreshUI();
        }

        // ---------- 내부 ----------

        private bool IsTargetable(long id)
        {
            return items.TryGetValue(id, out PlayerProfileItem item)
                   && item.IsAlive
                   && (allowSelfVote || id != myPlayerId);
        }

        private bool CanVote => votingOpen && amAlive;

        private void OnItemClicked(long id)
        {
            if (!CanVote || !IsTargetable(id)) return;
            selectedId = selectedId == id ? (long?)null : id; // 같은 카드를 다시 누르면 선택 해제
            RefreshUI();
        }

        private void OnConfirmClicked()
        {
            if (!CanVote || !selectedId.HasValue) return;
            submittedId = selectedId;
            VoteConfirmed?.Invoke(selectedId.Value);
            RefreshUI();
        }

        private void RefreshUI()
        {
            foreach (PlayerProfileItem item in items.Values)
            {
                item.SetSelected(selectedId.HasValue && item.PlayerId == selectedId.Value);
                item.SetClickable(CanVote && IsTargetable(item.PlayerId));
            }

            if (confirmButton != null)
                confirmButton.interactable = CanVote && selectedId.HasValue && selectedId != submittedId;
            if (confirmButtonText != null)
                confirmButtonText.text = submittedId.HasValue ? "투표 변경" : "투표하기";
            if (statusText != null)
                statusText.text = BuildStatus();
        }

        private string BuildStatus()
        {
            if (!votingOpen) return "지금은 투표 시간이 아닙니다.";
            if (!amAlive) return "사망한 플레이어는 투표할 수 없습니다.";
            if (submittedId.HasValue && selectedId == submittedId)
                return $"<b>{NameOf(submittedId.Value)}</b>에게 투표했습니다.\n다른 플레이어를 골라 변경할 수 있습니다.";
            if (selectedId.HasValue)
                return $"<b>{NameOf(selectedId.Value)}</b> 선택됨\n투표 버튼을 눌러 확정하세요.";
            return "처형할 플레이어를\n선택하세요.";
        }

        private string NameOf(long id) => items.TryGetValue(id, out PlayerProfileItem item) ? item.Nickname : $"#{id}";
    }
}
