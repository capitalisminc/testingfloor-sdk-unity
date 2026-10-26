using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using TestingFloorSdk.Internal;
using UnityEngine;

namespace TestingFloorSdk.Tests {
    public class SchemaTwoRecordTests {
        readonly List<TelemetryEvent> _records = new();
        TestingFloorSettings _settings;

        sealed class Provider : ITelemetryContextProvider {
            public readonly Dictionary<string, object> Values = new();

            public void FillSnapshot(ref ContextSnapshot snapshot) {
                foreach (var kvp in Values) {
                    switch (kvp.Value) {
                        case string s: snapshot.Set(kvp.Key, s); break;
                        case long l: snapshot.Set(kvp.Key, l); break;
                        case double d: snapshot.Set(kvp.Key, d); break;
                        case bool b: snapshot.Set(kvp.Key, b); break;
                    }
                }
            }
        }

        sealed class PendingSender : IBatchSender {
            public ValueTask<BatchSendOutcome> TrackBatchAsync(IReadOnlyList<TelemetryEvent> events) =>
                new(new TaskCompletionSource<BatchSendOutcome>().Task);
        }

        [SetUp]
        public void SetUp() {
            _records.Clear();
            _settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            global::TestingFloorSdk.TestingFloor.CaptureRecordsForTesting(ev => {
                _records.Add(ev);
                return true;
            }, _settings);
        }

        [TearDown]
        public void TearDown() {
            TestingFloorRecording.ResetStatics();
            global::TestingFloorSdk.TestingFloor.ResetForTesting();
            global::TestingFloorSdk.TestingFloor.SetForceDisabled(false);
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        string[] Names() => _records.Select(record => record.EventType).ToArray();
        long[] Seqs() => _records.Select(record => record.Seq).ToArray();

        static string Json(TelemetryEvent record) {
            var bytes = new JsonPayloadWriter().BuildBatch(
                new[] { record }, "example-write-key", "unused",
                JsonPayloadWriter.CollectorBodyByteCap, JsonPayloadWriter.CollectorEventByteCap, null, out _, out var written);
            Assert.AreEqual(1, written, "The record must serialize");
            return Encoding.UTF8.GetString(bytes);
        }

        [Test]
        public void SeqStartsAtOneAndCountsEveryRecordInOrder() {
            var settings = new Provider();
            settings.Values["pref.quality"] = "high";
            global::TestingFloorSdk.TestingFloor.RegisterStateProvider(settings);

            global::TestingFloorSdk.TestingFloor.Track("level_started").Set("game_level.id", "harbor").Send();
            global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => new Vector3(1, 0, 1));
            global::TestingFloorSdk.TestingFloor.Tick(0);
            settings.Values["pref.quality"] = "low";
            global::TestingFloorSdk.TestingFloor.Track("door_opened").Send();

            CollectionAssert.AreEqual(new[] { "$tf_state", "level_started", "$tf_objects", "$tf_state", "door_opened" }, Names());
            CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4, 5 }, Seqs());
            Assert.IsTrue(_records[0].Keyframe, "The session's first state record is a keyframe");
            Assert.IsFalse(_records[3].Keyframe);
            CollectionAssert.AreEquivalent(new[] { "pref.quality" }, _records[3].EventProperties.Keys);
            Assert.AreEqual("low", _records[3].EventProperties["pref.quality"]);
        }

        [Test]
        public void RecordTheQueueRefusesDoesNotUseASeq() {
            global::TestingFloorSdk.TestingFloor.CaptureRecordsForTesting(ev => {
                if (ev.EventType == "refused") return false;
                _records.Add(ev);
                return true;
            }, _settings);

            global::TestingFloorSdk.TestingFloor.SetState("difficulty", "hard");
            Assert.IsFalse(global::TestingFloorSdk.TestingFloor.Track("refused").Send());
            global::TestingFloorSdk.TestingFloor.Track("door_opened").Send();

            CollectionAssert.AreEqual(new[] { "$tf_state", "door_opened" }, Names());
            CollectionAssert.AreEqual(new long[] { 1, 2 }, Seqs());
        }

        [Test]
        public void StateChangeIsQueuedImmediatelyBeforeItsEvent() {
            global::TestingFloorSdk.TestingFloor.SetState("difficulty", "normal");
            global::TestingFloorSdk.TestingFloor.Track("level_started").Send();
            global::TestingFloorSdk.TestingFloor.SetState("difficulty", "hard");
            global::TestingFloorSdk.TestingFloor.Track("enemy_spotted").Send();

            CollectionAssert.AreEqual(new[] { "$tf_state", "level_started", "$tf_state", "enemy_spotted" }, Names());
            Assert.AreEqual("hard", _records[2].EventProperties["difficulty"]);
            Assert.AreEqual(_records[2].Seq + 1, _records[3].Seq);
            Assert.LessOrEqual(_records[2].TimestampMs, _records[3].TimestampMs);
        }

        [Test]
        public void ClearedStateKeyIsWrittenAsNull() {
            global::TestingFloorSdk.TestingFloor.SetState("difficulty", "hard");
            global::TestingFloorSdk.TestingFloor.SetState("pref.vsync", true);
            global::TestingFloorSdk.TestingFloor.Track("a").Send();
            global::TestingFloorSdk.TestingFloor.ClearState("difficulty");
            global::TestingFloorSdk.TestingFloor.Track("b").Send();

            var keyframe = Json(_records[0]);
            StringAssert.Contains("\"$name\":\"$tf_state\"", keyframe);
            StringAssert.Contains("\"$schema\":2", keyframe);
            StringAssert.Contains("\"seq\":1", keyframe);
            StringAssert.Contains("\"keyframe\":true", keyframe);

            var change = Json(_records[2]);
            StringAssert.Contains("\"difficulty\":null", change);
            StringAssert.DoesNotContain("pref.vsync", change);
            StringAssert.DoesNotContain("\"keyframe\"", change);
        }

        [Test]
        public void EventsCarryContextAndStateRecordsCarryOnlyState() {
            var context = new Provider();
            context.Values["game_level.id"] = "harbor";
            context.Values["player.hp"] = 75.0;
            var state = new Provider();
            state.Values["pref.quality"] = "high";
            global::TestingFloorSdk.TestingFloor.RegisterContextProvider(context);
            global::TestingFloorSdk.TestingFloor.RegisterStateProvider(state);
            global::TestingFloorSdk.TestingFloor.SetPositionSource(() => new Vector3(1, 2, 3));

            global::TestingFloorSdk.TestingFloor.Track("level_started").Send();

            var stateJson = Json(_records[0]);
            StringAssert.Contains("\"pref.quality\":\"high\"", stateJson);
            StringAssert.DoesNotContain("game_level.id", stateJson);
            StringAssert.DoesNotContain("player.", stateJson);

            var eventJson = Json(_records[1]);
            StringAssert.Contains("\"game_level.id\":\"harbor\"", eventJson);
            StringAssert.Contains("\"player.hp\":75", eventJson);
            StringAssert.Contains("\"player.position.x\":1", eventJson);
            StringAssert.Contains("\"seq\":2", eventJson);
            StringAssert.DoesNotContain("pref.quality", eventJson);
        }

        [Test]
        public void ObjectsRecordWritesParallelArraysAndTheLatestLevel() {
            global::TestingFloorSdk.TestingFloor.Track("level_started").Set("game_level.id", "harbor").Send();
            var guard = global::TestingFloorSdk.TestingFloor.TrackObject("guard", () => new Vector3(12.41f, 0, 48.13f), "guard_a");
            guard.SetState("behavior", "searching");
            global::TestingFloorSdk.TestingFloor.Tick(0);

            var record = _records.Single(r => r.EventType == "$tf_objects");
            var json = Json(record);
            StringAssert.Contains("\"$schema\":2", json);
            StringAssert.Contains("\"game_level.id\":\"harbor\"", json);
            StringAssert.Contains("\"sample.id\":[\"guard_a\"]", json);
            StringAssert.Contains("\"sample.kind\":[\"guard\"]", json);
            StringAssert.Contains("\"sample.x\":[12.41]", json);
            StringAssert.Contains("\"sample.y\":[0]", json);
            StringAssert.Contains("\"sample.z\":[48.13]", json);
            StringAssert.Contains("\"state.id\":[\"guard_a\"]", json);
            StringAssert.Contains("\"state.key\":[\"behavior\"]", json);
            StringAssert.Contains("\"state.value\":[\"searching\"]", json);
            StringAssert.DoesNotContain("player.", json);
            StringAssert.DoesNotContain("camera.", json);

            guard.Remove("knocked_out");
            global::TestingFloorSdk.TestingFloor.Tick(1);
            var removal = Json(_records.Last());
            StringAssert.Contains("\"removed.id\":[\"guard_a\"]", removal);
            StringAssert.Contains("\"removed.reason\":[\"knocked_out\"]", removal);
        }

        [Test]
        public void DroppedRecordTriggersStateAndObjectKeyframes() {
            global::TestingFloorSdk.TestingFloor.SetState("difficulty", "hard");
            global::TestingFloorSdk.TestingFloor.Track("level_started").Send();
            global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => Vector3.one);
            global::TestingFloorSdk.TestingFloor.Tick(0);
            global::TestingFloorSdk.TestingFloor.Tick(1);
            _records.Clear();

            var queueSettings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            try {
                queueSettings.queueMaxEvents = 1;
                var sender = new PendingSender();
                var filler = new TelemetryEvent("filler", null, ContextSnapshot.Create(default), null, null, 0, "example-play");
                Assert.IsTrue(TelemetryQueue.Enqueue(filler, queueSettings, () => sender, null));
                Assert.IsFalse(TelemetryQueue.Enqueue(filler, queueSettings, () => sender, null));
                Assert.AreEqual(1, TelemetryQueue.DroppedEventCount);

                global::TestingFloorSdk.TestingFloor.Tick(2);
            }
            finally {
                TelemetryQueue.ResetStatics();
                UnityEngine.Object.DestroyImmediate(queueSettings);
            }

            CollectionAssert.AreEqual(new[] { "$tf_state", "$tf_objects" }, Names());
            Assert.IsTrue(_records[0].Keyframe);
            Assert.AreEqual("hard", _records[0].EventProperties["difficulty"]);
            Assert.IsTrue(_records[1].Keyframe);
            CollectionAssert.AreEqual(new[] { "crate#1" }, (string[])_records[1].EventProperties["sample.id"]);
        }

        [Test]
        public void CompletionMarkerTakesTheNextSeq() {
            var recording = new TestingFloorRecording("example-recording", null, true);
            global::TestingFloorSdk.TestingFloor.Track("level_started").Send();
            global::TestingFloorSdk.TestingFloor.EnqueueRecordingCompletion(recording, global::TestingFloorSdk.TestingFloor.PlaySessionId);
            global::TestingFloorSdk.TestingFloor.Track("door_opened").Send();

            CollectionAssert.AreEqual(new[] { "level_started", "$tf_telemetry_complete", "door_opened" }, Names());
            CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, Seqs());
            StringAssert.Contains("\"seq\":2", Json(_records[1]));
        }

        [Test]
        public void ClosedRecordingIsDroppedFromStateAndObjectRecords() {
            var recording = new TestingFloorRecording("example-recording", null, true);
            TestingFloorRecording.UseForTesting(recording);
            var crate = new Vector3(1, 0, 1);
            global::TestingFloorSdk.TestingFloor.SetState("difficulty", "normal");
            global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => crate);
            global::TestingFloorSdk.TestingFloor.Tick(0);
            StringAssert.Contains("\"recording_uuid\":\"example-recording\"", Json(_records.Single(r => r.EventType == "$tf_state")));
            StringAssert.Contains("\"recording_uuid\":\"example-recording\"", Json(_records.Single(r => r.EventType == "$tf_objects")));
            _records.Clear();

            recording.Closed = true;
            global::TestingFloorSdk.TestingFloor.SetState("difficulty", "hard");
            crate = new Vector3(5, 0, 5);
            global::TestingFloorSdk.TestingFloor.Tick(1);

            CollectionAssert.AreEqual(new[] { "$tf_state", "$tf_objects" }, Names());
            Assert.IsFalse(_records[0].Keyframe, "Leaving a recording is not a keyframe");
            foreach (var record in _records) StringAssert.DoesNotContain("recording_uuid", Json(record));
        }

        [Test]
        public void NewRecordingSendsAStateAndObjectKeyframe() {
            global::TestingFloorSdk.TestingFloor.SetState("difficulty", "hard");
            global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => Vector3.one);
            global::TestingFloorSdk.TestingFloor.Tick(0);
            global::TestingFloorSdk.TestingFloor.Tick(1);
            _records.Clear();

            TestingFloorRecording.UseForTesting(new TestingFloorRecording("example-recording", null, true));
            global::TestingFloorSdk.TestingFloor.Tick(2);

            CollectionAssert.AreEqual(new[] { "$tf_state", "$tf_objects" }, Names());
            Assert.IsTrue(_records[0].Keyframe);
            Assert.AreEqual("hard", _records[0].EventProperties["difficulty"]);
            Assert.IsTrue(_records[1].Keyframe);
            CollectionAssert.AreEqual(new[] { "crate#1" }, (string[])_records[1].EventProperties["sample.id"]);
        }

        void RefuseFirstRemovalRecord() {
            var refused = false;
            global::TestingFloorSdk.TestingFloor.CaptureRecordsForTesting(ev => {
                if (!refused && ev.EventType == "$tf_objects" && ev.EventProperties.ContainsKey("removed.id")) {
                    refused = true;
                    return false;
                }
                _records.Add(ev);
                return true;
            }, _settings);
        }

        [Test]
        public void RefusedRemovalIsSentAgainAtTheNextCheck() {
            RefuseFirstRemovalRecord();
            var position = new Vector3(1, 0, 1);
            var crate = global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => position);
            global::TestingFloorSdk.TestingFloor.Tick(0);
            position = new Vector3(2, 0, 2);
            crate.Remove("destroyed");
            global::TestingFloorSdk.TestingFloor.Tick(1);
            Assert.IsFalse(_records.Any(r => r.EventProperties.ContainsKey("removed.id")), "The queue refused the removal");

            global::TestingFloorSdk.TestingFloor.Tick(2);
            var retried = _records.Last();
            Assert.AreEqual("$tf_objects", retried.EventType);
            CollectionAssert.AreEqual(new[] { "crate#1" }, (string[])retried.EventProperties["removed.id"]);
            CollectionAssert.AreEqual(new[] { "destroyed" }, (string[])retried.EventProperties["removed.reason"]);
            CollectionAssert.AreEqual(new[] { 2.0 }, (double[])retried.EventProperties["sample.x"], "The final position goes with it");
        }

        [Test]
        public void RefusedRemovalsBeyondTheCapAreDroppedAndCounted() {
            RefuseFirstRemovalRecord();
            _settings.maxTrackedObjects = 3;
            var objects = new[] {
                global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => Vector3.one, "crate_a"),
                global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => Vector3.one, "crate_b"),
                global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => Vector3.one, "crate_c"),
            };
            global::TestingFloorSdk.TestingFloor.Tick(0);
            _settings.maxTrackedObjects = 2;
            foreach (var tracked in objects) tracked.Remove("destroyed");
            global::TestingFloorSdk.TestingFloor.Tick(1);
            Assert.AreEqual(1, TelemetryQueue.DroppedEventCount, "The removal past the cap counts as dropped");

            global::TestingFloorSdk.TestingFloor.Tick(2);
            CollectionAssert.AreEqual(new[] { "crate_a", "crate_b" }, (string[])_records.Last(r => r.EventType == "$tf_objects").EventProperties["removed.id"]);
        }

        [Test]
        public void QuitSendsTheLastStateAndObjectChangesBeforeTheCompletionMarker() {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(directory);
            try {
                var recording = new TestingFloorRecording("example-recording", null, true, directory);
                TestingFloorRecording.UseForTesting(recording);
                Assert.IsTrue(RecordingTelemetry.Register(recording, global::TestingFloorSdk.TestingFloor.PlaySessionId));
                var position = Vector3.one;
                global::TestingFloorSdk.TestingFloor.SetState("difficulty", "normal");
                global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => position);
                global::TestingFloorSdk.TestingFloor.Track("level_started").Send();
                global::TestingFloorSdk.TestingFloor.Tick(0);
                _records.Clear();

                global::TestingFloorSdk.TestingFloor.SetState("difficulty", "hard");
                position = new Vector3(4, 0, 4);
                global::TestingFloorSdk.TestingFloor.FinishRecordingAndFlush();

                CollectionAssert.AreEqual(new[] { "$tf_state", "$tf_objects", "$tf_telemetry_complete" }, Names());
                Assert.AreEqual("hard", _records[0].EventProperties["difficulty"]);
                Assert.AreSame(recording, _records[0].Recording);
                Assert.AreSame(recording, _records[1].Recording);
            }
            finally {
                RecordingTelemetry.ResetStatics();
                System.IO.Directory.Delete(directory, true);
            }
        }

        [Test]
        public void QuitSendsEveryWaitingLifetimeOfAnIdInItsOwnRecord() {
            var first = global::TestingFloorSdk.TestingFloor.TrackObject("guard", () => new Vector3(1, 0, 0), "guard_a");
            global::TestingFloorSdk.TestingFloor.Tick(0);
            _records.Clear();
            first.Remove("knocked_out");
            global::TestingFloorSdk.TestingFloor.TrackObject("guard", () => new Vector3(2, 0, 0), "guard_a").Remove("despawned");
            global::TestingFloorSdk.TestingFloor.TrackObject("guard", () => new Vector3(3, 0, 0), "guard_a");

            global::TestingFloorSdk.TestingFloor.FinishRecordingAndFlush();

            var objects = _records.Where(r => r.EventType == "$tf_objects").ToList();
            Assert.AreEqual(3, objects.Count, "One record per lifetime of guard_a");
            CollectionAssert.AreEqual(new[] { "knocked_out" }, (string[])objects[0].EventProperties["removed.reason"]);
            CollectionAssert.AreEqual(new[] { 2.0 }, (double[])objects[1].EventProperties["sample.x"]);
            CollectionAssert.AreEqual(new[] { "despawned" }, (string[])objects[1].EventProperties["removed.reason"]);
            CollectionAssert.AreEqual(new[] { 3.0 }, (double[])objects[2].EventProperties["sample.x"]);
            Assert.Less(objects[0].TimestampMs, objects[1].TimestampMs);
            Assert.Less(objects[1].TimestampMs, objects[2].TimestampMs);
            Assert.AreEqual(0, TelemetryQueue.DroppedEventCount);
        }

        [Test]
        public void QuitCountsObjectChangesItCannotSend() {
            var refuseObjects = false;
            global::TestingFloorSdk.TestingFloor.CaptureRecordsForTesting(ev => !(refuseObjects && ev.EventType == "$tf_objects"), _settings);
            var first = global::TestingFloorSdk.TestingFloor.TrackObject("guard", () => Vector3.one, "guard_a");
            global::TestingFloorSdk.TestingFloor.Tick(0);
            refuseObjects = true;
            first.Remove("knocked_out");
            global::TestingFloorSdk.TestingFloor.TrackObject("guard", () => Vector3.zero, "guard_a");

            global::TestingFloorSdk.TestingFloor.FinishRecordingAndFlush();

            Assert.AreEqual(2, TelemetryQueue.DroppedEventCount, "The retried removal and the held-back replacement");
        }

        [Test]
        public void RefusedKeyframeObjectsGoFirstSoEveryObjectIsReached() {
            _settings.maxTrackedObjects = 700;
            var accepted = 0;
            var delivered = new HashSet<string>();
            global::TestingFloorSdk.TestingFloor.CaptureRecordsForTesting(ev => {
                if (ev.EventType != "$tf_objects") return true;
                if (accepted >= 2) {
                    TelemetryQueue.CountDropped(1, recording: false);
                    return false;
                }
                accepted++;
                foreach (var id in (string[])ev.EventProperties["sample.id"]) delivered.Add(id);
                return true;
            }, _settings);
            for (var i = 0; i < 650; i++) global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => Vector3.one);

            global::TestingFloorSdk.TestingFloor.Tick(0);
            Assert.AreEqual(600, delivered.Count, "The third record of the first check is refused");
            accepted = 0;
            global::TestingFloorSdk.TestingFloor.Tick(1);

            Assert.AreEqual(650, delivered.Count, "The recovery keyframe starts with the refused objects");
        }

        sealed class BarrierSender : IBatchSender {
            internal Func<IReadOnlyList<TelemetryEvent>, Task<BatchSendOutcome>> Respond;
            public async ValueTask<BatchSendOutcome> TrackBatchAsync(IReadOnlyList<TelemetryEvent> events) => await Respond(events);
        }

        [Test]
        public void RecordingCloseSendsBufferedStateAndRemovalBeforeTheMarker() {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(directory);
            try {
                var recording = new TestingFloorRecording("example-recording", null, true, directory);
                TestingFloorRecording.UseForTesting(recording);
                Assert.IsTrue(global::TestingFloorSdk.TestingFloor.RegisterRecording(recording));
                var position = Vector3.one;
                global::TestingFloorSdk.TestingFloor.SetState("difficulty", "normal");
                var crate = global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => position, "crate");
                global::TestingFloorSdk.TestingFloor.Track("level_started").Send();
                global::TestingFloorSdk.TestingFloor.Tick(0);
                _records.Clear();

                // Both change before the next state poll and object check, and then the recording closes.
                global::TestingFloorSdk.TestingFloor.SetState("difficulty", "hard");
                position = new Vector3(4, 0, 4);
                crate.Remove("destroyed");
                System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "closed.json"), "1");
                RecordingTelemetry.Poll();

                CollectionAssert.AreEqual(new[] { "$tf_state", "$tf_objects", "$tf_telemetry_complete" }, Names());
                Assert.AreEqual("hard", _records[0].EventProperties["difficulty"]);
                CollectionAssert.AreEqual(new[] { "destroyed" }, (string[])_records[1].EventProperties["removed.reason"]);
                Assert.AreSame(recording, _records[0].Recording);
                Assert.AreSame(recording, _records[1].Recording);
                Assert.Less(_records[0].Seq, _records[1].Seq);
                Assert.Less(_records[1].Seq, _records[2].Seq);
            }
            finally {
                RecordingTelemetry.ResetStatics();
                System.IO.Directory.Delete(directory, true);
            }
        }

        [Test]
        public void RecordingCloseToleratesCallbacksThatSendEvents() {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(directory);
            try {
                var recording = new TestingFloorRecording("example-recording", null, true, directory);
                TestingFloorRecording.UseForTesting(recording);
                Assert.IsTrue(global::TestingFloorSdk.TestingFloor.RegisterRecording(recording));
                global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => {
                    global::TestingFloorSdk.TestingFloor.Track("crate_sampled").Send();
                    return Vector3.one;
                }, "crate");
                _records.Clear();

                System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "closed.json"), "1");
                RecordingTelemetry.Poll();

                Assert.IsTrue(recording.Closed);
                Assert.AreEqual(1, Names().Count(name => name == "$tf_telemetry_complete"));
                var marker = Array.IndexOf(Names(), "$tf_telemetry_complete");
                var sampled = Array.IndexOf(Names(), "crate_sampled");
                Assert.That(sampled, Is.GreaterThanOrEqualTo(0).And.LessThan(marker), "The callback's event joins the recording ahead of its marker");
                Assert.AreSame(recording, _records[sampled].Recording);
            }
            finally {
                RecordingTelemetry.ResetStatics();
                System.IO.Directory.Delete(directory, true);
            }
        }

        [Test]
        public async Task RemovalRefusedAtRecordingCloseCountsTowardTheMarker() {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(directory);
            var first = new TaskCompletionSource<BatchSendOutcome>();
            var calls = 0;
            var markerLoss = -1;
            var sender = new BarrierSender { Respond = events => {
                calls++;
                if (calls == 1) return first.Task;
                foreach (var ev in events) {
                    if (ev.EventType == "$tf_telemetry_complete") markerLoss = Convert.ToInt32(ev.EventProperties["dropped_events"]);
                }
                return Task.FromResult(new BatchSendOutcome(SendResult.Success, events.Count));
            }};
            _settings.queueMaxEvents = 1;
            _settings.batchMaxFlushIntervalSeconds = 0;
            global::TestingFloorSdk.TestingFloor.QueueRecordsForTesting(sender, _settings);
            try {
                var recording = new TestingFloorRecording("example-recording", null, true, directory);
                TestingFloorRecording.UseForTesting(recording);
                Assert.IsTrue(global::TestingFloorSdk.TestingFloor.RegisterRecording(recording));
                var crate = global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => Vector3.one, "crate");
                Assert.IsTrue(global::TestingFloorSdk.TestingFloor.Track("level_started").Send(), "Takes the only queue slot");

                crate.Remove("destroyed");
                System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "closed.json"), "1");
                RecordingTelemetry.Poll();
                first.SetResult(new BatchSendOutcome(SendResult.Success, 1));

                var deadline = DateTime.UtcNow.AddSeconds(4);
                while (markerLoss < 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.AreEqual(1, markerLoss, "The removal refused while the recording was open is part of its loss");
            }
            finally {
                RecordingTelemetry.ResetStatics();
                TelemetryQueue.ResetStatics();
                System.IO.Directory.Delete(directory, true);
            }
        }

        [Test]
        public void DisabledCollectionReturnsAHandleThatDoesNothing() {
            global::TestingFloorSdk.TestingFloor.ResetForTesting();
            global::TestingFloorSdk.TestingFloor.SetForceDisabled(true);

            var handle = global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => Vector3.one, "crate_a");
            Assert.IsFalse(handle.IsTracked);
            Assert.AreEqual("crate_a", handle.Id);
            Assert.DoesNotThrow(() => handle.SetState("open", true).Remove("destroyed"));

            global::TestingFloorSdk.TestingFloor.Tick(0);
            global::TestingFloorSdk.TestingFloor.Tick(1);
            Assert.AreEqual(0, TelemetryQueue.PendingEventCount, "Nothing is sampled or queued");
            Assert.IsEmpty(_records);
        }

        [Test]
        public void ComponentTracksWhileEnabledAndRemovesWhenDisabled() {
            if (!Application.isPlaying) Assert.Ignore("Component lifecycle needs Play Mode.");
            var go = new GameObject("guard");
            try {
                go.SetActive(false);
                go.transform.position = new Vector3(3, 0, 4);
                var component = go.AddComponent<TestingFloorTrackedObject>();
                component.Kind = "guard";
                component.Id = "captain_zed";
                go.SetActive(true);
                Assert.IsNotNull(component.Handle);

                global::TestingFloorSdk.TestingFloor.Tick(0);
                var sample = _records.Last();
                CollectionAssert.AreEqual(new[] { "captain_zed" }, (string[])sample.EventProperties["sample.id"]);
                CollectionAssert.AreEqual(new[] { "guard" }, (string[])sample.EventProperties["sample.kind"]);

                component.enabled = false;
                global::TestingFloorSdk.TestingFloor.Tick(1);
                var removal = _records.Last();
                CollectionAssert.AreEqual(new[] { "disabled" }, (string[])removal.EventProperties["removed.reason"]);
            }
            finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ComponentEnabledBeforeCollectionStartsRegistersOnceItStarts() {
            if (!Application.isPlaying) Assert.Ignore("Component lifecycle needs Play Mode.");
            global::TestingFloorSdk.TestingFloor.ResetForTesting();
            global::TestingFloorSdk.TestingFloor.SetForceDisabled(true);
            var go = new GameObject("crate");
            try {
                var component = go.AddComponent<TestingFloorTrackedObject>();
                component.Kind = "crate";
                Assert.IsNull(component.Handle);

                global::TestingFloorSdk.TestingFloor.CaptureRecordsForTesting(ev => {
                    _records.Add(ev);
                    return true;
                }, _settings);
                global::TestingFloorSdk.TestingFloor.Tick(0);

                Assert.IsNotNull(component.Handle);
                CollectionAssert.AreEqual(new[] { "crate#1" }, (string[])_records.Single().EventProperties["sample.id"]);
            }
            finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
