using TestingFloorSdk.Internal;

namespace TestingFloorSdk {
    /// <summary>
    /// A handle to an object registered with <see cref="TestingFloor.TrackObject(string, UnityEngine.Transform, string)"/>.
    /// Registrations remain available while collection is dormant; sampling
    /// begins when the collection policy permits it. Handles beyond the live-object cap do nothing.
    /// </summary>
    public sealed class TrackedObject {
        readonly ObjectTracker.Entry _entry;

        internal TrackedObject(ObjectTracker.Entry entry, string kind, string id) {
            _entry = entry;
            Kind = entry?.Kind ?? kind;
            Id = entry?.Id ?? id;
        }

        /// <summary>The object's id: the one you supplied, or <c>kind#n</c>.</summary>
        public string Id { get; }

        public string Kind { get; }

        /// <summary>Whether the object is registered and has not been removed.</summary>
        public bool IsTracked => _entry != null && _entry.Live;

        /// <summary>Sets a state value. Only changed values are sent. A value named <c>label</c> becomes the display name.</summary>
        public TrackedObject SetState(string key, string value) => SetValue(key, value);
        public TrackedObject SetState(string key, long value) => SetValue(key, value);
        public TrackedObject SetState(string key, double value) => SetValue(key, value);
        public TrackedObject SetState(string key, bool value) => SetValue(key, value);

        /// <summary>Sends the object's final position and the reason, and stops tracking it.</summary>
        public void Remove(string reason) {
            _entry?.Owner.Remove(_entry, reason);
        }

        TrackedObject SetValue(string key, object value) {
            _entry?.Owner.SetState(_entry, key, value);
            return this;
        }
    }
}
