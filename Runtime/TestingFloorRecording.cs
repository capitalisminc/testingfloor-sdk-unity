using System;
using System.IO;
using UnityEngine;
using TestingFloorSdk.Internal;

namespace TestingFloorSdk {
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
        public bool SuppressQr { get; }
        public string TelemetryDirectory { get; }
        internal bool Closed { get; set; }
        internal bool Suspended { get; set; }
        internal bool Interrupted { get; set; }
        internal int LeaseVersion { get; set; }
        internal int EditorPid { get; set; }
        internal bool TelemetryRequested { get; set; }
        internal long LeaseExpiresAtUnixMs { get; set; }

        internal bool HasLease => LeaseVersion == 1 && TelemetryRequested &&
            LeaseExpiresAtUnixMs > 0 && !string.IsNullOrWhiteSpace(TelemetryDirectory);
        internal bool LeaseFresh(long now) => LeaseVersion == 1 && LeaseExpiresAtUnixMs > now && LeaseExpiresAtUnixMs <= now + 30000;
        internal bool LeaseActive(long now) => HasLease && LeaseFresh(now);

        internal void RefreshLease(TestingFloorRecording candidate) {
            LeaseVersion = candidate.LeaseVersion;
            TelemetryRequested = candidate.TelemetryRequested;
            LeaseExpiresAtUnixMs = candidate.LeaseExpiresAtUnixMs;
        }

        internal TestingFloorRecording(string recordingUuid, long? playtestId, bool suppressQr, string telemetryDirectory = null) {
            RecordingUuid = recordingUuid;
            PlaytestId = playtestId;
            SuppressQr = suppressQr;
            TelemetryDirectory = telemetryDirectory;
        }

        const string CliPrefix = "--testing-floor=";
        const string RecordingPayloadFileName = "recording-payload.json";
        const int MaxPayloadAgeHours = 12;

        static TestingFloorRecording _current;
        static TestingFloorRecording _observed;
        internal static Func<TestingFloorRecording> DiscoveryForTesting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() {
            _current = null;
            _observed = null;
            DiscoveryForTesting = null;
        }

        public static TestingFloorRecording Current {
            get {
                TestingFloor.RefreshRecordingContext();
                return _current != null && !_current.Closed && !_current.Suspended ? _current : null;
            }
        }

        internal static TestingFloorRecording Attached => _current;
        internal static void SetActive(TestingFloorRecording recording) => _current = recording;
        internal static void Observe(TestingFloorRecording recording) => _observed = recording;

        internal static TestingFloorRecording Discover() {
            if (DiscoveryForTesting != null) return DiscoveryForTesting();
            // A new editor handoff takes precedence over an argument from editor startup.
            var candidate = Application.isEditor ? ResolveFromSidecar() ?? ResolveFromArgs() : ResolveFromArgs() ?? ResolveFromSidecar();
            if (Application.isEditor && candidate != null && candidate.EditorPid > 0) {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                if (candidate.EditorPid != process.Id) return null;
            }
            return candidate;
        }

        internal static void UseForTesting(TestingFloorRecording recording) {
            _current = recording;
            _observed = recording;
        }

        internal static bool HasRecorderSession => CurrentRecorderHandoff != null;
        internal static bool SuppressesQr => CurrentRecorderHandoff?.SuppressQr == true;

        static TestingFloorRecording CurrentRecorderHandoff {
            get {
                TestingFloor.RefreshRecordingContext();
                var candidate = _observed ?? _current;
                if (candidate == null || candidate.Closed || RecordingTelemetry.IsClosed(candidate)) return null;
                if (candidate.LeaseVersion != 0 && !candidate.LeaseFresh(RecordingTelemetry.Clock())) return null;
                return candidate;
            }
        }

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
            var payload = new Payload();
            JsonUtility.FromJsonOverwrite(json, payload);
            if (payload == null) return null;

            var recordingUuid = Normalized(payload.recording_uuid);
            if (recordingUuid == null) {
                Debug.LogWarning($"[TestingFloor] {label} missing recording_uuid; ignoring.");
                return null;
            }

            var now = RecordingTelemetry.Clock();
            var freshLease = payload.lease_version == 1 && payload.telemetry_requested &&
                !string.IsNullOrWhiteSpace(payload.telemetry_directory) &&
                payload.lease_expires_at_unix_ms > now && payload.lease_expires_at_unix_ms <= now + 30000;
            if (payload.created_at_unix_ms > 0 && !freshLease) {
                var ageMs = now - payload.created_at_unix_ms;
                if (ageMs > TimeSpan.FromHours(MaxPayloadAgeHours).TotalMilliseconds) {
                    // Stale payload — recorder probably crashed or moved on.
                    // Don't link Play events to a recording that's no longer
                    // active; the recorder will rewrite the file when it
                    // starts a new recording.
                    Debug.LogWarning($"[TestingFloor] {label} is stale (>12h); ignoring.");
                    return null;
                }
            }

            return new TestingFloorRecording(recordingUuid, PayloadPlaytestId(payload), payload.suppress_qr, Normalized(payload.telemetry_directory)) {
                LeaseVersion = payload.lease_version,
                EditorPid = payload.editor_pid,
                TelemetryRequested = payload.telemetry_requested,
                LeaseExpiresAtUnixMs = payload.lease_expires_at_unix_ms,
            };
        }

        static string[] GetSidecarPaths() {
            try {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var dir = Path.Combine(projectRoot, "Library", "TestingFloor");
                return new[] {
                    Path.Combine(dir, RecordingPayloadFileName),
                };
            }
            catch {
                return null;
            }
        }

        static string Normalized(string value) {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        static long? PayloadPlaytestId(Payload payload) {
            return payload.playtest_id > 0 ? payload.playtest_id : null;
        }

        [Serializable]
        sealed class Payload {
            public int lease_version;
            public int editor_pid;
            public bool telemetry_requested;
            public long lease_expires_at_unix_ms;
            public string recording_uuid;
            public string telemetry_directory;
            public long playtest_id;
            public long created_at_unix_ms;
            public bool suppress_qr = true;
        }
    }
}
