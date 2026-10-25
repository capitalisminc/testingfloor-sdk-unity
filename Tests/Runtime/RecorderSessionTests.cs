using System;
using NUnit.Framework;
using UnityEngine;

namespace TestingFloorSdk.Tests {
    public class RecordingContextTests {
        [Test]
        public void CurrentHandoffHasOneRecordingIdentityAndDefaultsToSuppressedQr() {
            var recording = TestingFloorRecording.ParsePayloadForTesting(
                "{\"recording_uuid\":\" example-recording \",\"playtest_id\":42}");
            Assert.NotNull(recording);
            Assert.AreEqual("example-recording", recording.RecordingUuid);
            Assert.AreEqual(42, recording.PlaytestId);
            Assert.IsTrue(recording.SuppressQr);
        }

        [Test]
        public void ExplicitQrVisibilityIsPreserved() {
            var recording = TestingFloorRecording.ParsePayloadForTesting(
                "{\"recording_uuid\":\"example-recording\",\"suppress_qr\":false}");
            Assert.IsFalse(recording.SuppressQr);
        }

        [Test]
        public void MissingRecordingIdentityAndStaleHandoffsAreIgnored() {
            Assert.IsNull(TestingFloorRecording.ParsePayloadForTesting("{\"session_id\":\"retired-field\"}"));
            var timestamp = DateTimeOffset.UtcNow.AddHours(-13).ToUnixTimeMilliseconds();
            Assert.IsNull(TestingFloorRecording.ParsePayloadForTesting(
                "{\"recording_uuid\":\"stale\",\"created_at_unix_ms\":" + timestamp + "}"));
        }

        [Test]
        public void AutomationAlwaysWinsOverForcedQr() {
            var settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            try {
                Assert.IsFalse(QrHeartbeatDriver.EffectiveEnabledFor(settings, false, true, automation: true));
                Assert.IsTrue(QrHeartbeatDriver.EffectiveEnabledFor(settings, false, true));
                Assert.IsTrue(TestingFloorSettings.IsAutomationArgs(new[] { "-runTests" }));
                Assert.IsFalse(TestingFloorSettings.IsAutomationArgs(new[] { "-batchmode" }));
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }

        [Test]
        public void QrPolicyHonorsRecorderSuppressionAndExplicitOverrides() {
            var settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            try {
                settings.qrHeartbeatsEnabled = true;
                Assert.IsTrue(QrHeartbeatDriver.EffectiveEnabledFor(settings, false, false));
                Assert.IsFalse(QrHeartbeatDriver.EffectiveEnabledFor(settings, true, false));
                settings.qrHeartbeatsEnabledWithRecorderSession = true;
                settings.qrHeartbeatsEnabled = false;
                Assert.IsTrue(QrHeartbeatDriver.EffectiveEnabledFor(settings, true, false));
                Assert.IsFalse(QrHeartbeatDriver.EffectiveEnabledFor(settings, false, false));
                Assert.IsTrue(QrHeartbeatDriver.IsForceQrArgument("-forceqr"));
                Assert.IsTrue(QrHeartbeatDriver.IsForceQrArgument("--FORCEQR"));
                Assert.IsFalse(QrHeartbeatDriver.IsForceQrArgument("--forceqr=false"));
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }
    }
}
