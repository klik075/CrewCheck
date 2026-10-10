using System.Collections.Generic;

namespace WhoisntCitizen.Game
{
    /// <summary>밤 화면에 보여 줄 방: 배경(디자인 코드)과 그 방에 있는 사람(나 먼저, 나머지는 state.players 순서).</summary>
    public sealed class NightRoom
    {
        public string DesignId;   // 밤 배경·컷인 이미지의 디자인 코드 (예: CREW_DOCTOR, PIRATE_SHARED). 모르는 직업이면 null
        public bool Shared;       // 해적 동료와 같이 있는 방
        public readonly List<PlayerViewDto> Members = new List<PlayerViewDto>();
    }

    /// <summary>
    /// 밤에 누가 어느 방에 있는지 정한다. 화면 연출용이고 새 정보를 드러내지 않는다:
    /// 해적 방에는 내가 이미 아는 해적 동료(/me의 mafiaTeammateIds, 이번 판 접선으로 알게 된 해적)만 들어온다.
    /// - 해적 진영이고 살아 있는 동료를 알고 있으면 해적 공용 방(PIRATE_SHARED)에 다 같이 있다.
    /// - 앵무새는 접선 전에는 동료를 모르므로 앵무새 방에 혼자 있고, 접선하면 해적 방으로 들어간다.
    ///   해적은 다음 /me(페이즈가 바뀔 때)에 앵무새를 동료로 받으면 방에 앵무새가 들어온다.
    /// - 그 밖에는 내 직업의 방에 혼자 있다. 원숭이는 서버가 보여 주는 위장 직업의 방이다.
    /// </summary>
    public static class NightRoomRules
    {
        public const string PirateShared = "PIRATE_SHARED";

        /// <summary>직업 코드 → 밤 배경·능력 컷인의 디자인 코드. 서버의 중립 직업은 아트 이름이 다르다. 모르면 null.</summary>
        public static string DesignIdFor(string roleCode)
        {
            switch (roleCode)
            {
                case null:
                case "":
                    return null;
                case "NEUTRAL_SIREN": return "CONCEPT_SIREN";
                case "NEUTRAL_KRAKEN": return "PIRATE_KRAKEN";
                case "NEUTRAL_GHOST_CAPTAIN": return "CONCEPT_GHOST_CAPTAIN";
                case "NEUTRAL_MERMAID": return "THIRD_MERMAID";
                default: return roleCode; // PIRATE_*, CREW_* 는 아트 이름과 같다
            }
        }

        /// <param name="contactedPirateIds">이번 판에 내 접선으로 알게 된 해적 (밤 행동 응답의 contactedPirateIds를 모은 것). 없으면 null.</param>
        public static NightRoom RoomFor(MyRoleDto me, GameStateDto state, ICollection<long> contactedPirateIds)
        {
            var room = new NightRoom();
            if (me == null || state == null)
            {
                return room;
            }
            PlayerViewDto self = GameStateQueries.FindPlayer(state, me.playerId);
            room.Members.Add(self ?? new PlayerViewDto { playerId = me.playerId, nickname = string.Empty, alive = me.alive });
            if (me.faction == Factions.Pirate)
            {
                foreach (PlayerViewDto p in state.players)
                {
                    if (p.playerId == me.playerId || !p.alive)
                    {
                        continue;
                    }
                    bool known = (me.mafiaTeammateIds != null && me.mafiaTeammateIds.Contains(p.playerId))
                        || (contactedPirateIds != null && contactedPirateIds.Contains(p.playerId));
                    if (known)
                    {
                        room.Members.Add(p);
                    }
                }
            }
            room.Shared = room.Members.Count > 1;
            room.DesignId = room.Shared ? PirateShared : DesignIdFor(me.role);
            return room;
        }
    }
}
