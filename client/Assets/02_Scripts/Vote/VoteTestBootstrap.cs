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
        [SerializeField] private ChatLogView chatLog;
        [SerializeField] private Sprite[] portraits;

        [Header("Mock")]
        [SerializeField] private long myPlayerId = 1;
        [SerializeField, Range(2, 12)] private int playerCount = 8;
        [SerializeField] private int deadPlayerId = 5; // 0이면 사망자 없음

        private static readonly string[] MockNames =
            { "재영", "유진", "원우", "현교", "의사", "지용", "포수", "망꾼", "앵무새", "스파이", "해적", "원숭이" };

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

            bottomTab.ChatSubmitted += OnChatSubmitted;
            bottomTab.ModeChanged += mode => Debug.Log($"[VoteTest] 하단 탭: {mode}");

            AddFakeHistory();
        }

        // 실제로는 채팅 API/소켓으로 보내고, 서버가 돌려준 메시지를 기록에 추가한다.
        private void OnChatSubmitted(string text)
        {
            Debug.Log($"[VoteTest] 채팅 전송: {text}");
            if (chatLog != null) chatLog.AddPlayerMessage(NameOf(myPlayerId), text, isMe: true);
        }

        private void AddFakeHistory()
        {
            if (chatLog == null) return;
            chatLog.AddSystemMessage("게임이 시작되었습니다. 8명의 선원이 배에 올랐습니다.");
            chatLog.AddSystemMessage("당신의 역할은 [시민]입니다.");
            chatLog.AddSystemMessage("밤이 되었습니다.");
            chatLog.AddSystemMessage("아침이 밝았습니다. 지난 밤 갑판장이 살해당했습니다.");
            chatLog.AddPlayerMessage("재영", "누가 갑판장 노렸지?");
            chatLog.AddPlayerMessage("유진", "1번이 범인인 듯");
            chatLog.AddPlayerMessage(NameOf(myPlayerId), "나 아님. 나 경찰이야", isMe: true);
            chatLog.AddPlayerMessage("원우", "경찰이면 어젯밤 누구 조사했는데?");
            chatLog.AddPlayerMessage("현교", "일단 말 많은 사람부터 의심해보자");
            chatLog.AddPlayerMessage("지용", "일단 너");
            chatLog.AddSystemMessage("투표 시간입니다. 하단 [+] 버튼으로 투표하세요.");
        }

        private string NameOf(long id)
        {
            PlayerView p = players.Find(x => x.playerId == id);
            return p != null ? p.nickname : $"#{id}";
        }

        [ContextMenu("Test/Add 10 Fake Messages")]
        private void AddFakeMessages()
        {
            for (int i = 0; i < 10; i++)
                chatLog.AddPlayerMessage(MockNames[Random.Range(0, playerCount)], $"테스트 메시지 {Random.Range(100, 999)}");
        }

        private void OnDestroy()
        {
            if (votePanel != null) votePanel.VoteConfirmed -= OnVoteConfirmed;
        }

        // 실제로는 여기서 POST /api/v1/games/{gameId}/votes { targetId } 를 보낸다.
        private void OnVoteConfirmed(long targetId)
        {
            Debug.Log($"[VoteTest] POST /votes targetId={targetId}");
            if (chatLog != null) chatLog.AddSystemMessage($"{NameOf(targetId)}에게 투표했습니다.");

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
            PlayerView victim = alive[Random.Range(0, alive.Count)];
            victim.alive = false;
            votePanel.SetPlayers(players, myPlayerId);
            if (chatLog != null) chatLog.AddSystemMessage($"{victim.nickname}이(가) 처형당했습니다.");
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
