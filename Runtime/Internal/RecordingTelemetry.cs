using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TestingFloorSdk.Internal {
    internal static class RecordingTelemetry {
        static TestingFloorRecording _recording;
        static string _session;
        static Action<TestingFloorRecording, string> _onComplete;
        static Action _beforeClose;
        static bool _closing;
        static readonly HashSet<string> Completed = new(StringComparer.Ordinal);
        static string _lastStatus;
        static long _lastStatusAt;

        internal static Func<long> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        internal static bool Closing => _closing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() {
            _recording = null;
            _session = null;
            _onComplete = null;
            _beforeClose = null;
            _closing = false;
            Completed.Clear();
            _lastStatus = null;
            _lastStatusAt = 0;
            Clock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        internal static bool IsCompleted(TestingFloorRecording recording, string session) =>
            recording != null && Completed.Contains(recording.RecordingUuid + "\0" + session);

        // Register before admitting events, then check closure again. An SDK racing Stop either
        // enters the recorder's frozen participant list or admits no gameplay for the recording.
        internal static bool Register(TestingFloorRecording recording, string session, Action<TestingFloorRecording, string> onComplete = null, Action beforeClose = null) {
            if (recording == null || IsCompleted(recording, session)) return false;
            if (string.IsNullOrWhiteSpace(recording.TelemetryDirectory)) return false;
            try {
                if (IsClosed(recording) || !Directory.Exists(recording.TelemetryDirectory)) { recording.Closed = true; return false; }
                File.WriteAllText(Path.Combine(recording.TelemetryDirectory, session + ".session"), "");
                SetRecording(recording, session, onComplete, beforeClose);
                Poll();
                return !recording.Closed;
            } catch (IOException) { recording.Closed = true; return false; }
            catch (UnauthorizedAccessException) { recording.Closed = true; return false; }
        }

        internal static void RegisterLegacy(TestingFloorRecording recording, string session, Action<TestingFloorRecording, string> onComplete, Action beforeClose) {
            SetRecording(recording, session, onComplete, beforeClose);
        }

        static void SetRecording(TestingFloorRecording recording, string session, Action<TestingFloorRecording, string> onComplete, Action beforeClose) {
            _recording = recording;
            _session = session;
            _onComplete = onComplete ?? TestingFloor.EnqueueRecordingCompletion;
            _beforeClose = beforeClose;
            TestingFloorRecording.SetActive(recording);
        }

        internal static bool IsClosed(TestingFloorRecording recording) =>
            !string.IsNullOrWhiteSpace(recording.TelemetryDirectory) && File.Exists(Path.Combine(recording.TelemetryDirectory, "closed.json"));

        internal static void Poll() {
            if (_closing || _recording == null || _recording.Closed || !IsClosed(_recording)) return;
            Finish();
        }

        internal static void Suspend(string reason) {
            if (_recording == null || _recording.Closed) return;
            if (!_recording.Suspended) {
                _recording.Interrupted = true;
                _recording.Suspended = true;
                TestingFloor.NotifyRecordingChanged();
            }
            WriteStatus(_recording, _session, "suspended", reason);
        }

        internal static void Finish(string reason = "recording_closed", bool interrupted = false) {
            if (_closing || _recording == null || _recording.Closed) return;
            var recording = _recording;
            if (!recording.Suspended) DrainBuffered();
            recording.Interrupted |= interrupted;
            recording.Closed = true;
            Completed.Add(recording.RecordingUuid + "\0" + _session);
            _onComplete?.Invoke(recording, _session);
            TestingFloor.NotifyRecordingChanged();
            WriteStatus(recording, _session, "closed", reason, force: true);
        }

        static void DrainBuffered() {
            _closing = true;
            try { _beforeClose?.Invoke(); }
            finally { _closing = false; }
        }

        internal static void WriteStatus(TestingFloorRecording recording, string session, string state, string reason, bool force = false) {
            if (recording == null || string.IsNullOrWhiteSpace(recording.TelemetryDirectory) || !Directory.Exists(recording.TelemetryDirectory)) return;
            var now = Clock();
            var identity = recording.RecordingUuid + "\0" + session + "\0" + state + "\0" + reason + "\0" + recording.Interrupted;
            if (!force && identity == _lastStatus && now - _lastStatusAt < 1000) return;
            var path = Path.Combine(recording.TelemetryDirectory, session + ".status.json");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                var status = new Status {
                    recording_uuid = recording.RecordingUuid,
                    session_id = session,
                    engine = "unity",
                    sdk_version = TestingFloorVersion.Value,
                    state = state,
                    reason = reason,
                    interrupted = recording.Interrupted,
                    updated_at_unix_ms = now,
                };
                File.WriteAllText(temporary, JsonUtility.ToJson(status));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                _lastStatus = identity;
                _lastStatusAt = now;
            } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        [Serializable]
        sealed class Status {
            public string recording_uuid;
            public string session_id;
            public string engine;
            public string sdk_version;
            public string state;
            public string reason;
            public bool interrupted;
            public long updated_at_unix_ms;
            public string[] capabilities = { "recording_lease_v1", "dynamic_recording_attachment" };
        }
    }
}
