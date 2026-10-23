using System;

namespace WhoisntCitizen.Game
{
    /// <summary>게임 API 호출 결과 (성공/실패 공통). Core가 Network의 ApiResult를 참조하지 않도록 따로 둔다.</summary>
    public class GameApiResult
    {
        /// <summary>HTTP 2xx를 받고 본문까지 읽었으면 true</summary>
        public bool Success { get; protected set; }

        /// <summary>HTTP 상태 코드. 서버에 연결하지 못했으면 0.</summary>
        public long StatusCode { get; protected set; }

        /// <summary>서버 오류 code (GameErrorCodes). 성공이거나 코드가 없으면 null.</summary>
        public string ErrorCode { get; protected set; }

        /// <summary>실패 시 화면에 보여줄 메시지. 400 원문은 ApiClient가 이미 걸러 준다.</summary>
        public string Message { get; protected set; }

        /// <summary>서버에 연결하지 못함 (서버 꺼짐, 네트워크 끊김, 타임아웃)</summary>
        public bool IsConnectionError
        {
            get { return !Success && StatusCode == 0; }
        }

        /// <summary>409 GAME_RULE_VIOLATION (페이즈가 다르거나 규칙 위반). Message를 그대로 보여주면 된다.</summary>
        public bool IsRuleViolation
        {
            get { return ErrorCode == GameErrorCodes.GameRuleViolation; }
        }

        /// <summary>404. 끝난 게임은 결과 보관 시간이 지나면 서버에서 지워져 404가 된다.</summary>
        public bool IsNotFound
        {
            get { return StatusCode == 404; }
        }
    }

    /// <summary>응답 본문을 T로 읽은 결과까지 담는다. Success가 true면 Data는 null이 아니다.</summary>
    public sealed class GameApiResult<T> : GameApiResult where T : class
    {
        public T Data { get; private set; }

        public static GameApiResult<T> Ok(T data)
        {
            return Ok(data, 200);
        }

        public static GameApiResult<T> Ok(T data, long statusCode)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }
            return new GameApiResult<T> { Success = true, StatusCode = statusCode, Data = data };
        }

        public static GameApiResult<T> Fail(long statusCode, string errorCode, string message)
        {
            return new GameApiResult<T> { Success = false, StatusCode = statusCode, ErrorCode = errorCode, Message = message };
        }
    }
}
