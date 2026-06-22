using System;
using System.IO;
using UnityEngine;

namespace TestingFloor {
    /// <summary>
    /// The recording context handed off by the Testing Floor desktop recorder.
    /// One recording can contain many Play sessions — Editor stop+restart, domain
    /// reload, scene reload — and every event emitted while a recording is
    /// active should carry the same <see cref="RecordingUuid"/>. The SDK's
    /// per-Play session id is a separate concept (see
    /// <c>TestingFloor.PlaySessionId</c>) and rotates per Play boot.
    /// </summary>
    public sealed class TestingFloorRecording {
        public string RecordingUuid { get; }
        public string SessionId { get; }
        public long? PlaytestId { get; }
        public bool SuppressQr { get; }

        internal TestingFloorRecording(string recordingUuid, string sessionId, long? playtestId, bool suppressQr) {
            RecordingUuid = recordingUuid;
            SessionId = sessionId;
            PlaytestId = playtestId;
            SuppressQr = suppressQr;
        }

        const string CliPrefix = "--testing-floor=";
        const string SessionPayloadFileName = "session-payload.json";
        const string RecordingPayloadFileName = "recording-payload.json";
        const int MaxPayloadAgeHours = 12;

        static bool _resolved;
        static TestingFloorRecording _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            // Re-resolve on every Play start (and editor domain reload). The
            // recorder leaves the sidecar payload in place for the lifetime of
            // the recording, so re-reading is what lets a single recording
            // span multiple Play sessions in the editor.
            _resolved = false;
            _current = null;
        }

        public static TestingFloorRecording Current {
            get {
                if (_resolved) return _current;
                _resolved = true;
                _current = ResolveFromArgs() ?? ResolveFromSidecar();
                return _current;
            }
        }

        internal static bool HasRecorderSession => Current != null;
        internal static bool SuppressesQr => Current != null && Current.SuppressQr;

        static TestingFloorRecording ResolveFromArgs() {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++) {
                var arg = args[i];
                if (!arg.StartsWith(CliPrefix, StringComparison.Ordinal)) continue;
                var json = arg.Substring(CliPrefix.Length);
                try {
                    return ParsePayload(json, "--testing-floor JSON");
                }
                catch (Exception e) {
                    Debug.LogWarning($"[TestingFloor] Failed to parse --testing-floor JSON: {e.Message}");
                    return null;
                }
            }
            return null;
        }

        static TestingFloorRecording ResolveFromSidecar() {
            var paths = GetSidecarPaths();
            if (paths == null || paths.Length == 0) return null;

            for (var i = 0; i < paths.Length; i++) {
                var path = paths[i];
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;

                try {
                    var json = File.ReadAllText(path);
                    var recording = ParsePayload(json, "sidecar payload");
                    if (recording == null) continue;

                    // Intentionally NOT deleting the file. The recorder owns the
                    // file's lifetime and clears it when the recording stops.
                    // Keeping it lets every Play boot during this recording
                    // resolve the same recorder context.
                    return recording;
                }
                catch (Exception e) {
                    Debug.LogWarning($"[TestingFloor] Failed to parse sidecar payload '{path}': {e.Message}");
                }
            }

            return null;
        }

        internal static TestingFloorRecording ParsePayloadForTesting(string json) {
            return ParsePayload(json, "test payload");
        }

        static TestingFloorRecording ParsePayload(string json, string label) {
            var payload = JsonUtility.FromJson<Payload>(json);
            if (payload == null) return null;

            var sessionId = Normalized(payload.session_id);
            var recordingUuid = Normalized(payload.recording_uuid) ?? sessionId;
            if (string.IsNullOrWhiteSpace(recordingUuid) && string.IsNullOrWhiteSpace(sessionId)) {
                Debug.LogWarning($"[TestingFloor] {label} missing session_id/recording_uuid; ignoring.");
                return null;
            }

            if (payload.created_at_unix_ms > 0) {
                var ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - payload.created_at_unix_ms;
                if (ageMs > TimeSpan.FromHours(MaxPayloadAgeHours).TotalMilliseconds) {
                    // Stale payload — recorder probably crashed or moved on.
                    // Don't link Play events to a recording that's no longer
                    // active; the recorder will rewrite the file when it
                    // starts a new recording.
                    Debug.LogWarning($"[TestingFloor] {label} is stale (>12h); ignoring.");
                    return null;
                }
            }

            return new TestingFloorRecording(recordingUuid, sessionId, PayloadPlaytestId(payload), PayloadSuppressQr(json, payload));
        }

        static string[] GetSidecarPaths() {
            try {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var dir = Path.Combine(projectRoot, "Library", "TestingFloor");
                return new[] {
                    Path.Combine(dir, SessionPayloadFileName),
                    Path.Combine(dir, RecordingPayloadFileName),
                };
            }
            catch {
                return null;
            }
        }

        static string Normalized(string value) {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        static long? PayloadPlaytestId(Payload payload) {
            return payload.playtest_id > 0 ? payload.playtest_id : null;
        }

        static bool PayloadSuppressQr(string json, Payload payload) {
            return json.IndexOf("\"suppress_qr\"", StringComparison.Ordinal) < 0 || payload.suppress_qr;
        }

        [Serializable]
        sealed class Payload {
            public string session_id;
            public string recording_uuid;
            public long playtest_id;
            public long created_at_unix_ms;
            public int schema;
            public string source;
            public bool suppress_qr;
        }
    }
}
