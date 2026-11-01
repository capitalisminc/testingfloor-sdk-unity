using System;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TestingFloorSdk.Internal;

namespace TestingFloorSdk.Tests {
    // A directory the test can read but not create files in, like a sandboxed macOS app sees
    // the recorder's telemetry directory.
    internal sealed class UnwritableDirectory : IDisposable {
        [DllImport("libc", SetLastError = true)]
        static extern int chmod(string path, uint mode);

        internal string Path { get; }

        internal UnwritableDirectory() {
            if (Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.MacOSX) {
                Assert.Ignore("Directory permissions only block file creation on macOS and Linux.");
            }
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path);
            Assert.AreEqual(0, chmod(Path, Convert.ToUInt32("555", 8)));
            try {
                File.WriteAllText(System.IO.Path.Combine(Path, "probe"), "");
                Dispose();
                Assert.Ignore("This user can still create files in a read-only directory.");
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        // The recorder, which can write here, closes the recording.
        internal void Close() {
            chmod(Path, Convert.ToUInt32("755", 8));
            File.WriteAllText(System.IO.Path.Combine(Path, "closed.json"), "1");
        }

        public void Dispose() {
            if (!Directory.Exists(Path)) return;
            chmod(Path, Convert.ToUInt32("755", 8));
            Directory.Delete(Path, true);
        }
    }

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
        public void UnwritableRecorderDirectoryStillAttachesAndCompletes() {
            using var directory = new UnwritableDirectory();
            try {
                var recording = new TestingFloorRecording("example-recording", null, true, directory.Path);
                var completions = 0;
                Assert.IsTrue(RecordingTelemetry.Register(recording, "sandboxed-play", (_, _) => completions++));
                Assert.IsFalse(recording.Closed);
                Assert.AreSame(recording, TestingFloorRecording.Attached);
                Assert.IsFalse(File.Exists(Path.Combine(directory.Path, "sandboxed-play.session")));
                RecordingTelemetry.Poll();
                Assert.AreEqual(0, completions, "The run stays attached until the recorder closes the recording");

                directory.Close();
                RecordingTelemetry.Poll();
                Assert.IsTrue(recording.Closed);
                Assert.AreEqual(1, completions);
            } finally {
                RecordingTelemetry.ResetStatics();
                TestingFloorRecording.ResetStatics();
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
