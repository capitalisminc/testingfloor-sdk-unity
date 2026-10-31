using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using TestingFloorSdk.Internal;
using UnityEngine;

namespace TestingFloorSdk.Tests {
    public class EditorRecordingLifecycleTests {
        readonly List<TelemetryEvent> _records = new();
        readonly List<string> _directories = new();
        TestingFloorSettings _settings;
        TestingFloorRecording _candidate;
        long _now;

        [SetUp]
        public void SetUp() {
            _records.Clear();
            _directories.Clear();
            _candidate = null;
            _now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            _settings.writeKey = "example-write-key";
            TestingFloor.ConfigureRecordingLifecycleForTesting(_settings, () => _candidate, () => _now, ev => { _records.Add(ev); return true; });
        }

        [TearDown]
        public void TearDown() {
            TestingFloor.ResetForTesting();
            TestingFloorRecording.ResetStatics();
            RecordingTelemetry.ResetStatics();
            foreach (var directory in _directories) Directory.Delete(directory, true);
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        TestingFloorRecording Lease(string uuid) {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(directory);
            _directories.Add(directory);
            return new TestingFloorRecording(uuid, 42, true, directory) {
                LeaseVersion = 1, TelemetryRequested = true, LeaseExpiresAtUnixMs = _now + 5000,
            };
        }

        void Poll() { _now += 250; TestingFloor.RefreshRecordingContext(force: true); }
        TelemetryEvent[] Completions() => _records.Where(record => record.EventType == "$tf_telemetry_complete").ToArray();

        [Test]
        public void AttachesDuringExistingPlayAndKeepsSuccessiveRecordingsSeparate() {
            Assert.IsFalse(TestingFloor.IsCollecting);
            Assert.IsFalse(TestingFloor.Track("before_recording").Send());
            var reads = 0;
            var tracked = TestingFloor.TrackObject("crate", () => { reads++; return Vector3.zero; });
            Assert.AreEqual(0, reads, "Dormant registration must not sample the game");
            var play = TestingFloor.PlaySessionId;
            _candidate = Lease("example-first");
            Poll();
            Assert.IsTrue(TestingFloor.IsCollecting);
            Assert.IsTrue(File.Exists(Path.Combine(_candidate.TelemetryDirectory, play + ".session")));
            Assert.IsTrue(_records.Any(record => record.Kind == RecordKind.State && record.Keyframe && record.Recording?.RecordingUuid == "example-first"));
            Assert.IsTrue(_records.Any(record => record.Kind == RecordKind.Objects && record.Keyframe && record.Recording?.RecordingUuid == "example-first"));
            Assert.IsTrue(TestingFloor.Track("first_event").Send());
            var firstEvent = _records.Last(record => record.EventType == "first_event");
            File.WriteAllText(Path.Combine(_candidate.TelemetryDirectory, "closed.json"), _now.ToString());
            Poll();
            Assert.IsNull(TestingFloor.Recording);
            Assert.IsFalse(TestingFloor.IsCollecting);
            Assert.AreEqual(1, Completions().Length);
            _candidate = Lease("example-second");
            Poll();
            Assert.IsTrue(TestingFloor.Track("second_event").Send());
            Assert.AreEqual(play, TestingFloor.PlaySessionId);
            Assert.AreEqual("example-first", firstEvent.Recording.RecordingUuid);
            Assert.AreEqual("example-second", _records.Last(record => record.EventType == "second_event").Recording.RecordingUuid);
            Assert.IsTrue(_records.Any(record => record.Kind == RecordKind.State && record.Keyframe && record.Recording?.RecordingUuid == "example-second"));
            Assert.AreEqual(1, Completions().Length, "The first marker cannot satisfy the second recording");
        }

        [Test]
        public void ExpiredLeaseSuspendsWithoutCompletingAndRenewalRetainsInterruption() {
            _candidate = Lease("example-recording");
            Poll();
            _now = _candidate.LeaseExpiresAtUnixMs;
            Poll();
            Assert.IsFalse(TestingFloor.IsCollecting);
            Assert.IsNull(TestingFloor.Recording);
            Assert.IsEmpty(Completions());
            _candidate.LeaseExpiresAtUnixMs = _now + 5000;
            Poll();
            Assert.IsTrue(TestingFloor.IsCollecting);
            File.WriteAllText(Path.Combine(_candidate.TelemetryDirectory, "closed.json"), _now.ToString());
            Poll();
            var marker = Completions().Single();
            Assert.AreEqual(true, marker.EventProperties["interrupted"]);
            StringAssert.Contains("\"interrupted\":true", File.ReadAllText(Path.Combine(_candidate.TelemetryDirectory, TestingFloor.PlaySessionId + ".status.json")));
        }

        [Test]
        public void RecordingOnlyDoesNotAcceptStaticLegacyPayloadOrOverrideHardGates() {
            _candidate = Lease("example-recording");
            _candidate.LeaseVersion = 0;
            Poll();
            Assert.IsFalse(TestingFloor.IsCollecting);
            Assert.IsFalse(File.Exists(Path.Combine(_candidate.TelemetryDirectory, TestingFloor.PlaySessionId + ".session")));
            _candidate.LeaseVersion = 1;
            _settings.enabled = false;
            Poll();
            Assert.IsFalse(TestingFloor.IsCollecting);
            Assert.AreEqual("master_disabled", TestingFloor.CollectionReason);
            _settings.enabled = true;
            TestingFloor.SetForceDisabled(true);
            Poll();
            Assert.IsFalse(TestingFloor.IsCollecting);
            Assert.AreEqual("force_disabled", TestingFloor.CollectionReason);
            TestingFloor.SetForceDisabled(false);
            _settings.writeKey = "";
            Poll();
            Assert.IsFalse(TestingFloor.IsCollecting);
            Assert.AreEqual("missing_configuration", TestingFloor.CollectionReason);
        }

        [Test]
        public void ReplacingRecordingWithoutClosureFinishesOldPairAsInterrupted() {
            _candidate = Lease("example-first");
            Poll();
            _candidate = Lease("example-second");
            Poll();
            var marker = Completions().Single();
            Assert.AreEqual("example-first", marker.Recording.RecordingUuid);
            Assert.AreEqual(true, marker.EventProperties["interrupted"]);
            Assert.AreEqual("example-second", TestingFloor.Recording.RecordingUuid);
        }

        [Test]
        public void RecordingChangeCallbacksRefreshStateBeforeKeyframesAndClearOnSuspensionOrClose() {
            var transitions = new List<string>();
            TestingFloor.RecordingChanged += _ => throw new InvalidOperationException("example subscriber failure");
            TestingFloor.RecordingChanged += recording => {
                transitions.Add(recording?.RecordingUuid);
                TestingFloor.SetState("recording.label", recording?.RecordingUuid);
            };
            _candidate = Lease("example-recording");
            Poll();
            Assert.AreEqual("example-recording", _records.Single(record => record.Kind == RecordKind.State).EventProperties["recording.label"]);
            _now = _candidate.LeaseExpiresAtUnixMs;
            Poll();
            _candidate.LeaseExpiresAtUnixMs = _now + 5000;
            Poll();
            Assert.AreEqual("example-recording", _records.Last(record => record.Kind == RecordKind.State).EventProperties["recording.label"]);
            File.WriteAllText(Path.Combine(_candidate.TelemetryDirectory, "closed.json"), _now.ToString());
            Poll();
            CollectionAssert.AreEqual(new[] { "example-recording", null, "example-recording", null }, transitions);
            Assert.IsFalse(TestingFloor.IsCollecting);
        }

        [Test]
        public async Task AcceptedRecordsReachTheEncoderAfterCollectionStopsWithoutSendingTestTraffic() {
            _settings.enabled = false;
            Assert.IsFalse(TestingFloor.IsCollecting);
            var recording = new TestingFloorRecording("example-closed", null, true) { Closed = true };
            // An oversized accepted record is encoded then rejected locally; no HTTP request is
            // needed to establish that delivery reached the encoder after the collection gate closed.
            var record = new TelemetryEvent("already_queued", new Dictionary<string, object> { ["large"] = new string('x', 40000) },
                ContextSnapshot.Create(default), null, null, _now, TestingFloor.PlaySessionId, recording);
            var client = new CollectorClient(_settings, automation: false);
            var outcome = await client.TrackBatchAsync(new[] { record });
            Assert.AreEqual(SendResult.Success, outcome.Result, "A closed collection gate must not reject accepted work as unconfigured");
            Assert.AreEqual(1, outcome.Consumed);
            Assert.AreEqual(1, outcome.Dropped, "The encoder, rather than policy, refused the oversized payload");
            var automation = new CollectorClient(_settings, automation: true);
            outcome = await automation.TrackBatchAsync(new[] { record });
            Assert.AreEqual(SendResult.NotConfigured, outcome.Result, "Automation remains unable to send");
        }

        [Test]
        public void NeverAndAutomationCannotBeEnabledByARecording() {
            _candidate = Lease("example-recording");
            _settings.enableInEditorWhileRecording = false;
            Poll();
            Assert.IsFalse(TestingFloor.IsCollecting);
            _settings.enableInEditorWhileRecording = true;
            TestingFloor.ConfigureRecordingLifecycleForTesting(_settings, () => _candidate, () => _now,
                ev => { _records.Add(ev); return true; }, automation: true);
            Assert.IsFalse(TestingFloor.IsCollecting);
            Assert.AreEqual("automation", TestingFloor.CollectionReason);
            Assert.IsEmpty(_records);
        }
    }
}
