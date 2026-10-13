namespace WhoisntCitizen.Game.Tests
{
    // 서버(Spring Boot 4, Jackson 3)가 실제로 직렬화한 응답 샘플. 서버 응답 형식이 바뀌면 여기를 새 출력으로 바꾸고 테스트를 다시 돌린다.
    // 생성: 서버 dev b41b546 기준. 서버 DTO를 서버 JsonMapper로 직렬화한 값이고, RoleList는 실제 DB(Flyway V2·V4)의 GET /api/v1/roles 응답이다.
    public static class GameJsonFixtures
    {
        public const string GameState =
            @"{""gameId"":""3f2b9c1e-7d4a-4b8e-9a1f-2c6d8e0b5a71"",""phase"":""NIGHT"",""day"":2,""phaseEndsAt"":""2026-10-01T12:00:30.123Z"",""serverTime"":""2026-10-01T12:00:05.456Z"",""phaseVersion"":7,""players"":[{""playerId"":11,""nickname"":""철수"",""alive"":true},{""playerId"":12,""nickname"":""영희"",""alive"":false}],""winner"":null}";

        public const string GameStateEnded =
            @"{""gameId"":""g-ended"",""phase"":""ENDED"",""day"":3,""phaseEndsAt"":null,""serverTime"":""2026-10-01T12:10:00Z"",""phaseVersion"":21,""players"":[{""playerId"":11,""nickname"":""철수"",""alive"":true}],""winner"":""CREW""}";

        public const string MyRoleRaider =
            @"{""playerId"":11,""role"":""PIRATE_RAIDER"",""roleName"":""해적"",""faction"":""PIRATE"",""actionCode"":""SELECT_ATTACK_TARGET"",""remainingUses"":-1,""alive"":true,""mafiaTeammateIds"":[13,14],""contacted"":false}";

        public const string MyRoleSailor =
            @"{""playerId"":15,""role"":""CREW_SAILOR"",""roleName"":""선원"",""faction"":""CREW"",""actionCode"":null,""remainingUses"":-1,""alive"":false,""mafiaTeammateIds"":[],""contacted"":false}";

        public const string MyRoleDrunk =
            @"{""playerId"":16,""role"":""CREW_DRUNK"",""roleName"":""주정뱅이"",""faction"":""CREW"",""actionCode"":""READ_CORPSE_ROLE"",""remainingUses"":2,""alive"":true,""mafiaTeammateIds"":[],""contacted"":false}";

        public const string MyRoleParrotContacted =
            @"{""playerId"":14,""role"":""PIRATE_PARROT"",""roleName"":""앵무새"",""faction"":""PIRATE"",""actionCode"":""WATCH_ACTION"",""remainingUses"":-1,""alive"":true,""mafiaTeammateIds"":[11,13],""contacted"":true}";

        public const string NightActionContact =
            @"{""accepted"":true,""phase"":""NIGHT"",""phaseVersion"":7,""contactedPirateIds"":[11,13]}";

        public const string NightActionResolved =
            @"{""accepted"":true,""phase"":""NIGHT_RESULT"",""phaseVersion"":8,""contactedPirateIds"":[]}";

        public const string NightResultKilledFaction =
            @"{""day"":2,""killedPlayerId"":12,""killedNickname"":""영희"",""protectedByDoctor"":false,""reports"":[{""type"":""FACTION"",""targetId"":11,""targetNickname"":""철수"",""faction"":""PIRATE"",""roleCode"":null,""roleName"":null,""players"":[],""actions"":[]}]}";

        public const string NightResultSavedAllTypes =
            @"{""day"":2,""killedPlayerId"":null,""killedNickname"":null,""protectedByDoctor"":true,""reports"":[{""type"":""VISITORS"",""targetId"":12,""targetNickname"":""영희"",""faction"":null,""roleCode"":null,""roleName"":null,""players"":[{""playerId"":11,""nickname"":""철수""},{""playerId"":17,""nickname"":""민수""}],""actions"":[]},{""type"":""ACTIONS"",""targetId"":17,""targetNickname"":""민수"",""faction"":null,""roleCode"":null,""roleName"":null,""players"":[],""actions"":[{""actionCode"":""PROTECT"",""targetId"":12,""targetNickname"":""영희""}]},{""type"":""CORPSE_ROLE"",""targetId"":15,""targetNickname"":""지훈"",""faction"":null,""roleCode"":""CREW_MONKEY"",""roleName"":""원숭이"",""players"":[],""actions"":[]}]}";

        public const string NightResultEmpty =
            @"{""day"":1,""killedPlayerId"":null,""killedNickname"":null,""protectedByDoctor"":false,""reports"":[]}";

        public const string VoteAccepted =
            @"{""accepted"":true,""phase"":""VOTE"",""phaseVersion"":11}";

        public const string VoteResolved =
            @"{""accepted"":true,""phase"":""EXECUTION"",""phaseVersion"":12}";

        public const string ExecutionExecuted =
            @"{""day"":2,""executedPlayerId"":11,""executedNickname"":""철수"",""tie"":false,""voteCounts"":{""11"":3,""12"":1},""votes"":[{""playerId"":11,""nickname"":""철수"",""count"":3},{""playerId"":12,""nickname"":""영희"",""count"":1}]}";

        public const string ExecutionTie =
            @"{""day"":2,""executedPlayerId"":null,""executedNickname"":null,""tie"":true,""voteCounts"":{""11"":2,""12"":2},""votes"":[{""playerId"":11,""nickname"":""철수"",""count"":2},{""playerId"":12,""nickname"":""영희"",""count"":2}]}";

        public const string ExecutionNoVotes =
            @"{""day"":2,""executedPlayerId"":null,""executedNickname"":null,""tie"":false,""voteCounts"":{},""votes"":[]}";

        public const string GameResult =
            @"{""ended"":true,""winner"":""CREW"",""lastDay"":3,""players"":[{""playerId"":11,""nickname"":""철수"",""role"":""PIRATE_RAIDER"",""roleName"":""해적"",""alive"":false},{""playerId"":15,""nickname"":""지훈"",""role"":""CREW_MONKEY"",""roleName"":""원숭이"",""alive"":true}]}";

        public const string GameResultNotEnded =
            @"{""ended"":false,""winner"":null,""lastDay"":2,""players"":[]}";

        // 서버 Preventing-Never-Ending-Games 기준: 상태·결과에 endReason이 붙고, 취소된 게임은 winner가 null이다.
        public const string GameStateCancelled =
            @"{""gameId"":""g-cancelled"",""phase"":""ENDED"",""day"":10,""phaseEndsAt"":null,""serverTime"":""2026-10-01T12:30:00Z"",""phaseVersion"":51,""players"":[{""playerId"":11,""nickname"":""철수"",""alive"":true}],""winner"":null,""endReason"":""CANCELLED_NO_DEATHS""}";

        public const string GameResultCancelled =
            @"{""ended"":true,""winner"":null,""endReason"":""CANCELLED_ALL_DISCONNECTED"",""lastDay"":4,""players"":[{""playerId"":11,""nickname"":""철수"",""role"":""PIRATE_RAIDER"",""roleName"":""해적"",""alive"":true}]}";

        public const string RoleList =
            @"{""roles"":[{""code"":""CREW_BOATSWAIN"",""name"":""갑판장"",""faction"":""CREW"",""actionCode"":""BLOCK""},{""code"":""CREW_CAPTAIN"",""name"":""선장"",""faction"":""CREW"",""actionCode"":""INVESTIGATE_FACTION""},{""code"":""CREW_DOCTOR"",""name"":""선의"",""faction"":""CREW"",""actionCode"":""PROTECT""},{""code"":""CREW_DRUNK"",""name"":""주정뱅이"",""faction"":""CREW"",""actionCode"":""READ_CORPSE_ROLE""},{""code"":""CREW_LOOKOUT"",""name"":""망루지기"",""faction"":""CREW"",""actionCode"":""WATCH_VISITORS""},{""code"":""CREW_MONKEY"",""name"":""원숭이"",""faction"":""CREW"",""actionCode"":null},{""code"":""CREW_SAILOR"",""name"":""선원"",""faction"":""CREW"",""actionCode"":null},{""code"":""PIRATE_PARROT"",""name"":""앵무새"",""faction"":""PIRATE"",""actionCode"":""WATCH_ACTION""},{""code"":""PIRATE_RAIDER"",""name"":""해적"",""faction"":""PIRATE"",""actionCode"":""SELECT_ATTACK_TARGET""}]}";

        public const string Error409 =
            @"{""code"":""GAME_RULE_VIOLATION"",""message"":""이틀 연속으로 자신을 보호할 수 없습니다.""}";

        public const string Error404 =
            @"{""code"":""GAME_NOT_FOUND"",""message"":""게임을 찾을 수 없습니다: no-such-game""}";
    }
}
