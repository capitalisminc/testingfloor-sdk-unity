using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TestingFloor.Internal {
    internal sealed class CollectorClient {
        readonly TestingFloorSettings _settings;
        readonly JsonPayloadWriter _writer = new();
        bool _invalidWriteKey;
        bool _loggedInvalidWriteKey;

        public CollectorClient(TestingFloorSettings settings) {
            _settings = settings;
        }

        public async ValueTask<SendResult> TrackEventAsync(TelemetryEvent telemetryEvent) {
            if (_invalidWriteKey) return SendResult.FatalConfiguration;
            if (_settings == null || !_settings.IsEnabledForBuild) return SendResult.FatalConfiguration;
            if (string.IsNullOrWhiteSpace(_settings.writeKey)) return SendResult.FatalConfiguration;
            if (string.IsNullOrWhiteSpace(_settings.endpoint)) return SendResult.FatalConfiguration;
            if (string.IsNullOrWhiteSpace(telemetryEvent.EventType)) return SendResult.FatalConfiguration;

            var sessionId = TestingFloorSession.EffectiveSessionId;
            var bytes = _writer.Build(telemetryEvent, _settings.writeKey, sessionId).ToArray();

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
                return SendResult.TransientFailure;
            }

            if (request.result == UnityWebRequest.Result.Success) {
                if (_settings.logEventSends) {
                    Debug.Log($"[TestingFloor] Sent event {telemetryEvent.EventType} ({request.responseCode}).");
                }
                return SendResult.Success;
            }

            if (request.responseCode == 401) {
                _invalidWriteKey = true;
                if (!_loggedInvalidWriteKey) {
                    _loggedInvalidWriteKey = true;
                    Debug.LogError("[TestingFloor] Write key rejected (401). Collector sends are now disabled for this session.");
                }
                return SendResult.FatalConfiguration;
            }

            if (_settings.logErrors) {
                var responseText = request.downloadHandler?.text;
                var suffix = string.IsNullOrWhiteSpace(responseText) ? string.Empty : $" {responseText}";
                Debug.LogWarning($"[TestingFloor] Collector request failed: {request.result} {request.error} ({request.responseCode}).{suffix}");
            }

            return SendResult.TransientFailure;
        }

        string BuildEndpoint() {
            return $"{_settings.endpoint.TrimEnd('/')}/v1/batch";
        }
    }
}
