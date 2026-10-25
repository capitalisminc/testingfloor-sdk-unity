using System.Collections.Generic;

namespace TestingFloorSdk {
    internal readonly struct TelemetryEvent {
        public readonly System.Guid Id;
        public readonly TestingFloorRecording Recording;
        public readonly string EventType;
        public readonly Dictionary<string, object> EventProperties;
        public readonly ContextSnapshot Context;
        public readonly string DeviceId;
        public readonly string UserId;
        public readonly long TimestampMs;
        public readonly string SessionId;

        public TelemetryEvent(
            string eventType,
            Dictionary<string, object> eventProperties,
            ContextSnapshot context,
            string deviceId,
            string userId,
            long timestampMs,
            string sessionId) {
            Id = System.Guid.NewGuid();
            Recording = TestingFloorRecording.Current;
            EventType = eventType;
            EventProperties = eventProperties;
            Context = context;
            DeviceId = deviceId;
            UserId = userId;
            TimestampMs = timestampMs;
            SessionId = sessionId;
        }
    }
}
