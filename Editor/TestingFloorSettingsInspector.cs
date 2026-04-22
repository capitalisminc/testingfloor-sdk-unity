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
        SerializedProperty _qrInterval;
        SerializedProperty _qrVisibleSeconds;

        void OnEnable() {
            _enabled = serializedObject.FindProperty("enabled");
            _enableInEditor = serializedObject.FindProperty("enableInEditor");
            _logErrors = serializedObject.FindProperty("logErrors");
            _logEventSends = serializedObject.FindProperty("logEventSends");
            _writeKey = serializedObject.FindProperty("writeKey");
            _endpoint = serializedObject.FindProperty("endpoint");
            _qrEnabled = serializedObject.FindProperty("qrHeartbeatsEnabled");
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
                EditorGUILayout.PropertyField(_qrInterval);
                EditorGUILayout.PropertyField(
                    _qrVisibleSeconds,
                    new GUIContent("Visible Seconds", "0 keeps the QR visible continuously. Use 10 for 10-second beacon windows.")
                );
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
