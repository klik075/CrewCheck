using System;
using System.Collections.Generic;

namespace WhoisntCitizen.Game
{
    // 게임 API(/api/v1/games/...) 요청·응답 DTO. 서버 dev b41b546 기준. JsonUtility로 읽고 쓴다.
    // 규칙
    // - 필드 이름은 서버 JSON 키와 대소문자까지 같아야 한다(다르면 조용히 기본값이 된다).
    // - enum은 string으로 받는다. 비교는 GameConstants의 상수로 한다.
    // - playerId는 로그인 userId와 같다. 행동·투표 대상(targetId)도 userId로 보낸다.
    // - 서버의 null id는 0이 된다. userId는 0이 될 수 없으므로 0 = "없음"으로 쓴다.
    // - 서버의 null 문자열은 null 또는 빈 문자열일 수 있으니 string.IsNullOrEmpty로 확인한다.
    // - 시각(phaseEndsAt, serverTime)은 ISO-8601 UTC 문자열이다. GameJson.ParseServerTime으로 바꾼다.
    // - 목록은 해당 없으면 빈 배열로 온다. 혹시 빠져도 null이 되지 않도록 빈 List로 초기화해 둔다.

    // ---------------------------------------------------------------- 요청

    /// <summary>밤 능력·투표 요청 body: {"targetId": 대상 userId}</summary>
    [Serializable]
    public class TargetRequestDto
    {
        public long targetId;
    }

    // ---------------------------------------------------------------- 게임 상태 GET /games/{id}

    [Serializable]
    public class GameStateDto
    {
        public string gameId;
        public string phase;        // GamePhases
        public int day;             // 첫 밤이 1일차
        public string phaseEndsAt;  // ENDED이면 null
        public string serverTime;   // 응답을 만든 서버 시각. 남은 시간 = phaseEndsAt - serverTime
        public long phaseVersion;   // 페이즈가 바뀔 때마다 증가. 이 값이 바뀌면 화면을 전환한다
        public List<PlayerViewDto> players = new List<PlayerViewDto>();
        public string winner;       // Factions. 끝나기 전에는 null
    }

    [Serializable]
    public class PlayerViewDto
    {
        public long playerId;       // = userId
        public string nickname;
        public bool alive;
    }

    // ---------------------------------------------------------------- 내 역할 GET /games/{id}/me

    /// <summary>본인에게 보이는 직업. 원숭이는 위장 직업이 그대로 온다(클라이언트는 구분하지 않는다).</summary>
    [Serializable]
    public class MyRoleDto
    {
        public long playerId;       // 내 userId
        public string role;         // RoleCodes
        public string roleName;
        public string faction;      // Factions
        public string actionCode;   // ActionCodes. 능력이 없으면 null
        public int remainingUses;   // 남은 사용 횟수. -1이면 무제한(또는 능력 없음)
        public bool alive;
        public List<long> mafiaTeammateIds = new List<long>(); // 알고 있는 해적 진영 동료(사망자 포함)
        public bool contacted;      // 앵무새가 해적과 접선했는지

        public bool HasAbility => !string.IsNullOrEmpty(actionCode);
        public bool IsUnlimited => remainingUses < 0;
    }

    // ---------------------------------------------------------------- 밤 능력 POST /night-actions, /night-actions/skip

    [Serializable]
    public class NightActionResultDto
    {
        public bool accepted;
        public string phase;        // 전원 제출로 바로 판정됐으면 NIGHT_RESULT
        public long phaseVersion;
        public List<long> contactedPirateIds = new List<long>(); // 이번 제출로 앵무새가 접선했을 때만 채워진다
    }

    // ---------------------------------------------------------------- 밤 결과 GET /night-result

    [Serializable]
    public class NightResultDto
    {
        public int day;
        public long killedPlayerId;     // 0이면 사망자 없음
        public string killedNickname;
        public bool protectedByDoctor;  // 선의 보호로 살았는지 (전체 공개)
        public List<ReportDto> reports = new List<ReportDto>(); // 내 개인 결과만

        public bool HasKill => killedPlayerId != 0;
    }

    /// <summary>개인 결과 한 건. type에 따라 채워지는 필드가 다르다.</summary>
    [Serializable]
    public class ReportDto
    {
        public string type;             // ReportTypes
        public long targetId;
        public string targetNickname;
        public string faction;          // FACTION: 대상의 진영
        public string roleCode;         // CORPSE_ROLE: 시체의 직업
        public string roleName;         // CORPSE_ROLE
        public List<PlayerRefDto> players = new List<PlayerRefDto>(); // VISITORS: 방문자
        public List<ActionViewDto> actions = new List<ActionViewDto>(); // ACTIONS: 대상이 한 행동
    }

    [Serializable]
    public class PlayerRefDto
    {
        public long playerId;
        public string nickname;
    }

    [Serializable]
    public class ActionViewDto
    {
        public string actionCode;       // ActionCodes
        public long targetId;
        public string targetNickname;
    }

    // ---------------------------------------------------------------- 투표 POST /votes, 처형 결과 GET /execution-result

    [Serializable]
    public class VoteResultDto
    {
        public bool accepted;
        public string phase;            // 전원 투표로 바로 판정됐으면 EXECUTION
        public long phaseVersion;
    }

    /// <summary>처형 결과. 서버의 voteCounts(Map)는 JsonUtility가 읽지 못해서 votes 목록을 쓴다.</summary>
    [Serializable]
    public class ExecutionResultDto
    {
        public int day;
        public long executedPlayerId;   // 0이면 처형 없음 (동률 또는 투표 없음)
        public string executedNickname;
        public bool tie;
        public List<VoteCountDto> votes = new List<VoteCountDto>(); // 득표 많은 순, 같으면 playerId 순

        public bool HasExecution => executedPlayerId != 0;
    }

    [Serializable]
    public class VoteCountDto
    {
        public long playerId;
        public string nickname;
        public int count;
    }

    // ---------------------------------------------------------------- 게임 결과 GET /result (종료 후 60초까지)

    [Serializable]
    public class GameResultDto
    {
        public bool ended;              // false면 아직 진행 중 (players는 비어 있음)
        public string winner;           // Factions
        public int lastDay;
        public List<PlayerResultDto> players = new List<PlayerResultDto>();
    }

    /// <summary>게임 종료 후 공개되는 실제 직업. 원숭이도 여기서는 CREW_MONKEY로 나온다.</summary>
    [Serializable]
    public class PlayerResultDto
    {
        public long playerId;
        public string nickname;
        public string role;             // RoleCodes (실제 직업)
        public string roleName;
        public bool alive;
    }

    // ---------------------------------------------------------------- 직업 목록 GET /api/v1/roles (code 오름차순, ?faction=CREW|PIRATE로 거를 수 있음)

    [Serializable]
    public class RoleListDto
    {
        public List<RoleDto> roles = new List<RoleDto>();
    }

    [Serializable]
    public class RoleDto
    {
        public string code;             // RoleCodes
        public string name;
        public string faction;          // Factions
        public string actionCode;       // 능력이 없으면 null
    }

    // ---------------------------------------------------------------- 오류 (400, 404, 409)

    /// <summary>오류 응답 {code, message}. 401은 본문이 없다.</summary>
    [Serializable]
    public class GameErrorDto
    {
        public string code;             // GameErrorCodes
        public string message;          // 404·409는 사용자에게 그대로 보여줘도 되는 한국어 메시지. 400은 스프링 원문일 수 있다
    }
}
