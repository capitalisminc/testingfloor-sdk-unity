using System;
using UnityEngine;

namespace TestingFloor {
    [DisallowMultipleComponent]
    internal sealed class MovementTracker : MonoBehaviour {
        const string MovementEventName = "player_moved";
        const string MovementReasonKey = "movement.reason";
        const string MovementDistanceKey = "movement.distance";
        const string MovementDurationKey = "movement.duration";
        const string MovementRotationKey = "movement.rotation_degrees";
        const string MovementFovDeltaKey = "movement.fov_delta";
        const string MovementCameraDistanceKey = "movement.camera_distance";

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
            var sample = BuildSample();
            _sampler.Step(Time.unscaledTime, sample, _emit);
        }

        static ActivitySample BuildSample() {
            var playerPosition = global::TestingFloor.TestingFloor.GetPlayerPosition();
            var camera = global::TestingFloor.TestingFloor.GetCamera();
            if (camera == null) {
                return new ActivitySample(playerPosition: playerPosition);
            }
            var t = camera.transform;
            return new ActivitySample(
                playerPosition: playerPosition,
                cameraPosition: t.position,
                cameraRotation: t.rotation,
                cameraFov: camera.fieldOfView);
        }

        void ConfigureFromSettings() {
            var settings = TestingFloorSettings.Current;
            if (settings == null) return;
            _sampler.MinPingDistance = Mathf.Max(0f, settings.movementMinPingDistance);
            _sampler.MinPingIntervalSeconds = Mathf.Max(0f, settings.movementMinPingIntervalSeconds);
            _sampler.StartGraceSeconds = Mathf.Max(0f, settings.movementStartGraceSeconds);
            _sampler.StopGraceSeconds = Mathf.Max(0f, settings.movementStopGraceSeconds);
            _sampler.MinStep = Mathf.Max(0f, settings.movementMinStep);
            _sampler.MinPingRotationDegrees = Mathf.Max(0f, settings.movementMinPingRotationDegrees);
            _sampler.MinStepRotationDegrees = Mathf.Max(0f, settings.movementMinStepRotationDegrees);
            _sampler.MinPingFovDegrees = Mathf.Max(0f, settings.movementMinPingFovDegrees);
            _sampler.MinStepFovDegrees = Mathf.Max(0f, settings.movementMinStepFovDegrees);
            _sampler.MinPingCameraDistance = Mathf.Max(0f, settings.movementMinPingCameraDistance);
            _sampler.MinStepCameraDistance = Mathf.Max(0f, settings.movementMinStepCameraDistance);
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
            if (ev.RotationDegrees > 0f) {
                builder = builder.Set(MovementRotationKey, (double)ev.RotationDegrees);
            }
            if (ev.FovDelta > 0f) {
                builder = builder.Set(MovementFovDeltaKey, (double)ev.FovDelta);
            }
            if (ev.CameraDistance > 0f) {
                builder = builder.Set(MovementCameraDistanceKey, (double)ev.CameraDistance);
            }
            builder.Send();
        }
    }
}
