using System;
using System.Collections.Generic;
using UnityEngine;

namespace TestingFloor.Internal {
    internal sealed class JsonPayloadWriter {
        readonly TelemetryJsonWriter _writer = new();

        public ReadOnlySpan<byte> Build(TelemetryEvent telemetryEvent, string writeKey, string fallbackSessionId) {
            _writer.Reset();
            WritePayload(_writer, telemetryEvent, writeKey, fallbackSessionId);
            return _writer.WrittenSpan;
        }

        static void WritePayload(TelemetryJsonWriter writer, TelemetryEvent ev, string writeKey, string fallbackSessionId) {
            writer.WriteStartObject();
            writer.WriteString("$write_key", writeKey);
            writer.WriteString("$sent_at", DateTimeOffset.UtcNow);

            writer.WritePropertyName("events");
            writer.WriteStartArray();
            writer.WriteStartObject();

            writer.WriteString("$id", Guid.NewGuid());
            writer.WriteString("$name", ev.EventType);
            writer.WriteString("$time", DateTimeOffset.FromUnixTimeMilliseconds(ev.TimestampMs));
            writer.WriteNumber("$schema", 1);

            if (!string.IsNullOrWhiteSpace(ev.UserId)) {
                writer.WriteString("$user_id", ev.UserId);
            }
            if (!string.IsNullOrWhiteSpace(ev.DeviceId)) {
                writer.WriteString("$device_id", ev.DeviceId);
            }

            var effectiveSessionId = !string.IsNullOrWhiteSpace(ev.SessionId) ? ev.SessionId : fallbackSessionId;
            writer.WriteString("$session_id", effectiveSessionId);

            WriteTestingFloorContext(writer, ev.Context, effectiveSessionId);
            WriteMergedProperties(writer, ev.Context.Properties, ev.EventProperties);

            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        static void WriteTestingFloorContext(TelemetryJsonWriter writer, ContextSnapshot context, string effectiveSessionId) {
            var p = context.Platform;
            writer.WritePropertyName("$tf");
            writer.WriteStartObject();
            WriteString(writer, "sdk_version", p.SdkVersion);
            WriteString(writer, "app_version", p.AppVersion);
            WriteString(writer, "os", p.Os);
            WriteString(writer, "device_model", p.DeviceModel);
            if (!string.IsNullOrWhiteSpace(p.UnityVersion)) {
                writer.WriteString("engine", $"Unity {p.UnityVersion}");
            }
            if (p.ScreenWidth > 0) writer.WriteNumber("screen_width", p.ScreenWidth);
            if (p.ScreenHeight > 0) writer.WriteNumber("screen_height", p.ScreenHeight);
            WriteString(writer, "locale", p.Locale);

            WriteString(writer, "session_id", effectiveSessionId);
            var session = TestingFloorSession.Current;
            if (session != null) {
                if (session.PlaytestId.HasValue) {
                    writer.WriteNumber("playtest_id", session.PlaytestId.Value);
                }
            }
            writer.WriteEndObject();
        }

        static void WriteMergedProperties(
            TelemetryJsonWriter writer,
            Dictionary<string, object> contextProperties,
            Dictionary<string, object> eventProperties) {
            if ((contextProperties == null || contextProperties.Count == 0)
                && (eventProperties == null || eventProperties.Count == 0)) {
                return;
            }

            if (contextProperties != null) {
                foreach (var kvp in contextProperties) {
                    if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                    if (eventProperties != null && eventProperties.ContainsKey(kvp.Key)) continue;
                    ContextSnapshot.WriteObjectValue(writer, kvp.Key, kvp.Value);
                }
            }

            if (eventProperties != null) {
                foreach (var kvp in eventProperties) {
                    if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                    ContextSnapshot.WriteObjectValue(writer, kvp.Key, kvp.Value);
                }
            }
        }

        static void WriteString(TelemetryJsonWriter writer, string key, string value) {
            if (string.IsNullOrWhiteSpace(value)) return;
            writer.WriteString(key, value);
        }
    }
}
