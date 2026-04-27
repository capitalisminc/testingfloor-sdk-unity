using System;
using UnityEngine;

namespace TestingFloor {
    public static class MovementReasons {
        public const string Start = "start";
        public const string Stop = "stop";
        public const string Ping = "ping";
    }

    internal readonly struct MovementEvent {
        public readonly string Reason;
        public readonly float Distance; // meters covered in the segment this event closes
        public readonly float Duration; // seconds elapsed in the segment this event closes

        public MovementEvent(string reason, float distance = 0f, float duration = 0f) {
            Reason = reason;
            Distance = distance;
            Duration = duration;
        }
    }

    internal struct MovementSampler {
        public float MinPingDistance;
        public float MinPingIntervalSeconds;
        public float StartGraceSeconds;
        public float StopGraceSeconds;
        public float MinStep;

        bool _hasLastSample;
        bool _isMoving;
        float _lastPingTime;
        float _lastMovementTime;
        float _movementStartTime;
        float _runStartTime;
        float _runDistance;
        float _pingDistance;
        Vector3 _lastSamplePosition;

        public bool IsMoving => _isMoving;

        public void Reset() {
            _hasLastSample = false;
            _isMoving = false;
            _movementStartTime = -1f;
            _runDistance = 0f;
            _pingDistance = 0f;
        }

        public void Step(float now, Vector3? position, Action<MovementEvent> emit) {
            if (!position.HasValue) {
                Reset();
                return;
            }

            var pos = position.Value;
            if (!_hasLastSample) {
                Initialize(now, pos);
                return;
            }

            var delta = pos - _lastSamplePosition;
            var deltaSqr = delta.sqrMagnitude;
            _lastSamplePosition = pos;

            var minStepSqr = MinStep * MinStep;
            if (deltaSqr >= minStepSqr) {
                var stepDistance = Mathf.Sqrt(deltaSqr);
                _runDistance += stepDistance;
                _pingDistance += stepDistance;
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
                    }
                }
            }
            if (!_isMoving && deltaSqr < minStepSqr) {
                _movementStartTime = -1f;
            }

            if (_isMoving && now - _lastMovementTime >= StopGraceSeconds) {
                _isMoving = false;
                _movementStartTime = -1f;
                var runDuration = now - _runStartTime;
                emit?.Invoke(new MovementEvent(MovementReasons.Stop, _runDistance, runDuration));
                _runDistance = 0f;
                _pingDistance = 0f;
            }

            if (!ShouldEmitPing(now)) return;
            var pingDuration = now - _lastPingTime;
            var pingDistance = _pingDistance;
            _pingDistance = 0f;
            _lastPingTime = now;
            emit?.Invoke(new MovementEvent(MovementReasons.Ping, pingDistance, pingDuration));
        }

        bool ShouldEmitPing(float now) {
            if (now - _lastPingTime < MinPingIntervalSeconds) return false;
            var minPingSqr = MinPingDistance * MinPingDistance;
            return _pingDistance * _pingDistance >= minPingSqr;
        }

        void Initialize(float now, Vector3 position) {
            _lastSamplePosition = position;
            _lastPingTime = now;
            _lastMovementTime = now;
            _movementStartTime = -1f;
            _runStartTime = now;
            _runDistance = 0f;
            _pingDistance = 0f;
            _hasLastSample = true;
            _isMoving = false;
        }
    }
}
