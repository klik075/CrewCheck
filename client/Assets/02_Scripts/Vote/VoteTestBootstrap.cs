using System.Collections.Generic;
using UnityEngine;
using WhoisntCitizen.Game;
using WhoisntCitizen.GameUI;

namespace WhoisntCitizen.Vote
{
    // Vote_test 씬 전용: 서버 대신 가짜 플레이어 목록을 넣어 투표 UI를 확인한다.
    // 실제 Game 씬에서는 GET /games/{id} 폴링 결과를 votePanel.SetPlayers로 넘기면 된다.
    public class VoteTestBootstrap : MonoBehaviour
    {
        [SerializeField] private BottomTabController bottomTab;
        [SerializeField] private VotePanelController votePanel;
        [SerializeField] private Sprite[] portraits;

        [Header("Mock")]
        [SerializeField] private long myPlayerId = 1;
        [SerializeField, Range(2, 12)] private int playerCount = 8;
        [SerializeField] private int deadPlayerId = 5; // 0이면 사망자 없음

        private static readonly string[] MockNames =
            { "원우", "도로뇽", "김댄디", "선장", "갑판장", "의사", "포수", "망꾼", "앵무새", "스파이", "해적", "원숭이" };

        private readonly List<PlayerView> players = new List<PlayerView>();
        private readonly Dictionary<long, int> voteCounts = new Dictionary<long, int>();
        private long? myLastTarget;

        private void Start()
        {
            for (int i = 0; i < playerCount; i++)
            {
                long id = i + 1;
                players.Add(new PlayerView(id, MockNames[i % MockNames.Length], id != deadPlayerId));
            }

            votePanel.PortraitResolver = id =>
                portraits != null && portraits.Length > 0 ? portraits[(int)((id - 1) % portraits.Length)] : null;
            votePanel.SetPlayers(players, myPlayerId);
            votePanel.VoteConfirmed += OnVoteConfirmed;

            bottomTab.ChatSubmitted += text => Debug.Log($"[VoteTest] 채팅 전송: {text}");
            bottomTab.ModeChanged += mode => Debug.Log($"[VoteTest] 하단 탭: {mode}");
        }

        private void OnDestroy()
        {
            if (votePanel != null) votePanel.VoteConfirmed -= OnVoteConfirmed;
        }

        // 실제로는 여기서 POST /api/v1/games/{gameId}/votes { targetId } 를 보낸다.
        private void OnVoteConfirmed(long targetId)
        {
            Debug.Log($"[VoteTest] POST /votes targetId={targetId}");

            // 가짜 집계: 내 표만 반영 (재투표 시 이전 표를 옮긴다)
            if (myLastTarget.HasValue) voteCounts[myLastTarget.Value]--;
            voteCounts.TryGetValue(targetId, out int c);
            voteCounts[targetId] = c + 1;
            myLastTarget = targetId;
            votePanel.SetVoteCounts(voteCounts);
        }

        // 인스펙터 컴포넌트 메뉴(⋮)에서 실행해 상태 변화를 테스트한다.
        [ContextMenu("Test/Kill Random Alive Player")]
        private void KillRandomAlive()
        {
            var alive = players.FindAll(p => p.alive && p.playerId != myPlayerId);
            if (alive.Count == 0) return;
            alive[Random.Range(0, alive.Count)].alive = false;
            votePanel.SetPlayers(players, myPlayerId);
        }

        [ContextMenu("Test/Toggle My Death")]
        private void ToggleMyDeath()
        {
            PlayerView me = players.Find(p => p.playerId == myPlayerId);
            if (me == null) return;
            me.alive = !me.alive;
            votePanel.SetPlayers(players, myPlayerId);
        }

        [ContextMenu("Test/Toggle Voting Open")]
        private void ToggleVotingOpen()
        {
            votingOpen = !votingOpen;
            votePanel.SetVotingOpen(votingOpen);
        }

        private bool votingOpen = true;
    }
}
