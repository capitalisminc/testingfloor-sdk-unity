using UnityEngine;

namespace TestingFloorSdk {
    [CreateAssetMenu(fileName = "TestingFloorSettings", menuName = "Testing Floor/Settings", order = 999)]
    public sealed class TestingFloorSettings : ScriptableObject {
        public bool enabled = true;
        public bool enableInEditor = false;
        public bool logErrors = false;
        public bool logEventSends = false;

        public string writeKey;
        public string endpoint = "https://dataentry.testingfloor.com";

        public bool qrHeartbeatsEnabled = false;
        public bool qrHeartbeatsEnabledWithRecorderSession = false;
        public bool qrHeartbeatInverted = true;
        public const float DefaultQrHeartbeatIntervalSeconds = 15f;

        public float qrHeartbeatIntervalSeconds = DefaultQrHeartbeatIntervalSeconds;
        public float qrHeartbeatVisibleSeconds = 0f;

        public bool movementTrackingEnabled = false;
        public float movementMinPingDistance = 0.5f;
        public float movementMinPingIntervalSeconds = 0.5f;
        public float movementStartGraceSeconds = 0.05f;
        public float movementStopGraceSeconds = 0.25f;
        public float movementMinStep = 0.005f;
        public float movementMinPingRotationDegrees = 5f;
        public float movementMinStepRotationDegrees = 0.1f;
        public float movementMinPingFovDegrees = 2f;
        public float movementMinStepFovDegrees = 0.05f;
        public float movementMinPingCameraDistance = 0.5f;
        public float movementMinStepCameraDistance = 0.005f;

        public const float DefaultObjectSampleRateHz = 4f;
        public const int DefaultMaxTrackedObjects = 256;

        [Range(1f, 10f)] public float objectSampleRateHz = DefaultObjectSampleRateHz;
        public int maxTrackedObjects = DefaultMaxTrackedObjects;

        public int queueMaxEvents = 4096;
        public int batchMaxEvents = 50;
        public float batchMaxFlushIntervalSeconds = 0.25f;

        internal const string ResourcesKey = "TestingFloorSettings";

        static TestingFloorSettings _current;
        static bool _fallbackWarned;

        public static TestingFloorSettings Current {
            get {
                if (_current != null) return _current;
                _current = Resources.Load<TestingFloorSettings>(ResourcesKey);
                if (_current != null) return _current;
                _current = CreateFallback();
                return _current;
            }
        }

        public bool IsEnabledForBuild => ShouldCollect(Application.isEditor, IsAutomation);

        internal bool ShouldCollect(bool editor, bool automation) => enabled && !automation
            && (!editor || enableInEditor) && !string.IsNullOrWhiteSpace(writeKey) && !string.IsNullOrWhiteSpace(endpoint);

        internal static bool IsAutomation => IsAutomationArgs(System.Environment.GetCommandLineArgs());

        internal static bool IsAutomationArgs(string[] args) => System.Array.Exists(args,
            arg => string.Equals(arg, "-runTests", System.StringComparison.OrdinalIgnoreCase));

        internal float EffectiveQrHeartbeatIntervalSeconds {
            get {
                return Mathf.Max(1f, qrHeartbeatIntervalSeconds);
            }
        }

        internal float EffectiveObjectSampleRateHz {
            get {
                if (float.IsNaN(objectSampleRateHz) || float.IsInfinity(objectSampleRateHz)) return DefaultObjectSampleRateHz;
                return Mathf.Clamp(objectSampleRateHz, 1f, 10f);
            }
        }

        internal int EffectiveMaxTrackedObjects => Mathf.Max(1, maxTrackedObjects);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            _current = null;
            _fallbackWarned = false;
        }

        static TestingFloorSettings CreateFallback() {
            var inst = CreateInstance<TestingFloorSettings>();
            inst.name = "TestingFloorSettings (In-Memory Fallback)";
            inst.enabled = false;
            inst.writeKey = string.Empty;
            inst.hideFlags = HideFlags.DontUnloadUnusedAsset;
#if UNITY_EDITOR
            if (!_fallbackWarned) {
                _fallbackWarned = true;
                Debug.LogWarning($"[TestingFloor] No TestingFloorSettings asset found at Resources/{ResourcesKey}. Using in-memory fallback with telemetry disabled. Create one via Tools → Testing Floor → Create Settings Asset.");
            }
#endif
            return inst;
        }
    }
}
