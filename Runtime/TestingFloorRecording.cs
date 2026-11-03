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
        internal static TestingFloorRecording Observe(TestingFloorRecording recording) {
            var previous = _observed ?? _current;
            if (recording != null && recording.LeaseVersion == 0 && previous != null &&
                previous.LeaseVersion != 0 && previous.RecordingUuid == recording.RecordingUuid) return previous;
            _observed = recording;
            return recording;
        }

        internal static TestingFloorRecording Discover() {
            if (DiscoveryForTesting != null) return DiscoveryForTesting();
            return Resolve(SidecarText(), LaunchArgumentText(), Application.isEditor ? EditorProcessId : 0);
        }

        /// <summary>
        /// The recording a handoff names: the sidecar's text, the JSON after <c>--testing-floor=</c>
        /// (either may be null), and the editor's process ID, or 0 in a built game.
        /// </summary>
        internal static TestingFloorRecording Resolve(string sidecar, string launchArgument, int editorProcessId) {
            var fromSidecar = ForEditor(TryParsePayload(sidecar, "sidecar payload"), editorProcessId);
            if (fromSidecar != null && fromSidecar.LeaseVersion != 0) return fromSidecar;
            var recording = ForEditor(TryParsePayload(launchArgument, "--testing-floor JSON"), editorProcessId) ?? fromSidecar;
            var locator = ForEditor(TryParsePayload(launchArgument, "--testing-floor locator", allowStale: true), editorProcessId) ?? recording;
            if (locator == null || string.IsNullOrWhiteSpace(locator.TelemetryDirectory)) return recording;
            try {
                var path = Path.Combine(locator.TelemetryDirectory, RecordingPayloadFileName);
                if (File.Exists(path)) {
                    var lease = ForEditor(TryParsePayload(File.ReadAllText(path), "recording lease", allowStale: true), editorProcessId);
                    if (lease != null && lease.LeaseVersion != 0 && lease.RecordingUuid == locator.RecordingUuid) return lease;
                }
            }
            catch (IOException) { /* Retry on the next recording poll. */ }
            catch (UnauthorizedAccessException) { /* Keep the last observed lease until it expires. */ }
            return recording;
        }

        static int s_editorProcessId;

        static int EditorProcessId {
            get {
                if (s_editorProcessId == 0) {
                    using var process = System.Diagnostics.Process.GetCurrentProcess();
                    s_editorProcessId = process.Id;
                }
                return s_editorProcessId;
            }
        }

        // In an editor (a nonzero process id), a handoff naming another editor process belongs to that editor.
        internal static TestingFloorRecording ForEditor(TestingFloorRecording recording, int editorProcessId) {
            if (editorProcessId == 0 || recording == null || recording.EditorPid <= 0) return recording;
            return recording.EditorPid == editorProcessId ? recording : null;
        }

        // A versioned lease sidecar wins even once its lease expires, so expiry suspends the
        // association instead of reviving an older launch argument. Otherwise a valid launch
        // argument wins over a legacy sidecar.
        internal static TestingFloorRecording Choose(TestingFloorRecording sidecar, Func<TestingFloorRecording> launchArgument) {
            if (sidecar != null && sidecar.LeaseVersion != 0) return sidecar;
            return launchArgument() ?? sidecar;
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

        static string LaunchArgumentText() {
            foreach (var arg in Environment.GetCommandLineArgs()) {
                if (arg.StartsWith(CliPrefix, StringComparison.Ordinal)) return arg.Substring(CliPrefix.Length);
            }
            return null;
        }

        // Intentionally does not delete the file. The recorder owns its lifetime and clears it
        // when the recording stops, so every Play boot during the recording resolves the same context.
        static string SidecarText() {
            var paths = GetSidecarPaths();
            if (paths == null) return null;
            foreach (var path in paths) {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                try {
                    return File.ReadAllText(path);
                }
                catch (Exception e) {
                    Debug.LogWarning($"[TestingFloor] Failed to read sidecar payload '{path}': {e.Message}");
                }
            }
            return null;
        }

        static TestingFloorRecording TryParsePayload(string json, string label, bool allowStale = false) {
            if (json == null) return null;
            try {
                return ParsePayload(json, label, allowStale);
            }
            catch (Exception e) {
                Debug.LogWarning($"[TestingFloor] Failed to parse {label}: {e.Message}");
                return null;
            }
        }

        internal static TestingFloorRecording ParsePayloadForTesting(string json) {
            return ParsePayload(json, "test payload");
        }

        static TestingFloorRecording ParsePayload(string json, string label, bool allowStale = false) {
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
            if (!allowStale && payload.created_at_unix_ms > 0 && !freshLease) {
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
