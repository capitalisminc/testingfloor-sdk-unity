using UnityEngine;

namespace TestingFloor {
    internal static class MovementDriver {
        static GameObject _trackerGo;
        static bool? _enabledOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            _trackerGo = null;
            _enabledOverride = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            Apply();
        }

        internal static bool EffectiveEnabled {
            get {
                if (!TestingFloor.HasPositionSource) return false;
                if (_enabledOverride.HasValue) return _enabledOverride.Value;
                var settings = TestingFloorSettings.Current;
                return settings != null && settings.movementTrackingEnabled;
            }
        }

        internal static void SetEnabledOverride(bool enabled) {
            _enabledOverride = enabled;
            Apply();
        }

        internal static void ClearEnabledOverride() {
            _enabledOverride = null;
            Apply();
        }

        internal static void Apply() {
            if (!Application.isPlaying) return;
            if (EffectiveEnabled) {
                EnsureTracker();
            }
            else {
                DestroyTracker();
            }
        }

        static void EnsureTracker() {
            if (_trackerGo != null) return;
            var go = new GameObject("[TestingFloor.MovementTracker]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<MovementTracker>();
            _trackerGo = go;
        }

        static void DestroyTracker() {
            if (_trackerGo == null) return;
            if (Application.isPlaying) {
                Object.Destroy(_trackerGo);
            }
            else {
                Object.DestroyImmediate(_trackerGo);
            }
            _trackerGo = null;
        }
    }
}
