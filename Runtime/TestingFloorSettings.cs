using UnityEngine;

namespace TestingFloorSdk {
    /// <summary>One output space for known spatial properties and tracked objects.</summary>
    [System.Serializable]
    public sealed class CoordinateTransform {
        public bool enabled;
        public Vector3Int axes = new(1, 2, 3);
        public float scale = 1f;
        public Vector3 offset = Vector3.zero;
    }

    internal sealed class CoordinateSpace {
        readonly Vector3Int _axes;
        readonly float _scale;
        readonly Vector3 _offset;

        CoordinateSpace(CoordinateTransform config) {
            _axes = config.axes;
            _scale = config.scale;
            _offset = config.offset;
        }

        internal static bool TryCapture(CoordinateTransform config, out CoordinateSpace space) {
            space = null;
            if (config == null || !config.enabled) return true;
            var used = new System.Collections.Generic.HashSet<int>();
            for (var i = 0; i < 3; i++) {
                var axis = config.axes[i];
                if (axis < -3 || axis > 3 || axis == 0 || !used.Add(System.Math.Abs(axis))) return false;
            }
            if (!Finite(config.scale) || config.scale <= 0 ||
                !Finite(config.offset.x) || !Finite(config.offset.y) || !Finite(config.offset.z)) return false;
            space = new CoordinateSpace(config);
            return true;
        }

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        Vector3 Direction(Vector3 point) {
            var result = Vector3.zero;
            for (var i = 0; i < 3; i++) {
                var axis = _axes[i];
                result[i] = point[System.Math.Abs(axis) - 1] * (axis > 0 ? 1 : -1);
            }
            return result;
        }

        Vector3 Point(Vector3 point) => Direction(point) * _scale + _offset;

        static bool Number(System.Collections.Generic.Dictionary<string, object> properties, string key, out double number) {
            number = 0;
            if (properties == null || !properties.TryGetValue(key, out var value) ||
                !(value is double || value is float || value is long || value is int)) return false;
            number = System.Convert.ToDouble(value);
            return Finite(number);
        }

        internal void Properties(System.Collections.Generic.Dictionary<string, object> properties) {
            if (properties == null) return;
            foreach (var prefix in new[] { "player.position", "camera.position", "camera.focus", "teleport.start.position", "teleport.end.position", "camera.forward", "camera.up" }) {
                if (!Number(properties, prefix + ".x", out var x) || !Number(properties, prefix + ".y", out var y) || !Number(properties, prefix + ".z", out var z)) continue;
                var point = new Vector3((float)x, (float)y, (float)z);
                var output = prefix == "camera.forward" || prefix == "camera.up" ? Direction(point) : Point(point);
                properties[prefix + ".x"] = (double)output.x;
                properties[prefix + ".y"] = (double)output.y;
                properties[prefix + ".z"] = (double)output.z;
            }
            foreach (var key in new[] { "movement.distance", "movement.camera_distance", "movement.ortho_width_delta", "teleport.distance", "camera.ortho_width" }) {
                if (Number(properties, key, out var value)) properties[key] = value * _scale;
            }
            foreach (var axis in new[] { "x", "y", "z" }) properties.Remove("camera.euler." + axis);
        }

        internal void Objects(System.Collections.Generic.Dictionary<string, object> properties) {
            if (!properties.TryGetValue("sample.x", out var xValues) || xValues is not double[] xs ||
                !properties.TryGetValue("sample.y", out var yValues) || yValues is not double[] ys ||
                !properties.TryGetValue("sample.z", out var zValues) || zValues is not double[] zs) return;
            for (var i = 0; i < xs.Length; i++) {
                var point = Point(new Vector3((float)xs[i], (float)ys[i], (float)zs[i]));
                xs[i] = point.x;
                ys[i] = point.y;
                zs[i] = point.z;
            }
        }
    }

    [CreateAssetMenu(fileName = "TestingFloorSettings", menuName = "Testing Floor/Settings", order = 999)]
    public sealed class TestingFloorSettings : ScriptableObject {
        public bool enabled = true;
        public bool recordingOnly = false;
        public string buildId;
        public CoordinateTransform coordinateTransform = new();
        public bool enableInEditor = false;
        public bool enableInEditorWhileRecording = true;
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

        public bool movementTrackingEnabled = true;
        public float movementMinPingDistance = 0.5f;
        public float movementMinPingIntervalSeconds = 0.1f;
        public float movementStartGraceSeconds = 0.05f;
        public float movementStopGraceSeconds = 0.25f;
        public float movementMinStep = 0.005f;
        public float movementMinPingRotationDegrees = 5f;
        public float movementMinStepRotationDegrees = 0.1f;
        public float movementMinPingFovDegrees = 2f;
        public float movementMinStepFovDegrees = 0.05f;
        public float movementMinPingCameraDistance = 0.5f;
        public float movementMinStepCameraDistance = 0.005f;
        public float movementMinPingOrthoWidth = 0.5f;
        public float movementMinStepOrthoWidth = 0.005f;

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

        public bool IsEnabledForBuild => ShouldCollect(Application.isEditor, IsAutomation, TestingFloorRecording.Current?.HasLease == true);

        internal bool ShouldCollect(bool editor, bool automation, bool activeRecording = false) => enabled && CanDeliver(automation)
            && (!recordingOnly || activeRecording) && (!editor || enableInEditor || (enableInEditorWhileRecording && activeRecording));

        // Delivery drains accepted records after collection stops; editor policy gates producers.
        internal bool CanDeliver(bool automation) => !automation && !string.IsNullOrWhiteSpace(writeKey) && !string.IsNullOrWhiteSpace(endpoint);

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
