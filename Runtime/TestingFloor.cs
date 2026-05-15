using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestingFloor.Internal;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TestingFloor {
    public static class TestingFloor {
        const float QuitFlushTimeoutSeconds = 2f;

        static TestingFloorSettings _settings;
        static CollectorClient _client;
        static ITelemetryContextProvider _contextProvider;
        static Func<Vector3?> _positionSource;
        static Func<Camera> _cameraSource;
        static PlatformContext _platformContext;
        static bool _platformContextCaptured;
        static bool _initialized;
        static bool _forceDisabled;
        static TelemetryState _state;
        static bool _quittingHandlerRegistered;
        static string _playSessionId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            _settings = null;
            _client = null;
            _contextProvider = null;
            _positionSource = null;
            _cameraSource = null;
            _platformContext = default;
            _platformContextCaptured = false;
            _initialized = false;
            _forceDisabled = false;
            _state = TelemetryState.NotConfigured;
            _quittingHandlerRegistered = false;
            // PlaySessionId rotates per Play boot — the SubsystemRegistration
            // hook fires on every entry into Play (and in standalone builds at
            // startup), so resetting here is what makes Play stop+restart
            // produce a fresh analytics.session_id.
            _playSessionId = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OnBeforeSceneLoad() {
            RegisterQuitHandler();
        }

        static void RegisterQuitHandler() {
            if (_quittingHandlerRegistered) return;
            _quittingHandlerRegistered = true;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        static void RegisterEditorPlayModeHook() {
            EditorApplication.playModeStateChanged -= OnEditorPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnEditorPlayModeStateChanged;
        }

        static void OnEditorPlayModeStateChanged(PlayModeStateChange state) {
            if (state != PlayModeStateChange.ExitingPlayMode) return;
            // Drain pending events before the editor tears down the runtime.
            // The next Play start will get a fresh PlaySessionId (via
            // ResetStatics) so any late events from this Play that didn't make
            // it out are lost — that's intentional.
            _ = FlushAsync(TimeSpan.FromSeconds(QuitFlushTimeoutSeconds));
        }
#endif

        public static EventBuilder Track(string eventType) {
            return new EventBuilder { _eventType = eventType, _properties = null };
        }

        public static Task FlushAsync(TimeSpan timeout) {
            EnsureInitialized();
            if (_state == TelemetryState.Disabled) return Task.CompletedTask;
            return TelemetryQueue.FlushAsync(timeout, GetClient, ApplySendResult, _settings);
        }

        public static void SetForceDisabled(bool disabled) {
            _forceDisabled = disabled;
            _initialized = false;
        }

        public static void RegisterContextProvider(ITelemetryContextProvider provider) {
            _contextProvider = provider;
        }

        public static void SetPositionSource(Func<Vector3?> source) {
            _positionSource = source;
            MovementDriver.Apply();
        }

        public static void SetPositionSource(Transform transform) {
            if (transform == null) {
                ClearPositionSource();
                return;
            }
            _positionSource = () => transform != null ? transform.position : (Vector3?)null;
            MovementDriver.Apply();
        }

        public static void ClearPositionSource() {
            _positionSource = null;
            MovementDriver.Apply();
        }

        public static void SetCameraSource(Func<Camera> source) {
            _cameraSource = source;
        }

        public static void UseMainCamera() {
            _cameraSource = () => Camera.main;
        }

        public static void ClearCameraSource() {
            _cameraSource = null;
        }

        public static void SetMovementTrackingEnabled(bool enabled) {
            MovementDriver.SetEnabledOverride(enabled);
        }

        public static void UseConfiguredMovementTracking() {
            MovementDriver.ClearEnabledOverride();
        }

        internal static Vector3? GetPlayerPosition() {
            var src = _positionSource;
            if (src == null) return null;
            try {
                return src();
            }
            catch (Exception ex) {
                if (_settings != null && _settings.logErrors) {
                    Debug.LogWarning($"[TestingFloor] Position source threw: {ex}");
                }
                return null;
            }
        }

        internal static Camera GetCamera() {
            var src = _cameraSource;
            if (src == null) return null;
            try {
                return src();
            }
            catch (Exception ex) {
                if (_settings != null && _settings.logErrors) {
                    Debug.LogWarning($"[TestingFloor] Camera source threw: {ex}");
                }
                return null;
            }
        }

        internal static bool HasPositionSource => _positionSource != null;

        public static void SetQrHeartbeatsEnabled(bool enabled) {
            QrHeartbeatDriver.SetEnabledOverride(enabled);
        }

        public static void UseConfiguredQrHeartbeats() {
            QrHeartbeatDriver.ClearEnabledOverride();
        }

        public static void SetQrHeartbeatInverted(bool inverted) {
            QrHeartbeatDriver.SetInvertedOverride(inverted);
        }

        public static void UseConfiguredQrHeartbeatColors() {
            QrHeartbeatDriver.ClearInvertedOverride();
        }

        public static TelemetryState State {
            get {
                EnsureInitialized();
                return _state;
            }
        }

        public static bool QrHeartbeatsEnabled => QrHeartbeatDriver.EffectiveEnabled;

        public static bool QrHeartbeatInverted => QrHeartbeatDriver.EffectiveInverted;

        public static string DeviceId => Identity.DeviceId;

        public static string ProfileId => Identity.ProfileId;

        /// <summary>
        /// The active Testing Floor recording, if the game was launched by the
        /// desktop recorder. Stable across Play stop+restart for the lifetime
        /// of a single recording.
        /// </summary>
        public static TestingFloorRecording Recording => TestingFloorRecording.Current;

        /// <summary>
        /// The SDK's per-Play session id. Rotates on every Play boot (cold
        /// start, editor Play stop+restart, standalone relaunch). Lazily
        /// generated; safe to read at any time.
        /// </summary>
        public static string PlaySessionId {
            get {
                if (string.IsNullOrWhiteSpace(_playSessionId)) {
                    _playSessionId = Guid.NewGuid().ToString();
                }
                return _playSessionId;
            }
        }

        internal static void TrackEventInternal(string eventType, Dictionary<string, object> eventProperties) {
            EnsureInitialized();
            if (_state == TelemetryState.Disabled) {
                TelemetryQueue.ReturnProperties(eventProperties);
                return;
            }
            if (string.IsNullOrWhiteSpace(eventType)) {
                TelemetryQueue.ReturnProperties(eventProperties);
                return;
            }

            var context = BuildContext();
            var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var userId = !string.IsNullOrWhiteSpace(Identity.ProfileId) ? Identity.ProfileId : null;

            var ev = new TelemetryEvent(
                eventType: eventType,
                eventProperties: eventProperties,
                context: context,
                deviceId: Identity.DeviceId,
                userId: userId,
                timestampMs: timestampMs,
                sessionId: PlaySessionId
            );

            TelemetryQueue.Enqueue(ev, _settings, GetClient, ApplySendResult);
        }

        static ContextSnapshot BuildContext() {
            if (!_platformContextCaptured) {
                _platformContext = PlatformContext.Capture();
                _platformContextCaptured = true;
            }
            var snapshot = ContextSnapshot.Create(_platformContext);
            try {
                BuiltInContextProvider.Fill(ref snapshot);
            }
            catch (Exception ex) {
                if (_settings != null && _settings.logErrors) {
                    Debug.LogWarning($"[TestingFloor] Built-in context provider threw: {ex}");
                }
            }
            try {
                _contextProvider?.FillSnapshot(ref snapshot);
            }
            catch (Exception ex) {
                if (_settings != null && _settings.logErrors) {
                    Debug.LogWarning($"[TestingFloor] Context provider threw: {ex}");
                }
            }
            return snapshot;
        }

        static void EnsureInitialized() {
            if (_initialized) return;

            if (_forceDisabled) {
                _client = null;
                _state = TelemetryState.Disabled;
                _initialized = true;
                return;
            }

            _settings = TestingFloorSettings.Current;
            if (_settings == null || !_settings.enabled) {
                _client = null;
                _state = TelemetryState.Disabled;
                _initialized = true;
                return;
            }

            if (Application.isEditor && !_settings.enableInEditor) {
                _client = null;
                _state = TelemetryState.Disabled;
                _initialized = true;
                return;
            }

            if (!_settings.IsEnabledForBuild) {
                _client = null;
                _state = TelemetryState.NotConfigured;
                _initialized = true;
                return;
            }

            _client = new CollectorClient(_settings);
            _state = TelemetryState.Ok;
            _initialized = true;
        }

        static CollectorClient GetClient() => _client;

        static void ApplySendResult(SendResult result) {
            if (_state == TelemetryState.Disabled) return;
            _state = result switch {
                SendResult.Success => TelemetryState.Ok,
                SendResult.FatalConfiguration => TelemetryState.NotConfigured,
                _ => TelemetryState.NetworkDown,
            };
        }

        static void OnApplicationQuitting() {
            if (!TelemetryQueue.HasPending()) return;
            _ = FlushAsync(TimeSpan.FromSeconds(QuitFlushTimeoutSeconds));
        }
    }
}
