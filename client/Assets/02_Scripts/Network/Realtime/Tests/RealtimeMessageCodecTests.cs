using System;
using NUnit.Framework;

namespace WhoisntCitizen.Network.Realtime.Tests
{
    public class RealtimeMessageCodecTests
    {
        private static T Decode<T>(string json) where T : RealtimeServerMessage
        {
            bool ok = RealtimeMessageCodec.TryDecode(json, out var message, out var error);
            Assert.IsTrue(ok, "해석 실패: " + error);
            Assert.IsNull(error);
            Assert.IsInstanceOf<T>(message);
            return (T)message;
        }

        private static string DecodeFails(string json)
        {
            bool ok = RealtimeMessageCodec.TryDecode(json, out var message, out var error);
            Assert.IsFalse(ok);
            Assert.IsNull(message);
            Assert.IsNotEmpty(error);
            return error;
        }

        // ------------------------------------------------------------ 받기: 서버가 실제로 보내는 모양

        [Test]
        public void connected를_읽는다()
        {
            var m = Decode<ConnectedMessage>("{\"type\":\"connected\",\"userId\":12,\"heartbeatSeconds\":10,\"idleTimeoutSeconds\":30}");
            Assert.AreEqual(12L, m.UserId);
            Assert.AreEqual(10L, m.HeartbeatSeconds);
            Assert.AreEqual(30L, m.IdleTimeoutSeconds);
            Assert.AreEqual("connected", m.Type);
        }

        [Test]
        public void pong을_읽는다_epoch_ms는_long_범위다()
        {
            var m = Decode<PongMessage>("{\"type\":\"pong\",\"serverTime\":1791729000123}");
            Assert.AreEqual(1791729000123L, m.ServerTimeMs);
        }

        [Test]
        public void subscribed를_읽는다()
        {
            Assert.AreEqual(3L, Decode<SubscribedMessage>("{\"type\":\"subscribed\",\"roomId\":3}").RoomId);
        }

        [Test]
        public void game_changed를_읽는다()
        {
            var m = Decode<GameChangedMessage>("{\"type\":\"game.changed\",\"gameId\":\"a1b2-c3\",\"phaseVersion\":7}");
            Assert.AreEqual("a1b2-c3", m.GameId);
            Assert.AreEqual(7L, m.PhaseVersion);
        }

        [Test]
        public void room_changed를_읽는다()
        {
            Assert.AreEqual(5L, Decode<RoomChangedMessage>("{\"type\":\"room.changed\",\"roomId\":5}").RoomId);
        }

        [Test]
        public void room_created를_읽는다()
        {
            Assert.AreEqual(7L, Decode<RoomCreatedMessage>("{\"type\":\"room.created\",\"roomId\":7}").RoomId);
        }

        [Test]
        public void chat_changed를_읽는다()
        {
            var m = Decode<ChatChangedMessage>("{\"type\":\"chat.changed\",\"roomId\":5,\"lastMessageId\":104}");
            Assert.AreEqual(5L, m.RoomId);
            Assert.AreEqual(104L, m.LastMessageId);
        }

        [Test]
        public void error를_읽는다()
        {
            var m = Decode<RealtimeErrorMessage>("{\"type\":\"error\",\"code\":\"NOT_IN_ROOM\",\"message\":\"방에 없습니다\"}");
            Assert.AreEqual(RealtimeErrorCodes.NotInRoom, m.Code);
            Assert.AreEqual("방에 없습니다", m.Message);
        }

        [Test]
        public void 필드_순서와_공백_줄바꿈은_상관없다()
        {
            var m = Decode<ChatChangedMessage>(" \n{ \"lastMessageId\" : 9 ,\n \"roomId\":2, \"type\":\"chat.changed\" }\n");
            Assert.AreEqual(2L, m.RoomId);
            Assert.AreEqual(9L, m.LastMessageId);
        }

        [Test]
        public void 모르는_필드는_무시한다()
        {
            var m = Decode<RoomChangedMessage>("{\"type\":\"room.changed\",\"roomId\":5,\"extra\":{\"a\":[1,2]}}");
            Assert.AreEqual(5L, m.RoomId);
        }

        [Test]
        public void 모르는_type은_실패가_아니라_Unknown으로_돌려준다()
        {
            var m = Decode<UnknownServerMessage>("{\"type\":\"friend.changed\",\"userId\":1}");
            Assert.AreEqual("friend.changed", m.Type);
        }

        // ------------------------------------------------------------ 받기: 실패해야 하는 경우

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void 빈_메시지는_실패한다(string json)
        {
            DecodeFails(json);
        }

        [TestCase("not json")]
        [TestCase("[1,2,3]")]
        [TestCase("123")]
        [TestCase("\"pong\"")]
        [TestCase("null")]
        [TestCase("{\"type\":\"pong\"")]
        public void JSON_객체가_아니면_실패한다(string json)
        {
            DecodeFails(json);
        }

        [TestCase("{}")]
        [TestCase("{\"roomId\":3}")]
        [TestCase("{\"type\":\"\"}")]
        public void type이_없으면_실패한다(string json)
        {
            DecodeFails(json);
        }

        [TestCase("{\"type\":\"connected\"}")]
        [TestCase("{\"type\":\"subscribed\"}")]
        [TestCase("{\"type\":\"subscribed\",\"roomId\":0}")]
        [TestCase("{\"type\":\"room.changed\",\"roomId\":-1}")]
        [TestCase("{\"type\":\"chat.changed\",\"lastMessageId\":3}")]
        [TestCase("{\"type\":\"game.changed\",\"phaseVersion\":3}")]
        [TestCase("{\"type\":\"game.changed\",\"gameId\":\"\",\"phaseVersion\":3}")]
        [TestCase("{\"type\":\"error\",\"message\":\"x\"}")]
        public void 꼭_필요한_값이_없으면_실패한다(string json)
        {
            DecodeFails(json);
        }

        // ------------------------------------------------------------ 보내기

        [Test]
        public void ping을_만든다()
        {
            Assert.AreEqual("{\"type\":\"ping\"}", RealtimeMessageCodec.EncodePing());
        }

        [Test]
        public void subscribe와_unsubscribe를_만든다()
        {
            Assert.AreEqual("{\"type\":\"room.subscribe\",\"roomId\":3}", RealtimeMessageCodec.EncodeSubscribe(3));
            Assert.AreEqual("{\"type\":\"room.unsubscribe\",\"roomId\":3}", RealtimeMessageCodec.EncodeUnsubscribe(3));
            Assert.AreEqual("{\"type\":\"room.subscribe\",\"roomId\":9007199254740993}",
                RealtimeMessageCodec.EncodeSubscribe(9007199254740993L));
        }

        [TestCase(0L)]
        [TestCase(-5L)]
        public void roomId가_양수가_아니면_보내기_전에_막는다(long roomId)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RealtimeMessageCodec.EncodeSubscribe(roomId));
            Assert.Throws<ArgumentOutOfRangeException>(() => RealtimeMessageCodec.EncodeUnsubscribe(roomId));
        }

        [Test]
        public void 보낸_메시지는_서버_최대_길이보다_훨씬_짧다()
        {
            Assert.Less(RealtimeMessageCodec.EncodeSubscribe(long.MaxValue).Length, RealtimeProtocol.MaxClientMessageChars);
        }
    }
}
