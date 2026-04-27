using UnityEditor;
using UnityEngine;

namespace TestingFloor.Editor {
    [CustomEditor(typeof(TestingFloorSettings))]
    public sealed class TestingFloorSettingsInspector : UnityEditor.Editor {
        SerializedProperty _enabled;
        SerializedProperty _enableInEditor;
        SerializedProperty _logErrors;
        SerializedProperty _logEventSends;
        SerializedProperty _writeKey;
        SerializedProperty _endpoint;
        SerializedProperty _qrEnabled;
        SerializedProperty _qrInverted;
        SerializedProperty _qrInterval;
        SerializedProperty _qrVisibleSeconds;
        SerializedProperty _movementEnabled;
        SerializedProperty _movementMinPingDistance;
        SerializedProperty _movementMinPingIntervalSeconds;
        SerializedProperty _movementStartGraceSeconds;
        SerializedProperty _movementStopGraceSeconds;
        SerializedProperty _movementMinStep;
        bool _showAdvancedQrTiming;
        bool _showAdvancedMovementTuning;

        void OnEnable() {
            _enabled = serializedObject.FindProperty("enabled");
            _enableInEditor = serializedObject.FindProperty("enableInEditor");
            _logErrors = serializedObject.FindProperty("logErrors");
            _logEventSends = serializedObject.FindProperty("logEventSends");
            _writeKey = serializedObject.FindProperty("writeKey");
            _endpoint = serializedObject.FindProperty("endpoint");
            _qrEnabled = serializedObject.FindProperty("qrHeartbeatsEnabled");
            _qrInverted = serializedObject.FindProperty("qrHeartbeatInverted");
            _qrInterval = serializedObject.FindProperty("qrHeartbeatIntervalSeconds");
            _qrVisibleSeconds = serializedObject.FindProperty("qrHeartbeatVisibleSeconds");
            _movementEnabled = serializedObject.FindProperty("movementTrackingEnabled");
            _movementMinPingDistance = serializedObject.FindProperty("movementMinPingDistance");
            _movementMinPingIntervalSeconds = serializedObject.FindProperty("movementMinPingIntervalSeconds");
            _movementStartGraceSeconds = serializedObject.FindProperty("movementStartGraceSeconds");
            _movementStopGraceSeconds = serializedObject.FindProperty("movementStopGraceSeconds");
            _movementMinStep = serializedObject.FindProperty("movementMinStep");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enabled);
            EditorGUILayout.PropertyField(_enableInEditor);
            EditorGUILayout.PropertyField(_logErrors);
            EditorGUILayout.PropertyField(_logEventSends);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Collector", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_writeKey);
            if (string.IsNullOrWhiteSpace(_writeKey.stringValue)) {
                EditorGUILayout.HelpBox("Write key is required for telemetry sends.", MessageType.Warning);
            }
            EditorGUILayout.PropertyField(_endpoint);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("QR Heartbeat", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_qrEnabled);
            using (new EditorGUI.DisabledScope(!_qrEnabled.boolValue)) {
                EditorGUILayout.PropertyField(
                    _qrInverted,
                    new GUIContent("Inverted", "White modules on a black background. Disable for the normal black-on-white QR style.")
                );
                _showAdvancedQrTiming = EditorGUILayout.Foldout(_showAdvancedQrTiming, "Advanced Timing", true);
                if (_showAdvancedQrTiming) {
                    EditorGUILayout.HelpBox(
                        "Leave QR timing at its defaults unless Testing Floor support asks you to adjust sync behavior.",
                        MessageType.Info
                    );
                    EditorGUILayout.PropertyField(
                        _qrInterval,
                        new GUIContent("Interval Seconds", "Advanced sync setting. Defaults to 15 seconds between QR transitions; tuning is not usually recommended.")
                    );
                    EditorGUILayout.PropertyField(
                        _qrVisibleSeconds,
                        new GUIContent("Visible Seconds", "Advanced sync setting. 0 keeps the QR visible continuously.")
                    );
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Movement Tracking", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _movementEnabled,
                new GUIContent("Enabled", "Emit a debounced tf_player_moved event when the registered position source moves. Requires TestingFloor.SetPositionSource(...) at runtime.")
            );
            using (new EditorGUI.DisabledScope(!_movementEnabled.boolValue)) {
                _showAdvancedMovementTuning = EditorGUILayout.Foldout(_showAdvancedMovementTuning, "Advanced Tuning", true);
                if (_showAdvancedMovementTuning) {
                    EditorGUILayout.HelpBox(
                        "Defaults are tuned for a humanoid character on a 1-unit-per-meter scale. Tune if your project uses a different scale.",
                        MessageType.Info
                    );
                    EditorGUILayout.PropertyField(
                        _movementMinPingDistance,
                        new GUIContent("Min Ping Distance", "Accumulated distance required between ping events.")
                    );
                    EditorGUILayout.PropertyField(
                        _movementMinPingIntervalSeconds,
                        new GUIContent("Min Ping Interval (s)", "Minimum seconds between consecutive ping events.")
                    );
                    EditorGUILayout.PropertyField(
                        _movementStartGraceSeconds,
                        new GUIContent("Start Grace (s)", "How long sustained motion is required before a 'start' event fires.")
                    );
                    EditorGUILayout.PropertyField(
                        _movementStopGraceSeconds,
                        new GUIContent("Stop Grace (s)", "How long stillness is required before a 'stop' event fires.")
                    );
                    EditorGUILayout.PropertyField(
                        _movementMinStep,
                        new GUIContent("Min Step", "Per-frame movement smaller than this is treated as jitter and ignored.")
                    );
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
