using System;
using System.Collections.Generic;
using UnityEngine;

namespace TestingFloorSdk.Internal {
    /// <summary>
    /// Live tracked objects for one play session, and the `$tf_objects`
    /// records that describe what changed since the previous check.
    /// </summary>
    internal sealed class ObjectTracker {
        internal const double KeyframeIntervalSeconds = 300.0;
        internal const float MinSampleDistance = 0.25f;
        internal const int MaxSamplesPerRecord = 300;
        internal const int DefaultMaxLiveObjects = 256;
        internal const string DefaultKind = "object";
        internal const string ReasonDestroyed = "destroyed";
        internal const string ReasonGone = "gone";
        internal const string ReasonDisabled = "disabled";
        internal const string ReasonRemoved = "removed";

        internal sealed class Entry {
            internal readonly ObjectTracker Owner;
            internal readonly string Id;
            internal readonly string Kind;
            readonly Transform _transform;
            readonly Func<Vector3?> _source;
            readonly bool _usesTransform;
            internal bool Live = true;
            // Registered while a removal of the same id waits to be sent, so
            // its first sample waits one check and starts a new path.
            internal bool Deferred;
            internal Vector3? Pending;
            internal bool HasSent;
            internal Vector3 LastSent;
            internal bool HasObserved;
            internal Vector3 LastObserved;
            internal readonly Dictionary<string, object> State = new(StringComparer.Ordinal);
            internal readonly List<string> ChangedKeys = new();

            internal Entry(ObjectTracker owner, string id, string kind, Transform transform, Func<Vector3?> source) {
                Owner = owner;
                Id = id;
                Kind = kind;
                _transform = transform;
                _source = source;
                _usesTransform = source == null;
            }

            internal Vector3? Read(out bool destroyed) {
                destroyed = false;
                if (_usesTransform) {
                    if (_transform == null) {
                        destroyed = true;
                        return null;
                    }
                    return _transform.position;
                }
                try {
                    return _source();
                }
                catch (Exception ex) {
                    if (Owner.LogErrors) Debug.LogWarning($"[TestingFloor] Position source for tracked object '{Id}' threw: {ex}");
                    return null;
                }
            }
        }

        readonly struct Removal {
            public readonly Entry Entry;
            public readonly string Reason;
            public readonly Vector3? Final;

            public Removal(Entry entry, string reason, Vector3? final) {
                Entry = entry;
                Reason = reason;
                Final = final;
            }
        }

        readonly List<Entry> _live = new();
        readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _kindCounts = new(StringComparer.Ordinal);
        readonly List<Removal> _removals = new();
        readonly ObjectRecordBuilder _builder = new();
        double _lastKeyframeAt = double.NaN;
        bool _keyframeDue;
        bool _capWarned;

        internal int MaxLiveObjects = DefaultMaxLiveObjects;
        internal bool LogErrors;

        internal int LiveCount => _live.Count;

        public void RequestKeyframe() {
            _keyframeDue = true;
        }

        public Entry Register(string kind, Transform transform, Func<Vector3?> source, string id) {
            kind = string.IsNullOrWhiteSpace(kind) ? DefaultKind : kind.Trim();
            id = string.IsNullOrWhiteSpace(id) ? null : id.Trim();
            _byId.TryGetValue(id ?? string.Empty, out var replaced);
            if (replaced == null && _live.Count >= Math.Max(1, MaxLiveObjects)) {
                if (!_capWarned) {
                    _capWarned = true;
                    Debug.LogWarning($"[TestingFloor] {_live.Count} objects are already tracked. Further TrackObject calls are ignored until some are removed; raise maxTrackedObjects in the settings asset to track more.");
                }
                return null;
            }
            if (replaced != null) Detach(replaced);
            id ??= NextId(kind);

            var entry = new Entry(this, id, kind, transform, source) {
                Deferred = HasPendingRemoval(id),
            };
            entry.Pending = entry.Read(out _);
            if (entry.Pending.HasValue) {
                entry.HasObserved = true;
                entry.LastObserved = entry.Pending.Value;
            }
            _live.Add(entry);
            _byId[id] = entry;
            return entry;
        }

        public void SetState(Entry entry, string key, object value) {
            if (entry == null || !entry.Live || !ContextSnapshot.IsPropertyKey(key) || value == null) return;
            if (value is double d && (double.IsNaN(d) || double.IsInfinity(d))) return;
            if (entry.State.TryGetValue(key, out var previous) && SessionStateTracker.ValuesEqual(previous, value)) return;
            entry.State[key] = value;
            if (!entry.ChangedKeys.Contains(key)) entry.ChangedKeys.Add(key);
        }

        public void Remove(Entry entry, string reason) {
            if (entry == null || !entry.Live) return;
            Detach(entry);
            var final = entry.Read(out _);
            if (!final.HasValue && entry.HasObserved) final = entry.LastObserved;
            reason = string.IsNullOrWhiteSpace(reason) ? ReasonRemoved : reason.Trim();
            _removals.Add(new Removal(entry, reason, final));
        }

        /// <summary>
        /// Samples every live object and appends the properties of zero or
        /// more `$tf_objects` records to <paramref name="records"/>. Returns
        /// whether the records are a keyframe.
        /// </summary>
        public bool Check(double now, List<Dictionary<string, object>> records) {
            if (double.IsNaN(_lastKeyframeAt)) _lastKeyframeAt = now;
            if (now - _lastKeyframeAt >= KeyframeIntervalSeconds) _keyframeDue = true;
            var keyframe = _keyframeDue;
            if (keyframe) {
                _keyframeDue = false;
                _lastKeyframeAt = now;
            }

            _builder.Begin(records);
            const float minDistanceSquared = MinSampleDistance * MinSampleDistance;
            for (var i = 0; i < _live.Count; i++) {
                var entry = _live[i];
                if (entry.Deferred) {
                    entry.Deferred = false;
                    continue;
                }

                var position = entry.Read(out var destroyed);
                if (!position.HasValue) {
                    Detach(entry);
                    i--;
                    var final = entry.HasObserved ? entry.LastObserved : (Vector3?)null;
                    AppendRemoval(entry, destroyed ? ReasonDestroyed : ReasonGone, final);
                    continue;
                }

                entry.Pending = null;
                entry.HasObserved = true;
                entry.LastObserved = position.Value;
                var sample = keyframe || !entry.HasSent || (position.Value - entry.LastSent).sqrMagnitude >= minDistanceSquared;
                if (!sample && entry.ChangedKeys.Count == 0) continue;

                _builder.BeginGroup();
                if (sample) AppendSample(entry, position.Value);
                AppendState(entry, keyframe);
                _builder.EndGroup();
            }

            for (var i = 0; i < _removals.Count; i++) {
                var removal = _removals[i];
                AppendRemoval(removal.Entry, removal.Reason, removal.Final);
            }
            _removals.Clear();

            _builder.End();
            return keyframe && records.Count > 0;
        }

        void AppendRemoval(Entry entry, string reason, Vector3? final) {
            var pending = entry.Pending;
            entry.Pending = null;
            var sendFinal = final.HasValue && !(pending.HasValue && pending.Value == final.Value);
            // An object that never reached the wire has nothing to end.
            if (!entry.HasSent && !pending.HasValue && !sendFinal) return;

            _builder.BeginGroup();
            if (pending.HasValue) AppendSample(entry, pending.Value);
            if (sendFinal) AppendSample(entry, final.Value);
            AppendState(entry, full: false);
            _builder.AddRemoval(entry.Id, reason);
            _builder.EndGroup();
        }

        void AppendSample(Entry entry, Vector3 position) {
            _builder.AddSample(entry.Id, entry.Kind, position);
            entry.HasSent = true;
            entry.LastSent = position;
        }

        void AppendState(Entry entry, bool full) {
            if (full) {
                foreach (var kvp in entry.State) _builder.AddState(entry.Id, kvp.Key, kvp.Value);
            }
            else {
                for (var i = 0; i < entry.ChangedKeys.Count; i++) {
                    var key = entry.ChangedKeys[i];
                    _builder.AddState(entry.Id, key, entry.State[key]);
                }
            }
            entry.ChangedKeys.Clear();
        }

        void Detach(Entry entry) {
            entry.Live = false;
            _live.Remove(entry);
            if (_byId.TryGetValue(entry.Id, out var current) && current == entry) _byId.Remove(entry.Id);
        }

        bool HasPendingRemoval(string id) {
            for (var i = 0; i < _removals.Count; i++) {
                if (string.Equals(_removals[i].Entry.Id, id, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        string NextId(string kind) {
            _kindCounts.TryGetValue(kind, out var count);
            count++;
            _kindCounts[kind] = count;
            return kind + "#" + count;
        }
    }

    /// <summary>
    /// Collects one check's samples, state and removals as parallel arrays.
    /// One object's entries always stay in one record, and a record holds at
    /// most 300 samples and stays well inside the collector's event size cap.
    /// </summary>
    internal sealed class ObjectRecordBuilder {
        const int RecordByteBudget = 20 * 1024;
        const int NumberBytes = 12;

        readonly List<string> _sampleIds = new();
        readonly List<string> _sampleKinds = new();
        readonly List<double> _xs = new();
        readonly List<double> _ys = new();
        readonly List<double> _zs = new();
        readonly List<string> _removedIds = new();
        readonly List<string> _removedReasons = new();
        readonly List<string> _stateIds = new();
        readonly List<string> _stateKeys = new();
        readonly List<object> _stateValues = new();
        List<Dictionary<string, object>> _output;
        int _bytes;
        int _groupSamples;
        int _groupRemoved;
        int _groupStates;
        int _groupBytes;

        public void Begin(List<Dictionary<string, object>> output) {
            _output = output;
            Clear();
        }

        public void BeginGroup() {
            _groupSamples = _sampleIds.Count;
            _groupRemoved = _removedIds.Count;
            _groupStates = _stateIds.Count;
            _groupBytes = _bytes;
        }

        public void AddSample(string id, string kind, Vector3 position) {
            _sampleIds.Add(id);
            _sampleKinds.Add(kind);
            _xs.Add(Round(position.x));
            _ys.Add(Round(position.y));
            _zs.Add(Round(position.z));
            _bytes += id.Length + kind.Length + 3 * NumberBytes + 8;
        }

        public void AddState(string id, string key, object value) {
            _stateIds.Add(id);
            _stateKeys.Add(key);
            _stateValues.Add(value);
            _bytes += id.Length + key.Length + (value is string s ? s.Length + 2 : NumberBytes) + 8;
        }

        public void AddRemoval(string id, string reason) {
            _removedIds.Add(id);
            _removedReasons.Add(reason);
            _bytes += id.Length + reason.Length + 8;
        }

        public void EndGroup() {
            if (_sampleIds.Count <= ObjectTracker.MaxSamplesPerRecord && _bytes <= RecordByteBudget) return;
            if (_groupSamples == 0 && _groupRemoved == 0 && _groupStates == 0) return;
            Emit(_groupSamples, _groupRemoved, _groupStates);
            _bytes -= _groupBytes;
        }

        public void End() {
            if (_sampleIds.Count > 0 || _removedIds.Count > 0 || _stateIds.Count > 0) {
                Emit(_sampleIds.Count, _removedIds.Count, _stateIds.Count);
            }
            Clear();
            _output = null;
        }

        void Emit(int samples, int removed, int states) {
            var props = TelemetryQueue.RentProperties();
            if (samples > 0) {
                props["sample.id"] = Take(_sampleIds, samples);
                props["sample.kind"] = Take(_sampleKinds, samples);
                props["sample.x"] = Take(_xs, samples);
                props["sample.y"] = Take(_ys, samples);
                props["sample.z"] = Take(_zs, samples);
            }
            if (removed > 0) {
                props["removed.id"] = Take(_removedIds, removed);
                props["removed.reason"] = Take(_removedReasons, removed);
            }
            if (states > 0) {
                props["state.id"] = Take(_stateIds, states);
                props["state.key"] = Take(_stateKeys, states);
                props["state.value"] = Take(_stateValues, states);
            }
            _output.Add(props);
        }

        static T[] Take<T>(List<T> list, int count) {
            var values = new T[count];
            list.CopyTo(0, values, 0, count);
            list.RemoveRange(0, count);
            return values;
        }

        static double Round(float value) => Math.Round(value * 100.0) / 100.0;

        void Clear() {
            _sampleIds.Clear();
            _sampleKinds.Clear();
            _xs.Clear();
            _ys.Clear();
            _zs.Clear();
            _removedIds.Clear();
            _removedReasons.Clear();
            _stateIds.Clear();
            _stateKeys.Clear();
            _stateValues.Clear();
            _bytes = 0;
        }
    }
}
