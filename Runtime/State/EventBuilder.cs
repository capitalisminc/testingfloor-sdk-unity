using System.Collections.Generic;
using TestingFloor.Internal;

namespace TestingFloor {
    public struct EventBuilder {
        internal string _eventType;
        internal Dictionary<string, object> _properties;

        public EventBuilder Set(string key, string value) {
            _properties ??= TelemetryQueue.RentProperties();
            _properties[key] = value;
            return this;
        }

        public EventBuilder Set(string key, long value) {
            _properties ??= TelemetryQueue.RentProperties();
            _properties[key] = value;
            return this;
        }

        public EventBuilder Set(string key, double value) {
            _properties ??= TelemetryQueue.RentProperties();
            _properties[key] = value;
            return this;
        }

        public EventBuilder Set(string key, bool value) {
            _properties ??= TelemetryQueue.RentProperties();
            _properties[key] = value;
            return this;
        }

        public EventBuilder Set(string key, object value) {
            if (value == null) return this;
            _properties ??= TelemetryQueue.RentProperties();
            _properties[key] = value;
            return this;
        }

        public EventBuilder SetIfPresent(string key, string value) {
            if (string.IsNullOrWhiteSpace(value)) return this;
            _properties ??= TelemetryQueue.RentProperties();
            _properties[key] = value;
            return this;
        }

        public void Send() {
            global::TestingFloor.TestingFloor.TrackEventInternal(_eventType, _properties);
        }
    }
}
