using System;
using System.Collections.Generic;

namespace TestingFloorSdk.Internal {
    /// <summary>
    /// Turns the session state read at each poll into `$tf_state` records:
    /// only the keys that changed since the last record, null for a key that
    /// disappeared, or every current value in a keyframe.
    /// </summary>
    internal sealed class SessionStateTracker {
        internal const double KeyframeIntervalSeconds = 300.0;

        Dictionary<string, object> _sent = new(StringComparer.Ordinal);
        bool _keyframeDue = true;
        bool _sentAny;
        double _lastKeyframeAt;
        string _recordingUuid;

        internal IReadOnlyDictionary<string, object> Sent => _sent;

        public void RequestKeyframe() {
            _keyframeDue = true;
        }

        /// <summary>
        /// Compares <paramref name="current"/> with what earlier records sent.
        /// Takes ownership of <paramref name="current"/>. Returns the record's
        /// properties (rented from the queue's pool), or null when there is
        /// nothing to send.
        /// </summary>
        public Dictionary<string, object> Commit(double now, Dictionary<string, object> current, string recordingUuid, out bool keyframe) {
            current ??= TelemetryQueue.RentProperties();

            if (!string.Equals(recordingUuid, _recordingUuid, StringComparison.Ordinal)) {
                _recordingUuid = recordingUuid;
                if (recordingUuid != null) _keyframeDue = true;
            }
            if (_sentAny && now - _lastKeyframeAt >= KeyframeIntervalSeconds) {
                _keyframeDue = true;
            }

            keyframe = _keyframeDue;
            Dictionary<string, object> record = null;
            if (keyframe) {
                // Before anything is known there is nothing for a keyframe to
                // reset, so an empty session-start keyframe is skipped.
                if (current.Count > 0 || _sentAny) {
                    record = TelemetryQueue.RentProperties();
                    foreach (var kvp in current) record[kvp.Key] = kvp.Value;
                    _keyframeDue = false;
                    _lastKeyframeAt = now;
                }
            }
            else {
                foreach (var kvp in current) {
                    if (_sent.TryGetValue(kvp.Key, out var previous) && ValuesEqual(previous, kvp.Value)) continue;
                    record ??= TelemetryQueue.RentProperties();
                    record[kvp.Key] = kvp.Value;
                }
                foreach (var key in _sent.Keys) {
                    if (current.ContainsKey(key)) continue;
                    record ??= TelemetryQueue.RentProperties();
                    record[key] = null;
                }
            }

            if (record != null) _sentAny = true;
            else keyframe = false;
            TelemetryQueue.ReturnProperties(_sent);
            _sent = current;
            return record;
        }

        internal static bool ValuesEqual(object a, object b) {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            switch (a) {
                case string[] left when b is string[] right:
                    if (left.Length != right.Length) return false;
                    for (var i = 0; i < left.Length; i++) {
                        if (!string.Equals(left[i], right[i], StringComparison.Ordinal)) return false;
                    }
                    return true;
                case int[] left when b is int[] right:
                    if (left.Length != right.Length) return false;
                    for (var i = 0; i < left.Length; i++) {
                        if (left[i] != right[i]) return false;
                    }
                    return true;
                default:
                    return a.Equals(b);
            }
        }
    }
}
