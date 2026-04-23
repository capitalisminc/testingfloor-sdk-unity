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
        bool _showAdvancedQrTiming;

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

            serializedObject.ApplyModifiedProperties();
        }
    }
}
