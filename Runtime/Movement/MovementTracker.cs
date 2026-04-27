using System;
using UnityEngine;

namespace TestingFloor {
    [DisallowMultipleComponent]
    internal sealed class MovementTracker : MonoBehaviour {
        const string MovementEventName = "player_moved";
        const string MovementReasonKey = "movement.reason";
        const string MovementDistanceKey = "movement.distance";
        const string MovementDurationKey = "movement.duration";

        MovementSampler _sampler;
        Action<MovementEvent> _emit;

        void Awake() {
            _emit = TrackMovementEvent;
            ConfigureFromSettings();
        }

        void OnEnable() {
            _sampler.Reset();
            ConfigureFromSettings();
        }

        void Update() {
            ConfigureFromSettings();
            _sampler.Step(Time.unscaledTime, global::TestingFloor.TestingFloor.GetPlayerPosition(), _emit);
        }

        void ConfigureFromSettings() {
            var settings = TestingFloorSettings.Current;
            if (settings == null) return;
            _sampler.MinPingDistance = Mathf.Max(0f, settings.movementMinPingDistance);
            _sampler.MinPingIntervalSeconds = Mathf.Max(0f, settings.movementMinPingIntervalSeconds);
            _sampler.StartGraceSeconds = Mathf.Max(0f, settings.movementStartGraceSeconds);
            _sampler.StopGraceSeconds = Mathf.Max(0f, settings.movementStopGraceSeconds);
            _sampler.MinStep = Mathf.Max(0f, settings.movementMinStep);
        }

        static void TrackMovementEvent(MovementEvent ev) {
            var builder = global::TestingFloor.TestingFloor.Track(MovementEventName)
                .Set(MovementReasonKey, ev.Reason);
            if (ev.Distance > 0f) {
                builder = builder.Set(MovementDistanceKey, (double)ev.Distance);
            }
            if (ev.Duration > 0f) {
                builder = builder.Set(MovementDurationKey, (double)ev.Duration);
            }
            builder.Send();
        }
    }
}
