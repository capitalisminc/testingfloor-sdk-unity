using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using TestingFloorSdk.Internal;
using UnityEngine;
using UnityEngine.TestTools;

namespace TestingFloorSdk.Tests.Editor {
    /// <summary>
    /// Runs the shared SDK scenarios in <c>Tests/Contract/scenarios</c>, one test per file. The
    /// format is in <c>Tests/Contract/scenarios/README.md</c>. When a scenario fails, fix the SDK,
    /// or have the contract changed and copy it again. Never edit the copy to make a test pass.
    /// </summary>
    public class ContractScenarioTests {
        const string ScenarioFolder = "Packages/com.testingfloor.unity-sdk/Tests/Contract/scenarios";

        // The queue's sender loop runs on Unity's main-thread context and waits real time between
        // retries, starting at one second. Delivery steps let frames pass until the collector has
        // an open request, the queue is empty, or nothing has been sent for longer than a retry.
        const double SettleSeconds = 4;
        const double QuietSeconds = 1.25;

        static IEnumerable<string> Scenarios() =>
            Directory.GetFiles(Path.GetFullPath(ScenarioFolder), "*.json")
                .Select(Path.GetFileNameWithoutExtension).OrderBy(name => name, StringComparer.Ordinal);

        readonly List<string> _directories = new();
        TestingFloorSettings _settings;

        [TearDown]
        public void TearDown() {
            TestingFloor.ResetForTesting();
            TestingFloorRecording.ResetStatics();
            RecordingTelemetry.ResetStatics();
            foreach (var directory in _directories) {
                if (!Directory.Exists(directory)) continue;
                SetWritable(directory, true);
                Directory.Delete(directory, true);
            }
            _directories.Clear();
            if (_settings != null) UnityEngine.Object.DestroyImmediate(_settings);
        }

        [UnityTest]
        public IEnumerator Scenario([ValueSource(nameof(Scenarios))] string name) {
            var scenario = (Dictionary<string, object>)Json.Parse(File.ReadAllText(Path.Combine(Path.GetFullPath(ScenarioFolder), name + ".json")));
            Assert.AreEqual(1L, scenario["format"], "This runner reads scenario format 1 only");
            switch ((string)scenario["kind"]) {
                case "handoff":
                    RunHandoff(scenario);
                    break;
                case "lifecycle":
                    yield return RunLifecycle(scenario);
                    break;
                default:
                    Assert.Fail($"Unknown scenario kind {scenario["kind"]}");
                    break;
            }
        }

        static void RunHandoff(Dictionary<string, object> scenario) {
            var now = (long)scenario["clock_unix_ms"];
            RecordingTelemetry.Clock = () => now;
            var failures = new List<string>();
            foreach (Dictionary<string, object> entry in (List<object>)scenario["cases"]) {
                var name = (string)entry["name"];
                var editor = entry.TryGetValue("editor_process_id", out var pid) ? (int)(long)pid : 0;
                var recording = TestingFloorRecording.Resolve(PayloadText(entry, "sidecar"), PayloadText(entry, "launch_argument"), editor);
                var expect = entry["expect"] as Dictionary<string, object>;
                if (expect == null) {
                    if (recording != null) failures.Add($"{name}: picked {recording.RecordingUuid}, expected none");
                    continue;
                }
                if (recording == null) {
                    failures.Add($"{name}: picked none, expected {expect["recording_uuid"]}");
                    continue;
                }
                if (recording.RecordingUuid != (string)expect["recording_uuid"]) failures.Add($"{name}: picked {recording.RecordingUuid}");
                if (expect.TryGetValue("playtest_id", out var playtest) && !Equals(recording.PlaytestId, playtest == null ? null : (long?)(long)playtest))
                    failures.Add($"{name}: playtest_id is {recording.PlaytestId}");
                if (expect.TryGetValue("suppress_qr", out var suppress) && recording.SuppressQr != (bool)suppress)
                    failures.Add($"{name}: suppress_qr is {recording.SuppressQr}");
            }
            CollectionAssert.IsEmpty(failures);
        }

        IEnumerator RunLifecycle(Dictionary<string, object> scenario) {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(directory);
            _directories.Add(directory);
            if (scenario.TryGetValue("requires", out var requires) && ((List<object>)requires).Contains("unwritable_directory")) {
                // A sandboxed macOS app can read the desktop app's directory but cannot create files in it.
                if (!MakeUnwritable(directory)) {
                    Assert.Ignore("This platform cannot make a directory unwritable.");
                    yield break;
                }
            }
            var now = (long)scenario["clock_start_unix_ms"];
            var editor = (string)scenario["environment"] == "editor";
            _settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            _settings.writeKey = "test-only-never-transmitted";
            _settings.endpoint = "https://collector.invalid";
            // Records leave only on flush, shutdown, retries and the completion marker.
            _settings.batchMaxFlushIntervalSeconds = 1e9f;
            if (scenario.TryGetValue("settings", out var values)) {
                var settings = (Dictionary<string, object>)values;
                if (settings.TryGetValue("queue_max_events", out var queue)) _settings.queueMaxEvents = (int)(long)queue;
                if (settings.TryGetValue("enable_in_editor", out var always)) _settings.enableInEditor = (bool)always;
                if (settings.TryGetValue("enable_in_editor_while_recording", out var recording)) _settings.enableInEditorWhileRecording = (bool)recording;
            }
            var collector = new Collector((string)scenario["collector"] == "manual", _settings);
            string handoff = null;
            string Substituted(string text) {
                text = text.Replace("{telemetry_directory}", directory.Replace("\\", "\\\\").Replace("\"", "\\\""));
                return text.Contains("{play_session_id}") ? text.Replace("{play_session_id}", TestingFloor.PlaySessionId) : text;
            }
            // Handoffs reach the SDK through the same resolution it uses for the launch argument or sidecar.
            TestingFloorRecording Discover() =>
                handoff == null ? null : TestingFloorRecording.Resolve(editor ? handoff : null, editor ? null : handoff, 0);

            var started = false;
            foreach (Dictionary<string, object> step in (List<object>)scenario["steps"]) {
                var action = step.Keys.Single(key => key != "accepted");
                var value = step[action];
                if (!started && action != "handoff" && action != "start" && action != "advance_ms" && action != "create_file")
                    Assert.Fail($"Step {action} comes before start");
                switch (action) {
                    case "handoff":
                        handoff = value == null ? null : Substituted(PayloadText(step, "handoff"));
                        break;
                    case "start":
                        TestingFloor.ConfigureRecordingLifecycleForTesting(_settings, Discover, () => now, capture: null,
                            editor: editor, automation: false, delivery: collector);
                        started = true;
                        break;
                    case "advance_ms":
                        now += (long)value;
                        break;
                    case "create_file":
                        // The desktop app can always write in its own directory.
                        SetWritable(directory, true);
                        File.WriteAllText(Path.Combine(directory, (string)value), "{}");
                        break;
                    case "track":
                        var accepted = TestingFloor.Track((string)value).Send();
                        if (step.TryGetValue("accepted", out var expected)) Assert.AreEqual((bool)expected, accepted, $"track {value} accepted");
                        break;
                    case "poll":
                        TestingFloor.RefreshRecordingContext();
                        yield return Settle(collector);
                        break;
                    case "flush":
                        _ = TestingFloor.FlushAsync(TimeSpan.FromSeconds(SettleSeconds));
                        yield return Settle(collector);
                        break;
                    case "respond":
                        Assert.Greater(collector.OpenCount, 0, $"respond {value}: no request is waiting for an answer");
                        collector.Respond((int)(long)value);
                        yield return Settle(collector);
                        break;
                    case "shutdown":
                        TestingFloor.FinishRecordingAndFlush();
                        break;
                    case "check":
                        Check((Dictionary<string, object>)value, directory, Substituted);
                        break;
                    default:
                        Assert.Fail($"Unknown step {action}");
                        break;
                }
            }
            CompareRequests((Dictionary<string, object>)scenario["expect"], collector.Bodies, Substituted);
        }

        static IEnumerator Settle(Collector collector) {
            var deadline = DateTime.UtcNow.AddSeconds(SettleSeconds);
            var sent = collector.Bodies.Count;
            var quietSince = DateTime.UtcNow;
            do {
                yield return null;
                if (collector.Bodies.Count != sent) {
                    sent = collector.Bodies.Count;
                    quietSince = DateTime.UtcNow;
                }
            } while (DateTime.UtcNow < deadline && collector.OpenCount == 0 && TelemetryQueue.PendingEventCount > 0
                && (DateTime.UtcNow - quietSince).TotalSeconds < QuietSeconds);
        }

        static void Check(Dictionary<string, object> expected, string directory, Func<string, string> substituted) {
            if (expected.TryGetValue("collecting", out var collecting)) Assert.AreEqual((bool)collecting, TestingFloor.IsCollecting, "check collecting");
            if (expected.TryGetValue("recording_uuid", out var recording))
                Assert.AreEqual((string)recording, TestingFloor.Recording?.RecordingUuid, "check recording_uuid");
            if (expected.TryGetValue("files_exist", out var exist))
                foreach (string file in (List<object>)exist) Assert.IsTrue(File.Exists(Path.Combine(directory, substituted(file))), $"check file exists: {file}");
            if (expected.TryGetValue("files_absent", out var absent))
                foreach (string file in (List<object>)absent) Assert.IsFalse(File.Exists(Path.Combine(directory, substituted(file))), $"check file absent: {file}");
            if (expected.TryGetValue("status", out var status)) {
                var path = Path.Combine(directory, TestingFloor.PlaySessionId + ".status.json");
                var problems = new List<string>();
                Match(File.Exists(path) ? Json.Parse(File.ReadAllText(path)) as Dictionary<string, object> : null,
                    (Dictionary<string, object>)status, "status", substituted, problems);
                CollectionAssert.IsEmpty(problems, "check status");
            }
        }

        /// <summary>Every expected field must match. A null expectation means the field is absent, not present as JSON null.</summary>
        static void Match(Dictionary<string, object> actual, Dictionary<string, object> expected, string path, Func<string, string> substituted, List<string> problems) {
            foreach (var field in expected) {
                var name = $"{path}.{field.Key}";
                object found = null;
                var present = actual != null && actual.TryGetValue(field.Key, out found);
                if (field.Value == null) {
                    if (present) problems.Add($"{name} should be absent, but is {Write(found)}");
                }
                else if (!present) {
                    problems.Add($"{name} is missing");
                }
                else if (field.Value is Dictionary<string, object> nested) {
                    Match(found as Dictionary<string, object>, nested, name, substituted, problems);
                }
                else if (!SameValue(found, field.Value, substituted)) {
                    problems.Add($"{name} is {Write(found)}, expected {Write(field.Value)}");
                }
            }
        }

        static bool SameValue(object actual, object expected, Func<string, string> substituted) => expected switch {
            string text => actual is string value && value == substituted(text),
            bool flag => actual is bool value && value == flag,
            long or double => actual is long or double && Convert.ToDouble(actual, CultureInfo.InvariantCulture) == Convert.ToDouble(expected, CultureInfo.InvariantCulture),
            _ => false,
        };

        static void CompareRequests(Dictionary<string, object> expect, List<string> bodies, Func<string, string> substituted) {
            static List<Dictionary<string, object>> Events(object list) => ((List<object>)list).Cast<Dictionary<string, object>>().ToList();
            var expectedRequests = expect.TryGetValue("requests", out var requests) ? Events(requests) : null;
            var expectedEvents = expect.TryGetValue("events", out var events) ? Events(events) : null;

            // Only the records a scenario names are compared.
            var named = new HashSet<string>(
                (expectedRequests?.SelectMany(request => Events(request["events"])) ?? expectedEvents).Select(e => (string)e["$name"]));
            var received = bodies.Select(body => Events(((Dictionary<string, object>)Json.Parse(body))["events"]))
                .Select(all => (All: all, Named: all.Where(e => named.Contains((string)e["$name"])).ToList()))
                .Where(request => request.Named.Count > 0).ToList();
            static string Describe(List<Dictionary<string, object>> list) => "[" + string.Join(", ", list.Select(e => e["$name"])) + "]";

            var problems = new List<string>();
            void CompareEvents(List<Dictionary<string, object>> actual, List<Dictionary<string, object>> expected, string where) {
                if (actual.Count != expected.Count) {
                    problems.Add($"{where}: received {Describe(actual)}, expected {expected.Count} events");
                    return;
                }
                for (var i = 0; i < actual.Count; i++) Match(actual[i], expected[i], $"{where} event {i + 1}", substituted, problems);
            }

            if (expectedEvents != null) {
                CompareEvents(received.SelectMany(request => request.Named).ToList(), expectedEvents, "events");
            }
            else if (received.Count != expectedRequests.Count) {
                problems.Add($"received {received.Count} requests, expected {expectedRequests.Count}: " +
                    string.Join(" ", received.Select(request => Describe(request.All))));
            }
            else {
                for (var i = 0; i < received.Count; i++) {
                    var expected = expectedRequests[i];
                    var where = $"request {i + 1}";
                    var expectedList = Events(expected["events"]);
                    CompareEvents(received[i].Named, expectedList, where);
                    if (expected.TryGetValue("exclusive", out var exclusive) && (bool)exclusive && received[i].All.Count != expectedList.Count)
                        problems.Add($"{where} must hold only {expectedList.Count} events, but held {Describe(received[i].All)}");
                    if (expected.TryGetValue("repeats", out var repeats)) {
                        var original = received[(int)(long)repeats - 1].Named;
                        var same = original.Count == received[i].Named.Count && original.Zip(received[i].Named,
                            (a, b) => Equals(a["$id"], b["$id"]) && Equals(a["$time"], b["$time"])).All(match => match);
                        if (!same) problems.Add($"{where} does not repeat request {repeats} with the same $id and $time");
                    }
                }
            }
            CollectionAssert.IsEmpty(problems);
        }

        /// <summary>A payload given as an object is passed as compact JSON; a string is passed as it is.</summary>
        static string PayloadText(Dictionary<string, object> owner, string key) =>
            !owner.TryGetValue(key, out var value) || value == null ? null : value as string ?? Write(value);

        static string Write(object value) {
            switch (value) {
                case null: return "null";
                case string text:
                    var builder = new StringBuilder("\"");
                    foreach (var c in text) {
                        if (c == '"' || c == '\\') builder.Append('\\').Append(c);
                        else if (c < ' ') builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                    }
                    return builder.Append('"').ToString();
                case bool flag: return flag ? "true" : "false";
                case long number: return number.ToString(CultureInfo.InvariantCulture);
                case double number: return number.ToString("R", CultureInfo.InvariantCulture);
                case Dictionary<string, object> map: return "{" + string.Join(",", map.Select(field => Write(field.Key) + ":" + Write(field.Value))) + "}";
                case List<object> list: return "[" + string.Join(",", list.Select(Write)) + "]";
                default: throw new ArgumentException($"Cannot write {value.GetType().Name} as JSON");
            }
        }

        [DllImport("libc", SetLastError = true)]
        static extern int chmod(string path, uint mode);

        // libc's chmod exists only on macOS and Linux. Elsewhere every directory stays writable, and
        // the scenario that needs an unwritable one is skipped.
        static bool CanChangePermissions =>
            Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX;

        static void SetWritable(string directory, bool writable) {
            if (CanChangePermissions) chmod(directory, Convert.ToUInt32(writable ? "755" : "555", 8));
        }

        static bool MakeUnwritable(string directory) {
            if (!CanChangePermissions) return false;
            SetWritable(directory, false);
            try {
                File.WriteAllText(Path.Combine(directory, "probe"), "");
                return false;
            }
            catch (UnauthorizedAccessException) { return true; }
            catch (IOException) { return true; }
        }

        /// <summary>
        /// The fake collector. It writes each request body with the SDK's own payload writer and
        /// answers the way the collector client turns an HTTP status into an outcome.
        /// </summary>
        sealed class Collector : IBlockingBatchSender {
            sealed class Batch {
                internal int Consumed;
                internal int Written;
                internal int RecordingConsumed;
                internal int RecordingSkipped;
            }

            readonly bool _manual;
            readonly TestingFloorSettings _settings;
            readonly JsonPayloadWriter _writer = new();
            readonly Queue<(TaskCompletionSource<BatchSendOutcome> Answer, Batch Batch)> _open = new();
            internal readonly List<string> Bodies = new();

            internal Collector(bool manual, TestingFloorSettings settings) {
                _manual = manual;
                _settings = settings;
            }

            internal int OpenCount => _open.Count;

            internal void Respond(int status) {
                var (answer, batch) = _open.Dequeue();
                answer.SetResult(Outcome(status, batch));
            }

            Batch Record(IReadOnlyList<TelemetryEvent> events) {
                var batch = new Batch();
                var bytes = _writer.BuildBatch(events, _settings.writeKey, TestingFloor.PlaySessionId,
                    JsonPayloadWriter.CollectorBodyByteCap - JsonPayloadWriter.BodySafetyMargin, JsonPayloadWriter.CollectorEventByteCap,
                    _settings, out batch.Consumed, out batch.Written, ev => { if (CollectorClient.IsRecordingGameplay(ev)) batch.RecordingSkipped++; });
                batch.RecordingConsumed = CollectorClient.CountRecording(events, batch.Consumed);
                if (batch.Written > 0) Bodies.Add(Encoding.UTF8.GetString(bytes));
                return batch;
            }

            // Mirrors CollectorClient.Finish: a transient failure keeps the batch for a retry.
            static BatchSendOutcome Outcome(int status, Batch batch) {
                var result = CollectorClient.ClassifyResponse(status);
                return result == SendResult.TransientFailure
                    ? new BatchSendOutcome(result, 0)
                    : new BatchSendOutcome(result, batch.Consumed, result == SendResult.Success ? batch.Consumed - batch.Written : batch.Consumed,
                        result == SendResult.Success ? batch.RecordingSkipped : batch.RecordingConsumed);
            }

            static BatchSendOutcome Nothing(Batch batch) => new(SendResult.Success, batch.Consumed, batch.Consumed, batch.RecordingSkipped);

            public ValueTask<BatchSendOutcome> TrackBatchAsync(IReadOnlyList<TelemetryEvent> events) {
                var batch = Record(events);
                if (batch.Written == 0) return new ValueTask<BatchSendOutcome>(Nothing(batch));
                if (!_manual) return new ValueTask<BatchSendOutcome>(Outcome(200, batch));
                var answer = new TaskCompletionSource<BatchSendOutcome>();
                _open.Enqueue((answer, batch));
                return new ValueTask<BatchSendOutcome>(answer.Task);
            }

            // Shutdown cannot wait for a manual answer.
            public BatchSendOutcome TrackBatchBlocking(IReadOnlyList<TelemetryEvent> events, DateTime deadline) {
                var batch = Record(events);
                if (batch.Written == 0) return Nothing(batch);
                return _manual ? new BatchSendOutcome(SendResult.TransientFailure, 0) : Outcome(200, batch);
            }

            public BatchSendOutcome CompletePendingBlocking(DateTime deadline) =>
                _open.Count > 0 ? new BatchSendOutcome(SendResult.TransientFailure, 0) : new BatchSendOutcome(SendResult.Success, 0);
        }
    }
}
