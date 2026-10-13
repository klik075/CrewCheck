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
    }
}
