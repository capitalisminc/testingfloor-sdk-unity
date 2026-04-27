using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TestingFloor.Internal {
    internal readonly struct BatchSendOutcome {
        public readonly SendResult Result;
        public readonly int Consumed;

        public BatchSendOutcome(SendResult result, int consumed) {
            Result = result;
            Consumed = consumed;
        }
    }

    internal sealed class CollectorClient {
        readonly TestingFloorSettings _settings;
        readonly JsonPayloadWriter _writer = new();
        bool _invalidWriteKey;
        bool _loggedInvalidWriteKey;

        public CollectorClient(TestingFloorSettings settings) {
            _settings = settings;
        }

        public async ValueTask<BatchSendOutcome> TrackBatchAsync(IReadOnlyList<TelemetryEvent> events) {
            if (events == null || events.Count == 0) return new BatchSendOutcome(SendResult.Success, 0);
            if (_invalidWriteKey) return new BatchSendOutcome(SendResult.FatalConfiguration, events.Count);
            if (_settings == null || !_settings.IsEnabledForBuild) return new BatchSendOutcome(SendResult.FatalConfiguration, events.Count);
            if (string.IsNullOrWhiteSpace(_settings.writeKey)) return new BatchSendOutcome(SendResult.FatalConfiguration, events.Count);
            if (string.IsNullOrWhiteSpace(_settings.endpoint)) return new BatchSendOutcome(SendResult.FatalConfiguration, events.Count);

            var sessionId = TestingFloorSession.EffectiveSessionId;
            // Reserve room for the envelope and gzip headroom inside the collector's body cap.
            var maxBodyBytes = JsonPayloadWriter.CollectorBodyByteCap - JsonPayloadWriter.BodySafetyMargin;
            var span = _writer.BuildBatch(
                events,
                _settings.writeKey,
                sessionId,
                maxBodyBytes,
                JsonPayloadWriter.CollectorEventByteCap,
                _settings,
                out var consumed,
                out var written);

            if (written == 0) {
                // Every candidate event was oversized and skipped. Tell the caller to dequeue
                // them but treat the (non-)send as a Success — there's nothing to retry.
                return new BatchSendOutcome(SendResult.Success, consumed);
            }

            var bytes = span.ToArray();

            using var request = new UnityWebRequest(BuildEndpoint(), UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            try {
                await WebRequestAwaiter.SendAsync(request);
            }
            catch (Exception ex) {
                if (_settings.logErrors) {
                    Debug.LogWarning($"[TestingFloor] Collector request failed: {ex.Message}");
                }
                return new BatchSendOutcome(SendResult.TransientFailure, consumed);
            }

            if (request.result == UnityWebRequest.Result.Success) {
                if (_settings.logEventSends) {
                    Debug.Log($"[TestingFloor] Sent batch of {written} event(s) ({request.responseCode}).");
                }
                return new BatchSendOutcome(SendResult.Success, consumed);
            }

            if (request.responseCode == 401) {
                _invalidWriteKey = true;
                if (!_loggedInvalidWriteKey) {
                    _loggedInvalidWriteKey = true;
                    Debug.LogError("[TestingFloor] Write key rejected (401). Collector sends are now disabled for this session.");
                }
                return new BatchSendOutcome(SendResult.FatalConfiguration, consumed);
            }

            if (_settings.logErrors) {
                var responseText = request.downloadHandler?.text;
                var suffix = string.IsNullOrWhiteSpace(responseText) ? string.Empty : $" {responseText}";
                Debug.LogWarning($"[TestingFloor] Collector request failed: {request.result} {request.error} ({request.responseCode}).{suffix}");
            }

            return new BatchSendOutcome(SendResult.TransientFailure, consumed);
        }

        string BuildEndpoint() {
            return $"{_settings.endpoint.TrimEnd('/')}/v1/batch";
        }
    }
}
