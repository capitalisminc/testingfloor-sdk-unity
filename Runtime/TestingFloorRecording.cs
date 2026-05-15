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
        public long? PlaytestId { get; }

        internal TestingFloorRecording(string recordingUuid, long? playtestId) {
            RecordingUuid = recordingUuid;
            PlaytestId = playtestId;
        }

        const string CliPrefix = "--testing-floor=";
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

        static TestingFloorRecording ResolveFromArgs() {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++) {
                var arg = args[i];
                if (!arg.StartsWith(CliPrefix, StringComparison.Ordinal)) continue;
                var json = arg.Substring(CliPrefix.Length);
                try {
                    var payload = JsonUtility.FromJson<Payload>(json);
                    if (payload == null || string.IsNullOrWhiteSpace(payload.recording_uuid)) return null;
                    return new TestingFloorRecording(payload.recording_uuid, PayloadPlaytestId(payload));
                }
                catch (Exception e) {
                    Debug.LogWarning($"[TestingFloor] Failed to parse --testing-floor JSON: {e.Message}");
                    return null;
                }
            }
            return null;
        }

        static TestingFloorRecording ResolveFromSidecar() {
            var path = GetSidecarPath();
            if (string.IsNullOrWhiteSpace(path)) return null;

            try {
                if (!File.Exists(path)) return null;

                var json = File.ReadAllText(path);
                var payload = JsonUtility.FromJson<Payload>(json);
                if (payload == null || string.IsNullOrWhiteSpace(payload.recording_uuid)) {
                    Debug.LogWarning("[TestingFloor] Sidecar payload missing recording_uuid; ignoring.");
                    return null;
                }

                if (payload.created_at_unix_ms > 0) {
                    var ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - payload.created_at_unix_ms;
                    if (ageMs > TimeSpan.FromHours(MaxPayloadAgeHours).TotalMilliseconds) {
                        // Stale payload — recorder probably crashed or moved on.
                        // Don't link Play events to a recording that's no longer
                        // active; the recorder will rewrite the file when it
                        // starts a new recording.
                        Debug.LogWarning("[TestingFloor] Sidecar payload is stale (>12h); ignoring.");
                        return null;
                    }
                }

                // Intentionally NOT deleting the file. The recorder owns the
                // file's lifetime and clears it when the recording stops.
                // Keeping it lets every Play boot during this recording
                // resolve the same recording_uuid.
                return new TestingFloorRecording(payload.recording_uuid, PayloadPlaytestId(payload));
            }
            catch (Exception e) {
                Debug.LogWarning($"[TestingFloor] Failed to parse sidecar payload: {e.Message}");
                return null;
            }
        }

        static string GetSidecarPath() {
            try {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(projectRoot, "Library", "TestingFloor", "recording-payload.json");
            }
            catch {
                return null;
            }
        }

        static long? PayloadPlaytestId(Payload payload) {
            return payload.playtest_id > 0 ? payload.playtest_id : null;
        }

        [Serializable]
        sealed class Payload {
            public string recording_uuid;
            public long playtest_id;
            public long created_at_unix_ms;
            public int schema;
            public string source;
        }
    }
}
