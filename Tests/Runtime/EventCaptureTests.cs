using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TestingFloorSdk.Internal;
using UnityEngine;

namespace TestingFloorSdk.Tests {
    public class EventCaptureTests {
        readonly List<TelemetryEvent> _records = new();
        readonly List<string> _warnings = new();
        TestingFloorSettings _settings;

        delegate void Fill(ref ContextSnapshot snapshot);

        sealed class Provider : ITelemetryContextProvider {
            readonly Fill _fill;
            public Provider(Fill fill) { _fill = fill; }
            public void FillSnapshot(ref ContextSnapshot snapshot) => _fill(ref snapshot);
        }

        [SetUp]
        public void SetUp() {
            _records.Clear();
            _warnings.Clear();
            _settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            Application.logMessageReceived += OnLog;
            global::TestingFloorSdk.TestingFloor.CaptureRecordsForTesting(ev => {
                _records.Add(ev);
                return true;
            }, _settings);
        }

        [TearDown]
        public void TearDown() {
            Application.logMessageReceived -= OnLog;
            Identity.ResetStatics();
            global::TestingFloorSdk.TestingFloor.ResetForTesting();
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        void OnLog(string message, string stackTrace, LogType type) {
            if (type == LogType.Warning) _warnings.Add(message);
        }

        string[] Names() => _records.Select(record => record.EventType).ToArray();

        TelemetryEvent Single(string name) => _records.Single(record => record.EventType == name);

        static string Json(TelemetryEvent record) => Encoding.UTF8.GetString(new JsonPayloadWriter().BuildBatch(
            new[] { record }, "example-write-key", "example-play",
            JsonPayloadWriter.CollectorBodyByteCap, JsonPayloadWriter.CollectorEventByteCap, null, out _, out _));

        [Test]
        public void OutputSpaceAlignsEventsObjectsAndLengthsBeforeTheProcessor() {
            _settings.buildId = "example-build";
            _settings.coordinateTransform = new CoordinateTransform { enabled = true, axes = new Vector3Int(2, -1, 3), scale = 2, offset = new Vector3(10, 20, 30) };
            global::TestingFloorSdk.TestingFloor.CaptureRecordsForTesting(ev => { _records.Add(ev); return true; }, _settings);
            _settings.coordinateTransform.scale = 50;
            global::TestingFloorSdk.TestingFloor.SetPositionSource(() => new Vector3(2, 1, 3));
            object observed = null;
            global::TestingFloorSdk.TestingFloor.RegisterEventProcessor(new Provider((ref ContextSnapshot context) => context.TryGetValue("player.position.x", out observed)));
            global::TestingFloorSdk.TestingFloor.Track("example").SetVector("camera.up", Vector3.up)
                .SetVector("camera.euler", Vector3.one).Set("camera.ortho_width", 4.0).Set("custom.position.x", 99.0).Send();
            Assert.AreEqual(12.0, observed, "The processor sees the captured output space");
            var json = Json(Single("example"));
            StringAssert.Contains("\"build_id\":\"example-build\"", json);
            StringAssert.Contains("\"player.position.y\":16", json);
            StringAssert.Contains("\"player.position.z\":36", json);
            StringAssert.Contains("\"camera.up.x\":1", json);
            StringAssert.Contains("\"camera.ortho_width\":8", json);
            StringAssert.Contains("\"custom.position.x\":99", json);
            StringAssert.DoesNotContain("camera.euler", json);
            global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => new Vector3(2, 1, 3));
            global::TestingFloorSdk.TestingFloor.Tick(0);
            var objects = Single("$tf_objects").EventProperties;
            CollectionAssert.AreEqual(new[] { 12.0 }, (double[])objects["sample.x"]);
            CollectionAssert.AreEqual(new[] { 16.0 }, (double[])objects["sample.y"]);
            TeleportTracker.Record(Vector3.zero, new Vector3(3, 4, 0), "respawn", 0);
            StringAssert.Contains("\"teleport.distance\":10", Json(Single("$tf_player_teleported")));
            Assert.IsTrue(global::TestingFloorSdk.TestingFloor.Track("player_moved").Send(), "Legacy game-owned names remain usable");
        }

        [Test]
        public void InvalidOutputSpaceDisablesCollection() {
            _settings.coordinateTransform = new CoordinateTransform { enabled = true, axes = new Vector3Int(1, 1, 3) };
            global::TestingFloorSdk.TestingFloor.CaptureRecordsForTesting(ev => { _records.Add(ev); return true; }, _settings);
            Assert.IsFalse(global::TestingFloorSdk.TestingFloor.IsCollecting);
            Assert.IsFalse(global::TestingFloorSdk.TestingFloor.Track("example").Send());
            Assert.IsEmpty(_records);
        }

        [Test]
        public void ReservedEventNamesAreRefusedWithOneWarningPerName() {
            Assert.IsFalse(global::TestingFloorSdk.TestingFloor.Track("$tf_state").Set("example.value", 1L).Send());
            Assert.IsFalse(global::TestingFloorSdk.TestingFloor.Track("$tf_state").Send());
            Assert.IsFalse(global::TestingFloorSdk.TestingFloor.Track("$TF_Custom").Send(), "Any casing is refused");
            Assert.IsTrue(global::TestingFloorSdk.TestingFloor.Track("level_started").Send());

            CollectionAssert.AreEqual(new[] { "level_started" }, Names());
            Assert.AreEqual(2, _warnings.Count(warning => warning.Contains("reserved")));
            Assert.AreEqual(3, global::TestingFloorSdk.TestingFloor.DroppedEventCount);
            Assert.AreEqual(0, TelemetryQueue.LostRecordCount, "A refused game event is not a lost record and starts no keyframe");
        }

        [Test]
        public void RefusedEventNamesDoNotStartKeyframes() {
            global::TestingFloorSdk.TestingFloor.SetState("difficulty", "hard");
            global::TestingFloorSdk.TestingFloor.Track("level_started").Send();
            global::TestingFloorSdk.TestingFloor.Track("$tf_objects").Send();
            global::TestingFloorSdk.TestingFloor.Tick(1);

            CollectionAssert.AreEqual(new[] { "$tf_state", "level_started" }, Names());
        }

        [Test]
        public void EventProcessorRunsLastOverTheMergedProperties() {
            global::TestingFloorSdk.TestingFloor.RegisterContextProvider(new Provider((ref ContextSnapshot snapshot) => {
                snapshot.Set("game_level.id", "from-context");
                snapshot.Set("hud.visible", true);
            }));
            global::TestingFloorSdk.TestingFloor.SetPositionSource(() => new Vector3(1f, 2f, 3f));
            object seenLevel = null;
            string[] seenKeys = null;
            var removed = false;
            // The SDK catches processor exceptions, so the checks below read what the processor saw.
            global::TestingFloorSdk.TestingFloor.RegisterEventProcessor(new Provider((ref ContextSnapshot snapshot) => {
                seenKeys = snapshot.Keys;
                snapshot.TryGetValue("game_level.id", out seenLevel);
                if (snapshot.TryGetValue("player.position.x", out var x)) snapshot.Set("player.position.x", Convert.ToDouble(x) * 100);
                removed = snapshot.Remove("hud.visible");
                snapshot.Set("game_level.id", "processed-" + seenLevel);
            }));

            global::TestingFloorSdk.TestingFloor.Track("level_started").Set("game_level.id", "from-event").Set("example.value", 1L).Send();
            global::TestingFloorSdk.TestingFloor.TrackObject("crate", () => Vector3.zero);
            global::TestingFloorSdk.TestingFloor.Tick(0);

            Assert.AreEqual("from-event", seenLevel, "The event's own value wins before the processor runs");
            CollectionAssert.IsSubsetOf(new[] { "game_level.id", "hud.visible", "player.position.x", "example.value" }, seenKeys);
            Assert.IsTrue(removed);
            var json = Json(Single("level_started"));
            StringAssert.Contains("\"game_level.id\":\"processed-from-event\"", json);
            StringAssert.Contains("\"player.position.x\":100", json);
            StringAssert.Contains("\"player.position.y\":2", json);
            StringAssert.DoesNotContain("hud.visible", json);
            StringAssert.Contains("\"game_level.id\":\"processed-from-event\"", Json(Single(global::TestingFloorSdk.TestingFloor.ObjectsRecordName)),
                "Object records copy the processed level");
        }

        [Test]
        public void EventProcessorThatThrowsStillSendsTheEvent() {
            global::TestingFloorSdk.TestingFloor.RegisterEventProcessor(new Provider((ref ContextSnapshot snapshot) =>
                throw new InvalidOperationException("example failure")));

            Assert.IsTrue(global::TestingFloorSdk.TestingFloor.Track("level_started").Set("example.value", 1L).Send());

            StringAssert.Contains("\"example.value\":1", Json(Single("level_started")));
        }

        [Test]
        public void SetStateAcceptsListsAndCopiesThem() {
            var items = new[] { "sword", "shield" };
            global::TestingFloorSdk.TestingFloor.SetState("inventory.items", items);
            global::TestingFloorSdk.TestingFloor.SetState("inventory.counts", new[] { 1, 2 });
            items[0] = "changed";
            global::TestingFloorSdk.TestingFloor.Track("a").Send();
            global::TestingFloorSdk.TestingFloor.Track("b").Send();
            global::TestingFloorSdk.TestingFloor.SetState("inventory.items", (string[])null);
            global::TestingFloorSdk.TestingFloor.Track("c").Send();

            CollectionAssert.AreEqual(new[] { "$tf_state", "a", "b", "$tf_state", "c" }, Names(), "An unchanged list sends no state");
            var keyframe = Json(_records[0]);
            StringAssert.Contains("\"inventory.items\":[\"sword\",\"shield\"]", keyframe);
            StringAssert.Contains("\"inventory.counts\":[1,2]", keyframe);
            StringAssert.Contains("\"inventory.items\":null", Json(_records[3]));
        }

        [Test]
        public void GameIdsReplaceTheGeneratedIds() {
            var generatedProfile = global::TestingFloorSdk.TestingFloor.ProfileId;
            var generatedDevice = global::TestingFloorSdk.TestingFloor.DeviceId;

            global::TestingFloorSdk.TestingFloor.SetUserId("example-account");
            global::TestingFloorSdk.TestingFloor.SetDeviceId(" example-install ");
            Assert.AreEqual("example-account", global::TestingFloorSdk.TestingFloor.PlayerId);
            global::TestingFloorSdk.TestingFloor.Track("a").Send();
            global::TestingFloorSdk.TestingFloor.SetUserId(null);
            global::TestingFloorSdk.TestingFloor.SetDeviceId("");
            global::TestingFloorSdk.TestingFloor.Track("b").Send();

            var withGameIds = Json(Single("a"));
            StringAssert.Contains("\"$user_id\":\"example-account\"", withGameIds);
            StringAssert.Contains("\"$device_id\":\"example-install\"", withGameIds);
            var generated = Json(Single("b"));
            StringAssert.Contains("\"$device_id\":\"" + generatedDevice + "\"", generated);
            if (generatedProfile != null) StringAssert.Contains("\"$user_id\":\"" + generatedProfile + "\"", generated);
            Assert.AreEqual(generatedProfile, global::TestingFloorSdk.TestingFloor.ProfileId);
        }
    }
}
