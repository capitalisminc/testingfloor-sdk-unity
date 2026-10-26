using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TestingFloorSdk.Internal;
using UnityEngine;
using UnityEngine.TestTools;

namespace TestingFloorSdk.Tests {
    public class ObjectTrackerTests {
        sealed class Point {
            public Vector3? Position;
            public Point(Vector3? position) => Position = position;
        }

        static ObjectTracker.Entry Register(ObjectTracker tracker, string kind, Point point, string id = null) =>
            tracker.Register(kind, null, () => point.Position, id);

        static List<Dictionary<string, object>> Check(ObjectTracker tracker, double now) => Check(tracker, now, out _);

        static List<Dictionary<string, object>> Check(ObjectTracker tracker, double now, out bool keyframe) {
            var records = new List<Dictionary<string, object>>();
            keyframe = tracker.Check(now, records);
            return records;
        }

        static string[] Strings(Dictionary<string, object> record, string key) =>
            record.TryGetValue(key, out var value) ? (string[])value : System.Array.Empty<string>();

        static double[] Numbers(Dictionary<string, object> record, string key) =>
            record.TryGetValue(key, out var value) ? (double[])value : System.Array.Empty<double>();

        static object[] Values(Dictionary<string, object> record, string key) =>
            record.TryGetValue(key, out var value) ? (object[])value : System.Array.Empty<object>();

        [Test]
        public void RegisteredObjectsAreSampledAtTheNextCheckWithIdsCountedPerKind() {
            var tracker = new ObjectTracker();
            Register(tracker, "guard", new Point(new Vector3(1, 0, 2)));
            Register(tracker, "guard", new Point(new Vector3(3, 0, 4)));
            Register(tracker, "crate", new Point(new Vector3(5, 1, 6)));
            Register(tracker, "guard", new Point(new Vector3(7, 0, 8)), id: "captain_zed");

            var records = Check(tracker, 0);

            Assert.AreEqual(1, records.Count);
            CollectionAssert.AreEqual(new[] { "guard#1", "guard#2", "crate#1", "captain_zed" }, Strings(records[0], "sample.id"));
            CollectionAssert.AreEqual(new[] { "guard", "guard", "crate", "guard" }, Strings(records[0], "sample.kind"));
            CollectionAssert.AreEqual(new[] { 1.0, 3.0, 5.0, 7.0 }, Numbers(records[0], "sample.x"));
            CollectionAssert.AreEqual(new[] { 0.0, 0.0, 1.0, 0.0 }, Numbers(records[0], "sample.y"));
            CollectionAssert.AreEqual(new[] { 2.0, 4.0, 6.0, 8.0 }, Numbers(records[0], "sample.z"));
        }

        [Test]
        public void ObjectIsSampledOnlyAfterMovingAQuarterMetreFromItsLastSentSample() {
            var tracker = new ObjectTracker();
            var crate = new Point(Vector3.zero);
            Register(tracker, "crate", crate);
            Check(tracker, 0);

            crate.Position = new Vector3(0.24f, 0, 0);
            Assert.IsEmpty(Check(tracker, 0.25), "Below the threshold");

            crate.Position = new Vector3(0.26f, 0, 0);
            var records = Check(tracker, 0.5);
            Assert.AreEqual(1, records.Count);
            CollectionAssert.AreEqual(new[] { 0.26 }, Numbers(records[0], "sample.x"));

            crate.Position = new Vector3(0.5f, 0, 0);
            Assert.IsEmpty(Check(tracker, 0.75), "Distance is measured from the last sent sample, not the last check");
            crate.Position = new Vector3(0.52f, 0, 0);
            CollectionAssert.AreEqual(new[] { 0.52 }, Numbers(Check(tracker, 1.0)[0], "sample.x"));
        }

        [Test]
        public void FirstSampleIsThePositionReadAtRegistration() {
            var tracker = new ObjectTracker();
            var rocket = new Point(Vector3.zero);
            Register(tracker, "rocket", rocket);
            rocket.Position = new Vector3(0.1f, 0, 0);
            CollectionAssert.AreEqual(new[] { 0.0 }, Numbers(Check(tracker, 0)[0], "sample.x"), "A move below the threshold adds no sample");

            var crate = new Point(Vector3.zero);
            Register(tracker, "crate", crate);
            crate.Position = new Vector3(3, 0, 0);
            var record = Check(tracker, 0.25)[0];
            CollectionAssert.AreEqual(new[] { "crate#1", "crate#1" }, Strings(record, "sample.id"));
            CollectionAssert.AreEqual(new[] { 0.0, 3.0 }, Numbers(record, "sample.x"));
        }

        [Test]
        public void NoRecordIsSentWhenNothingChanged() {
            var tracker = new ObjectTracker();
            Assert.IsEmpty(Check(tracker, 0), "No objects");
            Register(tracker, "crate", new Point(Vector3.one));
            Assert.AreEqual(1, Check(tracker, 0.25).Count);
            Assert.IsEmpty(Check(tracker, 0.5));
        }

        [Test]
        public void RemovalSendsTheFinalPositionAndReason() {
            var tracker = new ObjectTracker();
            var guard = new Point(Vector3.zero);
            var entry = Register(tracker, "guard", guard, id: "guard_a");
            Check(tracker, 0);

            guard.Position = new Vector3(0.1f, 0, 0);
            tracker.Remove(entry, "knocked_out");
            var records = Check(tracker, 0.25);

            Assert.AreEqual(1, records.Count);
            CollectionAssert.AreEqual(new[] { "guard_a" }, Strings(records[0], "sample.id"));
            CollectionAssert.AreEqual(new[] { 0.1 }, Numbers(records[0], "sample.x"), "The final position is sent even below the movement threshold");
            CollectionAssert.AreEqual(new[] { "guard_a" }, Strings(records[0], "removed.id"));
            CollectionAssert.AreEqual(new[] { "knocked_out" }, Strings(records[0], "removed.reason"));
            Assert.AreEqual(0, tracker.LiveCount);
            Assert.IsEmpty(Check(tracker, 0.5));
        }

        [Test]
        public void ObjectRemovedBeforeAnyCheckStillGetsAStartAndAnEnd() {
            var tracker = new ObjectTracker();
            var rocket = new Point(Vector3.zero);
            var entry = Register(tracker, "rocket", rocket);
            rocket.Position = new Vector3(4, 0, 0);
            tracker.Remove(entry, "exploded");

            var records = Check(tracker, 0);
            CollectionAssert.AreEqual(new[] { "rocket#1", "rocket#1" }, Strings(records[0], "sample.id"));
            CollectionAssert.AreEqual(new[] { 0.0, 4.0 }, Numbers(records[0], "sample.x"));
            CollectionAssert.AreEqual(new[] { "exploded" }, Strings(records[0], "removed.reason"));
        }

        [Test]
        public void DestroyedGameObjectIsRemovedWithItsLastPosition() {
            var tracker = new ObjectTracker();
            var go = new GameObject("tracked");
            try {
                go.transform.position = new Vector3(1, 2, 3);
                tracker.Register("crate", go.transform, null, null);
                Check(tracker, 0);
                go.transform.position = new Vector3(1.1f, 2, 3);
                Check(tracker, 0.25);
            }
            finally {
                Object.DestroyImmediate(go);
            }

            var records = Check(tracker, 0.5);
            CollectionAssert.AreEqual(new[] { "destroyed" }, Strings(records[0], "removed.reason"));
            CollectionAssert.AreEqual(new[] { 1.1 }, Numbers(records[0], "sample.x"));
            Assert.AreEqual(0, tracker.LiveCount);
        }

        [Test]
        public void PositionSourceReturningNothingRemovesTheObjectAsGone() {
            var tracker = new ObjectTracker();
            var pickup = new Point(new Vector3(2, 0, 2));
            Register(tracker, "pickup", pickup);
            Check(tracker, 0);

            pickup.Position = null;
            var records = Check(tracker, 0.25);
            CollectionAssert.AreEqual(new[] { "pickup#1" }, Strings(records[0], "removed.id"));
            CollectionAssert.AreEqual(new[] { "gone" }, Strings(records[0], "removed.reason"));
            Assert.AreEqual(0, tracker.LiveCount);
        }

        [Test]
        public void OnlyChangedStateValuesAreSent() {
            var tracker = new ObjectTracker();
            var entry = Register(tracker, "guard", new Point(Vector3.zero));
            tracker.SetState(entry, "behavior", "patrol");
            tracker.SetState(entry, "alert", 0L);

            var first = Check(tracker, 0)[0];
            CollectionAssert.AreEqual(new[] { "guard#1", "guard#1" }, Strings(first, "state.id"));
            CollectionAssert.AreEqual(new[] { "behavior", "alert" }, Strings(first, "state.key"));
            CollectionAssert.AreEqual(new object[] { "patrol", 0L }, Values(first, "state.value"));

            tracker.SetState(entry, "behavior", "patrol");
            Assert.IsEmpty(Check(tracker, 0.25), "Setting the same value again sends nothing");

            tracker.SetState(entry, "behavior", "searching");
            var change = Check(tracker, 0.5)[0];
            Assert.IsFalse(change.ContainsKey("sample.id"), "A state change of a still object needs no sample");
            CollectionAssert.AreEqual(new[] { "behavior" }, Strings(change, "state.key"));
            CollectionAssert.AreEqual(new object[] { "searching" }, Values(change, "state.value"));
        }

        [Test]
        public void KeyframeRepeatsEveryLiveObjectWithFullStateEveryFiveMinutes() {
            var tracker = new ObjectTracker();
            var guard = Register(tracker, "guard", new Point(Vector3.zero));
            Register(tracker, "crate", new Point(Vector3.one));
            tracker.SetState(guard, "behavior", "patrol");
            Check(tracker, 10, out var first);
            Assert.IsFalse(first);

            Assert.IsEmpty(Check(tracker, 309.9));
            var records = Check(tracker, 310, out var keyframe);
            Assert.IsTrue(keyframe);
            CollectionAssert.AreEqual(new[] { "guard#1", "crate#1" }, Strings(records[0], "sample.id"));
            CollectionAssert.AreEqual(new[] { "behavior" }, Strings(records[0], "state.key"));
            Assert.IsEmpty(Check(tracker, 310.25));
        }

        [Test]
        public void RequestedKeyframeRepeatsEveryLiveObject() {
            var tracker = new ObjectTracker();
            Register(tracker, "crate", new Point(Vector3.one));
            Check(tracker, 0);

            tracker.RequestKeyframe();
            var records = Check(tracker, 0.25, out var keyframe);
            Assert.IsTrue(keyframe);
            CollectionAssert.AreEqual(new[] { "crate#1" }, Strings(records[0], "sample.id"));
        }

        [Test]
        public void LargeCheckIsSplitIntoRecordsOfAtMostThreeHundredSamples() {
            var tracker = new ObjectTracker { MaxLiveObjects = 400 };
            for (var i = 0; i < 301; i++) Register(tracker, "crate", new Point(new Vector3(i, 0, 0)));

            var records = Check(tracker, 0);

            Assert.AreEqual(2, records.Count);
            Assert.AreEqual(300, Strings(records[0], "sample.id").Length);
            Assert.AreEqual(1, Strings(records[1], "sample.id").Length);
            Assert.AreEqual("crate#301", Strings(records[1], "sample.id")[0]);
        }

        [Test]
        public void SplitKeepsAnObjectsStateWithItsSample() {
            var tracker = new ObjectTracker { MaxLiveObjects = 400 };
            ObjectTracker.Entry last = null;
            for (var i = 0; i < 301; i++) last = Register(tracker, "guard", new Point(new Vector3(i, 0, 0)));
            tracker.SetState(last, "behavior", "patrol");

            var records = Check(tracker, 0);
            Assert.IsFalse(records[0].ContainsKey("state.id"));
            CollectionAssert.AreEqual(new[] { last.Id }, Strings(records[1], "state.id"));
        }

        [Test]
        public void RecordsStayUnderTheCollectorCapWithMultiByteState() {
            var tracker = new ObjectTracker();
            var label = new string('\u8B66', 100);
            for (var i = 0; i < ObjectTracker.DefaultMaxLiveObjects; i++) {
                var guard = Register(tracker, "guard", new Point(new Vector3(i, 0, i)));
                tracker.SetState(guard, "label", label + i);
            }

            var records = Check(tracker, 0);
            Assert.Greater(records.Count, 1, "Multi-byte state must split the check into several records");
            var samples = 0;
            foreach (var record in records) {
                samples += Strings(record, "sample.id").Length;
                var ev = new TelemetryEvent("$tf_objects", record, ContextSnapshot.Create(default), "d", null, 0, "play", seq: 1, kind: RecordKind.Objects);
                var bytes = new JsonPayloadWriter().BuildBatch(new[] { ev }, "wk", "unused",
                    JsonPayloadWriter.CollectorBodyByteCap, JsonPayloadWriter.CollectorEventByteCap, null, out _, out var written);
                Assert.AreEqual(1, written, $"A record of {bytes.Length} bytes was dropped as oversized");
            }
            Assert.AreEqual(ObjectTracker.DefaultMaxLiveObjects, samples);
        }

        [Test]
        public void GeneratedIdsSkipIdsAlreadyInUse() {
            var tracker = new ObjectTracker();
            var explicitGuard = Register(tracker, "guard", new Point(Vector3.zero), id: "guard#1");
            var generated = Register(tracker, "guard", new Point(Vector3.one));

            Assert.AreEqual("guard#2", generated.Id);
            Assert.IsTrue(explicitGuard.Live);
            Assert.AreEqual(2, tracker.LiveCount);
            CollectionAssert.AreEqual(new[] { "guard#1", "guard#2" }, Strings(Check(tracker, 0)[0], "sample.id"));

            var removed = Register(tracker, "guard", new Point(Vector3.zero), id: "guard#3");
            tracker.Remove(removed, "knocked_out");
            Assert.AreEqual("guard#4", Register(tracker, "guard", new Point(Vector3.zero)).Id, "A pending removal still holds its id");
        }

        [Test]
        public void RegistrationsBeyondTheCapAreIgnoredWithOneWarning() {
            var tracker = new ObjectTracker();
            for (var i = 0; i < ObjectTracker.DefaultMaxLiveObjects; i++) {
                Assert.IsNotNull(Register(tracker, "crate", new Point(Vector3.zero)));
            }

            LogAssert.Expect(LogType.Warning, new Regex("Further TrackObject calls are ignored"));
            Assert.IsNull(Register(tracker, "crate", new Point(Vector3.zero)));
            Assert.IsNull(Register(tracker, "crate", new Point(Vector3.zero)));
            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(ObjectTracker.DefaultMaxLiveObjects, tracker.LiveCount);
        }

        [Test]
        public void ReRegisteringARemovedIdStartsANewPathInALaterRecord() {
            var tracker = new ObjectTracker();
            var entry = Register(tracker, "guard", new Point(Vector3.zero), id: "guard_a");
            Check(tracker, 0);

            tracker.Remove(entry, "knocked_out");
            Register(tracker, "guard", new Point(new Vector3(9, 0, 0)), id: "guard_a");

            var removal = Check(tracker, 0.25)[0];
            CollectionAssert.AreEqual(new[] { "knocked_out" }, Strings(removal, "removed.reason"));
            CollectionAssert.AreEqual(new[] { 0.0 }, Numbers(removal, "sample.x"), "Only the old path's final sample");

            var next = Check(tracker, 0.5)[0];
            CollectionAssert.AreEqual(new[] { 9.0 }, Numbers(next, "sample.x"));
            Assert.IsFalse(next.ContainsKey("removed.id"));
        }

        [Test]
        public void PositionsAreRoundedToOneCentimetre() {
            var tracker = new ObjectTracker();
            Register(tracker, "crate", new Point(new Vector3(12.4149f, 0.004f, -3.336f)));

            var record = Check(tracker, 0)[0];
            CollectionAssert.AreEqual(new[] { 12.41 }, Numbers(record, "sample.x"));
            CollectionAssert.AreEqual(new[] { 0.0 }, Numbers(record, "sample.y"));
            CollectionAssert.AreEqual(new[] { -3.34 }, Numbers(record, "sample.z"));
        }
    }
}
