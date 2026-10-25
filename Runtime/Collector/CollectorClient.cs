using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TestingFloorSdk.Internal {
    internal readonly struct BatchSendOutcome {
        public readonly SendResult Result;
        public readonly int Consumed;
        public readonly int Dropped;

        public BatchSendOutcome(SendResult result, int consumed, int dropped = 0) {
            Result = result;
            Consumed = consumed;
            Dropped = dropped;
        }
    }

    internal interface IBatchSender {
        ValueTask<BatchSendOutcome> TrackBatchAsync(IReadOnlyList<TelemetryEvent> events);
    }

    internal sealed class CollectorClient : IBatchSender {
        readonly TestingFloorSettings _settings;
        readonly JsonPayloadWriter _writer = new();
        bool _invalidWriteKey;
        bool _loggedInvalidWriteKey;

        public CollectorClient(TestingFloorSettings settings) {
            _settings = settings;
        }

        public async ValueTask<BatchSendOutcome> TrackBatchAsync(IReadOnlyList<TelemetryEvent> events) {
            if (events == null || events.Count == 0) return new BatchSendOutcome(SendResult.Success, 0);
            if (_invalidWriteKey) return new BatchSendOutcome(SendResult.Rejected, events.Count, events.Count);
            if (_settings == null || !_settings.IsEnabledForBuild) return new BatchSendOutcome(SendResult.NotConfigured, events.Count, events.Count);
            if (string.IsNullOrWhiteSpace(_settings.writeKey)) return new BatchSendOutcome(SendResult.NotConfigured, events.Count, events.Count);
            if (string.IsNullOrWhiteSpace(_settings.endpoint)) return new BatchSendOutcome(SendResult.NotConfigured, events.Count, events.Count);

            var sessionId = TestingFloor.PlaySessionId;
            // Reserve room for the envelope and gzip headroom inside the collector's body cap.
            var maxBodyBytes = JsonPayloadWriter.CollectorBodyByteCap - JsonPayloadWriter.BodySafetyMargin;
            var bytes = _writer.BuildBatch(
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
                return new BatchSendOutcome(SendResult.Success, consumed, consumed - written);
            }

            using var request = new UnityWebRequest(BuildEndpoint(), UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 10;
            request.SetRequestHeader("Content-Type", "application/json");

            try {
                await WebRequestAwaiter.SendAsync(request);
            }
            catch (Exception ex) {
                if (_settings.logErrors) {
                    Debug.LogWarning($"[TestingFloor] Collector request failed: {ex.Message}");
                }
                return new BatchSendOutcome(SendResult.TransientFailure, 0);
            }

            if (request.result == UnityWebRequest.Result.Success) {
                if (_settings.logEventSends) {
                    Debug.Log($"[TestingFloor] Sent batch of {written} event(s) ({request.responseCode}).");
                }
                return new BatchSendOutcome(SendResult.Success, consumed, consumed - written);
            }

            var result = ClassifyResponse(request.responseCode, false);
            if (result == SendResult.Rejected) {
                _invalidWriteKey = true;
                if (!_loggedInvalidWriteKey) {
                    _loggedInvalidWriteKey = true;
                    Debug.LogWarning("[TestingFloor] Write key rejected. Collector sends are disabled for this play session.");
                }
            }
            if (_settings.logErrors) {
                Debug.LogWarning($"[TestingFloor] Collector request failed (HTTP {request.responseCode}).");
            }
            return result == SendResult.TransientFailure
                ? new BatchSendOutcome(result, 0)
                : new BatchSendOutcome(result, consumed, consumed);
        }

        internal static SendResult ClassifyResponse(long status, bool succeeded = true) {
            if (status >= 200 && status < 300) return succeeded ? SendResult.Success : SendResult.TransientFailure;
            if (status == 401 || status == 403) return SendResult.Rejected;
            if (status >= 400 && status < 500 && status != 408 && status != 429) return SendResult.InvalidRequest;
            return SendResult.TransientFailure;
        }

        string BuildEndpoint() {
            return $"{_settings.endpoint.Trim().TrimEnd('/')}/v1/batch";
        }
    }
}
