using System;

namespace WhoisntCitizen.Network.Realtime
{
    /// <summary>연결이 끝났을 때 할 일.</summary>
    public enum CloseAction
    {
        /// <summary>백오프만큼 기다렸다 다시 연결한다. (네트워크 오류, 서버 재시작, 4408 응답 없음 등)</summary>
        Reconnect,

        /// <summary>토큰이 만료됐다. 다시 연결해도 또 거절되므로 멈추고, 다시 로그인하라고 알린다. (4401, 핸드셰이크 401)</summary>
        StopAuthExpired
    }

    /// <summary>서버 error 메시지를 받았을 때 할 일.</summary>
    public enum ErrorAction
    {
        /// <summary>기록만 한다. (BAD_MESSAGE, UNKNOWN_TYPE — 클라이언트 버그. 다시 보내도 같다)</summary>
        Ignore,

        /// <summary>방 구독이 거절됐다(NOT_IN_ROOM). 신호 없이 빠른 폴링으로 돌아가고, 방 화면이 GET으로 실제 상태를 확인한다.</summary>
        SubscribeRejected,

        /// <summary>서버 쪽 일시 오류(SERVER_ERROR). 구독이 확인되지 않았으면 잠시 뒤 다시 구독해 본다.</summary>
        RetrySubscribeLater
    }

    /// <summary>
    /// 연결 종료 코드와 서버 오류 코드를 "다음에 할 일"로 바꾼다. 소켓·시간을 다루지 않는 순수 규칙이라 테스트로 고정한다.
    ///
    ///  종료 코드                          → 할 일
    ///  4401 토큰 만료 / 핸드셰이크 401    → 멈춤 + 다시 로그인 안내
    ///  4408 응답 없음                     → 재연결 (앱이 잠시 멈췄던 경우가 대부분)
    ///  1000·1001(서버 종료/재시작), 1006(비정상 끊김), 코드 없음(네트워크 오류), 그 밖 → 재연결
    ///
    ///  오류 코드                          → 할 일
    ///  NOT_IN_ROOM                        → 구독 거절 처리
    ///  SERVER_ERROR                       → 잠시 뒤 구독 재시도
    ///  BAD_MESSAGE, UNKNOWN_TYPE, 그 밖   → 무시(기록만)
    /// </summary>
    public static class RealtimeFailurePolicy
    {
        /// <summary>SERVER_ERROR 뒤 구독을 다시 시도하기까지 기다릴 초.</summary>
        public const double RetrySubscribeDelaySeconds = 5.0;

        /// <param name="closeCode">서버가 보낸 종료 코드. 네트워크 오류처럼 코드가 없으면 null.</param>
        /// <param name="handshakeUnauthorized">연결 단계에서 HTTP 401을 받았다(토큰이 이미 만료된 채로 연결 시도).</param>
        public static CloseAction ForClose(int? closeCode, bool handshakeUnauthorized)
        {
            if (handshakeUnauthorized || closeCode == RealtimeProtocol.CloseTokenExpired)
            {
                return CloseAction.StopAuthExpired;
            }
            return CloseAction.Reconnect;
        }

        public static ErrorAction ForError(string errorCode)
        {
            switch (errorCode)
            {
                case RealtimeErrorCodes.NotInRoom:
                    return ErrorAction.SubscribeRejected;
                case RealtimeErrorCodes.ServerError:
                    return ErrorAction.RetrySubscribeLater;
                default:
                    return ErrorAction.Ignore;
            }
        }

        /// <summary>
        /// 연결 실패 예외 메시지가 "서버가 101 대신 401을 돌려줬다"는 뜻인지 본다.
        /// .NET/Mono는 핸드셰이크 거절을 상태 코드가 든 WebSocketException 메시지로만 알려 준다.
        ///   예) "The server returned status code '401' when status code '101' was expected."
        /// </summary>
        public static bool IsUnauthorizedHandshake(string exceptionMessage)
        {
            if (string.IsNullOrEmpty(exceptionMessage))
            {
                return false;
            }
            return exceptionMessage.IndexOf("'401'", StringComparison.Ordinal) >= 0
                || exceptionMessage.IndexOf("(401)", StringComparison.Ordinal) >= 0
                || exceptionMessage.IndexOf("401 Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 연결을 시도하기 전에 토큰이 이미 만료됐는지. 남은 시간을 모르면(null) false — 서버에 맡긴다.
        /// </summary>
        public static bool IsTokenExpired(double? tokenMinutesLeft)
        {
            return tokenMinutesLeft.HasValue && tokenMinutesLeft.Value <= 0;
        }
    }
}
