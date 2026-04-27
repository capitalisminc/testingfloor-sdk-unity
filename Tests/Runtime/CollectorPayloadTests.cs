using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using TestingFloor.Internal;
using UnityEngine;

namespace TestingFloor.Tests {
    public class CollectorPayloadTests {
        const int BodyCap = JsonPayloadWriter.CollectorBodyByteCap;
        const int EventCap = JsonPayloadWriter.CollectorEventByteCap;

        static string BuildSingle(JsonPayloadWriter writer, TelemetryEvent ev, string writeKey, string fallbackSessionId) {
            var bytes = writer.BuildBatch(
                new[] { ev },
                writeKey,
                fallbackSessionId,
                BodyCap,
                EventCap,
                settingsForLogging: null,
                out _,
                out _);
            return Encoding.UTF8.GetString(bytes);
        }

        [Test]
        public void PayloadContainsWriteKeyAndEventShape() {
            var writer = new JsonPayloadWriter();
            var props = new Dictionary<string, object> { ["weapon.id"] = "plasma", ["damage"] = 42L };
            var platform = GetPlatformContextForTesting();
            var snapshot = ContextSnapshot.Create(platform);

            var ev = new TelemetryEvent(
                eventType: "weapon_fire",
                eventProperties: props,
                context: snapshot,
                deviceId: "device123",
                userId: "user456",
                timestampMs: 1711234567890,
                sessionId: "session-abc"
            );

            var json = BuildSingle(writer, ev, "wk_test", fallbackSessionId: "fallback");

            StringAssert.Contains("\"$write_key\":\"wk_test\"", json);
            StringAssert.Contains("\"$name\":\"weapon_fire\"", json);
            StringAssert.Contains("\"$device_id\":\"device123\"", json);
            StringAssert.Contains("\"$user_id\":\"user456\"", json);
            StringAssert.Contains("\"$session_id\":\"session-abc\"", json);
            StringAssert.Contains("\"$schema\":1", json);
            StringAssert.Contains("\"weapon.id\":\"plasma\"", json);
            StringAssert.Contains("\"damage\":42", json);
            StringAssert.Contains("\"$tf\":{", json);
        }

        [Test]
        public void EventPropertyWinsOverContextOnCollision() {
            var writer = new JsonPayloadWriter();
            var snapshot = ContextSnapshot.Create(GetPlatformContextForTesting());
            snapshot.Set("level.id", "from_context");

            var eventProps = new Dictionary<string, object> { ["level.id"] = "from_event" };
            var ev = new TelemetryEvent("t", eventProps, snapshot, "d", "u", 0, "s");
            var json = BuildSingle(writer, ev, "wk", "fallback");

            StringAssert.Contains("\"level.id\":\"from_event\"", json);
            StringAssert.DoesNotContain("\"level.id\":\"from_context\"", json);
        }

        [Test]
        public void FallbackSessionIdUsedWhenEventHasNone() {
            var writer = new JsonPayloadWriter();
            var snapshot = ContextSnapshot.Create(GetPlatformContextForTesting());
            var ev = new TelemetryEvent("t", null, snapshot, "d", "u", 0, null);
            var json = BuildSingle(writer, ev, "wk", "fallback-uuid");

            StringAssert.Contains("\"$session_id\":\"fallback-uuid\"", json);
            StringAssert.Contains("\"session_id\":\"fallback-uuid\"", json);
        }

        [Test]
        public void PayloadEscapesStringsAndWritesArrays() {
            var writer = new JsonPayloadWriter();
            var snapshot = ContextSnapshot.Create(GetPlatformContextForTesting());
            var props = new Dictionary<string, object> {
                ["line"] = "hello \"quoted\"\nnext",
                ["tags"] = new[] { "alpha", "beta" },
            };
            var ev = new TelemetryEvent("t", props, snapshot, "d", "u", 0, "s");
            var json = BuildSingle(writer, ev, "wk", "fallback");

            StringAssert.Contains("\"line\":\"hello \\\"quoted\\\"\\nnext\"", json);
            StringAssert.Contains("\"tags\":[\"alpha\",\"beta\"]", json);
        }

        [Test]
        public void NumericAndBoolPropertiesRenderAsJsonTokensNotStrings() {
            var writer = new JsonPayloadWriter();
            var snapshot = ContextSnapshot.Create(GetPlatformContextForTesting());
            var props = new Dictionary<string, object> {
                ["i"] = 7,
                ["l"] = 9000000000L,
                ["f"] = 1.5f,
                ["d"] = 2.25,
                ["b_true"] = true,
                ["b_false"] = false,
            };
            var ev = new TelemetryEvent("t", props, snapshot, "d", "u", 0, "s");
            var json = BuildSingle(writer, ev, "wk", "fallback");

            StringAssert.Contains("\"i\":7", json);
            StringAssert.Contains("\"l\":9000000000", json);
            StringAssert.Contains("\"f\":1.5", json);
            StringAssert.Contains("\"d\":2.25", json);
            StringAssert.Contains("\"b_true\":true", json);
            StringAssert.Contains("\"b_false\":false", json);
            StringAssert.DoesNotContain("\"i\":\"7\"", json);
            StringAssert.DoesNotContain("\"b_true\":\"true\"", json);
        }

        [Test]
        public void GuidPropertyRendersAsString() {
            var writer = new JsonPayloadWriter();
            var snapshot = ContextSnapshot.Create(GetPlatformContextForTesting());
            var id = System.Guid.Parse("11111111-2222-3333-4444-555555555555");
            var props = new Dictionary<string, object> { ["entity.id"] = id };
            var ev = new TelemetryEvent("t", props, snapshot, "d", "u", 0, "s");
            var json = BuildSingle(writer, ev, "wk", "fallback");

            StringAssert.Contains("\"entity.id\":\"11111111-2222-3333-4444-555555555555\"", json);
        }

        [Test]
        public void UnsupportedPropertyTypeIsDroppedWithoutPoisoningTheBatch() {
            // The typed Set overloads on EventBuilder/ContextSnapshot prevent unsupported
            // types at the call site, but a developer who pokes the dictionary directly
            // (or mutates it from a context provider) could still slip something through.
            // The writer should drop just that event instead of failing the whole batch.
            var writer = new JsonPayloadWriter();
            var snapshot = ContextSnapshot.Create(GetPlatformContextForTesting());
            var bad = new TelemetryEvent(
                "bad",
                new Dictionary<string, object> { ["mood"] = System.DayOfWeek.Friday },
                snapshot, "d", "u", 1000, "s");
            var good = new TelemetryEvent(
                "good",
                new Dictionary<string, object> { ["k"] = "v" },
                snapshot, "d", "u", 1001, "s");

            var bytes = writer.BuildBatch(new[] { bad, good }, "wk", "fallback", BodyCap, EventCap, null, out var consumed, out var written);
            var json = Encoding.UTF8.GetString(bytes);

            Assert.AreEqual(2, consumed, "both events must be dequeued — bad as a drop, good as a send");
            Assert.AreEqual(1, written, "only the good event should land in the events array");
            StringAssert.DoesNotContain("\"$name\":\"bad\"", json);
            StringAssert.Contains("\"$name\":\"good\"", json);
        }

        [Test]
        public void BatchWritesAllEventsInOrderInsideEventsArray() {
            var writer = new JsonPayloadWriter();
            var snapshot = ContextSnapshot.Create(GetPlatformContextForTesting());
            var events = new TelemetryEvent[] {
                new("first", new Dictionary<string, object> { ["seq"] = 1L }, snapshot, "d", "u", 1000, "s"),
                new("second", new Dictionary<string, object> { ["seq"] = 2L }, snapshot, "d", "u", 1001, "s"),
                new("third", new Dictionary<string, object> { ["seq"] = 3L }, snapshot, "d", "u", 1002, "s"),
            };

            var bytes = writer.BuildBatch(events, "wk", "fallback", BodyCap, EventCap, null, out var consumed, out var written);
            var json = Encoding.UTF8.GetString(bytes);

            Assert.AreEqual(3, consumed);
            Assert.AreEqual(3, written);
            // Single $write_key, single events array, all three names present in source order.
            StringAssert.Contains("\"events\":[", json);
            var firstIdx = json.IndexOf("\"$name\":\"first\"", System.StringComparison.Ordinal);
            var secondIdx = json.IndexOf("\"$name\":\"second\"", System.StringComparison.Ordinal);
            var thirdIdx = json.IndexOf("\"$name\":\"third\"", System.StringComparison.Ordinal);
            Assert.Greater(firstIdx, 0);
            Assert.Greater(secondIdx, firstIdx);
            Assert.Greater(thirdIdx, secondIdx);
        }

        [Test]
        public void EmptyBatchReturnsEmptyArrayAndZeroCounts() {
            var writer = new JsonPayloadWriter();
            var bytes = writer.BuildBatch(System.Array.Empty<TelemetryEvent>(), "wk", "fallback", BodyCap, EventCap, null, out var consumed, out var written);

            Assert.AreEqual(0, consumed);
            Assert.AreEqual(0, written);
            Assert.AreEqual(0, bytes.Length);
        }

        [Test]
        public void OversizedEventIsSkippedButCountedAsConsumed() {
            var writer = new JsonPayloadWriter();
            var snapshot = ContextSnapshot.Create(GetPlatformContextForTesting());
            // Build a giant string property — well over the 32 KB per-event cap.
            var giant = new string('x', EventCap + 1024);
            var bigProps = new Dictionary<string, object> { ["payload"] = giant };
            var smallProps = new Dictionary<string, object> { ["k"] = "v" };

            var events = new TelemetryEvent[] {
                new("too_big", bigProps, snapshot, "d", "u", 1000, "s"),
                new("ok", smallProps, snapshot, "d", "u", 1001, "s"),
            };

            var bytes = writer.BuildBatch(events, "wk", "fallback", BodyCap, EventCap, null, out var consumed, out var written);
            var json = Encoding.UTF8.GetString(bytes);

            Assert.AreEqual(2, consumed, "both events must be marked consumed so the queue advances");
            Assert.AreEqual(1, written, "only the small event should land in the array");
            StringAssert.DoesNotContain("\"too_big\"", json);
            StringAssert.Contains("\"$name\":\"ok\"", json);
        }

        [Test]
        public void BodyCapStopsBatchEarlyAndLeavesOverflowUnconsumed() {
            var writer = new JsonPayloadWriter();
            var snapshot = ContextSnapshot.Create(GetPlatformContextForTesting());
            // Each event carries a ~4 KB payload; with a 12 KB body cap only ~2 fit.
            var blob = new string('y', 4000);

            var events = new TelemetryEvent[] {
                new("a", new Dictionary<string, object> { ["payload"] = blob }, snapshot, "d", "u", 1000, "s"),
                new("b", new Dictionary<string, object> { ["payload"] = blob }, snapshot, "d", "u", 1001, "s"),
                new("c", new Dictionary<string, object> { ["payload"] = blob }, snapshot, "d", "u", 1002, "s"),
                new("d", new Dictionary<string, object> { ["payload"] = blob }, snapshot, "d", "u", 1003, "s"),
            };

            const int tinyBody = 12 * 1024;
            var bytes = writer.BuildBatch(events, "wk", "fallback", tinyBody, EventCap, null, out var consumed, out var written);
            var json = Encoding.UTF8.GetString(bytes);

            Assert.Greater(written, 0, "first event must always go through (matches existing one-per-request behavior)");
            Assert.Less(written, events.Length, "body cap must stop the batch before all events fit");
            Assert.AreEqual(written, consumed, "body-cap drops do not skip events; they are deferred for the next batch");
            Assert.LessOrEqual(json.Length, tinyBody, "actual payload stays under the body cap");
        }

        static PlatformContext GetPlatformContextForTesting() {
            return PlatformContext.Capture();
        }
    }
}
