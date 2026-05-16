using System;
using UnityEngine;

namespace TestingFloor {
    public static class MovementReasons {
        public const string Start = "start";
        public const string Stop = "stop";
        public const string Ping = "ping";
    }

    internal readonly struct ActivitySample {
        public readonly Vector3? PlayerPosition;
        public readonly Vector3? CameraPosition;
        public readonly Quaternion? CameraRotation;
        public readonly float? CameraFov;

        public ActivitySample(
            Vector3? playerPosition = null,
            Vector3? cameraPosition = null,
            Quaternion? cameraRotation = null,
            float? cameraFov = null
        ) {
            PlayerPosition = playerPosition;
            CameraPosition = cameraPosition;
            CameraRotation = cameraRotation;
            CameraFov = cameraFov;
        }

        public bool IsEmpty =>
            !PlayerPosition.HasValue
            && !CameraPosition.HasValue
            && !CameraRotation.HasValue
            && !CameraFov.HasValue;
    }

    internal readonly struct MovementEvent {
        public readonly string Reason;
        public readonly float Distance;        // player position meters in the segment
        public readonly float Duration;        // seconds elapsed in the segment
        public readonly float RotationDegrees; // camera rotation degrees in the segment
        public readonly float FovDelta;        // sum of |fov_step| in the segment
        public readonly float CameraDistance;  // camera world position meters in the segment

        public MovementEvent(
            string reason,
            float distance = 0f,
            float duration = 0f,
            float rotationDegrees = 0f,
            float fovDelta = 0f,
            float cameraDistance = 0f
        ) {
            Reason = reason;
            Distance = distance;
            Duration = duration;
            RotationDegrees = rotationDegrees;
            FovDelta = fovDelta;
            CameraDistance = cameraDistance;
        }
    }

    internal struct MovementSampler {
        public float MinPingDistance;
        public float MinPingIntervalSeconds;
        public float StartGraceSeconds;
        public float StopGraceSeconds;
        public float MinStep;

        public float MinPingRotationDegrees;
        public float MinStepRotationDegrees;
        public float MinPingFovDegrees;
        public float MinStepFovDegrees;
        public float MinPingCameraDistance;
        public float MinStepCameraDistance;

        bool _hasLastSample;
        bool _isMoving;
        float _lastPingTime;
        float _lastMovementTime;
        float _movementStartTime;
        float _runStartTime;

        // Run accumulators reset on each Stop emission.
        float _runDistance;
        float _runRotation;
        float _runFov;
        float _runCameraDistance;

        // Ping accumulators reset on each Ping emission and after Start.
        float _pingDistance;
        float _pingRotation;
        float _pingFov;
        float _pingCameraDistance;

        Vector3 _lastSamplePosition;
        bool _hasLastPlayerPosition;
        Vector3 _lastCameraPosition;
        bool _hasLastCameraPosition;
        Quaternion _lastCameraRotation;
        bool _hasLastCameraRotation;
        float _lastCameraFov;
        bool _hasLastCameraFov;

        public bool IsMoving => _isMoving;

        public void Reset() {
            _hasLastSample = false;
            _isMoving = false;
            _movementStartTime = -1f;
            _runDistance = 0f;
            _runRotation = 0f;
            _runFov = 0f;
            _runCameraDistance = 0f;
            _pingDistance = 0f;
            _pingRotation = 0f;
            _pingFov = 0f;
            _pingCameraDistance = 0f;
            _hasLastPlayerPosition = false;
            _hasLastCameraPosition = false;
            _hasLastCameraRotation = false;
            _hasLastCameraFov = false;
        }

        public void Step(float now, in ActivitySample sample, Action<MovementEvent> emit) {
            if (sample.IsEmpty) {
                Reset();
                return;
            }

            if (!_hasLastSample) {
                Initialize(now, sample);
                return;
            }

            var posStep = StepDistance(sample.PlayerPosition, ref _lastSamplePosition, ref _hasLastPlayerPosition);
            var camPosStep = StepDistance(sample.CameraPosition, ref _lastCameraPosition, ref _hasLastCameraPosition);
            var rotStep = StepRotation(sample.CameraRotation, ref _lastCameraRotation, ref _hasLastCameraRotation);
            var fovStep = StepFov(sample.CameraFov, ref _lastCameraFov, ref _hasLastCameraFov);

            var movedPos = posStep >= MinStep;
            var movedCamPos = camPosStep >= MinStepCameraDistance;
            var movedRot = rotStep >= MinStepRotationDegrees;
            var movedFov = fovStep >= MinStepFovDegrees;
            var movedAny = movedPos || movedCamPos || movedRot || movedFov;

            if (movedAny) {
                if (movedPos) {
                    _runDistance += posStep;
                    _pingDistance += posStep;
                }
                if (movedCamPos) {
                    _runCameraDistance += camPosStep;
                    _pingCameraDistance += camPosStep;
                }
                if (movedRot) {
                    _runRotation += rotStep;
                    _pingRotation += rotStep;
                }
                if (movedFov) {
                    _runFov += fovStep;
                    _pingFov += fovStep;
                }
                _lastMovementTime = now;

                if (!_isMoving) {
                    if (_movementStartTime < 0f) {
                        _movementStartTime = now;
                    }
                    if (now - _movementStartTime >= StartGraceSeconds) {
                        _isMoving = true;
                        _runStartTime = _movementStartTime;
                        _movementStartTime = -1f;
                        emit?.Invoke(new MovementEvent(MovementReasons.Start));
                        // Restart the ping window so the first ping after start
                        // measures the segment from the start emit, not from init.
                        _lastPingTime = now;
                        _pingDistance = 0f;
                        _pingRotation = 0f;
                        _pingFov = 0f;
                        _pingCameraDistance = 0f;
                    }
                }
            }
            if (!_isMoving && !movedAny) {
                _movementStartTime = -1f;
            }

            if (_isMoving && now - _lastMovementTime >= StopGraceSeconds) {
                _isMoving = false;
                _movementStartTime = -1f;
                var runDuration = now - _runStartTime;
                emit?.Invoke(new MovementEvent(
                    MovementReasons.Stop,
                    _runDistance,
                    runDuration,
                    _runRotation,
                    _runFov,
                    _runCameraDistance));
                _runDistance = 0f;
                _runRotation = 0f;
                _runFov = 0f;
                _runCameraDistance = 0f;
                _pingDistance = 0f;
                _pingRotation = 0f;
                _pingFov = 0f;
                _pingCameraDistance = 0f;
            }

            if (!ShouldEmitPing(now)) return;
            var pingDuration = now - _lastPingTime;
            var pingDistance = _pingDistance;
            var pingRotation = _pingRotation;
            var pingFov = _pingFov;
            var pingCameraDistance = _pingCameraDistance;
            _pingDistance = 0f;
            _pingRotation = 0f;
            _pingFov = 0f;
            _pingCameraDistance = 0f;
            _lastPingTime = now;
            emit?.Invoke(new MovementEvent(
                MovementReasons.Ping,
                pingDistance,
                pingDuration,
                pingRotation,
                pingFov,
                pingCameraDistance));
        }

        bool ShouldEmitPing(float now) {
            if (now - _lastPingTime < MinPingIntervalSeconds) return false;
            return _pingDistance >= MinPingDistance
                || _pingRotation >= MinPingRotationDegrees
                || _pingFov >= MinPingFovDegrees
                || _pingCameraDistance >= MinPingCameraDistance;
        }

        static float StepDistance(Vector3? current, ref Vector3 last, ref bool hasLast) {
            if (!current.HasValue) {
                hasLast = false;
                return 0f;
            }
            var c = current.Value;
            if (!hasLast) {
                last = c;
                hasLast = true;
                return 0f;
            }
            var step = (c - last).magnitude;
            last = c;
            return step;
        }

        static float StepRotation(Quaternion? current, ref Quaternion last, ref bool hasLast) {
            if (!current.HasValue) {
                hasLast = false;
                return 0f;
            }
            var c = current.Value;
            if (!hasLast) {
                last = c;
                hasLast = true;
                return 0f;
            }
            var step = Quaternion.Angle(last, c);
            last = c;
            return step;
        }

        static float StepFov(float? current, ref float last, ref bool hasLast) {
            if (!current.HasValue) {
                hasLast = false;
                return 0f;
            }
            var c = current.Value;
            if (!hasLast) {
                last = c;
                hasLast = true;
                return 0f;
            }
            var step = Mathf.Abs(c - last);
            last = c;
            return step;
        }

        void Initialize(float now, in ActivitySample sample) {
            _hasLastPlayerPosition = false;
            _hasLastCameraPosition = false;
            _hasLastCameraRotation = false;
            _hasLastCameraFov = false;
            // Seed last-known values without producing a delta on this frame.
            if (sample.PlayerPosition.HasValue) {
                _lastSamplePosition = sample.PlayerPosition.Value;
                _hasLastPlayerPosition = true;
            }
            if (sample.CameraPosition.HasValue) {
                _lastCameraPosition = sample.CameraPosition.Value;
                _hasLastCameraPosition = true;
            }
            if (sample.CameraRotation.HasValue) {
                _lastCameraRotation = sample.CameraRotation.Value;
                _hasLastCameraRotation = true;
            }
            if (sample.CameraFov.HasValue) {
                _lastCameraFov = sample.CameraFov.Value;
                _hasLastCameraFov = true;
            }
            _lastPingTime = now;
            _lastMovementTime = now;
            _movementStartTime = -1f;
            _runStartTime = now;
            _runDistance = 0f;
            _runRotation = 0f;
            _runFov = 0f;
            _runCameraDistance = 0f;
            _pingDistance = 0f;
            _pingRotation = 0f;
            _pingFov = 0f;
            _pingCameraDistance = 0f;
            _hasLastSample = true;
            _isMoving = false;
        }
    }
}
