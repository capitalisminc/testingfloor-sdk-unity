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
        const double StatePollIntervalSeconds = 1.0;
        internal const string StateRecordName = "$tf_state";
        internal const string ObjectsRecordName = "$tf_objects";
        const string LevelKeyPrefix = "game_level.";

        static TestingFloorSettings _settings;
        static CollectorClient _client;
        static ITelemetryContextProvider _contextProvider;
        static ITelemetryContextProvider _stateProvider;
        static Dictionary<string, object> _directState = new(StringComparer.Ordinal);
        static SessionStateTracker _stateTracker = new();
        static ObjectTracker _objects = new();
        static Dictionary<string, object> _levelValues = new(StringComparer.Ordinal);
        static readonly List<Dictionary<string, object>> s_objectRecords = new();
        static long _seq;
        static int _droppedSeen;
        static double _nextStatePollAt;
        static double _nextObjectCheckAt;
        static string _objectsRecordingUuid;
        static long _lastObjectsTimestampMs;
        static Func<TelemetryEvent, bool> s_captureForTesting;
        static IBatchSender s_senderForTesting;
        static int s_bufferingDepth;
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
            _stateProvider = null;
            _directState = new Dictionary<string, object>(StringComparer.Ordinal);
            _stateTracker = new SessionStateTracker();
            _objects = new ObjectTracker();
            _levelValues = new Dictionary<string, object>(StringComparer.Ordinal);
            s_objectRecords.Clear();
            // Seq counts every record queued in one play session, from 1.
            _seq = 0;
            _droppedSeen = 0;
            _nextStatePollAt = 0;
            _nextObjectCheckAt = 0;
            _objectsRecordingUuid = null;
            _lastObjectsTimestampMs = 0;
            s_captureForTesting = null;
            s_senderForTesting = null;
            s_bufferingDepth = 0;
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

        /// <summary>
        /// Registers the provider for small values that describe each moment,
        /// such as level, health and game clock. Its values are added to every
        /// event. Pass null to clear it.
        /// </summary>
        public static void RegisterContextProvider(ITelemetryContextProvider provider) {
            _contextProvider = provider;
        }

        /// <summary>
        /// Registers the provider for values that stay true until they change,
        /// such as settings, device, build, inventory and outfit. The SDK reads
        /// it before each event and once a second, and sends only changed
        /// values as session state. Pass null to clear it.
        /// </summary>
        public static void RegisterStateProvider(ITelemetryContextProvider provider) {
            _stateProvider = provider;
        }

        /// <summary>Sets one session state value directly. A null string clears the key.</summary>
        public static void SetState(string key, string value) => PutState(key, value);
        public static void SetState(string key, long value) => PutState(key, value);
        public static void SetState(string key, double value) => PutState(key, value);
        public static void SetState(string key, bool value) => PutState(key, value);

        /// <summary>Clears a value set with <c>SetState</c>. The next state record sends it as null.</summary>
        public static void ClearState(string key) {
            if (key != null) _directState.Remove(key);
        }

        static void PutState(string key, object value) {
            if (!ContextSnapshot.IsPropertyKey(key)) return;
            if (value == null) {
                _directState.Remove(key);
                return;
            }
            if (value is double d && (double.IsNaN(d) || double.IsInfinity(d))) return;
            _directState[key] = value;
        }

        /// <summary>
        /// Tracks an object's position from its transform. The SDK samples it
        /// while it moves and removes it with the reason <c>destroyed</c> when
        /// the GameObject is destroyed. Without an id, the SDK assigns
        /// <c>kind#n</c>. Supply the id your events use for the object so
        /// events and its path line up.
        /// </summary>
        public static TrackedObject TrackObject(string kind, Transform transform, string id = null) {
            if (transform == null) return new TrackedObject(null, kind, id);
            return Register(kind, transform, null, id);
        }

        /// <summary>
        /// Tracks an object's position from a function. Return null when the
        /// object is gone; the SDK then removes it with the reason <c>gone</c>.
        /// </summary>
        public static TrackedObject TrackObject(string kind, Func<Vector3?> position, string id = null) {
            if (position == null) return new TrackedObject(null, kind, id);
            return Register(kind, null, position, id);
        }

        static TrackedObject Register(string kind, Transform transform, Func<Vector3?> position, string id) {
            if (!IsCollecting) return new TrackedObject(null, kind, id);
            ConfigureObjects();
            return new TrackedObject(_objects.Register(kind, transform, position, id), kind, id);
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

        internal static bool IsCollecting {
            get {
                EnsureInitialized();
                return _state == TelemetryState.Ok || _state == TelemetryState.NetworkDown;
            }
        }

        internal static bool TrackEventInternal(string eventType, Dictionary<string, object> eventProperties) {
            if (!IsCollecting) {
                TelemetryQueue.ReturnProperties(eventProperties);
                return false;
            }
            if (string.IsNullOrWhiteSpace(eventType)) {
                TelemetryQueue.ReturnProperties(eventProperties);
                return false;
            }

            // An event that a state provider or position callback sends while the SDK polls state or
            // checks objects neither polls them again nor closes the recording; the active poll or
            // check finishes first, and the next poll closes the recording.
            if (s_bufferingDepth == 0) {
                RecordingTelemetry.Poll();
                // A state change is queued before the event it applies to.
                PollState(Time.unscaledTimeAsDouble);
            }


            var context = BuildContext();
            RememberLevel(context.Properties, eventProperties);
            var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var ev = new TelemetryEvent(
                eventType: eventType,
                eventProperties: eventProperties,
                context: context,
                deviceId: Identity.DeviceId,
                userId: UserId,
                timestampMs: timestampMs,
                sessionId: PlaySessionId,
                seq: _seq + 1
            );

            return Enqueue(ev);
        }

        static string UserId => !string.IsNullOrWhiteSpace(Identity.ProfileId) ? Identity.ProfileId : null;

        /// <summary>Called every frame by the session driver, in unscaled time.</summary>
        internal static void Tick(double now) {
            if (!IsCollecting) return;
            TestingFloorTrackedObject.RegisterWaiting();

            if (now >= _nextStatePollAt) {
                _nextStatePollAt = now + StatePollIntervalSeconds;
                var dropped = TelemetryQueue.DroppedEventCount;
                if (dropped != _droppedSeen) {
                    // Readers recover from a lost record at the next keyframe.
                    _droppedSeen = dropped;
                    _stateTracker.RequestKeyframe();
                    _objects.RequestKeyframe();
                }
                PollState(now);
            }

            if (now >= _nextObjectCheckAt) {
                ConfigureObjects();
                _nextObjectCheckAt = now + 1.0 / (_settings != null ? _settings.EffectiveObjectSampleRateHz : TestingFloorSettings.DefaultObjectSampleRateHz);
                CheckObjects(now);
            }
        }

        static void PollState(double now) {
            s_bufferingDepth++;
            try {
                PollStateNow(now);
            }
            finally {
                s_bufferingDepth--;
            }
        }

        static void PollStateNow(double now) {
            var snapshot = ContextSnapshot.Create(default);
            try {
                BuiltInContextProvider.FillState(ref snapshot);
            }
            catch (Exception ex) {
                if (_settings != null && _settings.logErrors) {
                    Debug.LogWarning($"[TestingFloor] Built-in state provider threw: {ex}");
                }
            }
            try {
                _stateProvider?.FillSnapshot(ref snapshot);
            }
            catch (Exception ex) {
                if (_settings != null && _settings.logErrors) {
                    Debug.LogWarning($"[TestingFloor] State provider threw: {ex}");
                }
            }
            foreach (var kvp in _directState) {
                ContextSnapshot.Put(ref snapshot.Properties, kvp.Key, kvp.Value);
            }

            var recordingUuid = TestingFloorRecording.Current?.RecordingUuid;
            var properties = _stateTracker.Commit(now, snapshot.Properties, recordingUuid, out var keyframe);
            if (properties == null) return;
            QueueRecord(RecordKind.State, StateRecordName, properties, keyframe, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        // Returns whether the queue refused any of the check's records.
        static bool CheckObjects(double now) {
            s_bufferingDepth++;
            try {
                return CheckObjectsNow(now);
            }
            finally {
                s_bufferingDepth--;
            }
        }

        static bool CheckObjectsNow(double now) {
            // A Play run that joins a new recording repeats every live object, as state does.
            var recordingUuid = TestingFloorRecording.Current?.RecordingUuid;
            if (!string.Equals(recordingUuid, _objectsRecordingUuid, StringComparison.Ordinal)) {
                _objectsRecordingUuid = recordingUuid;
                if (recordingUuid != null) _objects.RequestKeyframe();
            }
            var keyframe = _objects.Check(now, s_objectRecords);
            if (s_objectRecords.Count == 0) return false;
            // Split records of one check share one $time. Each check gets a later $time than the
            // last, so readers that order by time keep consecutive checks apart.
            var timestampMs = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _lastObjectsTimestampMs + 1);
            _lastObjectsTimestampMs = timestampMs;
            var refused = false;
            for (var i = 0; i < s_objectRecords.Count; i++) {
                var properties = s_objectRecords[i];
                foreach (var kvp in _levelValues) properties[kvp.Key] = kvp.Value;
                // A refused record's properties go back to the pool, so read what a retry needs first.
                var removals = ObjectTracker.RemovalsIn(properties);
                var sampleIds = properties.TryGetValue("sample.id", out var ids) ? ids as string[] : null;
                if (QueueRecord(RecordKind.Objects, ObjectsRecordName, properties, keyframe, timestampMs)) continue;
                refused = true;
                _objects.Prioritize(sampleIds);
                // Removals that do not fit the retry backlog are lost like any dropped record.
                var overflow = _objects.Retry(removals);
                if (overflow > 0) TelemetryQueue.CountDropped(overflow, recording: TestingFloorRecording.Current != null);
            }
            s_objectRecords.Clear();
            return refused;
        }

        static bool QueueRecord(RecordKind kind, string name, Dictionary<string, object> properties, bool keyframe, long timestampMs) {
            var ev = new TelemetryEvent(
                eventType: name,
                eventProperties: properties,
                context: ContextSnapshot.Create(CurrentPlatform()),
                deviceId: Identity.DeviceId,
                userId: UserId,
                timestampMs: timestampMs,
                sessionId: PlaySessionId,
                seq: _seq + 1,
                keyframe: keyframe,
                kind: kind
            );
            return Enqueue(ev);
        }

        // A record the queue refuses was never queued, so it does not use a seq.
        static bool Enqueue(TelemetryEvent ev) {
            var capture = s_captureForTesting;
            var queued = capture != null ? capture(ev) : TelemetryQueue.Enqueue(ev, _settings, GetClient, ApplySendResult);
            if (queued) _seq = ev.Seq;
            return queued;
        }

        // `$tf_objects` records copy the level of the most recent event.
        static void RememberLevel(Dictionary<string, object> contextProperties, Dictionary<string, object> eventProperties) {
            _levelValues.Clear();
            CopyLevelValues(contextProperties);
            CopyLevelValues(eventProperties);
        }

        static void CopyLevelValues(Dictionary<string, object> properties) {
            if (properties == null) return;
            foreach (var kvp in properties) {
                if (kvp.Key.StartsWith(LevelKeyPrefix, StringComparison.Ordinal)) _levelValues[kvp.Key] = kvp.Value;
            }
        }

        static void ConfigureObjects() {
            _objects.MaxLiveObjects = _settings != null ? _settings.EffectiveMaxTrackedObjects : TestingFloorSettings.DefaultMaxTrackedObjects;
            _objects.LogErrors = _settings != null && _settings.logErrors;
        }

        static PlatformContext CurrentPlatform() {
            if (!_platformContextCaptured) {
                _platformContext = PlatformContext.Capture();
                _platformContextCaptured = true;
            }
            return _platformContext.WithScreenSize(Screen.width, Screen.height);
        }

        /// <summary>
        /// Test seam: hand each record to <paramref name="capture"/> instead of
        /// the delivery queue. Automated tests never send live telemetry.
        /// </summary>
        internal static void CaptureRecordsForTesting(Func<TelemetryEvent, bool> capture, TestingFloorSettings settings) {
            ResetStatics();
            TelemetryQueue.ResetStatics();
            _settings = settings;
            _state = TelemetryState.Ok;
            _initialized = true;
            s_captureForTesting = capture;
        }

        /// <summary>
        /// Test seam: deliver records through the real queue to <paramref name="sender"/>.
        /// Automated tests never send live telemetry.
        /// </summary>
        internal static void QueueRecordsForTesting(IBatchSender sender, TestingFloorSettings settings) {
            ResetStatics();
            TelemetryQueue.ResetStatics();
            _settings = settings;
            _state = TelemetryState.Ok;
            _initialized = true;
            s_senderForTesting = sender;
        }

        internal static void ResetForTesting() {
            ResetStatics();
            TelemetryQueue.ResetStatics();
        }

        static ContextSnapshot BuildContext() {
            var snapshot = ContextSnapshot.Create(CurrentPlatform());
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
            if (RegisterRecording(TestingFloorRecording.Current)) {
                var driver = new GameObject("[TestingFloor.RecordingTelemetry]");
                UnityEngine.Object.DontDestroyOnLoad(driver);
                driver.AddComponent<RecordingTelemetryDriver>();
            }
        }

        static IBatchSender GetClient() => s_senderForTesting ?? _client;

        internal static bool RegisterRecording(TestingFloorRecording recording) =>
            RecordingTelemetry.Register(recording, PlaySessionId, EnqueueRecordingCompletion, () => SendBufferedRecords(shuttingDown: false));

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
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), session, recording, seq: _seq + 1);
            Enqueue(marker);
        }

        // Queues the state change and object rows that wait for the next poll or check, while the
        // recording is still open, so they go out with it ahead of its completion marker.
        static void SendBufferedRecords(bool shuttingDown) {
            if (!IsCollecting) return;
            var now = Time.unscaledTimeAsDouble;
            PollState(now);
            // A removal carried to a later check, and the later lifetime of an id that it holds
            // back, each need another check. Stop when the queue refuses one.
            var passes = _objects.WaitingRemovalCount + 2;
            var refused = CheckObjects(now);
            while (!refused && _objects.WaitingCount > 0 && --passes > 0) refused = CheckObjects(now);
            // A refused record already counts as dropped. When a recording closes mid-session, what
            // still waits goes out at later checks without the recording; at shutdown it is lost,
            // and counts toward dropped_events before the completion marker.
            if (shuttingDown) TelemetryQueue.CountDropped(_objects.WaitingCount, recording: TestingFloorRecording.Current != null);
        }

        internal static void FinishRecordingAndFlush() {
            SendBufferedRecords(shuttingDown: true);
            RecordingTelemetry.Finish();
            TelemetryQueue.FlushBlocking(TimeSpan.FromSeconds(QuitFlushTimeoutSeconds), _client, _settings);
        }

        static void OnApplicationQuitting() {
            FinishRecordingAndFlush();
        }
    }
}
