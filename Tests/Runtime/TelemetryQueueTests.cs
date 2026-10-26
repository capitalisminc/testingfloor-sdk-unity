using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using TestingFloorSdk.Internal;

namespace TestingFloorSdk.Tests {
    public class TelemetryQueueTests {
        TestingFloorSettings _settings;

        [SetUp]
        public void SetUp() {
            TelemetryQueue.ResetStatics();
            _settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            _settings.batchMaxFlushIntervalSeconds = 0;
            _settings.batchMaxEvents = 1;
        }

        [TearDown]
        public void TearDown() {
            TelemetryQueue.ResetStatics();
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        sealed class Sender : IBatchSender {
            public readonly List<Guid> Ids = new();
            public readonly List<string> Names = new();
            public Func<int, Task<BatchSendOutcome>> Respond;
            public async ValueTask<BatchSendOutcome> TrackBatchAsync(IReadOnlyList<TelemetryEvent> events) {
                Ids.Add(events[0].Id);
                Names.Add(events[0].EventType);
                return await Respond(Ids.Count);
            }
        }

        static TelemetryEvent Event(string name) => new(name, null, ContextSnapshot.Create(default), null, null, 0, "example-play");
        static async Task Until(Func<bool> condition) {
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.IsTrue(condition(), "Sender did not reach the expected state");
        }

        [Test]
        public async Task TransientFailureRetriesInOrderWithTheSameId() {
            var sender = new Sender { Respond = call => Task.FromResult(call == 1
                ? new BatchSendOutcome(SendResult.TransientFailure, 0)
                : new BatchSendOutcome(SendResult.Success, 1)) };
            TelemetryQueue.Enqueue(Event("first"), _settings, () => sender, null);
            TelemetryQueue.Enqueue(Event("second"), _settings, () => sender, null);
            await Task.Delay(50);
            Assert.AreEqual(1, sender.Ids.Count, "Backoff must prevent a tight retry loop");
            Assert.AreEqual(2, TelemetryQueue.PendingEventCount);
            await Until(() => !TelemetryQueue.HasPending());
            CollectionAssert.AreEqual(new[] { "first", "first", "second" }, sender.Names);
            Assert.AreEqual(sender.Ids[0], sender.Ids[1]);
            Assert.AreEqual(0, TelemetryQueue.DroppedEventCount);
        }

        [Test]
        public async Task QueueCapacityIncludesInflightAndDropsTheNewestEvent() {
            _settings.queueMaxEvents = 2;
            var first = new TaskCompletionSource<BatchSendOutcome>();
            var sender = new Sender { Respond = call => call == 1 ? first.Task : Task.FromResult(new BatchSendOutcome(SendResult.Success, 1)) };
            Assert.IsTrue(TelemetryQueue.Enqueue(Event("first"), _settings, () => sender, null));
            Assert.IsTrue(TelemetryQueue.HasPending(), "In-flight requests are pending work");
            Assert.IsTrue(TelemetryQueue.Enqueue(Event("second"), _settings, () => sender, null));
            Assert.IsFalse(TelemetryQueue.Enqueue(Event("excess"), _settings, () => sender, null));
            Assert.AreEqual(2, TelemetryQueue.PendingEventCount);
            first.SetResult(new BatchSendOutcome(SendResult.Success, 1));
            await Until(() => !TelemetryQueue.HasPending());
            CollectionAssert.AreEqual(new[] { "first", "second" }, sender.Names);
            Assert.AreEqual(1, TelemetryQueue.DroppedEventCount);
        }

        [Test]
        public async Task RejectedKeyDropsWaitingEventsWithoutAnotherRequest() {
            var response = new TaskCompletionSource<BatchSendOutcome>();
            var sender = new Sender { Respond = _ => response.Task };
            var state = SendResult.Success;
            TelemetryQueue.Enqueue(Event("first"), _settings, () => sender, result => state = result);
            TelemetryQueue.Enqueue(Event("second"), _settings, () => sender, result => state = result);
            response.SetResult(new BatchSendOutcome(SendResult.Rejected, 1, 1));
            await Until(() => !TelemetryQueue.HasPending());
            Assert.AreEqual(SendResult.Rejected, state);
            Assert.AreEqual(1, sender.Ids.Count);
            Assert.AreEqual(2, TelemetryQueue.DroppedEventCount);
        }

        [Test]
        public async Task ResetPreventsAnOldCompletionFromChangingTheNextPlayQueue() {
            var oldResponse = new TaskCompletionSource<BatchSendOutcome>();
            var oldSender = new Sender { Respond = _ => oldResponse.Task };
            TelemetryQueue.Enqueue(Event("old-play"), _settings, () => oldSender, null);
            TelemetryQueue.ResetStatics();
            var currentResponse = new TaskCompletionSource<BatchSendOutcome>();
            var currentSender = new Sender { Respond = _ => currentResponse.Task };
            TelemetryQueue.Enqueue(Event("current-play"), _settings, () => currentSender, null);
            oldResponse.SetResult(new BatchSendOutcome(SendResult.Rejected, 1, 1));
            await Task.Delay(20);
            Assert.AreEqual(1, TelemetryQueue.PendingEventCount);
            Assert.AreEqual(0, TelemetryQueue.DroppedEventCount);
            currentResponse.SetResult(new BatchSendOutcome(SendResult.Success, 1));
            await Until(() => !TelemetryQueue.HasPending());
        }

        [Test]
        public async Task CompletionWaitsForPriorAcknowledgmentAndOwnsItsRequest() {
            _settings.batchMaxEvents = 50;
            var first = new TaskCompletionSource<BatchSendOutcome>();
            var batches = new List<List<string>>();
            var sender = new BarrierSender {
                Respond = events => {
                    batches.Add(new List<string>());
                    foreach (var ev in events) batches[batches.Count - 1].Add(ev.EventType);
                    return batches.Count == 1 ? first.Task : Task.FromResult(new BatchSendOutcome(SendResult.Success, events.Count));
                }
            };
            TelemetryQueue.Enqueue(Event("gameplay"), _settings, () => sender, null);
            await Until(() => batches.Count == 1);
            var marker = new TelemetryEvent("$tf_telemetry_complete", new Dictionary<string, object>(), ContextSnapshot.Create(default), null, null, 0, "example-play");
            TelemetryQueue.Enqueue(marker, _settings, () => sender, null);
            TelemetryQueue.Enqueue(Event("later"), _settings, () => sender, null);
            Assert.AreEqual(1, batches.Count, "No marker request while gameplay is unacknowledged");
            first.SetResult(new BatchSendOutcome(SendResult.Success, 1));
            await Until(() => !TelemetryQueue.HasPending());
            CollectionAssert.AreEqual(new[] { "$tf_telemetry_complete" }, batches[1]);
            CollectionAssert.AreEqual(new[] { "later" }, batches[2]);
        }

        [Test]
        public async Task BlockingShutdownTakesOverTheInflightRequestWithoutResendingIt() {
            var pending = new TaskCompletionSource<BatchSendOutcome>();
            var sender = new BlockingSender { Pending = pending };
            TelemetryQueue.Enqueue(Event("gameplay"), _settings, () => sender, null);
            var marker = new TelemetryEvent("$tf_telemetry_complete", new Dictionary<string, object>(), ContextSnapshot.Create(default), null, null, 0, "example-play");
            TelemetryQueue.Enqueue(marker, _settings, () => sender, null);
            TelemetryQueue.FlushBlocking(TimeSpan.FromSeconds(1), sender, _settings);
            Assert.AreEqual(1, sender.AsyncRequests);
            Assert.AreEqual(1, sender.Completions);
            CollectionAssert.AreEqual(new[] { "$tf_telemetry_complete" }, sender.BlockingNames);
            Assert.AreEqual(0, TelemetryQueue.PendingEventCount);
            pending.SetResult(new BatchSendOutcome(SendResult.Success, 1));
            await Task.Delay(20);
            Assert.AreEqual(0, TelemetryQueue.PendingEventCount, "Old continuation cannot consume the new queue");
        }

        [Test]
        public void BlockingShutdownSendsGatheredEventsInsteadOfConsumingAnOlderResponse() {
            _settings.batchMaxEvents = 50;
            _settings.batchMaxFlushIntervalSeconds = 1000;
            var sender = new BlockingSender { Pending = new TaskCompletionSource<BatchSendOutcome>() };
            TelemetryQueue.Enqueue(Event("gameplay"), _settings, () => sender, null);
            TelemetryQueue.FlushBlocking(TimeSpan.FromSeconds(1), sender, _settings);
            Assert.AreEqual(0, sender.AsyncRequests);
            Assert.AreEqual(0, sender.Completions);
            CollectionAssert.AreEqual(new[] { "gameplay" }, sender.BlockingNames);
            Assert.AreEqual(0, TelemetryQueue.PendingEventCount);
        }

        sealed class BlockingSender : IBlockingBatchSender {
            internal TaskCompletionSource<BatchSendOutcome> Pending;
            internal int AsyncRequests;
            internal int Completions;
            internal List<string> BlockingNames = new();
            public async ValueTask<BatchSendOutcome> TrackBatchAsync(IReadOnlyList<TelemetryEvent> events) {
                AsyncRequests++;
                return await Pending.Task;
            }
            public BatchSendOutcome CompletePendingBlocking(DateTime deadline) {
                Completions++;
                return new BatchSendOutcome(SendResult.Success, 1);
            }
            public BatchSendOutcome TrackBatchBlocking(IReadOnlyList<TelemetryEvent> events, DateTime deadline) {
                foreach (var ev in events) BlockingNames.Add(ev.EventType);
                return new BatchSendOutcome(SendResult.Success, events.Count);
            }
        }

        sealed class BarrierSender : IBatchSender {
            internal Func<IReadOnlyList<TelemetryEvent>, Task<BatchSendOutcome>> Respond;
            public async ValueTask<BatchSendOutcome> TrackBatchAsync(IReadOnlyList<TelemetryEvent> events) => await Respond(events);
        }

        [TestCase(401, (int)SendResult.Rejected)]
        [TestCase(403, (int)SendResult.Rejected)]
        [TestCase(400, (int)SendResult.InvalidRequest)]
        [TestCase(413, (int)SendResult.InvalidRequest)]
        [TestCase(408, (int)SendResult.TransientFailure)]
        [TestCase(429, (int)SendResult.TransientFailure)]
        [TestCase(503, (int)SendResult.TransientFailure)]
        [TestCase(0, (int)SendResult.TransientFailure)]
        public void HttpOutcomesHaveExplicitRetrySemantics(int status, int expected) {
            Assert.AreEqual((SendResult)expected, CollectorClient.ClassifyResponse(status));
        }

        [Test]
        public void RentAndReturnReusesDictionary() {
            var a = TelemetryQueue.RentProperties();
            Assert.IsNotNull(a);
            a["k"] = "v";
            TelemetryQueue.ReturnProperties(a);

            var b = TelemetryQueue.RentProperties();
            Assert.AreSame(a, b, "pool should hand back the same instance");
            Assert.AreEqual(0, b.Count, "returned dict must be cleared");
            TelemetryQueue.ReturnProperties(b);
        }

        [Test]
        public void ReturnNullIsSafe() {
            Assert.DoesNotThrow(() => TelemetryQueue.ReturnProperties(null));
        }

        [Test]
        public void RentAfterPoolExhaustionReturnsFreshInstance() {
            var first = TelemetryQueue.RentProperties();
            var second = TelemetryQueue.RentProperties();
            Assert.AreNotSame(first, second);
            TelemetryQueue.ReturnProperties(first);
            TelemetryQueue.ReturnProperties(second);
        }
    }
}
