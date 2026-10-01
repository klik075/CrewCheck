using System;

namespace WhoisntCitizen.Network
{
    /// <summary>
    /// API 호출 결과 (성공/실패 공통).
    /// UI 코드는 success로 성공 여부만 확인하고, 실패하면 message를 그대로 화면에 보여주면 된다.
    /// </summary>
    public class ApiResult
    {
        /// <summary>HTTP 2xx 응답을 받았으면 true</summary>
        public bool success;

        /// <summary>HTTP 상태 코드. 서버에 연결하지 못했으면 0.</summary>
        public long statusCode;

        /// <summary>서버 에러 코드 (예: "CONFLICT", "BAD_REQUEST"). 성공이거나 코드가 없으면 null.</summary>
        public string errorCode;

        /// <summary>사람이 읽을 수 있는 메시지. 실패 시에는 서버가 보낸 에러 메시지를 우선 사용한다.</summary>
        public string message;

        /// <summary>서버가 보낸 응답 본문 원문 (디버깅용)</summary>
        public string body;

        public bool IsUnauthorized => statusCode == 401; // 토큰 없음/만료
        public bool IsNotFound => statusCode == 404;
        public bool IsConflict => statusCode == 409;     // 규칙/상태 충돌 (정원 초과, 게임 중 등)
        public bool IsConnectionError => statusCode == 0; // 서버 꺼짐, 네트워크 끊김, 타임아웃

        /// <summary>결과 공통 필드를 다른 결과 객체로 복사한다. (ApiResult → ApiResult&lt;T&gt; 변환용)</summary>
        protected void CopyFrom(ApiResult other)
        {
            success = other.success;
            statusCode = other.statusCode;
            errorCode = other.errorCode;
            message = other.message;
            body = other.body;
        }
    }

    /// <summary>
    /// 응답 본문을 T 타입으로 파싱한 결과까지 담는 API 결과.
    /// success가 true면 data에 파싱된 값이 들어 있다.
    /// </summary>
    public class ApiResult<T> : ApiResult
    {
        public T data;

        /// <summary>
        /// 원본 결과를 받아 본문을 parser로 파싱한다.
        /// 성공 응답인데 본문을 해석하지 못하면 실패로 바꿔서, UI가 null 데이터를 쓰지 않도록 한다.
        /// </summary>
        public static ApiResult<T> From(ApiResult raw, Func<string, T> parser)
        {
            var result = new ApiResult<T>();
            result.CopyFrom(raw);

            if (result.success)
            {
                result.data = parser(raw.body);
                if (result.data == null)
                {
                    result.success = false;
                    result.message = "서버 응답을 해석하지 못했습니다.";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// 서버 에러 응답 형식.
    /// 우리 서버(GlobalExceptionHandler): { "code": "CONFLICT", "message": "방이 가득 찼습니다." }
    /// Spring 기본 에러(예외 처리기를 거치지 않은 경우): { "status": 500, "error": "Internal Server Error", ... }
    /// 두 형식을 모두 받을 수 있도록 필드를 함께 둔다.
    /// </summary>
    [Serializable]
    public class ApiErrorResponse
    {
        public string code;
        public string message;
        public string error;
        public int status;
    }
}
