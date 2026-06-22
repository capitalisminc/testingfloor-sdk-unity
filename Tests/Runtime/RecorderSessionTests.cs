using NUnit.Framework;
using UnityEngine;

namespace TestingFloor.Tests {
    public class RecorderSessionTests {
        [Test]
        public void RecorderSessionPayloadResolvesSessionId() {
            var recording = TestingFloorRecording.ParsePayloadForTesting(
                "{\"schema\":1,\"session_id\":\"session-123\",\"playtest_id\":42}");

            Assert.NotNull(recording);
            Assert.AreEqual("session-123", recording.SessionId);
            Assert.AreEqual("session-123", recording.RecordingUuid);
            Assert.AreEqual(42, recording.PlaytestId);
            Assert.IsTrue(recording.SuppressQr);
        }

        [Test]
        public void RecorderSessionPayloadCanKeepQrVisible() {
            var recording = TestingFloorRecording.ParsePayloadForTesting(
                "{\"schema\":1,\"session_id\":\"session-123\",\"playtest_id\":42,\"suppress_qr\":false}");

            Assert.NotNull(recording);
            Assert.IsFalse(recording.SuppressQr);
        }

        [Test]
        public void RecordingUuidPayloadStillResolves() {
            var recording = TestingFloorRecording.ParsePayloadForTesting(
                "{\"schema\":1,\"recording_uuid\":\"recording-123\",\"playtest_id\":42}");

            Assert.NotNull(recording);
            Assert.IsNull(recording.SessionId);
            Assert.AreEqual("recording-123", recording.RecordingUuid);
            Assert.AreEqual(42, recording.PlaytestId);
        }

        [Test]
        public void MissingSessionAndRecordingUuidPayloadIsIgnored() {
            var recording = TestingFloorRecording.ParsePayloadForTesting(
                "{\"schema\":1,\"playtest_id\":42}");

            Assert.IsNull(recording);
        }

        [Test]
        public void QrHeartbeatsAreHiddenForRecorderSessionUnlessExplicitlyAllowed() {
            var settings = ScriptableObject.CreateInstance<TestingFloorSettings>();

            try {
                settings.qrHeartbeatsEnabled = true;

                Assert.IsTrue(QrHeartbeatDriver.EffectiveEnabledFor(settings, suppressQr: false, forceQrArgument: false));
                Assert.IsFalse(QrHeartbeatDriver.EffectiveEnabledFor(settings, suppressQr: true, forceQrArgument: false));

                settings.qrHeartbeatsEnabledWithRecorderSession = true;

                Assert.IsTrue(QrHeartbeatDriver.EffectiveEnabledFor(settings, suppressQr: true, forceQrArgument: false));

                settings.qrHeartbeatsEnabled = false;

                Assert.IsTrue(QrHeartbeatDriver.EffectiveEnabledFor(settings, suppressQr: true, forceQrArgument: false));
                Assert.IsFalse(QrHeartbeatDriver.EffectiveEnabledFor(settings, suppressQr: false, forceQrArgument: false));
            }
            finally {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ForceQrArgumentShowsQrEvenWhenSettingIsOff() {
            var settings = ScriptableObject.CreateInstance<TestingFloorSettings>();

            try {
                settings.qrHeartbeatsEnabled = false;

                Assert.IsTrue(QrHeartbeatDriver.EffectiveEnabledFor(settings, suppressQr: true, forceQrArgument: true));
                Assert.IsTrue(QrHeartbeatDriver.IsForceQrArgument("--forceqr"));
                Assert.IsTrue(QrHeartbeatDriver.IsForceQrArgument("--FORCEQR"));
                Assert.IsFalse(QrHeartbeatDriver.IsForceQrArgument("--force-qr"));
            }
            finally {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
