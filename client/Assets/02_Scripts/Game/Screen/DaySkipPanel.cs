using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Vote;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 낮 토론 넘기기. NightActionPanel과 같은 배치(VotePanel 프리팹 복제)로 하단 시트 Drawer 안에 둔다.
    /// 플레이어 카드는 보기 전용(누를 수 없음)이고, [토론 넘기기] 버튼 하나와 넘긴 인원 안내만 있다.
    /// 서버 통신은 하지 않는다. 넘기기는 이벤트로 넘기고, GameScreen이 GameController로 보낸다.
    /// 낮에는 GameScreen이 VotePanel 대신 이 패널을 보여 준다.
    /// </summary>
    public sealed class DaySkipPanel : MonoBehaviour
    {
        [Header("Prefab / Layout")]
        [SerializeField] private PlayerProfileItem itemPrefab;
        [SerializeField] private RectTransform gridRoot; // GridLayoutGroup이 붙은 Content

        [Header("UI")]
        [SerializeField] private Button skipButton;
        [SerializeField] private TextMeshProUGUI skipButtonText;
        [SerializeField] private TextMeshProUGUI statusText;

        private readonly Dictionary<long, PlayerProfileItem> items = new Dictionary<long, PlayerProfileItem>();
        private readonly HashSet<long> seen = new HashSet<long>();
        private readonly List<long> toRemove = new List<long>();

        private bool canSkip;

        /// <summary>이번 낮 토론을 넘김</summary>
        public event Action Skipped;

        /// <summary>playerId → 초상화. NightActionPanel.PortraitResolver와 같은 것을 넣는다.</summary>
        public Func<long, Sprite> PortraitResolver { get; set; }

        private void Awake()
        {
            if (skipButton != null)
            {
                skipButton.onClick.AddListener(OnSkipClicked);
            }
        }

        private void OnDestroy()
        {
            if (skipButton != null)
            {
                skipButton.onClick.RemoveListener(OnSkipClicked);
            }
        }

        /// <summary>최신 상태 반영. GameScreen이 상태·내 역할·접수 결과가 바뀔 때마다 부른다.</summary>
        public void Refresh(GameSession session)
        {
            if (session == null || session.State == null || session.Me == null)
            {
                return;
            }
            GameStateDto state = session.State;
            long myId = session.Me.playerId;
            bool isDay = state.phase == GamePhases.Day;
            bool alive = GameStateQueries.IsAlive(state, myId);
            bool skipped = session.SkippedToday;
            canSkip = session.CanSkipDay;

            SyncItems(state, myId);
            foreach (PlayerProfileItem item in items.Values)
            {
                item.SetSelected(false);
                item.SetClickable(false); // 보기 전용
            }

            if (skipButton != null)
            {
                skipButton.interactable = canSkip;
            }
            if (skipButtonText != null)
            {
                skipButtonText.text = skipped ? "넘김 완료" : "토론 넘기기";
            }
            if (statusText != null)
            {
                statusText.text = GameScreenText.NoRichText(
                    GameScreenText.DayStatus(isDay, alive, skipped, session.DaySkipProgress));
            }
        }

        private void SyncItems(GameStateDto state, long myId)
        {
            if (itemPrefab == null || gridRoot == null)
            {
                return;
            }
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
                    item.Setup(view, p.playerId == myId, portrait, null);
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

        private void OnSkipClicked()
        {
            if (canSkip && Skipped != null)
            {
                Skipped();
            }
        }
    }
}
