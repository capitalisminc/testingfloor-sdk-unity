using System;
using UnityEngine;

namespace TestingFloor {
    internal static class QrHeartbeatDriver {
        const string ForceQrArg = "--forceqr";

        static GameObject _overlayGo;
        static bool? _enabledOverride;
        static bool? _invertedOverride;
        static bool _forceQrArgumentResolved;
        static bool _forceQrArgumentPresent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            _overlayGo = null;
            _enabledOverride = null;
            _invertedOverride = null;
            _forceQrArgumentResolved = false;
            _forceQrArgumentPresent = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            Apply();
        }

        internal static bool EffectiveEnabled {
            get {
                if (_enabledOverride.HasValue) return _enabledOverride.Value;
                var settings = TestingFloorSettings.Current;
                return EffectiveEnabledFor(settings, TestingFloorRecording.HasRecorderSession, ForceQrArgumentPresent);
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

        internal static bool EffectiveEnabledFor(TestingFloorSettings settings, bool hasRecorderSession, bool forceQrArgument) {
            if (forceQrArgument) return true;
            if (settings == null) return false;
            if (hasRecorderSession && settings.qrHeartbeatsEnabledWithRecorderSession) return true;
            if (!settings.qrHeartbeatsEnabled) return false;
            if (hasRecorderSession) return false;
            return true;
        }

        internal static bool IsForceQrArgument(string arg) {
            return string.Equals(arg, ForceQrArg, StringComparison.OrdinalIgnoreCase);
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

        static bool ForceQrArgumentPresent {
            get {
                if (_forceQrArgumentResolved) return _forceQrArgumentPresent;
                _forceQrArgumentResolved = true;

                try {
                    var args = Environment.GetCommandLineArgs();
                    for (var i = 0; i < args.Length; i++) {
                        if (!IsForceQrArgument(args[i])) continue;
                        _forceQrArgumentPresent = true;
                        return true;
                    }
                }
                catch (Exception ex) {
                    var settings = TestingFloorSettings.Current;
                    if (settings != null && settings.logErrors) {
                        Debug.LogWarning($"[TestingFloor] Failed to inspect command-line args for {ForceQrArg}: {ex.Message}");
                    }
                }

                return false;
            }
        }
    }
}
