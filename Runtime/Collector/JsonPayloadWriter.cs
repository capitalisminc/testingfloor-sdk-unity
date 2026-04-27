using System;
using System.Collections.Generic;
using UnityEngine;

namespace TestingFloor.Internal {
    internal sealed class JsonPayloadWriter {
        // Collector enforces these caps. Mirrored here so the SDK can short-circuit
        // before the request goes out and split a too-big batch into chunks.
        public const int CollectorBodyByteCap = 1 << 20;   // 1 MB — collector handler.go:17
        public const int CollectorEventByteCap = 32 << 10; // 32 KB — collector handler.go:19
        // Reserve room inside the body cap for the envelope's closing bytes (`]}`)
        // plus a comma per event. The exact accounting is done per-event but this
        // gives the caller a default to subtract when picking a target body size.
        public const int BodySafetyMargin = 8 << 10;       // 8 KB

        readonly TelemetryJsonWriter _main = new();
        readonly TelemetryJsonWriter _scratch = new();

        /// <summary>
        /// Serialize a batch of telemetry events into a single /v1/batch payload. Events that
        /// would push the request past <paramref name="maxBodyBytes"/> are left in the input
        /// list unconsumed (the caller should send them in the next batch). Events whose own
        /// serialized size exceeds <paramref name="maxEventBytes"/> are skipped with a warning
        /// and counted toward <paramref name="consumed"/> so the caller dequeues them.
        /// </summary>
        /// <returns>UTF-8 JSON bytes of the request body. Caller must copy if it needs to
        /// retain them past the next call to this writer.</returns>
        public ReadOnlySpan<byte> BuildBatch(
            IReadOnlyList<TelemetryEvent> events,
            string writeKey,
            string fallbackSessionId,
            int maxBodyBytes,
            int maxEventBytes,
            TestingFloorSettings settingsForLogging,
            out int consumed,
            out int written) {
            if (events == null || events.Count == 0) {
                consumed = 0;
                written = 0;
                return ReadOnlySpan<byte>.Empty;
            }

            _main.Reset();
            _main.WriteStartObject();
            _main.WriteString("$write_key", writeKey);
            _main.WriteString("$sent_at", DateTimeOffset.UtcNow);
            _main.WritePropertyName("events");
            _main.WriteStartArray();

            // Closing `]}` plus a single comma between events; small fixed overhead.
            const int ClosingFootprint = 2;

            consumed = 0;
            written = 0;
            for (var i = 0; i < events.Count; i++) {
                var ev = events[i];

                _scratch.Reset();
                try {
                    WriteEventObject(_scratch, ev, fallbackSessionId);
                }
                catch (Exception ex) {
                    // A single bad event (e.g. an unsupported property type that slipped past
                    // the typed Set overloads via the dictionary) shouldn't poison the whole
                    // batch. Drop it with a warning, count it as consumed, and keep going.
                    if (settingsForLogging != null && settingsForLogging.logErrors) {
                        Debug.LogWarning(
                            $"[TestingFloor] Dropping event '{ev.EventType}': failed to serialize: {ex.Message}");
                    }
                    consumed++;
                    continue;
                }
                var probe = _scratch.WrittenSpan;

                if (probe.Length > maxEventBytes) {
                    if (settingsForLogging != null && settingsForLogging.logErrors) {
                        Debug.LogWarning(
                            $"[TestingFloor] Dropping event '{ev.EventType}': serialized size {probe.Length} exceeds per-event cap {maxEventBytes}.");
                    }
                    consumed++;
                    continue;
                }

                var commaCost = written > 0 ? 1 : 0;
                var projected = _main.WrittenSpan.Length + commaCost + probe.Length + ClosingFootprint;
                if (written > 0 && projected > maxBodyBytes) {
                    // Leave this event (and the rest) for the next batch.
                    break;
                }

                _main.WriteRawJsonValue(probe);
                written++;
                consumed++;
            }

            _main.WriteEndArray();
            _main.WriteEndObject();
            return _main.WrittenSpan;
        }

        static void WriteEventObject(TelemetryJsonWriter writer, TelemetryEvent ev, string fallbackSessionId) {
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
