using UnityEngine;

namespace TestingFloor {
    internal static class BuiltInContextProvider {
        internal static void Fill(ref ContextSnapshot snapshot) {
            var position = global::TestingFloor.TestingFloor.GetPlayerPosition();
            if (position.HasValue) {
                var p = position.Value;
                snapshot.Set("player.position.x", p.x);
                snapshot.Set("player.position.y", p.y);
                snapshot.Set("player.position.z", p.z);
            }

            var camera = global::TestingFloor.TestingFloor.GetCamera();
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
                snapshot.Set("camera.fov", (double)camera.fieldOfView);
                snapshot.Set("viewport.width", (long)camera.pixelWidth);
                snapshot.Set("viewport.height", (long)camera.pixelHeight);
            }
        }
    }
}
