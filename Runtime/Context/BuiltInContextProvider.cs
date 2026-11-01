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
                snapshot.SetVector("camera.up", t.up);
                // An orthographic camera ignores fieldOfView, so it sends its view width instead.
                if (camera.orthographic) {
                    snapshot.Set("camera.ortho_width", (double)OrthographicWidth(camera));
                } else {
                    snapshot.Set("camera.fov", (double)camera.fieldOfView);
                }
                snapshot.Platform = snapshot.Platform.WithScreenSize(camera.pixelWidth, camera.pixelHeight);
            }

            var focus = global::TestingFloorSdk.TestingFloor.GetCameraFocus();
            if (focus.HasValue) {
                snapshot.SetVector("camera.focus", focus.Value);
            }
        }

        internal static float OrthographicWidth(Camera camera) => 2f * camera.orthographicSize * camera.aspect;

        // Values that stay true until they change are sent as session state.
        internal static void FillState(ref ContextSnapshot snapshot) {
            var camera = global::TestingFloorSdk.TestingFloor.GetCamera();
            if (camera == null) return;
            if (camera.orthographic) {
                snapshot.Set("camera.projection", "orthographic");
            } else {
                snapshot.Set("camera.projection", "perspective");
                snapshot.Set("camera.fov_axis", "vertical");
            }
            snapshot.Set("viewport.width", (long)camera.pixelWidth);
            snapshot.Set("viewport.height", (long)camera.pixelHeight);
        }
    }
}
