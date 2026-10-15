using NUnit.Framework;

namespace WhoisntCitizen.Network.Realtime.Tests
{
    public class RealtimeFailurePolicyTests
    {
        // ------------------------------------------------------------ 종료 코드

        [Test]
        public void 종료코드_4401이면_재연결을_멈춘다()
        {
            Assert.AreEqual(CloseAction.StopAuthExpired, RealtimeFailurePolicy.ForClose(4401, false));
        }

        [Test]
        public void 종료코드_4409이면_재연결하지_않고_중복_로그인으로_처리한다()
        {
            Assert.AreEqual(CloseAction.StopDuplicateLogin, RealtimeFailurePolicy.ForClose(4409, false));
        }

        [Test]
        public void 핸드셰이크에서_401을_받으면_재연결을_멈춘다()
        {
            Assert.AreEqual(CloseAction.StopAuthExpired, RealtimeFailurePolicy.ForClose(null, true));
        }

        [TestCase(4408)] // 응답 없음
        [TestCase(1000)] // 정상 종료(서버 종료)
        [TestCase(1001)] // 서버 재시작
        [TestCase(1006)] // 비정상 끊김
        [TestCase(1011)] // 서버 내부 오류
        [TestCase(4000)] // 모르는 코드
        public void 그_밖의_종료코드는_재연결한다(int code)
        {
            Assert.AreEqual(CloseAction.Reconnect, RealtimeFailurePolicy.ForClose(code, false));
        }

        [Test]
        public void 코드_없는_네트워크_오류는_재연결한다()
        {
            Assert.AreEqual(CloseAction.Reconnect, RealtimeFailurePolicy.ForClose(null, false));
        }

        // ------------------------------------------------------------ 오류 코드

        [Test]
        public void NOT_IN_ROOM은_구독_거절이다()
        {
            Assert.AreEqual(ErrorAction.SubscribeRejected, RealtimeFailurePolicy.ForError("NOT_IN_ROOM"));
        }

        [Test]
        public void SERVER_ERROR는_잠시_뒤_구독을_다시_해_본다()
        {
            Assert.AreEqual(ErrorAction.RetrySubscribeLater, RealtimeFailurePolicy.ForError("SERVER_ERROR"));
            Assert.Greater(RealtimeFailurePolicy.RetrySubscribeDelaySeconds, 0);
        }

        [TestCase("BAD_MESSAGE")]
        [TestCase("UNKNOWN_TYPE")]
        [TestCase("SOMETHING_NEW")]
        [TestCase("")]
        [TestCase(null)]
        public void 그_밖의_오류는_기록만_한다(string code)
        {
            Assert.AreEqual(ErrorAction.Ignore, RealtimeFailurePolicy.ForError(code));
        }

        // ------------------------------------------------------------ 401 판별

        [TestCase("The server returned status code '401' when status code '101' was expected.")]
        [TestCase("The remote server returned an error: (401) Unauthorized.")]
        [TestCase("HTTP/1.1 401 Unauthorized")]
        public void 핸드셰이크_401_메시지를_알아본다(string message)
        {
            Assert.IsTrue(RealtimeFailurePolicy.IsUnauthorizedHandshake(message));
        }

        [TestCase("The server returned status code '403' when status code '101' was expected.")]
        [TestCase("Unable to connect to the remote server")]
        [TestCase("No connection could be made because the target machine actively refused it. 127.0.0.1:8080")]
        [TestCase("port 4010")]
        [TestCase("")]
        [TestCase(null)]
        public void 다른_연결_실패는_401로_보지_않는다(string message)
        {
            Assert.IsFalse(RealtimeFailurePolicy.IsUnauthorizedHandshake(message));
        }

        // ------------------------------------------------------------ 토큰 남은 시간

        [Test]
        public void 남은_시간이_0_이하면_만료다()
        {
            Assert.IsTrue(RealtimeFailurePolicy.IsTokenExpired(0));
            Assert.IsTrue(RealtimeFailurePolicy.IsTokenExpired(-3.5));
        }

        [Test]
        public void 남은_시간이_있거나_모르면_만료가_아니다()
        {
            Assert.IsFalse(RealtimeFailurePolicy.IsTokenExpired(0.1));
            Assert.IsFalse(RealtimeFailurePolicy.IsTokenExpired(null));
        }
    }
}
