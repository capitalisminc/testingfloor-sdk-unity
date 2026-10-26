using System.Collections.Generic;
using NUnit.Framework;
using TestingFloorSdk.Internal;

namespace TestingFloorSdk.Tests {
    public class SessionStateTrackerTests {
        static Dictionary<string, object> State(params (string Key, object Value)[] values) {
            var state = TelemetryQueue.RentProperties();
            foreach (var (key, value) in values) state[key] = value;
            return state;
        }

        [Test]
        public void FirstRecordIsAKeyframeWithEveryValue() {
            var tracker = new SessionStateTracker();
            var record = tracker.Commit(0, State(("pref.vsync", true), ("app.commit", "abc123")), null, out var keyframe);

            Assert.IsTrue(keyframe);
            Assert.AreEqual(2, record.Count);
            Assert.AreEqual(true, record["pref.vsync"]);
            Assert.AreEqual("abc123", record["app.commit"]);
        }

        [Test]
        public void EmptySessionStartWaitsForTheFirstValue() {
            var tracker = new SessionStateTracker();
            Assert.IsNull(tracker.Commit(0, null, null, out var keyframe));
            Assert.IsFalse(keyframe);

            var record = tracker.Commit(1, State(("pref.vsync", true)), null, out keyframe);
            Assert.IsTrue(keyframe, "The first record that carries state is the session's keyframe");
            Assert.AreEqual(1, record.Count);
        }

        [Test]
        public void LaterRecordsHoldOnlyTheChangedKeys() {
            var tracker = new SessionStateTracker();
            tracker.Commit(0, State(("pref.quality", "high"), ("pref.vsync", true)), null, out _);

            Assert.IsNull(tracker.Commit(1, State(("pref.quality", "high"), ("pref.vsync", true)), null, out _), "Unchanged state sends nothing");

            var record = tracker.Commit(2, State(("pref.quality", "low"), ("pref.vsync", true)), null, out var keyframe);
            Assert.IsFalse(keyframe);
            CollectionAssert.AreEquivalent(new[] { "pref.quality" }, record.Keys);
            Assert.AreEqual("low", record["pref.quality"]);
        }

        [Test]
        public void RemovedKeyIsSentAsNull() {
            var tracker = new SessionStateTracker();
            tracker.Commit(0, State(("player.outfit.id", "guard_uniform"), ("pref.vsync", true)), null, out _);

            var record = tracker.Commit(1, State(("pref.vsync", true)), null, out _);
            CollectionAssert.AreEquivalent(new[] { "player.outfit.id" }, record.Keys);
            Assert.IsNull(record["player.outfit.id"]);
        }

        [Test]
        public void ArraysCompareByContent() {
            var tracker = new SessionStateTracker();
            tracker.Commit(0, State(("inventory.items", new[] { "key_red", "wrench" })), null, out _);

            Assert.IsNull(tracker.Commit(1, State(("inventory.items", new[] { "key_red", "wrench" })), null, out _));
            var record = tracker.Commit(2, State(("inventory.items", new[] { "key_red" })), null, out _);
            CollectionAssert.AreEqual(new[] { "key_red" }, (string[])record["inventory.items"]);
        }

        [Test]
        public void KeyframeRepeatsEveryValueEveryFiveMinutes() {
            var tracker = new SessionStateTracker();
            tracker.Commit(10, State(("pref.quality", "high"), ("pref.vsync", true)), null, out _);

            Assert.IsNull(tracker.Commit(309.9, State(("pref.quality", "high"), ("pref.vsync", true)), null, out _));
            var record = tracker.Commit(310, State(("pref.quality", "high"), ("pref.vsync", true)), null, out var keyframe);
            Assert.IsTrue(keyframe);
            Assert.AreEqual(2, record.Count);

            Assert.IsNull(tracker.Commit(311, State(("pref.quality", "high"), ("pref.vsync", true)), null, out _), "The interval restarts at each keyframe");
        }

        [Test]
        public void RequestedKeyframeRepeatsEveryValue() {
            var tracker = new SessionStateTracker();
            tracker.Commit(0, State(("pref.quality", "high"), ("pref.vsync", true)), null, out _);

            tracker.RequestKeyframe();
            var record = tracker.Commit(1, State(("pref.quality", "high"), ("pref.vsync", true)), null, out var keyframe);
            Assert.IsTrue(keyframe);
            Assert.AreEqual(2, record.Count);
        }

        [Test]
        public void KeyframeAfterARemovalOmitsTheRemovedKey() {
            var tracker = new SessionStateTracker();
            tracker.Commit(0, State(("pref.quality", "high"), ("pref.vsync", true)), null, out _);

            tracker.RequestKeyframe();
            var record = tracker.Commit(1, State(("pref.quality", "high")), null, out var keyframe);
            Assert.IsTrue(keyframe);
            CollectionAssert.AreEquivalent(new[] { "pref.quality" }, record.Keys, "A keyframe replaces all state, so it needs no null");
        }

        [Test]
        public void RecordingStartTriggersAKeyframe() {
            var tracker = new SessionStateTracker();
            tracker.Commit(0, State(("pref.quality", "high")), null, out _);

            var record = tracker.Commit(1, State(("pref.quality", "high")), "example-recording", out var keyframe);
            Assert.IsTrue(keyframe);
            Assert.AreEqual("high", record["pref.quality"]);
        }
    }
}
