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
        static PlatformContext _platformContext;
        static bool _platformContextCaptured;
        static bool _initialized;
        static bool _forceDisabled;
        static TelemetryState _state;
        static bool _sessionStartSent;
        static bool _sessionEndSent;
        static bool _quittingHandlerRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            _settings = null;
            _client = null;
            _contextProvider = null;
            _platformContext = default;
            _platformContextCaptured = false;
            _initialized = false;
            _forceDisabled = false;
            _state = TelemetryState.NotConfigured;
            _sessionStartSent = false;
            _sessionEndSent = false;
            _quittingHandlerRegistered = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OnBeforeSceneLoad() {
            RegisterQuitHandler();
            if (_sessionStartSent) return;
            _sessionStartSent = true;
            if (TestingFloorSession.Current != null) {
                Track("tf_session_start").Send();
            }
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
            TrySendSessionEnd();
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

        public static TestingFloorSession Session => TestingFloorSession.Current;

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
            var sessionId = TestingFloorSession.Current?.SessionId;

            var ev = new TelemetryEvent(
                eventType: eventType,
                eventProperties: eventProperties,
                context: context,
                deviceId: Identity.DeviceId,
                userId: userId,
                timestampMs: timestampMs,
                sessionId: sessionId
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
            TrySendSessionEnd();
            if (!TelemetryQueue.HasPending()) return;
            _ = FlushAsync(TimeSpan.FromSeconds(QuitFlushTimeoutSeconds));
        }

        static void TrySendSessionEnd() {
            if (_sessionEndSent) return;
            if (TestingFloorSession.Current == null) return;
            _sessionEndSent = true;
            Track("tf_session_end").Send();
        }
    }
}
