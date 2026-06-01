using System;
using System.Collections.Generic;
using UnityEngine;

namespace TestingFloor {
    internal static class TeleportTracker {
        const string TeleportEventName = "player_teleported";
        const string TeleportKindKey = "teleport.kind";
        const string TeleportReasonKey = "teleport.reason";
        const string TeleportDurationKey = "teleport.duration";
        const string TeleportDistanceKey = "teleport.distance";
        const string TeleportStartXKey = "teleport.start.position.x";
        const string TeleportStartYKey = "teleport.start.position.y";
        const string TeleportStartZKey = "teleport.start.position.z";
        const string TeleportEndXKey = "teleport.end.position.x";
        const string TeleportEndYKey = "teleport.end.position.y";
        const string TeleportEndZKey = "teleport.end.position.z";

        static readonly Dictionary<int, ActiveTeleport> s_active = new();
        static int _nextId;
        static bool _movementResetRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            s_active.Clear();
            _nextId = 0;
            _movementResetRequested = false;
        }

        internal static bool IsTeleporting => s_active.Count > 0;

        internal static TeleportScope Begin(string reason) {
            var id = ++_nextId;
            s_active[id] = new ActiveTeleport(
                startPosition: global::TestingFloor.TestingFloor.GetPlayerPosition(),
                startTime: Time.unscaledTime,
                reason: reason
            );
            return new TeleportScope(id);
        }

        internal static void End(int id, bool cancelled) {
            if (!s_active.TryGetValue(id, out var active)) return;
            s_active.Remove(id);

            _movementResetRequested = true;
            if (cancelled) return;

            var endPosition = global::TestingFloor.TestingFloor.GetPlayerPosition();
            if (!active.StartPosition.HasValue || !endPosition.HasValue) return;

            var durationSeconds = Math.Max(0f, Time.unscaledTime - active.StartTime);
            Record(active.StartPosition.Value, endPosition.Value, active.Reason, durationSeconds);
        }

        internal static void Record(Vector3 start, Vector3 end, string reason, double durationSeconds) {
            _movementResetRequested = true;

            var distance = Vector3.Distance(start, end);
            if (distance < MinDistance()) return;

            var builder = global::TestingFloor.TestingFloor.Track(TeleportEventName)
                .Set(TeleportKindKey, "teleport")
                .Set(TeleportDurationKey, Math.Max(0.0, durationSeconds))
                .Set(TeleportDistanceKey, (double)distance)
                .Set(TeleportStartXKey, (double)start.x)
                .Set(TeleportStartYKey, (double)start.y)
                .Set(TeleportStartZKey, (double)start.z)
                .Set(TeleportEndXKey, (double)end.x)
                .Set(TeleportEndYKey, (double)end.y)
                .Set(TeleportEndZKey, (double)end.z);

            if (!string.IsNullOrWhiteSpace(reason)) {
                builder = builder.Set(TeleportReasonKey, reason);
            }

            builder.Send();
        }

        internal static bool ConsumeMovementResetRequested() {
            if (!_movementResetRequested) return false;
            _movementResetRequested = false;
            return true;
        }

        static float MinDistance() {
            var settings = TestingFloorSettings.Current;
            return settings == null ? 0f : Mathf.Max(0f, settings.movementMinStep);
        }

        readonly struct ActiveTeleport {
            public readonly Vector3? StartPosition;
            public readonly float StartTime;
            public readonly string Reason;

            public ActiveTeleport(Vector3? startPosition, float startTime, string reason) {
                StartPosition = startPosition;
                StartTime = startTime;
                Reason = reason;
            }
        }
    }
}
