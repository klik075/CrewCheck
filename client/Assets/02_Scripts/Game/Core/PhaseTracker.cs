using System.Collections.Generic;

namespace WhoisntCitizen.Game
{
    public enum GameEventType
    {
        PhaseChanged,   // 페이즈가 바뀜 (처음 받은 상태도 여기에 해당)
        PlayerDied,     // 직전 상태에서 살아 있던 사람이 사망함
        GameEnded       // ENDED가 됨
    }

    /// <summary>PhaseTracker가 만드는 이벤트. Type에 따라 채워지는 필드가 다르다.</summary>
    public sealed class GameEvent
    {
        public GameEventType Type;
        public string Phase;          // 새 페이즈 (GamePhases)
        public string PreviousPhase;  // PhaseChanged: 이전 페이즈. 처음 받은 상태면 null
        public int Day;
        public long PhaseVersion;
        public int MissedPhases;      // PhaseChanged: 폴링 사이에 지나가 버린 페이즈 수
        public long PlayerId;         // PlayerDied
        public string Nickname;       // PlayerDied
        public string Winner;         // GameEnded (Factions)
    }

    /// <summary>
    /// 폴링한 게임 상태를 직전 상태와 비교해 이벤트로 바꾼다. 이벤트 순서: PhaseChanged → PlayerDied → GameEnded.
    /// - phaseVersion이 줄어든 응답(늦게 도착한 옛 응답)은 무시한다.
    /// - 페이즈를 놓쳐도(MissedPhases > 0) 밤 결과·처형 결과는 서버에 마지막 결과가 남아 있어 나중에 조회할 수 있다.
    /// - 처음 받은 상태에서는 이미 죽어 있는 사람을 PlayerDied로 알리지 않는다(재접속 대비).
    /// </summary>
    public sealed class PhaseTracker
    {
        /// <summary>마지막으로 반영한 상태. 아직 없으면 null.</summary>
        public GameStateDto Current { get; private set; }

        public void Reset()
        {
            Current = null;
        }

        /// <summary>새 상태를 반영하고 생긴 이벤트를 돌려준다. 바뀐 게 없으면 빈 목록.</summary>
        public List<GameEvent> Update(GameStateDto next)
        {
            var events = new List<GameEvent>();
            if (next == null)
            {
                return events;
            }

            GameStateDto previous = Current;
            bool sameGame = previous != null && previous.gameId == next.gameId;
            if (sameGame && next.phaseVersion <= previous.phaseVersion)
            {
                if (next.phaseVersion == previous.phaseVersion)
                {
                    Current = next;
                }
                return events;
            }

            Current = next;
            if (!sameGame)
            {
                events.Add(PhaseChanged(next, null, 0));
            }
            else
            {
                int missed = (int)(next.phaseVersion - previous.phaseVersion - 1);
                events.Add(PhaseChanged(next, previous.phase, missed));
                AddDeaths(events, previous, next);
            }

            bool wasEnded = sameGame && previous.phase == GamePhases.Ended;
            if (next.phase == GamePhases.Ended && !wasEnded)
            {
                events.Add(new GameEvent
                {
                    Type = GameEventType.GameEnded,
                    Phase = next.phase,
                    Day = next.day,
                    PhaseVersion = next.phaseVersion,
                    Winner = next.winner
                });
            }
            return events;
        }

        private static GameEvent PhaseChanged(GameStateDto next, string previousPhase, int missed)
        {
            return new GameEvent
            {
                Type = GameEventType.PhaseChanged,
                Phase = next.phase,
                PreviousPhase = previousPhase,
                Day = next.day,
                PhaseVersion = next.phaseVersion,
                MissedPhases = missed
            };
        }

        private static void AddDeaths(List<GameEvent> events, GameStateDto previous, GameStateDto next)
        {
            foreach (PlayerViewDto p in next.players)
            {
                if (p.alive)
                {
                    continue;
                }
                PlayerViewDto before = GameStateQueries.FindPlayer(previous, p.playerId);
                if (before != null && before.alive)
                {
                    events.Add(new GameEvent
                    {
                        Type = GameEventType.PlayerDied,
                        Phase = next.phase,
                        Day = next.day,
                        PhaseVersion = next.phaseVersion,
                        PlayerId = p.playerId,
                        Nickname = p.nickname
                    });
                }
            }
        }
    }
}
