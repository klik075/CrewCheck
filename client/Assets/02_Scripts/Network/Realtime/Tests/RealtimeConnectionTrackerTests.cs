using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace WhoisntCitizen.Network.Realtime.Tests
{
    public class RealtimeConnectionTrackerTests
    {
        private RealtimeConnectionTracker tracker;
        private List<string> changes;

        [SetUp]
        public void SetUp()
        {
            tracker = new RealtimeConnectionTracker();
            changes = new List<string>();
            tracker.StateChanged += (from, to) => changes.Add(from + ">" + to);
        }

        private void Connect(long userId = 7)
        {
            tracker.OnConnecting();
            tracker.OnConnected(userId);
        }

        // ------------------------------------------------------------ 연결

        [Test]
        public void 처음에는_끊김이고_방도_없다()
        {
            Assert.AreEqual(RealtimeState.Disconnected, tracker.State);
            Assert.AreEqual(0L, tracker.DesiredRoomId);
            Assert.AreEqual(0L, tracker.SubscribedRoomId);
            Assert.AreEqual(0L, tracker.UserId);
        }

        [Test]
        public void 연결_중을_거쳐_connected를_받으면_연결됨이다()
        {
            Assert.IsTrue(tracker.OnConnecting());
            Assert.AreEqual(RealtimeState.Connecting, tracker.State);

            Assert.AreEqual(RealtimeAction.None, tracker.OnConnected(7));
            Assert.AreEqual(RealtimeState.Connected, tracker.State);
            Assert.AreEqual(7L, tracker.UserId);
            CollectionAssert.AreEqual(new[] { "Disconnected>Connecting", "Connecting>Connected" }, changes);
        }

        [Test]
        public void 연결_중이_아닐_때_온_connected는_무시한다()
        {
            Assert.AreEqual(RealtimeAction.None, tracker.OnConnected(7)); // 끊김 상태
            Assert.AreEqual(RealtimeState.Disconnected, tracker.State);

            Connect();
            changes.Clear();
            Assert.AreEqual(RealtimeAction.None, tracker.OnConnected(8)); // 중복
            Assert.AreEqual(7L, tracker.UserId);
            CollectionAssert.IsEmpty(changes);
        }

        [Test]
        public void 이미_연결_중이거나_연결됐으면_OnConnecting은_무시한다()
        {
            tracker.OnConnecting();
            Assert.IsFalse(tracker.OnConnecting());
            tracker.OnConnected(7);
            Assert.IsFalse(tracker.OnConnecting());
            Assert.AreEqual(RealtimeState.Connected, tracker.State);
        }

        // ------------------------------------------------------------ 방 구독

        [Test]
        public void 연결된_뒤_방에_들어가면_구독을_보내고_subscribed로_구독됨이_된다()
        {
            Connect();
            Assert.AreEqual(RealtimeAction.Subscribe(3), tracker.EnterRoom(3));
            Assert.AreEqual(RealtimeState.Connected, tracker.State); // 답이 오기 전까지는 아직
            Assert.IsFalse(tracker.ReceivesSignalsFor(3));

            Assert.IsTrue(tracker.OnSubscribed(3)); // true → 한 번 GET
            Assert.AreEqual(RealtimeState.Subscribed, tracker.State);
            Assert.AreEqual(3L, tracker.SubscribedRoomId);
            Assert.IsTrue(tracker.ReceivesSignalsFor(3));
        }

        [Test]
        public void 끊긴_상태에서_방에_들어가면_기억했다가_connected때_구독한다()
        {
            Assert.AreEqual(RealtimeAction.None, tracker.EnterRoom(3));
            Assert.AreEqual(3L, tracker.DesiredRoomId);

            tracker.OnConnecting();
            Assert.AreEqual(RealtimeAction.Subscribe(3), tracker.OnConnected(7));
        }

        [Test]
        public void 같은_방_구독_중에_다시_들어오면_아무것도_보내지_않는다()
        {
            Connect();
            tracker.EnterRoom(3);
            tracker.OnSubscribed(3);
            changes.Clear();

            Assert.AreEqual(RealtimeAction.None, tracker.EnterRoom(3)); // Room → GameScene
            Assert.AreEqual(RealtimeState.Subscribed, tracker.State);
            CollectionAssert.IsEmpty(changes);
        }

        [Test]
        public void 같은_subscribed가_두_번_오면_두_번째는_GET하지_않는다()
        {
            Connect();
            tracker.EnterRoom(3);
            Assert.IsTrue(tracker.OnSubscribed(3));
            Assert.IsFalse(tracker.OnSubscribed(3));
        }

        [Test]
        public void 다른_방으로_옮기면_새_subscribed가_올_때까지_신호를_믿지_않는다()
        {
            Connect();
            tracker.EnterRoom(3);
            tracker.OnSubscribed(3);

            Assert.AreEqual(RealtimeAction.Subscribe(5), tracker.EnterRoom(5));
            Assert.AreEqual(RealtimeState.Connected, tracker.State);
            Assert.IsFalse(tracker.ReceivesSignalsFor(3));
            Assert.IsFalse(tracker.ReceivesSignalsFor(5));

            Assert.IsFalse(tracker.OnSubscribed(3)); // 이전 방에 대한 늦은 답
            Assert.AreEqual(RealtimeState.Connected, tracker.State);

            Assert.IsTrue(tracker.OnSubscribed(5));
            Assert.IsTrue(tracker.ReceivesSignalsFor(5));
        }

        [Test]
        public void 연결_전이나_보고_있지_않은_방의_subscribed는_무시한다()
        {
            tracker.EnterRoom(3);
            Assert.IsFalse(tracker.OnSubscribed(3)); // 끊김 상태
            tracker.OnConnecting();
            Assert.IsFalse(tracker.OnSubscribed(3)); // connected 전

            tracker.OnConnected(7);
            Assert.IsFalse(tracker.OnSubscribed(9));
            Assert.IsFalse(tracker.OnSubscribed(0));
            Assert.AreEqual(RealtimeState.Connected, tracker.State);
        }

        [Test]
        public void 방에서_나가면_구독을_풀고_연결은_유지한다()
        {
            Connect();
            tracker.EnterRoom(3);
            tracker.OnSubscribed(3);

            Assert.AreEqual(RealtimeAction.Unsubscribe(3), tracker.LeaveRoom());
            Assert.AreEqual(RealtimeState.Connected, tracker.State);
            Assert.AreEqual(0L, tracker.DesiredRoomId);
            Assert.AreEqual(0L, tracker.SubscribedRoomId);
        }

        [Test]
        public void 구독_답을_기다리는_중에_나가도_구독을_푼다()
        {
            Connect();
            tracker.EnterRoom(3);
            Assert.AreEqual(RealtimeAction.Unsubscribe(3), tracker.LeaveRoom());
            Assert.IsFalse(tracker.OnSubscribed(3)); // 나간 뒤 도착한 답은 무시
        }

        [Test]
        public void 방이_없거나_끊긴_상태에서_나가면_보낼_것이_없다()
        {
            Connect();
            Assert.AreEqual(RealtimeAction.None, tracker.LeaveRoom());

            tracker.OnDisconnected();
            tracker.EnterRoom(3);
            Assert.AreEqual(RealtimeAction.None, tracker.LeaveRoom());
            Assert.AreEqual(0L, tracker.DesiredRoomId); // 다시 연결돼도 구독하지 않는다
            tracker.OnConnecting();
            Assert.AreEqual(RealtimeAction.None, tracker.OnConnected(7));
        }

        [Test]
        public void 구독이_거절되면_연결됨으로_돌아가고_보고_있는_방은_유지한다()
        {
            Connect();
            tracker.EnterRoom(3);
            tracker.OnSubscribeRejected();
            Assert.AreEqual(RealtimeState.Connected, tracker.State);
            Assert.AreEqual(3L, tracker.DesiredRoomId);
            Assert.IsFalse(tracker.ReceivesSignalsFor(3));
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        public void 잘못된_방_번호로는_들어갈_수_없다(long roomId)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.EnterRoom(roomId));
        }

        // ------------------------------------------------------------ 끊김과 재연결

        [Test]
        public void 끊기면_구독도_풀리지만_보고_있는_방은_기억한다()
        {
            Connect();
            tracker.EnterRoom(3);
            tracker.OnSubscribed(3);

            tracker.OnDisconnected();
            Assert.AreEqual(RealtimeState.Disconnected, tracker.State);
            Assert.AreEqual(0L, tracker.SubscribedRoomId);
            Assert.AreEqual(0L, tracker.UserId);
            Assert.AreEqual(3L, tracker.DesiredRoomId);
            Assert.IsFalse(tracker.ReceivesSignalsFor(3));
        }

        [Test]
        public void 재연결하면_자동으로_재구독하고_subscribed때_한_번_GET한다()
        {
            Connect();
            tracker.EnterRoom(3);
            tracker.OnSubscribed(3);
            tracker.OnDisconnected();

            tracker.OnConnecting();
            Assert.AreEqual(RealtimeAction.Subscribe(3), tracker.OnConnected(7));
            Assert.IsTrue(tracker.OnSubscribed(3));
            Assert.AreEqual(RealtimeState.Subscribed, tracker.State);
        }

        [Test]
        public void 연결_중에_실패해도_끊김으로_돌아간다()
        {
            tracker.OnConnecting();
            tracker.OnDisconnected();
            Assert.AreEqual(RealtimeState.Disconnected, tracker.State);
            CollectionAssert.AreEqual(new[] { "Disconnected>Connecting", "Connecting>Disconnected" }, changes);
        }

        [Test]
        public void 이미_끊긴_상태에서_또_끊겨도_이벤트는_나지_않는다()
        {
            tracker.OnDisconnected();
            CollectionAssert.IsEmpty(changes);
        }

        // ------------------------------------------------------------ 구독 요청 중복 방지

        [Test]
        public void 구독_답을_기다리는_중에_같은_방으로_또_들어와도_다시_보내지_않는다()
        {
            Connect();
            Assert.AreEqual(RealtimeAction.Subscribe(3), tracker.EnterRoom(3)); // 방 화면
            Assert.AreEqual(3L, tracker.PendingSubscribeRoomId);
            Assert.AreEqual(RealtimeAction.None, tracker.EnterRoom(3));         // 채팅
            Assert.IsTrue(tracker.OnSubscribed(3));
            Assert.AreEqual(0L, tracker.PendingSubscribeRoomId);
        }

        [Test]
        public void 끊긴_동안_여러_화면이_들어와도_connected때_한_번만_보낸다()
        {
            tracker.EnterRoom(3);
            tracker.EnterRoom(3);
            tracker.OnConnecting();
            Assert.AreEqual(RealtimeAction.Subscribe(3), tracker.OnConnected(7));
            Assert.AreEqual(RealtimeAction.None, tracker.EnterRoom(3));
        }

        [Test]
        public void 답을_기다리는_중에_다른_방으로_옮기면_새_방은_보낸다()
        {
            Connect();
            tracker.EnterRoom(3);
            Assert.AreEqual(RealtimeAction.Subscribe(5), tracker.EnterRoom(5));
            Assert.AreEqual(5L, tracker.PendingSubscribeRoomId);
        }

        [Test]
        public void 거절되면_같은_방으로_다시_들어올_때_다시_보낸다()
        {
            Connect();
            tracker.EnterRoom(3);
            tracker.OnSubscribeRejected();
            Assert.AreEqual(0L, tracker.PendingSubscribeRoomId);
            Assert.AreEqual(RealtimeAction.Subscribe(3), tracker.EnterRoom(3));
        }

        [Test]
        public void 끊기면_답_대기가_풀리고_재연결때_다시_보낸다()
        {
            Connect();
            tracker.EnterRoom(3);
            tracker.OnDisconnected();
            Assert.AreEqual(0L, tracker.PendingSubscribeRoomId);
            tracker.OnConnecting();
            Assert.AreEqual(RealtimeAction.Subscribe(3), tracker.OnConnected(7));
        }

        [Test]
        public void 나가면_답_대기도_풀린다()
        {
            Connect();
            tracker.EnterRoom(3);
            tracker.LeaveRoom();
            Assert.AreEqual(0L, tracker.PendingSubscribeRoomId);
            Assert.AreEqual(RealtimeAction.Subscribe(3), tracker.EnterRoom(3));
        }

        // ------------------------------------------------------------ 신호 수신 여부

        [Test]
        public void 신호는_구독된_그_방에_대해서만_받는다()
        {
            Assert.IsFalse(tracker.ReceivesSignalsFor(3)); // 끊김
            Connect();
            Assert.IsFalse(tracker.ReceivesSignalsFor(3)); // 연결됐지만 구독 전
            tracker.EnterRoom(3);
            tracker.OnSubscribed(3);
            Assert.IsTrue(tracker.ReceivesSignalsFor(3));
            Assert.IsFalse(tracker.ReceivesSignalsFor(4));
            Assert.IsFalse(tracker.ReceivesSignalsFor(0));
        }
    }
}
