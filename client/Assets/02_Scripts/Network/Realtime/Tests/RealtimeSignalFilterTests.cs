using NUnit.Framework;

namespace WhoisntCitizen.Network.Realtime.Tests
{
    public class RealtimeSignalFilterTests
    {
        private RealtimeSignalFilter filter;

        [SetUp]
        public void SetUp()
        {
            filter = new RealtimeSignalFilter();
        }

        private static RoomChangedMessage Room(long roomId) => new RoomChangedMessage(roomId);
        private static GameChangedMessage Game(string gameId, long version) => new GameChangedMessage(gameId, version);
        private static ChatChangedMessage Chat(long roomId, long lastId) => new ChatChangedMessage(roomId, lastId);

        // ------------------------------------------------------------ 거르기

        [Test]
        public void 구독_확인_전에는_어떤_신호도_받지_않는다()
        {
            Assert.IsFalse(filter.Offer(Room(3), 0));
            Assert.IsFalse(filter.Offer(Game("g", 1), 0));
            Assert.IsFalse(filter.Offer(Chat(3, 10), 0));
            Assert.IsTrue(filter.TakePending().IsEmpty);
        }

        [Test]
        public void 구독한_방이_아닌_방의_room과_chat_신호는_버린다()
        {
            Assert.IsFalse(filter.Offer(Room(9), 3));
            Assert.IsFalse(filter.Offer(Chat(9, 10), 3));
            Assert.IsTrue(filter.TakePending().IsEmpty);
        }

        [Test]
        public void changed가_아닌_메시지는_받지_않는다()
        {
            Assert.IsFalse(filter.Offer(new PongMessage(1), 3));
            Assert.IsFalse(filter.Offer(new SubscribedMessage(3), 3));
            Assert.IsFalse(filter.Offer(null, 3));
        }

        [Test]
        public void 이미_알린_채팅_id_이하의_신호는_버린다()
        {
            Assert.IsTrue(filter.Offer(Chat(3, 10), 3));
            filter.TakePending();

            Assert.IsFalse(filter.Offer(Chat(3, 10), 3)); // 중복
            Assert.IsFalse(filter.Offer(Chat(3, 8), 3));  // 순서 뒤바뀜
            Assert.IsTrue(filter.TakePending().IsEmpty);

            Assert.IsTrue(filter.Offer(Chat(3, 11), 3));
            Assert.AreEqual(11L, filter.TakePending().Chat.LastMessageId);
        }

        // ------------------------------------------------------------ 합치기

        [Test]
        public void 같은_프레임의_room_changed_여러_번은_한_번으로_합친다()
        {
            filter.Offer(Room(3), 3);
            filter.Offer(Room(3), 3);
            filter.Offer(Room(3), 3);

            var s = filter.TakePending();
            Assert.IsTrue(s.RoomChanged);
            Assert.AreEqual(3L, s.RoomId);
            Assert.IsNull(s.Game);
            Assert.IsNull(s.Chat);
        }

        [Test]
        public void game_changed는_phaseVersion이_가장_큰_것을_남긴다()
        {
            filter.Offer(Game("g", 7), 3);
            filter.Offer(Game("g", 9), 3);
            filter.Offer(Game("g", 8), 3); // 늦게 도착한 옛 버전

            Assert.AreEqual(9L, filter.TakePending().Game.PhaseVersion);
        }

        [Test]
        public void 같은_phaseVersion의_game_changed도_받는다_투표처럼_버전이_그대로인_변경()
        {
            filter.Offer(Game("g", 7), 3);
            filter.TakePending();

            Assert.IsTrue(filter.Offer(Game("g", 7), 3));
            Assert.IsNotNull(filter.TakePending().Game);
        }

        [Test]
        public void chat_changed는_가장_큰_id_하나만_남긴다()
        {
            filter.Offer(Chat(3, 10), 3);
            filter.Offer(Chat(3, 12), 3);
            filter.Offer(Chat(3, 11), 3); // 버려짐

            Assert.AreEqual(12L, filter.TakePending().Chat.LastMessageId);
        }

        [Test]
        public void 종류가_다른_신호는_한_번에_같이_넘긴다()
        {
            filter.Offer(Room(3), 3);
            filter.Offer(Game("g", 2), 3);
            filter.Offer(Chat(3, 5), 3);

            var s = filter.TakePending();
            Assert.IsTrue(s.RoomChanged);
            Assert.AreEqual("g", s.Game.GameId);
            Assert.AreEqual(5L, s.Chat.LastMessageId);
            Assert.IsFalse(s.IsEmpty);
        }

        [Test]
        public void 꺼내면_비워져서_다음_프레임에는_다시_나오지_않는다()
        {
            filter.Offer(Room(3), 3);
            Assert.IsFalse(filter.TakePending().IsEmpty);
            Assert.IsTrue(filter.TakePending().IsEmpty);
        }

        // ------------------------------------------------------------ 방 이동·재구독·끊김

        [Test]
        public void 방이_바뀌면_이전_방의_모인_신호와_섞이지_않는다()
        {
            filter.Offer(Room(3), 3);
            filter.Offer(Game("g3", 4), 3);
            filter.Offer(Room(5), 5);

            var s = filter.TakePending();
            Assert.AreEqual(5L, s.RoomId);
            Assert.IsTrue(s.RoomChanged);
            Assert.IsNull(s.Game);
        }

        [Test]
        public void 방이_바뀌면_채팅_기준_id도_새로_센다()
        {
            filter.Offer(Chat(3, 100), 3);
            filter.TakePending();

            Assert.IsTrue(filter.Offer(Chat(5, 7), 5));
            Assert.AreEqual(7L, filter.TakePending().Chat.LastMessageId);
        }

        [Test]
        public void 재구독되면_모인_신호를_버리고_채팅_기준도_초기화한다()
        {
            filter.Offer(Chat(3, 10), 3);
            filter.Offer(Room(3), 3);
            filter.OnResubscribed();

            Assert.IsTrue(filter.TakePending().IsEmpty);
            Assert.IsTrue(filter.Offer(Chat(3, 10), 3)); // 다시 받음
        }

        [Test]
        public void Clear하면_아직_넘기지_않은_신호를_버린다()
        {
            filter.Offer(Room(3), 3);
            filter.Clear();
            Assert.IsTrue(filter.TakePending().IsEmpty);
        }
    }
}
