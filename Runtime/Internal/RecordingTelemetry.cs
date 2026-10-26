using System;
using System.IO;
using UnityEngine;

namespace TestingFloorSdk.Internal {
    internal static class RecordingTelemetry {
        static TestingFloorRecording _recording;
        static string _session;
        static Action<TestingFloorRecording, string> _onComplete;
        static Action _beforeClose;
        static bool _closing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() { _recording = null; _session = null; _onComplete = null; _beforeClose = null; _closing = false; }

        // `beforeClose` runs when the recorder closes the recording, while it is still open, so
        // records buffered until then go out with it, ahead of its completion marker.
        internal static bool Register(TestingFloorRecording recording, string session, Action<TestingFloorRecording, string> onComplete = null, Action beforeClose = null) {
            if (recording == null || string.IsNullOrWhiteSpace(recording.TelemetryDirectory)) return false;
            try {
                if (File.Exists(Path.Combine(recording.TelemetryDirectory, "closed.json"))) { recording.Closed = true; return false; }
                if (!Directory.Exists(recording.TelemetryDirectory)) { recording.Closed = true; return false; }
                // Register first, then test closure. A recorder closing concurrently
                // either sees this participant, or this SDK admits no gameplay.
                File.WriteAllText(Path.Combine(recording.TelemetryDirectory, session + ".session"), "");
                _recording = recording;
                _session = session;
                _onComplete = onComplete ?? TestingFloor.EnqueueRecordingCompletion;
                _beforeClose = beforeClose;
                Poll();
                return true;
            } catch (Exception) { recording.Closed = true; return false; }
        }

        internal static void Poll() {
            if (_closing || _recording == null || _recording.Closed ||
                !File.Exists(Path.Combine(_recording.TelemetryDirectory, "closed.json"))) return;
            // A position callback or state provider can send an event while buffered records go
            // out, and every event polls here. Those events join the still-open recording.
            _closing = true;
            try {
                _beforeClose?.Invoke();
            }
            finally {
                _closing = false;
            }
            Finish();
        }

        internal static void Finish() {
            if (_recording == null || _recording.Closed) return;
            _recording.Closed = true;
            _onComplete(_recording, _session);
        }
    }

    internal sealed class RecordingTelemetryDriver : MonoBehaviour {
        float _nextPoll;
        void Update() {
            if (Time.realtimeSinceStartup < _nextPoll) return;
            _nextPoll = Time.realtimeSinceStartup + 0.25f;
            RecordingTelemetry.Poll();
        }
    }
}
