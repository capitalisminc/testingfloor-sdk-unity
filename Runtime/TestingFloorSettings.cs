using UnityEngine;

namespace TestingFloor {
    [CreateAssetMenu(fileName = "TestingFloorSettings", menuName = "Testing Floor/Settings", order = 999)]
    public sealed class TestingFloorSettings : ScriptableObject {
        public bool enabled = true;
        public bool enableInEditor = false;
        public bool logErrors = false;
        public bool logEventSends = false;

        public string writeKey;
        public string endpoint = "https://dataentry.testingfloor.com";

        public bool qrHeartbeatsEnabled = false;
        public bool qrHeartbeatInverted = true;
        public float qrHeartbeatIntervalSeconds = 30f;
        public float qrHeartbeatVisibleSeconds = 0f;
        [HideInInspector] public int qrHeartbeatVisibleFrames = 6;

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

        public bool IsEnabledForBuild {
            get {
                if (!enabled) return false;
                if (Application.isEditor && !enableInEditor) return false;
                if (string.IsNullOrWhiteSpace(writeKey)) return false;
                if (string.IsNullOrWhiteSpace(endpoint)) return false;
                return true;
            }
        }

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
