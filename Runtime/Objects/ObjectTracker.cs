using System;
using System.Collections.Generic;
using System.Text;
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

            // A position with a NaN or infinite axis cannot be written as JSON, so it counts as
            // `invalid`: no position this time, without ending the object.
            internal Vector3? Read(out bool destroyed, out bool invalid) {
                destroyed = false;
                invalid = false;
                Vector3? position;
                if (_usesTransform) {
                    if (_transform == null) {
                        destroyed = true;
                        return null;
                    }
                    position = _transform.position;
                }
                else {
                    try {
                        position = _source();
                    }
                    catch (Exception ex) {
                        if (Owner.LogErrors) Debug.LogWarning($"[TestingFloor] Position source for tracked object '{Id}' threw: {ex}");
                        return null;
                    }
                }
                if (position.HasValue && !IsFinite(position.Value)) {
                    invalid = true;
                    return null;
                }
                return position;
            }

            static bool IsFinite(Vector3 v) =>
                !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
                !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
                !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }

        // A removal from a record that the queue refused, to send again.
        internal readonly struct RetriedRemoval {
            public readonly string Id;
            public readonly string Kind;
            public readonly string Reason;
            public readonly Vector3? Final;

            public RetriedRemoval(string id, string kind, string reason, Vector3? final) {
                Id = id;
                Kind = kind;
                Reason = reason;
                Final = final;
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
        readonly List<Removal> _carriedRemovals = new();
        readonly List<RetriedRemoval> _retries = new();
        readonly List<RetriedRemoval> _carriedRetries = new();
        readonly HashSet<string> _endedIds = new(StringComparer.Ordinal);
        readonly ObjectRecordBuilder _builder = new();
        double _lastKeyframeAt = double.NaN;
        bool _keyframeDue;
        bool _capWarned;

        internal int MaxLiveObjects = DefaultMaxLiveObjects;
        internal bool LogErrors;
        internal bool ObserveOnMutation = true;

        internal int LiveCount => _live.Count;
        internal int RetryCount => _retries.Count;

        /// <summary>Removals still waiting to be sent: carried to a later check or retried.</summary>
        internal int WaitingRemovalCount => _removals.Count + _retries.Count;

        /// <summary>Waiting removals plus live objects whose first sample is still held back.</summary>
        internal int WaitingCount {
            get {
                var count = WaitingRemovalCount;
                for (var i = 0; i < _live.Count; i++) {
                    if (!_live[i].HasSent && _live[i].Pending.HasValue) count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Moves the live objects with these ids to the front, in their current order, so the next
        /// check reaches them before the rest. A record that the queue refuses would otherwise be
        /// refused again at the next keyframe, and the same objects would never be sent.
        /// </summary>
        public void Prioritize(string[] ids) {
            if (ids == null || ids.Length == 0) return;
            var first = new HashSet<string>(ids, StringComparer.Ordinal);
            var front = new List<Entry>();
            var back = new List<Entry>();
            foreach (var entry in _live) (first.Contains(entry.Id) ? front : back).Add(entry);
            _live.Clear();
            _live.AddRange(front);
            _live.AddRange(back);
        }

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

            var entry = new Entry(this, id, kind, transform, source);
            entry.Pending = ObserveOnMutation ? entry.Read(out _, out _) : null;
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
            if (!ObserveOnMutation && !entry.HasSent) return;
            var final = ObserveOnMutation ? entry.Read(out _, out _) : null;
            if (!final.HasValue && entry.HasObserved) final = entry.LastObserved;
            reason = string.IsNullOrWhiteSpace(reason) ? ReasonRemoved : reason.Trim();
            _removals.Add(new Removal(entry, reason, final));
        }

        /// <summary>
        /// Samples every live object and appends the properties of zero or
        /// more `$tf_objects` records to <paramref name="records"/>. Returns
        /// whether the records are a keyframe.
        /// </summary>
        /// <summary>
        /// The removals in a `$tf_objects` record's properties, with each removed object's final
        /// sample from the same record. Null when the record has none. Read them before queueing:
        /// a refused record's properties go back to the pool.
        /// </summary>
        internal static List<RetriedRemoval> RemovalsIn(Dictionary<string, object> properties) {
            if (!properties.TryGetValue("removed.id", out var value) || value is not string[] ids || ids.Length == 0) return null;
            var reasons = properties.TryGetValue("removed.reason", out var r) ? r as string[] : null;
            var sampleIds = properties.TryGetValue("sample.id", out var si) ? si as string[] : null;
            var kinds = properties.TryGetValue("sample.kind", out var sk) ? sk as string[] : null;
            var xs = properties.TryGetValue("sample.x", out var sx) ? sx as double[] : null;
            var ys = properties.TryGetValue("sample.y", out var sy) ? sy as double[] : null;
            var zs = properties.TryGetValue("sample.z", out var sz) ? sz as double[] : null;
            var removals = new List<RetriedRemoval>(ids.Length);
            for (var i = 0; i < ids.Length; i++) {
                string kind = null;
                Vector3? final = null;
                if (sampleIds != null && kinds != null && xs != null && ys != null && zs != null) {
                    for (var s = sampleIds.Length - 1; s >= 0; s--) {
                        if (!string.Equals(sampleIds[s], ids[i], StringComparison.Ordinal)) continue;
                        kind = kinds[s];
                        final = new Vector3((float)xs[s], (float)ys[s], (float)zs[s]);
                        break;
                    }
                }
                removals.Add(new RetriedRemoval(ids[i], kind, reasons != null && i < reasons.Length ? reasons[i] : ReasonRemoved, final));
            }
            return removals;
        }

        /// <summary>
        /// Keeps the removals of a record that the queue refused, to send again at the next
        /// check. Readers end a path only on a removal, so a lost one would leave the object on
        /// the map. At most <see cref="MaxLiveObjects"/> wait; returns how many did not fit.
        /// </summary>
        public int Retry(List<RetriedRemoval> removals) {
            if (removals == null) return 0;
            var overflow = 0;
            foreach (var removal in removals) {
                if (_retries.Count >= Math.Max(1, MaxLiveObjects)) overflow++;
                else _retries.Add(removal);
            }
            return overflow;
        }

        public bool Check(double now, List<Dictionary<string, object>> records) {
            if (double.IsNaN(_lastKeyframeAt)) _lastKeyframeAt = now;
            if (now - _lastKeyframeAt >= KeyframeIntervalSeconds) _keyframeDue = true;
            var keyframe = _keyframeDue;
            if (keyframe) {
                _keyframeDue = false;
                _lastKeyframeAt = now;
            }

            _builder.Begin(records);

            // Readers apply a record's samples before its removals. So once one lifetime of an id
            // ends in this check, a later lifetime of that id waits for the next check, where it
            // starts a new path instead of extending the old one.
            _endedIds.Clear();
            _carriedRetries.Clear();
            for (var i = 0; i < _retries.Count; i++) {
                var retry = _retries[i];
                if (_endedIds.Contains(retry.Id)) {
                    _carriedRetries.Add(retry);
                    continue;
                }
                _builder.BeginGroup();
                if (retry.Final.HasValue && retry.Kind != null) _builder.AddSample(retry.Id, retry.Kind, retry.Final.Value);
                _builder.AddRemoval(retry.Id, retry.Reason);
                _builder.EndGroup();
                _endedIds.Add(retry.Id);
            }
            _retries.Clear();
            _retries.AddRange(_carriedRetries);

            _carriedRemovals.Clear();
            for (var i = 0; i < _removals.Count; i++) {
                var removal = _removals[i];
                if (_endedIds.Contains(removal.Entry.Id)) {
                    _carriedRemovals.Add(removal);
                    continue;
                }
                if (AppendRemoval(removal.Entry, removal.Reason, removal.Final)) _endedIds.Add(removal.Entry.Id);
            }
            _removals.Clear();
            _removals.AddRange(_carriedRemovals);

            const float minDistanceSquared = MinSampleDistance * MinSampleDistance;
            for (var i = 0; i < _live.Count; i++) {
                var entry = _live[i];
                if (!entry.HasSent && _endedIds.Contains(entry.Id)) continue;

                var position = entry.Read(out var destroyed, out var invalid);
                if (invalid) continue;
                if (!position.HasValue) {
                    Detach(entry);
                    i--;
                    var final = entry.HasObserved ? entry.LastObserved : (Vector3?)null;
                    if (AppendRemoval(entry, destroyed ? ReasonDestroyed : ReasonGone, final)) _endedIds.Add(entry.Id);
                    continue;
                }

                // The position read at registration is the object's first sample.
                var pending = entry.Pending;
                entry.Pending = null;
                entry.HasObserved = true;
                entry.LastObserved = position.Value;
                var hasSample = pending.HasValue || entry.HasSent;
                var reference = pending ?? entry.LastSent;
                var sample = !hasSample || (keyframe && !pending.HasValue) || (position.Value - reference).sqrMagnitude >= minDistanceSquared;
                if (!pending.HasValue && !sample && entry.ChangedKeys.Count == 0) continue;

                _builder.BeginGroup();
                if (pending.HasValue) AppendSample(entry, pending.Value);
                if (sample) AppendSample(entry, position.Value);
                AppendState(entry, keyframe);
                _builder.EndGroup();
            }

            _builder.End();
            return keyframe && records.Count > 0;
        }

        // Returns whether the removal reached the record.
        bool AppendRemoval(Entry entry, string reason, Vector3? final) {
            var pending = entry.Pending;
            entry.Pending = null;
            var sendFinal = final.HasValue && !(pending.HasValue && pending.Value == final.Value);
            // An object that never reached the wire has nothing to end.
            if (!entry.HasSent && !pending.HasValue && !sendFinal) return false;

            _builder.BeginGroup();
            if (pending.HasValue) AppendSample(entry, pending.Value);
            if (sendFinal) AppendSample(entry, final.Value);
            AppendState(entry, full: false);
            _builder.AddRemoval(entry.Id, reason);
            _builder.EndGroup();
            return true;
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

        // Skips ids that a live object or a pending removal already uses, such as an
        // explicit `guard#1`, so two objects never share one path.
        string NextId(string kind) {
            _kindCounts.TryGetValue(kind, out var count);
            string id;
            do {
                count++;
                id = kind + "#" + count;
            } while (_byId.ContainsKey(id) || HasPendingRemoval(id));
            _kindCounts[kind] = count;
            return id;
        }
    }

    /// <summary>
    /// Collects one check's samples, state and removals as parallel arrays.
    /// One object's entries always stay in one record, and a record holds at
    /// most 300 samples and stays well inside the collector's event size cap.
    /// </summary>
    internal sealed class ObjectRecordBuilder {
        const int RecordByteBudget = 20 * 1024;
        const int CoordinateBytes = 12;
        const int NumberBytes = 25;

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
            _bytes += JsonStringBytes(id) + JsonStringBytes(kind) + 3 * CoordinateBytes;
        }

        public void AddState(string id, string key, object value) {
            _stateIds.Add(id);
            _stateKeys.Add(key);
            _stateValues.Add(value);
            _bytes += JsonStringBytes(id) + JsonStringBytes(key) + (value is string s ? JsonStringBytes(s) : NumberBytes) + 2;
        }

        public void AddRemoval(string id, string reason) {
            _removedIds.Add(id);
            _removedReasons.Add(reason);
            _bytes += JsonStringBytes(id) + JsonStringBytes(reason) + 1;
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

        // Serialized size of a JSON string value: UTF-8 bytes, the quotes, a comma, and the
        // escapes the writer adds for quotes, backslashes and control characters.
        internal static int JsonStringBytes(string value) {
            var bytes = Encoding.UTF8.GetByteCount(value) + 3;
            for (var i = 0; i < value.Length; i++) {
                var ch = value[i];
                if (ch == '"' || ch == '\\') bytes += 1;
                else if (ch < ' ') bytes += 5;
            }
            return bytes;
        }

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
