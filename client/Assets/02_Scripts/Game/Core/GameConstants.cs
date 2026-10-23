namespace WhoisntCitizen.Game
{
    // 서버 enum 값. JsonUtility는 JSON 문자열을 C# enum으로 읽지 못해서 DTO에는 string으로 받고 이 상수와 비교한다.
    // 값은 서버(GamePhase, Faction, ActionCode, ReportType, roles.code, 오류 code)와 대소문자까지 같아야 한다.

    /// <summary>게임 페이즈. NIGHT → NIGHT_RESULT → DAY → VOTE → EXECUTION → NIGHT ... → ENDED</summary>
    public static class GamePhases
    {
        public const string Night = "NIGHT";
        public const string NightResult = "NIGHT_RESULT";
        public const string Day = "DAY";
        public const string Vote = "VOTE";
        public const string Execution = "EXECUTION";
        public const string Ended = "ENDED";
    }

    /// <summary>진영. 앵무새는 PIRATE, 원숭이는 CREW.</summary>
    /// <summary>게임이 끝난 이유 (서버 GameEndReason). 진행 중이면 null. 취소된 게임은 winner가 null이고 전적에 반영되지 않는다.</summary>
    public static class EndReasons
    {
        public const string Win = "WIN";                                              // 승리 팀이 정해짐
        public const string CancelledAllDisconnected = "CANCELLED_ALL_DISCONNECTED";  // 살아 있는 플레이어가 모두 연결이 끊김
        public const string CancelledNoDeaths = "CANCELLED_NO_DEATHS";                // 정해진 일수 동안 연속으로 아무도 죽지 않음
        public const string CancelledError = "CANCELLED_ERROR";                       // 서버 오류

        public static bool IsCancelled(string endReason)
        {
            return !string.IsNullOrEmpty(endReason) && endReason != Win;
        }
    }

    public static class Factions
    {
        public const string Crew = "CREW";
        public const string Pirate = "PIRATE";
        public const string Neutral = "NEUTRAL"; // 세이렌·크라켄·유령 선장·인어 (각자 승리 조건이 다르다)
    }

    /// <summary>
    /// 게임 안에서 지금 속한 팀 (서버 Team, /me의 team, 결과의 players[].team). 처음에는 직업의 진영을 따르고,
    /// 세이렌에게 유혹당하면 직업은 그대로 두고 SIREN이 된다.
    /// </summary>
    public static class Teams
    {
        public const string Crew = "CREW";       // 유혹되지 않은 선원 진영
        public const string Pirate = "PIRATE";   // 해적 진영 (유혹되지 않는다)
        public const string Siren = "SIREN";     // 세이렌 + 유혹당한 선원 진영·인어
        public const string Neutral = "NEUTRAL"; // 혼자 이기는 제3 세력 (크라켄, 유령 선장, 유혹되지 않은 인어)
    }

    /// <summary>
    /// 이긴 쪽 (서버 Winner, state·result의 winner). NEUTRAL은 오지 않는다. 팀 승리(CREW, PIRATE, SIREN)는 그 팀 전원이,
    /// 단독 승리(KRAKEN, GHOST_CAPTAIN, MERMAID)는 그 사람만 이긴다. 실제로 이긴 사람은 결과의 winnerIds.
    /// </summary>
    public static class Winners
    {
        public const string Crew = "CREW";
        public const string Pirate = "PIRATE";
        public const string Siren = "SIREN";
        public const string Kraken = "KRAKEN";
        public const string GhostCaptain = "GHOST_CAPTAIN";
        public const string Mermaid = "MERMAID";

        /// <summary>팀이 아니라 그 사람 혼자 이기는 승리인지 (세이렌 팀도 결과 화면에서는 개인 승리로 보여 준다).</summary>
        public static bool IsPersonal(string winner)
        {
            return winner == Siren || winner == Kraken || winner == GhostCaptain || winner == Mermaid;
        }
    }

    /// <summary>밤 결과 deaths[].cause (서버 DeathCause).</summary>
    public static class DeathCauses
    {
        public const string Attack = "ATTACK";         // 밤에 해적의 습격
        public const string Kraken = "KRAKEN";         // 밤에 크라켄의 발동
        public const string Execution = "EXECUTION";   // 낮 투표로 처형
        public const string Disconnect = "DISCONNECT"; // 연결이 끊겨 사망 처리
    }

    /// <summary>밤 능력 코드 (/me의 actionCode). null이면 능력 없음.</summary>
    public static class ActionCodes
    {
        public const string SelectAttackTarget = "SELECT_ATTACK_TARGET"; // 해적: 공격
        public const string InvestigateFaction = "INVESTIGATE_FACTION";  // 선장: 진영 조사
        public const string Protect = "PROTECT";                         // 선의: 보호
        public const string WatchVisitors = "WATCH_VISITORS";            // 망루지기: 방문자 감시
        public const string Block = "BLOCK";                             // 갑판장: 차단
        public const string ReadCorpseRole = "READ_CORPSE_ROLE";         // 주정뱅이: 시체 직업 확인
        public const string WatchAction = "WATCH_ACTION";                // 앵무새: 행동 관찰 (해적 지목 시 접선)
        public const string BanVote = "BAN_VOTE";                        // 요리사: 대상은 다음 투표를 못 함
        public const string Seduce = "SEDUCE";                           // 세이렌: 유혹 (성공한 다음 밤은 쉰다)
        public const string KrakenMark = "KRAKEN_MARK";                  // 크라켄: 표식 (/me의 actionCode)
        public const string KrakenStrike = "KRAKEN_STRIKE";              // 크라켄: 발동. 표식된 사람을 모두 처치 (대상 없음, 요청의 actionCode로 고른다)
    }

    /// <summary>밤 결과 개인 결과(reports)의 종류.</summary>
    public static class ReportTypes
    {
        public const string Faction = "FACTION";        // faction
        public const string CorpseRole = "CORPSE_ROLE"; // roleCode, roleName
        public const string Visitors = "VISITORS";      // players
        public const string Actions = "ACTIONS";        // actions
        public const string Block = "BLOCK";            // 갑판장: 대상을 차단함
        public const string Blocked = "BLOCKED";        // 갑판장에게 차단당해 능력이 무효 (targetId 없음)
        public const string VoteBan = "VOTE_BAN";       // 요리사: 대상이 다음 투표를 못 하게 함
        public const string VoteBanned = "VOTE_BANNED"; // 요리사에게 당해 오늘 투표를 못 함 (targetId 없음)
        public const string SeduceSuccess = "SEDUCE_SUCCESS"; // 세이렌: 대상을 세이렌 팀으로 만듦 (다음 밤은 쉰다)
        public const string SeduceFail = "SEDUCE_FAIL";       // 세이렌: 유혹할 수 없는 대상이라 실패
        public const string Seduced = "SEDUCED";              // 유혹당해 세이렌 팀이 됨 (targetId 없음, players = 세이렌 팀 동료)
        public const string KrakenMark = "KRAKEN_MARK";       // 크라켄: 대상에게 표식을 남김
        public const string KrakenMarked = "KRAKEN_MARKED";   // 크라켄의 표식이 남음 (targetId 없음)
    }

    /// <summary>직업 코드 (roles.code). 이름은 서버가 roleName으로 내려주므로 아이콘·색 등에만 쓴다.</summary>
    public static class RoleCodes
    {
        public const string PirateRaider = "PIRATE_RAIDER";
        public const string PirateParrot = "PIRATE_PARROT";
        public const string CrewCaptain = "CREW_CAPTAIN";
        public const string CrewDoctor = "CREW_DOCTOR";
        public const string CrewLookout = "CREW_LOOKOUT";
        public const string CrewBoatswain = "CREW_BOATSWAIN";
        public const string CrewDrunk = "CREW_DRUNK";
        public const string CrewMonkey = "CREW_MONKEY";
        public const string CrewSailor = "CREW_SAILOR";
    }

    /// <summary>오류 응답의 code (서버 GlobalExceptionHandler). 401은 본문이 없다.</summary>
    public static class GameErrorCodes
    {
        public const string BadRequest = "BAD_REQUEST";                // 400: 요청 형식 오류. message는 스프링 원문일 수 있어 화면에 그대로 쓰지 않는다
        public const string GameNotFound = "GAME_NOT_FOUND";           // 404
        public const string GameRuleViolation = "GAME_RULE_VIOLATION"; // 409: 페이즈가 다르거나 규칙 위반. message를 그대로 보여줘도 된다
        public const string Conflict = "CONFLICT";                     // 409: 로비 등 기타 상태 충돌
    }
}
