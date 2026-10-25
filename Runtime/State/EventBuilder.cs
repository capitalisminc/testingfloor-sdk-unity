using System;
using System.Collections.Generic;

namespace TestingFloorSdk {
    /// <summary>A single event. Configure properties, then call Send once.</summary>
    public sealed class EventBuilder {
        internal string _eventType;
        internal Dictionary<string, object> _properties;
        bool _sent;

        EventBuilder SetValue(string key, object value) {
            if (_sent) throw new InvalidOperationException("This event has already been sent. Call Track to create another event.");
            ContextSnapshot.Put(ref _properties, key, value);
            return this;
        }

        public EventBuilder Set(string key, string value) => SetValue(key, value);
        public EventBuilder Set(string key, long value) => SetValue(key, value);
        public EventBuilder Set(string key, double value) => SetValue(key, value);
        public EventBuilder Set(string key, bool value) => SetValue(key, value);
        public EventBuilder Set(string key, string[] value) => SetValue(key, value);
        public EventBuilder Set(string key, int[] value) => SetValue(key, value);
        public EventBuilder Set(string key, Guid value) => SetValue(key, value);
        public EventBuilder SetVector(string prefix, UnityEngine.Vector3 value) => Set(prefix + ".x", value.x).Set(prefix + ".y", value.y).Set(prefix + ".z", value.z);
        public EventBuilder SetIfPresent(string key, string value) => string.IsNullOrWhiteSpace(value) ? this : Set(key, value);
        public EventBuilder SetIfPresent(string key, int? value) => value.HasValue ? Set(key, value.Value) : this;
        public EventBuilder SetIfPresent(string key, long? value) => value.HasValue ? Set(key, value.Value) : this;
        public EventBuilder SetIfPresent(string key, float? value) => value.HasValue ? Set(key, value.Value) : this;
        public EventBuilder SetIfPresent(string key, double? value) => value.HasValue ? Set(key, value.Value) : this;
        public EventBuilder SetIfPresent(string key, bool? value) => value.HasValue ? Set(key, value.Value) : this;

        /// <summary>Returns whether the event entered the in-memory queue, not whether it reached the server.</summary>
        public bool Send() {
            if (_sent) throw new InvalidOperationException("This event has already been sent. Call Track to create another event.");
            _sent = true;
            var properties = _properties;
            _properties = null;
            return global::TestingFloorSdk.TestingFloor.TrackEventInternal(_eventType, properties);
        }
    }
}
