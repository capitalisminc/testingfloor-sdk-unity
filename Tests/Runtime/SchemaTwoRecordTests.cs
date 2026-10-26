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
