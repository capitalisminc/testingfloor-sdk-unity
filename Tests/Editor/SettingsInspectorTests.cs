using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TestingFloorSdk.Tests.Editor {
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
                Assert.IsTrue(settings.ShouldCollect(editor: false, automation: false));
                Assert.IsTrue(settings.ShouldCollect(editor: true, automation: false));
                Assert.IsFalse(settings.IsEnabledForBuild, "Automated tests must never send live telemetry");
                Assert.IsFalse(settings.ShouldCollect(editor: false, automation: true));
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
        public void RecordingOnlyEditorPolicyRequiresRecordingAndRespectsHardGates() {
            var settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            try {
                settings.writeKey = "example-write-key";
                Assert.IsFalse(settings.enableInEditor);
                Assert.IsTrue(settings.enableInEditorWhileRecording);
                Assert.IsFalse(settings.ShouldCollect(editor: true, automation: false, activeRecording: false));
                Assert.IsTrue(settings.ShouldCollect(editor: true, automation: false, activeRecording: true));
                Assert.IsFalse(settings.ShouldCollect(editor: true, automation: true, activeRecording: true));
                settings.enableInEditorWhileRecording = false;
                Assert.IsFalse(settings.ShouldCollect(editor: true, automation: false, activeRecording: true));
                settings.enableInEditor = true;
                Assert.IsTrue(settings.ShouldCollect(editor: true, automation: false, activeRecording: false));
                settings.enabled = false;
                Assert.IsFalse(settings.ShouldCollect(editor: true, automation: false, activeRecording: true));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void CustomInspectorIsRegistered() {
            var settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            try {
                var editor = UnityEditor.Editor.CreateEditor(settings);
                try {
                    Assert.IsInstanceOf<global::TestingFloorSdk.Editor.TestingFloorSettingsInspector>(editor);
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
