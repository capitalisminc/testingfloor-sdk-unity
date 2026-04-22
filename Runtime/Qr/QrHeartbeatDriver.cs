using UnityEngine;

namespace TestingFloor {
    internal static class QrHeartbeatDriver {
        static GameObject _overlayGo;
        static bool? _enabledOverride;
        static bool? _invertedOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            _overlayGo = null;
            _enabledOverride = null;
            _invertedOverride = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            Apply();
        }

        internal static bool EffectiveEnabled {
            get {
                if (_enabledOverride.HasValue) return _enabledOverride.Value;
                var settings = TestingFloorSettings.Current;
                return settings != null && settings.qrHeartbeatsEnabled;
            }
        }

        internal static bool EffectiveInverted {
            get {
                if (_invertedOverride.HasValue) return _invertedOverride.Value;
                var settings = TestingFloorSettings.Current;
                return settings == null || settings.qrHeartbeatInverted;
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

        internal static void SetInvertedOverride(bool inverted) {
            _invertedOverride = inverted;
        }

        internal static void ClearInvertedOverride() {
            _invertedOverride = null;
        }

        static void Apply() {
            if (EffectiveEnabled) {
                EnsureOverlay();
            }
            else {
                DestroyOverlay();
            }
        }

        static void EnsureOverlay() {
            if (_overlayGo != null) return;
            var settings = TestingFloorSettings.Current;
            if (settings == null) return;

            var go = new GameObject("[TestingFloor.QrHeartbeat]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<QrHeartbeatOverlay>();
            _overlayGo = go;
        }

        static void DestroyOverlay() {
            if (_overlayGo == null) return;
            if (Application.isPlaying) {
                Object.Destroy(_overlayGo);
            }
            else {
                Object.DestroyImmediate(_overlayGo);
            }
            _overlayGo = null;
        }
    }
}
