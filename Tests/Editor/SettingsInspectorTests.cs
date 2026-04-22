using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TestingFloor.Tests.Editor {
    public class SettingsInspectorTests {
        [Test]
        public void DefaultSettingsAreNotEnabledForBuildWithoutWriteKey() {
            var settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            try {
                Assert.IsTrue(settings.enabled);
                Assert.IsFalse(settings.IsEnabledForBuild, "no write key → not enabled for build");
            }
            finally {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void SettingsWithWriteKeyAreEnabledForRuntimeBuild() {
            var settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            try {
                settings.writeKey = "wk_test";
                settings.enableInEditor = true;
                Assert.IsTrue(settings.IsEnabledForBuild);
            }
            finally {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void DisabledFlagOverridesWriteKey() {
            var settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            try {
                settings.writeKey = "wk_test";
                settings.enableInEditor = true;
                settings.enabled = false;
                Assert.IsFalse(settings.IsEnabledForBuild);
            }
            finally {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void CustomInspectorIsRegistered() {
            var settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            try {
                var editor = UnityEditor.Editor.CreateEditor(settings);
                try {
                    Assert.IsInstanceOf<global::TestingFloor.Editor.TestingFloorSettingsInspector>(editor);
                }
                finally {
                    Object.DestroyImmediate(editor);
                }
            }
            finally {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
