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
    public static class Factions
    {
        public const string Crew = "CREW";
        public const string Pirate = "PIRATE";
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
    }

    /// <summary>밤 결과 개인 결과(reports)의 종류.</summary>
    public static class ReportTypes
    {
        public const string Faction = "FACTION";        // faction
        public const string CorpseRole = "CORPSE_ROLE"; // roleCode, roleName
        public const string Visitors = "VISITORS";      // players
        public const string Actions = "ACTIONS";        // actions
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
