using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestingFloorSdk.Internal;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TestingFloorSdk {
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
            // produce a fresh $session_id.
            _playSessionId = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OnBeforeSceneLoad() {
            RegisterQuitHandler();
            EnsureInitialized();

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
            // Bound the final flush before Unity destroys this Play runtime.
            // A failed flush leaves no successful completion marker.
            FinishRecordingAndFlush();
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
            MovementDriver.Apply();
        }

        public static void UseMainCamera() {
            _cameraSource = () => Camera.main;
            MovementDriver.Apply();
        }

        public static void ClearCameraSource() {
            _cameraSource = null;
            MovementDriver.Apply();
        }

        public static void SetMovementTrackingEnabled(bool enabled) {
            MovementDriver.SetEnabledOverride(enabled);
        }

        public static void UseConfiguredMovementTracking() {
            MovementDriver.ClearEnabledOverride();
        }

        public static TeleportScope BeginTeleport(string reason = null) {
            return TeleportTracker.Begin(reason);
        }

        public static void RecordTeleport(Vector3 start, Vector3 end, string reason = null, double durationSeconds = 0.0) {
            TeleportTracker.Record(start, end, reason, durationSeconds);
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

        internal static bool HasActivitySource => _positionSource != null || _cameraSource != null;

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

        public static int PendingEventCount => TelemetryQueue.PendingEventCount;

        public static int DroppedEventCount => TelemetryQueue.DroppedEventCount;

        /// <summary>
        /// A random id the SDK creates on first launch and keeps in
        /// <c>PlayerPrefs</c>. Sent on every event as <c>$device_id</c>.
        /// Testing Floor shows it as the Player ID only when an event has no
        /// <see cref="ProfileId"/>.
        /// </summary>
        public static string DeviceId => Identity.DeviceId;

        /// <summary>
        /// A random id the SDK creates on first use and keeps in
        /// <c>{Application.persistentDataPath}/synced/profile_id</c>. Sent on
        /// every event as <c>$user_id</c>; Testing Floor shows it as the
        /// Player ID. Null if the SDK can't read or write that file.
        /// </summary>
        public static string ProfileId => Identity.ProfileId;

        /// <summary>
        /// The Player ID that Testing Floor shows for this player:
        /// <see cref="ProfileId"/>, or <see cref="DeviceId"/> when there is no
        /// profile id. Show it to testers (for example on a feedback screen)
        /// so you can assign it to the right tester in Testing Floor. Not a
        /// separate field on the wire.
        /// </summary>
        public static string PlayerId => Identity.PlayerId;

        /// <summary>
        /// The active Testing Floor recording, if the game was launched by the
        /// desktop recorder. Stable across Play stop+restart for the lifetime
        /// of a single recording.
        /// </summary>
        public static TestingFloorRecording Recording => TestingFloorRecording.Current;

        /// <summary>
        /// The SDK's per-Play session id, sent on every event as
        /// <c>$session_id</c>. Testing Floor shows each one as a run. Rotates
        /// on every Play boot (cold start, editor Play stop+restart,
        /// standalone relaunch), so one recording can contain several runs.
        /// Lazily generated; safe to read at any time.
        /// </summary>
        public static string PlaySessionId {
            get {
                if (string.IsNullOrWhiteSpace(_playSessionId)) {
                    _playSessionId = Guid.NewGuid().ToString();
                }
                return _playSessionId;
            }
        }

        internal static bool TrackEventInternal(string eventType, Dictionary<string, object> eventProperties) {
            EnsureInitialized();
            if (_state != TelemetryState.Ok && _state != TelemetryState.NetworkDown) {
                TelemetryQueue.ReturnProperties(eventProperties);
                return false;
            }
            if (string.IsNullOrWhiteSpace(eventType)) {
                TelemetryQueue.ReturnProperties(eventProperties);
                return false;
            }

            RecordingTelemetry.Poll();
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

            return TelemetryQueue.Enqueue(ev, _settings, GetClient, ApplySendResult);
        }

        static ContextSnapshot BuildContext() {
            if (!_platformContextCaptured) {
                _platformContext = PlatformContext.Capture();
                _platformContextCaptured = true;
            }
            var snapshot = ContextSnapshot.Create(_platformContext.WithScreenSize(Screen.width, Screen.height));
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

            if (_forceDisabled || TestingFloorSettings.IsAutomation) {
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
            if (RecordingTelemetry.Register(TestingFloorRecording.Current, PlaySessionId)) {
                var driver = new GameObject("[TestingFloor.RecordingTelemetry]");
                UnityEngine.Object.DontDestroyOnLoad(driver);
                driver.AddComponent<RecordingTelemetryDriver>();
            }
        }

        static IBatchSender GetClient() => _client;

        static void ApplySendResult(SendResult result) {
            if (_state == TelemetryState.Disabled) return;
            _state = result switch {
                SendResult.Success => TelemetryState.Ok,
                SendResult.InvalidRequest => TelemetryState.Ok,
                SendResult.Rejected => TelemetryState.Rejected,
                SendResult.NotConfigured => TelemetryState.NotConfigured,
                _ => TelemetryState.NetworkDown,
            };
        }

        internal static void EnqueueRecordingCompletion(TestingFloorRecording recording, string session) {
            var marker = new TelemetryEvent("$tf_telemetry_complete", new Dictionary<string, object>(),
                ContextSnapshot.Create(_platformContext), Identity.DeviceId, Identity.ProfileId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), session, recording);
            TelemetryQueue.Enqueue(marker, _settings, GetClient, ApplySendResult);
        }

        static void FinishRecordingAndFlush() {
            RecordingTelemetry.Finish();
            TelemetryQueue.FlushBlocking(TimeSpan.FromSeconds(QuitFlushTimeoutSeconds), _client, _settings);
        }

        static void OnApplicationQuitting() {
            FinishRecordingAndFlush();
        }
    }
}
