using System;
using NUnit.Framework;

namespace WhoisntCitizen.Network.Realtime.Tests
{
    public class RealtimeProtocolTests
    {
        [TestCase("http://localhost:8080", "ws://localhost:8080/ws/v1")]
        [TestCase("http://localhost:8080/", "ws://localhost:8080/ws/v1")]
        [TestCase("https://api.example.com", "wss://api.example.com/ws/v1")]
        [TestCase("HTTP://Host:9000", "ws://Host:9000/ws/v1")]
        [TestCase("  http://10.0.0.5:8080  ", "ws://10.0.0.5:8080/ws/v1")]
        [TestCase("ws://localhost:8080", "ws://localhost:8080/ws/v1")]
        public void REST_주소를_WebSocket_주소로_바꾼다(string baseUrl, string expected)
        {
            Assert.AreEqual(expected, RealtimeProtocol.BuildUrl(baseUrl));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("localhost:8080")]
        [TestCase("ftp://host")]
        public void 잘못된_주소는_예외를_던진다(string baseUrl)
        {
            Assert.Throws<ArgumentException>(() => RealtimeProtocol.BuildUrl(baseUrl));
        }

        [Test]
        public void 서버와_같은_약속값을_쓴다()
        {
            Assert.AreEqual("/ws/v1", RealtimeProtocol.Path);
            Assert.AreEqual(10, RealtimeProtocol.HeartbeatIntervalSeconds);
            Assert.AreEqual(30, RealtimeProtocol.IdleTimeoutSeconds);
            Assert.AreEqual(4408, RealtimeProtocol.CloseIdleTimeout);
            Assert.AreEqual(4401, RealtimeProtocol.CloseTokenExpired);
            // ping 주기가 끊김 기준보다 충분히 짧아야 한 번 놓쳐도 연결이 유지된다.
            Assert.Less(RealtimeProtocol.HeartbeatIntervalSeconds * 2, RealtimeProtocol.IdleTimeoutSeconds);
        }

        // ------------------------------------------------------------ ?token= (WebGL)

        [TestCase("ws://host:8080/ws/v1", "abc.def.ghi", "ws://host:8080/ws/v1?token=abc.def.ghi")]
        [TestCase("wss://host/ws/v1?lang=ko", "abc", "wss://host/ws/v1?lang=ko&token=abc")]
        [TestCase("ws://host/ws/v1?", "abc", "ws://host/ws/v1?token=abc")]
        [TestCase("ws://host/ws/v1?a=1&", "abc", "ws://host/ws/v1?a=1&token=abc")]
        [TestCase("ws://host/ws/v1#x", "abc", "ws://host/ws/v1?token=abc#x")]
        [TestCase("ws://host/ws/v1", "  abc  ", "ws://host/ws/v1?token=abc")]
        public void 주소_뒤에_token_쿼리를_붙인다(string url, string token, string expected)
        {
            Assert.AreEqual(expected, RealtimeProtocol.WithTokenQuery(url, token));
        }

        [Test]
        public void JWT는_인코딩해도_글자가_바뀌지_않는다()
        {
            // 서버는 쿼리 값을 디코딩하지 않고 읽으므로 JWT가 그대로 가야 한다.
            const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiI3IiwiZXhwIjoxNzkyMDAwMDAwfQ.Ab-_cd0123456789xyzXYZ";
            Assert.AreEqual("ws://h/ws/v1?token=" + jwt, RealtimeProtocol.WithTokenQuery("ws://h/ws/v1", jwt));
        }

        [Test]
        public void 주소에_쓸_수_없는_글자는_퍼센트_인코딩한다()
        {
            Assert.AreEqual("ws://h/ws/v1?token=a%20b%2Bc%2F%3D%26",
                RealtimeProtocol.WithTokenQuery("ws://h/ws/v1", "a b+c/=&"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void 토큰이_비어_있으면_주소를_그대로_돌려준다(string token)
        {
            Assert.AreEqual("ws://h/ws/v1", RealtimeProtocol.WithTokenQuery("ws://h/ws/v1", token));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void 주소가_비어_있으면_예외를_던진다(string url)
        {
            Assert.Throws<ArgumentException>(() => RealtimeProtocol.WithTokenQuery(url, "abc"));
        }

        [Test]
        public void BuildUrl과_이어서_쓰면_서버가_받는_주소가_된다()
        {
            string url = RealtimeProtocol.WithTokenQuery(RealtimeProtocol.BuildUrl("http://3.38.222.51:8080"), "abc.def.ghi");
            Assert.AreEqual("ws://3.38.222.51:8080/ws/v1?token=abc.def.ghi", url);
        }

        // ------------------------------------------------------------ 로그용 토큰 가리기

        [TestCase("ws://h/ws/v1?token=eyJ.abc.def", "ws://h/ws/v1?token=***")]
        [TestCase("ws://h/ws/v1?token=abc&lang=ko", "ws://h/ws/v1?token=***&lang=ko")]
        [TestCase("ws://h/ws/v1?lang=ko&token=abc#x", "ws://h/ws/v1?lang=ko&token=***#x")]
        [TestCase("ws://h/ws/v1?mytoken=abc", "ws://h/ws/v1?mytoken=abc")]
        [TestCase("ws://h/ws/v1", "ws://h/ws/v1")]
        [TestCase("", "")]
        [TestCase(null, null)]
        public void 로그에_남길_주소의_토큰을_가린다(string url, string expected)
        {
            Assert.AreEqual(expected, RealtimeProtocol.RedactToken(url));
        }

        [Test]
        public void 토큰을_붙인_주소를_가리면_토큰이_남지_않는다()
        {
            string url = RealtimeProtocol.WithTokenQuery("ws://h/ws/v1", "secret.jwt.value");
            string redacted = RealtimeProtocol.RedactToken(url);
            StringAssert.DoesNotContain("secret", redacted);
            Assert.AreEqual("ws://h/ws/v1?token=***", redacted);
        }

        [Test]
        public void 서버와_같은_쿼리_이름을_쓴다()
        {
            Assert.AreEqual("token", RealtimeProtocol.TokenQueryParam);
        }
    }
}
