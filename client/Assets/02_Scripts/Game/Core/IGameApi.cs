using System;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 게임 API 연결 지점. GameController는 이 인터페이스에만 의존한다.
    /// 구현: RealGameApi(실제 서버, ApiClient 사용), FakeGameApi(서버 없이 한 판을 흉내 냄).
    /// 규칙
    /// - 콜백은 성공·실패 모두 정확히 한 번 불린다.
    /// - 콜백이 언제 불리는지는 구현마다 다르다. RealGameApi는 나중에(비동기), FakeGameApi는 호출 안에서 바로(동기) 부른다.
    /// - MonoBehaviour에서 쓸 때는 콜백 첫 줄에 if (this == null) return; 을 넣는다(응답 전에 씬이 바뀐 경우).
    /// </summary>
    public interface IGameApi
    {
        /// <summary>GET /api/v1/games/{gameId} — 1초마다 폴링한다.</summary>
        void GetState(string gameId, Action<GameApiResult<GameStateDto>> onDone);

        /// <summary>GET /api/v1/games/{gameId}/me — 밤이 시작될 때마다 다시 받는다(남은 횟수·동료가 바뀜).</summary>
        void GetMe(string gameId, Action<GameApiResult<MyRoleDto>> onDone);

        /// <summary>POST /api/v1/games/{gameId}/night-actions {"targetId": 대상} — 판정 전까지 다시 내면 덮어쓴다.</summary>
        void SubmitNightAction(string gameId, long targetId, Action<GameApiResult<NightActionResultDto>> onDone);

        /// <summary>POST /api/v1/games/{gameId}/night-actions/skip — 이번 밤 능력을 쓰지 않는다.</summary>
        void SkipNightAction(string gameId, Action<GameApiResult<NightActionResultDto>> onDone);

        /// <summary>POST /api/v1/games/{gameId}/day/skip — 이번 낮 토론을 넘긴다. 살아 있는 전원이 넘기면 바로 투표로 넘어간다.</summary>
        void SkipDay(string gameId, Action<GameApiResult<DaySkipResultDto>> onDone);

        /// <summary>GET /api/v1/games/{gameId}/night-result — 가장 최근 밤 결과. 페이즈와 상관없이 조회된다.</summary>
        void GetNightResult(string gameId, Action<GameApiResult<NightResultDto>> onDone);

        /// <summary>
        /// POST /api/v1/games/{gameId}/votes {"targetId": 대상, "confirm": 완료 여부} — 다시 내면 덮어쓴다.
        /// targetId가 0이면 표를 거둔다(기권). confirm이면 "투표 완료": 지금 상태로 고정하고, 전원이 완료하면 바로 처형 판정한다.
        /// confirm이 아니면 임시 선택이다(시간이 끝나면 그 표가 집계된다).
        /// </summary>
        void Vote(string gameId, long targetId, bool confirm, Action<GameApiResult<VoteResultDto>> onDone);

        /// <summary>GET /api/v1/games/{gameId}/execution-result — 가장 최근 처형 결과. 페이즈와 상관없이 조회된다.</summary>
        void GetExecutionResult(string gameId, Action<GameApiResult<ExecutionResultDto>> onDone);

        /// <summary>GET /api/v1/games/{gameId}/result — 끝나기 전에는 ended=false, players 빈 목록.</summary>
        void GetResult(string gameId, Action<GameApiResult<GameResultDto>> onDone);
    }
}
