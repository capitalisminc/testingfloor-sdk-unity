using UnityEngine;

namespace TestingFloorSdk {
    [DisallowMultipleComponent]
    internal sealed class SessionTicker : MonoBehaviour {
        void Update() {
            global::TestingFloorSdk.TestingFloor.Tick(Time.unscaledTimeAsDouble);
        }
    }
}
