using System;
using System.IO;
using NUnit.Framework;
using TestingFloorSdk.Internal;

namespace TestingFloorSdk.Tests {
    public class RecordingCompletionTests {
        [Test]
        public void RegistersPlayRunsInRecorderDirectoryAndStopsAssociationOnClosure() {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(directory);
            try {
                var recording = new TestingFloorRecording("example-recording", null, true, directory);
                var completions = 0;
                Assert.IsTrue(RecordingTelemetry.Register(recording, "first-play", (_, _) => completions++));
                Assert.IsTrue(File.Exists(Path.Combine(directory, "first-play.session")));
                RecordingTelemetry.Finish();
                RecordingTelemetry.Finish();
                Assert.IsTrue(recording.Closed);
                Assert.AreEqual(1, completions);
                var second = new TestingFloorRecording("example-recording", null, true, directory);
                Assert.IsTrue(RecordingTelemetry.Register(second, "second-play", (_, _) => completions++));
                Assert.IsTrue(File.Exists(Path.Combine(directory, "second-play.session")));
                File.WriteAllText(Path.Combine(directory, "closed.json"), "1");
                RecordingTelemetry.Poll();
                Assert.IsTrue(second.Closed);
                Assert.AreEqual(2, completions);
                var late = new TestingFloorRecording("example-recording", null, true, directory);
                Assert.IsFalse(RecordingTelemetry.Register(late, "late-play", (_, _) => completions++));
                Assert.IsFalse(File.Exists(Path.Combine(directory, "late-play.session")));
            } finally {
                RecordingTelemetry.ResetStatics();
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void MissingRecorderDirectoryCannotAdmitRecordingTelemetry() {
            var recording = new TestingFloorRecording("example-recording", null, true,
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
            Assert.IsFalse(RecordingTelemetry.Register(recording, "example-play"));
            Assert.IsTrue(recording.Closed);
        }
    }
}
