using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace WhoisntCitizen.Network.Realtime.Tests
{
    public class RealtimeTransportInboxTests
    {
        private static List<RealtimeTransportEvent> DrainAll(RealtimeTransportInbox inbox)
        {
            var events = new List<RealtimeTransportEvent>();
            while (inbox.TryDequeue(out RealtimeTransportEvent e))
            {
                events.Add(e);
            }
            return events;
        }

        private static List<string> Kinds(List<RealtimeTransportEvent> events)
        {
            var kinds = new List<string>();
            foreach (RealtimeTransportEvent e in events)
            {
                kinds.Add(e.Kind == RealtimeTransportEventKind.Message ? "Message:" + e.Text : e.Kind.ToString());
            }
            return kinds;
        }

        // ------------------------------------------------------------ 기본

        [Test]
        public void 비어_있으면_false이고_사건은_null이다()
        {
            var inbox = new RealtimeTransportInbox();
            Assert.IsFalse(inbox.TryDequeue(out RealtimeTransportEvent e));
            Assert.IsNull(e);
        }

        [Test]
        public void 넣은_순서대로_꺼낸다()
        {
            var inbox = new RealtimeTransportInbox();
            inbox.PostOpened();
            inbox.PostMessage("a");
            inbox.PostMessage("b");
            inbox.PostClosed(1000, "bye");

            CollectionAssert.AreEqual(
                new[] { "Opened", "Message:a", "Message:b", "Closed" },
                Kinds(DrainAll(inbox)));
        }

        [Test]
        public void Closed의_종료코드_이유_401여부를_그대로_전달한다()
        {
            var inbox = new RealtimeTransportInbox();
            inbox.PostClosed(null, "HTTP 401", unauthorized: true);

            Assert.IsTrue(inbox.TryDequeue(out RealtimeTransportEvent e));
            Assert.AreEqual(RealtimeTransportEventKind.Closed, e.Kind);
            Assert.IsNull(e.CloseCode);
            Assert.AreEqual("HTTP 401", e.Reason);
            Assert.IsTrue(e.Unauthorized);
        }

        [Test]
        public void null은_버린다()
        {
            var inbox = new RealtimeTransportInbox();
            Assert.IsFalse(inbox.Post(null));
            Assert.AreEqual(0, inbox.Count);
        }

        // ------------------------------------------------------------ Closed 규칙

        [Test]
        public void 연결에_실패하면_Opened_없이_Closed만_올_수_있다()
        {
            var inbox = new RealtimeTransportInbox();
            Assert.IsTrue(inbox.PostClosed(null, "connect timeout"));
            CollectionAssert.AreEqual(new[] { "Closed" }, Kinds(DrainAll(inbox)));
        }

        [Test]
        public void Closed는_한_번만_들어간다()
        {
            var inbox = new RealtimeTransportInbox();
            inbox.PostOpened();
            Assert.IsTrue(inbox.PostClosed(4408, "idle"));
            Assert.IsFalse(inbox.PostClosed(1000, "again"));

            List<RealtimeTransportEvent> events = DrainAll(inbox);
            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(4408, events[1].CloseCode); // 첫 번째 Closed가 남는다
        }

        [Test]
        public void Closed_뒤에_들어오는_사건은_모두_버린다()
        {
            var inbox = new RealtimeTransportInbox();
            inbox.PostOpened();
            inbox.PostClosed(1000, "bye");

            Assert.IsFalse(inbox.PostMessage("late"));
            Assert.IsFalse(inbox.PostOpened());
            CollectionAssert.AreEqual(new[] { "Opened", "Closed" }, Kinds(DrainAll(inbox)));
        }

        [Test]
        public void IsClosed는_Closed를_넣는_순간_true다_꺼내기_전이어도()
        {
            var inbox = new RealtimeTransportInbox();
            Assert.IsFalse(inbox.IsClosed);
            inbox.PostClosed(null, "x");
            Assert.IsTrue(inbox.IsClosed);
            Assert.AreEqual(1, inbox.Count);
        }

        [Test]
        public void 꺼낸_뒤에도_Closed_이후_사건은_계속_버린다()
        {
            var inbox = new RealtimeTransportInbox();
            inbox.PostClosed(null, "x");
            DrainAll(inbox);
            Assert.IsFalse(inbox.PostMessage("late"));
            Assert.AreEqual(0, inbox.Count);
        }

        // ------------------------------------------------------------ Opened·Message 규칙

        [Test]
        public void Opened는_한_번만_들어간다()
        {
            var inbox = new RealtimeTransportInbox();
            Assert.IsTrue(inbox.PostOpened());
            Assert.IsFalse(inbox.PostOpened());
            CollectionAssert.AreEqual(new[] { "Opened" }, Kinds(DrainAll(inbox)));
        }

        [Test]
        public void Opened_전의_Message는_버린다()
        {
            var inbox = new RealtimeTransportInbox();
            Assert.IsFalse(inbox.PostMessage("too early"));
            inbox.PostOpened();
            inbox.PostMessage("ok");
            CollectionAssert.AreEqual(new[] { "Opened", "Message:ok" }, Kinds(DrainAll(inbox)));
        }

        [Test]
        public void Message에_null을_넣으면_빈_문자열이_된다()
        {
            var inbox = new RealtimeTransportInbox();
            inbox.PostOpened();
            inbox.PostMessage(null);
            List<RealtimeTransportEvent> events = DrainAll(inbox);
            Assert.AreEqual("", events[1].Text);
        }

        // ------------------------------------------------------------ 스레드

        [Test]
        public void 다른_스레드가_넣는_동안_꺼내도_순서가_유지되고_Closed가_마지막이다()
        {
            const int messageCount = 20000;
            var inbox = new RealtimeTransportInbox();

            var producer = new Thread(() =>
            {
                inbox.PostOpened();
                for (int i = 0; i < messageCount; i++)
                {
                    inbox.PostMessage(i.ToString());
                }
                inbox.PostClosed(1000, "done");
            });
            producer.Start();

            // 메인 스레드는 넣는 중에도 계속 꺼낸다 (RealtimeClient.Update처럼)
            var events = new List<RealtimeTransportEvent>();
            while (true)
            {
                if (inbox.TryDequeue(out RealtimeTransportEvent e))
                {
                    events.Add(e);
                    if (e.Kind == RealtimeTransportEventKind.Closed)
                    {
                        break;
                    }
                }
                else if (!producer.IsAlive && inbox.Count == 0)
                {
                    break;
                }
            }
            producer.Join();

            Assert.AreEqual(messageCount + 2, events.Count);
            Assert.AreEqual(RealtimeTransportEventKind.Opened, events[0].Kind);
            for (int i = 0; i < messageCount; i++)
            {
                Assert.AreEqual(i.ToString(), events[i + 1].Text);
            }
            Assert.AreEqual(RealtimeTransportEventKind.Closed, events[events.Count - 1].Kind);
            Assert.IsFalse(inbox.TryDequeue(out _));
        }

        [Test]
        public void 여러_스레드가_동시에_넣어도_Closed는_하나이고_그_뒤에는_아무것도_없다()
        {
            var inbox = new RealtimeTransportInbox();
            inbox.PostOpened();

            var threads = new List<Thread>();
            for (int t = 0; t < 4; t++)
            {
                int id = t;
                threads.Add(new Thread(() =>
                {
                    for (int i = 0; i < 5000; i++)
                    {
                        inbox.PostMessage(id + ":" + i);
                        if (i == 2500)
                        {
                            inbox.PostClosed(1000, "thread " + id); // 네 스레드가 서로 먼저 닫으려 한다
                        }
                    }
                }));
            }
            threads.ForEach(th => th.Start());
            threads.ForEach(th => th.Join());

            List<RealtimeTransportEvent> events = DrainAll(inbox);
            int closedCount = events.FindAll(e => e.Kind == RealtimeTransportEventKind.Closed).Count;
            Assert.AreEqual(1, closedCount);
            Assert.AreEqual(RealtimeTransportEventKind.Closed, events[events.Count - 1].Kind);
        }
    }
}
