using System;
using WhoisntCitizen.Network;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 실제 서버를 부르는 IGameApi. ApiClient가 서버 주소(ApiConfig), Bearer 토큰, 401 처리(세션 삭제 후 타이틀로 이동)를 맡는다.
    /// ApiClient가 기본 어셈블리(Assembly-CSharp)에 있어서 이 파일은 Game.Core 밖에 둔다.
    /// 응답 JSON은 ApiClient가 JsonUtility로 읽는다. b41b546 응답은 모두 JsonUtility로 읽히므로 따로 파서를 쓰지 않는다.
    /// </summary>
    public sealed class RealGameApi : IGameApi
    {
        public void GetState(string gameId, Action<GameApiResult<GameStateDto>> onDone)
        {
            ApiClient.Get<GameStateDto>(GameApiPaths.State(gameId), r => Reply(onDone, r));
        }

        public void GetMe(string gameId, Action<GameApiResult<MyRoleDto>> onDone)
        {
            ApiClient.Get<MyRoleDto>(GameApiPaths.Me(gameId), r => Reply(onDone, r));
        }

        public void SubmitNightAction(string gameId, long targetId, Action<GameApiResult<NightActionResultDto>> onDone)
        {
            // body는 DTO 객체로 넘긴다. ApiClient가 JsonUtility.ToJson으로 직렬화한다(문자열을 넘기면 {}가 간다).
            var body = new TargetRequestDto { targetId = targetId };
            ApiClient.Post<NightActionResultDto>(GameApiPaths.NightActions(gameId), body, r => Reply(onDone, r));
        }

        public void SkipNightAction(string gameId, Action<GameApiResult<NightActionResultDto>> onDone)
        {
            ApiClient.Post<NightActionResultDto>(GameApiPaths.SkipNightAction(gameId), null, r => Reply(onDone, r));
        }

        public void SkipDay(string gameId, Action<GameApiResult<DaySkipResultDto>> onDone)
        {
            ApiClient.Post<DaySkipResultDto>(GameApiPaths.SkipDay(gameId), null, r => Reply(onDone, r));
        }

        public void GetNightResult(string gameId, Action<GameApiResult<NightResultDto>> onDone)
        {
            ApiClient.Get<NightResultDto>(GameApiPaths.NightResult(gameId), r => Reply(onDone, r));
        }

        public void Vote(string gameId, long targetId, bool confirm, Action<GameApiResult<VoteResultDto>> onDone)
        {
            var body = new VoteRequestDto { targetId = targetId, confirm = confirm }; // targetId 0 = 기권
            ApiClient.Post<VoteResultDto>(GameApiPaths.Votes(gameId), body, r => Reply(onDone, r));
        }

        public void GetExecutionResult(string gameId, Action<GameApiResult<ExecutionResultDto>> onDone)
        {
            ApiClient.Get<ExecutionResultDto>(GameApiPaths.ExecutionResult(gameId), r => Reply(onDone, r));
        }

        public void GetResult(string gameId, Action<GameApiResult<GameResultDto>> onDone)
        {
            ApiClient.Get<GameResultDto>(GameApiPaths.Result(gameId), r => Reply(onDone, r));
        }

        /// <summary>ApiResult&lt;T&gt; → GameApiResult&lt;T&gt;. ApiResult는 성공인데 본문을 못 읽으면 이미 실패로 바꿔 둔다.</summary>
        private static void Reply<T>(Action<GameApiResult<T>> onDone, ApiResult<T> r) where T : class
        {
            if (onDone == null)
            {
                return;
            }
            onDone(r.success
                ? GameApiResult<T>.Ok(r.data, r.statusCode)
                : GameApiResult<T>.Fail(r.statusCode, r.errorCode, r.message));
        }
    }
}
