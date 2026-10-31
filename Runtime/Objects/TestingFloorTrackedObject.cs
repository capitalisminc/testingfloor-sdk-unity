using System.Collections.Generic;
using UnityEngine;

namespace TestingFloorSdk {
    /// <summary>
    /// Tracks this GameObject's position while the component is enabled.
    /// Disabling the component removes the object with the reason
    /// <c>disabled</c>. Add it to a prefab and set its kind.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Testing Floor/Tracked Object")]
    public sealed class TestingFloorTrackedObject : MonoBehaviour {
        static readonly HashSet<TestingFloorTrackedObject> s_waiting = new();
        static readonly List<TestingFloorTrackedObject> s_registerBuffer = new();

        [Tooltip("What this object is, such as guard, crate or rocket. Objects are grouped and colored by kind.")]
        [SerializeField] string kind = "object";

        [Tooltip("Optional stable id. Use the id your events already send for this object so they line up. Leave empty to get kind#n.")]
        [SerializeField] string id;

        TrackedObject _handle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            s_waiting.Clear();
            s_registerBuffer.Clear();
        }

        public string Kind {
            get => kind;
            set => kind = value;
        }

        /// <summary>The stable id to register with. Changes apply the next time the component is enabled.</summary>
        public string Id {
            get => id;
            set => id = value;
        }

        /// <summary>The registered handle, or null while disabled or blocked by a hard collection gate.</summary>
        public TrackedObject Handle => _handle != null && _handle.IsTracked ? _handle : null;

        void OnEnable() {
            Register();
        }

        void OnDisable() {
            s_waiting.Remove(this);
            _handle?.Remove(Internal.ObjectTracker.ReasonDisabled);
            _handle = null;
        }

        void Register() {
            _handle = global::TestingFloorSdk.TestingFloor.TrackObject(kind, transform, id);
            // Collection can start after the scene loads; register then.
            if (!_handle.IsTracked && !global::TestingFloorSdk.TestingFloor.IsCollecting) s_waiting.Add(this);
            else s_waiting.Remove(this);
        }

        internal static void RegisterWaiting() {
            if (s_waiting.Count == 0) return;
            s_registerBuffer.Clear();
            s_registerBuffer.AddRange(s_waiting);
            s_waiting.Clear();
            for (var i = 0; i < s_registerBuffer.Count; i++) {
                var component = s_registerBuffer[i];
                if (component != null && component.isActiveAndEnabled) component.Register();
            }
            s_registerBuffer.Clear();
        }
    }
}
