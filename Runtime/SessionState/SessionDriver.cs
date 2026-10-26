using UnityEngine;

namespace TestingFloorSdk {
    /// <summary>
    /// Owns the hidden GameObject that polls session state and samples tracked
    /// objects on unscaled time, so both continue while gameplay is paused.
    /// </summary>
    internal static class SessionDriver {
        static GameObject _tickerGo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            _tickerGo = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            if (!Application.isPlaying || TestingFloorSettings.IsAutomation || _tickerGo != null) return;
            var go = new GameObject("[TestingFloor.Session]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<SessionTicker>();
            _tickerGo = go;
        }
    }
}
