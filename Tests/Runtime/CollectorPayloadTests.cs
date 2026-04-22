using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using TestingFloor.Internal;
using UnityEngine;

namespace TestingFloor.Tests {
    public class CollectorPayloadTests {
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

            var bytes = writer.Build(ev, "wk_test", fallbackSessionId: "fallback").ToArray();
            var json = Encoding.UTF8.GetString(bytes);

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
            var platform = GetPlatformContextForTesting();
            var snapshot = ContextSnapshot.Create(platform);
            snapshot.Set("level.id", "from_context");

            var eventProps = new Dictionary<string, object> { ["level.id"] = "from_event" };
            var ev = new TelemetryEvent("t", eventProps, snapshot, "d", "u", 0, "s");
            var bytes = writer.Build(ev, "wk", "fallback").ToArray();
            var json = Encoding.UTF8.GetString(bytes);

            StringAssert.Contains("\"level.id\":\"from_event\"", json);
            StringAssert.DoesNotContain("\"level.id\":\"from_context\"", json);
        }

        [Test]
        public void FallbackSessionIdUsedWhenEventHasNone() {
            var writer = new JsonPayloadWriter();
            var snapshot = ContextSnapshot.Create(GetPlatformContextForTesting());
            var ev = new TelemetryEvent("t", null, snapshot, "d", "u", 0, null);
            var bytes = writer.Build(ev, "wk", "fallback-uuid").ToArray();
            var json = Encoding.UTF8.GetString(bytes);

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
            var bytes = writer.Build(ev, "wk", "fallback").ToArray();
            var json = Encoding.UTF8.GetString(bytes);

            StringAssert.Contains("\"line\":\"hello \\\"quoted\\\"\\nnext\"", json);
            StringAssert.Contains("\"tags\":[\"alpha\",\"beta\"]", json);
        }

        static PlatformContext GetPlatformContextForTesting() {
            // Can be called from non-play tests — Application.platform is still safe.
            return PlatformContext.Capture();
        }
    }
}
