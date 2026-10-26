using UnityEngine;

namespace TestingFloorSdk {
    internal static class BuiltInContextProvider {
        internal static void Fill(ref ContextSnapshot snapshot) {
            var position = global::TestingFloorSdk.TestingFloor.GetPlayerPosition();
            if (position.HasValue) {
                var p = position.Value;
                snapshot.Set("player.position.x", p.x);
                snapshot.Set("player.position.y", p.y);
                snapshot.Set("player.position.z", p.z);
            }

            var camera = global::TestingFloorSdk.TestingFloor.GetCamera();
            if (camera != null) {
                var t = camera.transform;
                var pos = t.position;
                var euler = t.eulerAngles;
                snapshot.Set("camera.position.x", pos.x);
                snapshot.Set("camera.position.y", pos.y);
                snapshot.Set("camera.position.z", pos.z);
                snapshot.Set("camera.euler.x", euler.x);
                snapshot.Set("camera.euler.y", euler.y);
                snapshot.Set("camera.euler.z", euler.z);
                var forward = t.forward;
                snapshot.SetVector("camera.forward", forward);
                snapshot.Set("camera.fov", (double)camera.fieldOfView);
                snapshot.Platform = snapshot.Platform.WithScreenSize(camera.pixelWidth, camera.pixelHeight);
            }
        }

        // Values that stay true until they change are sent as session state.
        internal static void FillState(ref ContextSnapshot snapshot) {
            var camera = global::TestingFloorSdk.TestingFloor.GetCamera();
            if (camera == null) return;
            snapshot.Set("camera.fov_axis", "vertical");
            snapshot.Set("viewport.width", (long)camera.pixelWidth);
            snapshot.Set("viewport.height", (long)camera.pixelHeight);
        }
    }
}
