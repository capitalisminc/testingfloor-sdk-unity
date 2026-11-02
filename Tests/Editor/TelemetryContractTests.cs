using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using TestingFloorSdk.Internal;
using UnityEngine;

namespace TestingFloorSdk.Tests.Editor {
    /// <summary>
    /// Compares what the SDK sends with the vendored Testing Floor telemetry
    /// contract. A failure means the SDK and the contract disagree; see
    /// Tests/Contract/README.md.
    /// </summary>
    public class TelemetryContractTests {
        // SDK 0.5.0 sends exactly milliseconds and Z, the form the contract asks new SDKs for.
        static readonly Regex UtcTimestamp = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$");
        static readonly Regex LanguageTag = new(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$");
        static readonly string[] OsFamilies = { "Windows", "macOS", "Linux", "iOS", "Android", "tvOS", "visionOS", "Web", "PlayStation", "Xbox", "Switch" };

        readonly List<TelemetryEvent> _records = new();
        TestingFloorSettings _settings;
        GameObject _camera;

        sealed class CapturingSender : IBatchSender {
            public readonly List<Dictionary<string, object>> Sent = new();

            public ValueTask<BatchSendOutcome> TrackBatchAsync(IReadOnlyList<TelemetryEvent> events) {
                // Read the records now: the queue returns their properties to its pool afterwards.
                Sent.AddRange(Events(Serialize(events.ToArray(), out _)));
                return new ValueTask<BatchSendOutcome>(new BatchSendOutcome(SendResult.Success, events.Count));
            }
        }

        [SetUp]
        public void SetUp() {
            _records.Clear();
            _settings = ScriptableObject.CreateInstance<TestingFloorSettings>();
            TestingFloor.CaptureRecordsForTesting(record => {
                _records.Add(record);
                return true;
            }, _settings);
        }

        [TearDown]
        public void TearDown() {
            TestingFloorRecording.ResetStatics();
            TestingFloor.ResetForTesting();
            if (_camera != null) UnityEngine.Object.DestroyImmediate(_camera);
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        [Test]
        public void TestsReadContractVersionOne() {
            Assert.AreEqual(1L, TelemetryContract.Root["contract_version"],
                "These tests read version 1 of the contract's layout. Update them for the new version.");
        }

        [Test]
        public void SdkSendsTheContractsWireSchemaVersion() {
            var schema = (long)JsonPayloadWriter.SchemaVersion;
            Assert.AreEqual(TelemetryContract.Root["wire_schema_version"], schema);
            CollectionAssert.Contains((List<object>)TelemetryContract.Root["accepted_wire_schema_versions"], schema);
            Assert.AreEqual(schema, Wire(MinimalEvent())["$schema"]);
        }

        [Test]
        public void EndpointIsTheContractsBatchUrl() {
            var endpoint = TelemetryContract.Section("endpoint");
            Assert.AreEqual(endpoint["url"], _settings.endpoint.TrimEnd('/') + CollectorClient.BatchPath);
        }

        [Test]
        public void RequestEnvelopeHasExactlyTheContractsRequestFields() {
            var request = Request(FullRecord());
            var rows = TelemetryContract.Rows("request_fields");
            CollectionAssert.AreEquivalent(TelemetryContract.Keys(rows), request.Keys);
            foreach (var row in rows) AssertValue(row, request[(string)row["key"]], "Request field");
        }

        [Test]
        public void EventEnvelopeHasExactlyTheContractsEventFields() {
            var wire = Wire(FullRecord());
            var rows = TelemetryContract.Rows("event_fields");
            CollectionAssert.AreEquivalent(TelemetryContract.Keys(rows), Envelope(wire));
            foreach (var row in rows) AssertValue(row, wire[(string)row["key"]], "Event field");
        }

        [Test]
        public void EveryEventCarriesTheContractsRequiredFields() {
            var wire = Wire(MinimalEvent());
            foreach (var row in TelemetryContract.Rows("event_fields", field => (bool)field["required"])) {
                var key = (string)row["key"];
                Assert.IsTrue(wire.ContainsKey(key), $"Required event field {key} is missing");
                AssertValue(row, wire[key], "Event field");
            }
        }

        [Test]
        public void TfContextHasExactlyTheContractsSdkContextFields() {
            var context = (Dictionary<string, object>)Wire(FullRecord())["$tf"];
            var rows = TelemetryContract.Rows("context_fields", row => TelemetryContract.Text(row, "set_by") == "sdk");
            CollectionAssert.AreEquivalent(TelemetryContract.Keys(rows), context.Keys);
            foreach (var row in rows) AssertValue(row, context[(string)row["key"]], "$tf field");
        }

        [Test]
        public void SdkRecordNamesAreTheContractsCurrentSdkPlatformRecords() {
            var rows = TelemetryContract.Rows("platform_records",
                row => TelemetryContract.Text(row, "sent_by") == "sdk" && TelemetryContract.Text(row, "status") == "current");
            CollectionAssert.AreEquivalent(TelemetryContract.Keys(rows, "name"), new[] {
                TestingFloor.StateRecordName,
                TestingFloor.ObjectsRecordName,
                TestingFloor.TelemetryCompleteRecordName,
            });
        }

        [Test]
        public void CompletionMarkerHasExactlyTheContractsFields() {
            var sender = new CapturingSender();
            TestingFloor.QueueRecordsForTesting(sender, _settings);

            TestingFloor.EnqueueRecordingCompletion(new TestingFloorRecording("example-recording", null, true), TestingFloor.PlaySessionId);

            Assert.AreEqual(1, sender.Sent.Count, "The marker goes out at once in its own request");
            var marker = sender.Sent[0];
            Assert.AreEqual(TestingFloor.TelemetryCompleteRecordName, marker["$name"]);
            var record = TelemetryContract.Rows("platform_records", row => TelemetryContract.Text(row, "id") == "telemetry_complete").Single();
            var fields = TelemetryContract.Rows(record, "fields");
            CollectionAssert.AreEquivalent(TelemetryContract.Keys(fields), Properties(marker));
            foreach (var row in fields) AssertValue(row, marker[(string)row["key"]], "Completion field");
            AssertDeclaredEnvelope(marker);
            var context = (Dictionary<string, object>)marker["$tf"];
            CollectionAssert.IsSubsetOf(new[] { "sdk_version", "os", "editor", "recording_uuid", "seq" }, context.Keys,
                "The marker carries the same context as other records");
        }

        [Test]
        public void CapturedContextFollowsTheContractsValueRules() {
            TestingFloor.Track("example_event").Send();

            var context = (Dictionary<string, object>)Wire(First("example_event"))["$tf"];
            var os = (string)context["os"];
            Assert.IsTrue(OsFamilies.Contains(os) || os == Application.platform.ToString(),
                $"$tf.os is {os}, neither an OS family nor the engine's platform name");
            Assert.AreEqual(Application.isEditor, context["editor"]);
            if (context.TryGetValue("locale", out var locale)) {
                StringAssert.IsMatch(LanguageTag.ToString(), (string)locale, "$tf.locale is a BCP 47 tag");
            }
            foreach (var kvp in context) {
                if (kvp.Value is string text) Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"$tf.{kvp.Key} is empty");
            }
            Assert.AreEqual(!string.IsNullOrWhiteSpace(Application.version), context.ContainsKey("app_version"));
        }

        [Test]
        public void PoseAndSessionStateKeysAreTheContractsSdkProperties() {
            UseExampleSources();
            TestingFloor.SetCameraFocusSource(() => new Vector3(7f, 0f, 8f));
            TestingFloor.Track("example_event").Send();
            var camera = _camera.GetComponent<Camera>();
            camera.orthographic = true;
            camera.aspect = 2f;
            TestingFloor.Track("example_orthographic_event").Send();

            var state = Wire(First(TestingFloor.StateRecordName));
            var stateKeys = _records.Where(record => record.EventType == TestingFloor.StateRecordName)
                .SelectMany(record => Properties(Wire(record))).Distinct().ToArray();
            var poseKeys = Properties(Wire(First("example_event")))
                .Union(Properties(Wire(First("example_orthographic_event")))).ToArray();
            var rows = TelemetryContract.Rows("conventional_properties",
                row => TelemetryContract.Text(row, "set_by") == "sdk" && !row.ContainsKey("event"));

            CollectionAssert.IsEmpty(stateKeys.Intersect(poseKeys), "A value is either session state or on every event");
            CollectionAssert.AreEquivalent(TelemetryContract.Keys(rows), stateKeys.Concat(poseKeys));
            AssertDeclaredProperties(state);
            AssertDeclaredProperties(Wire(First("example_event")));
            AssertDeclaredProperties(Wire(First("example_orthographic_event")));
        }

        [Test]
        public void OrthographicCameraSendsItsViewWidthInsteadOfAFieldOfView() {
            UseExampleSources();
            var camera = _camera.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 2f;
            TestingFloor.SetCameraFocusSource(() => new Vector3(7f, 0f, 8f));
            TestingFloor.Track("example_event").Send();

            var state = Wire(First(TestingFloor.StateRecordName));
            var wire = Wire(First("example_event"));
            Assert.AreEqual("orthographic", state["camera.projection"]);
            Assert.IsFalse(state.ContainsKey("camera.fov_axis"), "Only a perspective camera has a FOV axis");
            Assert.AreEqual(20.0, Convert.ToDouble(wire["camera.ortho_width"]), 1e-4, "Width is twice the orthographic size times the aspect");
            Assert.IsFalse(wire.ContainsKey("camera.fov"), "An orthographic camera ignores its field of view");
            Assert.AreEqual(7.0, Convert.ToDouble(wire["camera.focus.x"]), 1e-4);
            Assert.AreEqual(8.0, Convert.ToDouble(wire["camera.focus.z"]), 1e-4);
        }

        [Test]
        public void SdkEventsAndTheirPropertiesAreTheContracts() {
            UseExampleSources();
            MovementTracker.TrackMovementEvent(new MovementEvent("ping", distance: 1f, duration: 2f, rotationDegrees: 3f, fovDelta: 4f, cameraDistance: 5f, orthoWidthDelta: 6f));
            TestingFloor.RecordTeleport(Vector3.zero, new Vector3(3f, 0f, 4f), "example_reason", 0.5);
            TestingFloor.Track("example_event").Send();

            var poseKeys = Properties(Wire(First("example_event")));
            var sdkEvents = _records.Select(record => record.EventType)
                .Where(type => (type == "$tf_player_moved" || type == "$tf_player_teleported" || !type.StartsWith("$", StringComparison.Ordinal)) && type != "example_event").Distinct();
            var contractEvents = TelemetryContract.Keys(TelemetryContract.Rows("sdk_events"), "name");
            CollectionAssert.AreEquivalent(contractEvents, sdkEvents);

            foreach (var name in contractEvents) {
                var wire = Wire(First(name));
                var expected = TelemetryContract.Keys(TelemetryContract.Rows("conventional_properties",
                    row => TelemetryContract.Text(row, "event") == name));
                CollectionAssert.AreEquivalent(expected, Properties(wire).Except(poseKeys), $"{name} properties");
                AssertDeclaredProperties(wire);
            }
        }

        [Test]
        public void TrackedObjectRecordsHaveExactlyTheContractsObjectFields() {
            TestingFloor.Track("level_started").Set("game_level.id", "example-level").Send();
            var crate = TestingFloor.TrackObject("crate", () => new Vector3(1f, 2f, 3f), "crate-1");
            crate.SetState("label", "Example crate");
            TestingFloor.Tick(0);
            crate.Remove("example_reason");
            TestingFloor.Tick(1);

            var records = _records.Where(record => record.EventType == TestingFloor.ObjectsRecordName).Select(Wire).ToList();
            Assert.IsNotEmpty(records, "The SDK sends tracked-object records");
            var keys = records.SelectMany(Properties).Distinct().ToArray();
            var levelKeys = keys.Where(key => key.StartsWith("game_level.", StringComparison.Ordinal)).ToArray();
            CollectionAssert.AreEquivalent(new[] { "game_level.id" }, levelKeys, "Object records copy the event's level");
            var rows = TelemetryContract.Rows("tracked_object_fields");
            CollectionAssert.AreEquivalent(TelemetryContract.Keys(rows), keys.Except(levelKeys));

            var fields = rows.ToDictionary(row => (string)row["key"]);
            var properties = ConventionalProperties();
            foreach (var wire in records) {
                AssertDeclaredEnvelope(wire);
                foreach (var key in Properties(wire)) {
                    AssertValue(fields.TryGetValue(key, out var field) ? field : properties[key], wire[key], "Object record field");
                }
            }
        }

        [Test]
        public void PropertyValueTypesAreTheContractsAllowedTypes() {
            TestingFloor.Track("example_event")
                .Set("example.string", "value")
                .Set("example.integer", 7L)
                .Set("example.number", 1.5)
                .Set("example.boolean", true)
                .Set("example.strings", new[] { "a", "b" })
                .Set("example.integers", new[] { 1, 2 })
                .Set("example.guid", Guid.NewGuid())
                .Send();

            var wire = Wire(First("example_event"));
            var types = Properties(wire).Select(key => TelemetryContract.TypeOf(wire[key])).Distinct();
            var allowed = ((List<object>)TelemetryContract.Section("value_types")["allowed"]).Cast<string>();
            CollectionAssert.AreEquivalent(allowed, types);
        }

        [Test]
        public void DollarPrefixedPropertiesAreDropped() {
            var rule = TelemetryContract.Rows("reserved", row => TelemetryContract.Text(row, "kind") == "property_prefix").Single();
            Assert.AreEqual("$", rule["pattern"]);
            Assert.AreEqual("disallowed", rule["rule"]);

            var context = ContextSnapshot.Create(default);
            context.Set("$example_context", "value");
            TestingFloor.Track("example_event").Set("$example", "value").Set("example.kept", "value").Send();

            var wire = Wire(First("example_event"));
            AssertDeclaredEnvelope(wire);
            CollectionAssert.AreEquivalent(new[] { "example.kept" }, Properties(wire));
            Assert.IsNull(context.Properties, "Context values with $ keys are dropped as well");
        }

        [Test]
        public void ReservedEventNamesAreRefused() {
            var rule = TelemetryContract.Rows("reserved", row => TelemetryContract.Text(row, "kind") == "event_name_prefix").Single();
            Assert.AreEqual(TestingFloor.ReservedEventPrefix, rule["pattern"]);
            Assert.AreEqual("platform_only", rule["rule"]);
            var platformRecords = TelemetryContract.Keys(TelemetryContract.Rows("platform_records"), "name");
            Assert.IsTrue(platformRecords.All(name => !name.StartsWith("$") || name.StartsWith(TestingFloor.ReservedEventPrefix)));

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new Regex("reserved"));
            Assert.IsFalse(TestingFloor.Track(TestingFloor.StateRecordName).Send());
            CollectionAssert.IsEmpty(_records);
        }

        [Test]
        public void LimitsAreTheContracts() {
            var sdk = new Dictionary<string, long> {
                ["event_bytes"] = JsonPayloadWriter.CollectorEventByteCap,
                ["request_bytes"] = JsonPayloadWriter.CollectorBodyByteCap,
                ["events_per_request"] = TelemetryQueue.MaxBatchEvents,
                ["live_tracked_objects"] = TestingFloorSettings.DefaultMaxTrackedObjects,
                ["object_samples_per_record"] = ObjectTracker.MaxSamplesPerRecord,
                ["object_record_bytes"] = ObjectRecordBuilder.RecordByteBudget,
            };
            var contract = TelemetryContract.Rows("limits").ToDictionary(row => (string)row["key"], row => (long)row["value"]);

            CollectionAssert.AreEquivalent(contract.Keys, sdk.Keys, "Map every contract limit to the SDK value that enforces it");
            foreach (var kvp in contract) Assert.AreEqual(kvp.Value, sdk[kvp.Key], kvp.Key);
            Assert.AreEqual(TestingFloorSettings.DefaultMaxTrackedObjects, ObjectTracker.DefaultMaxLiveObjects);
            Assert.AreEqual(TestingFloorSettings.DefaultMaxTrackedObjects, _settings.maxTrackedObjects);
        }

        [Test]
        public void EveryRecordInAPlaySessionUsesDeclaredNamesAndFields() {
            UseExampleSources();
            TestingFloor.Track("level_started").Set("game_level.id", "example-level").Send();
            var crate = TestingFloor.TrackObject("crate", () => new Vector3(1f, 2f, 3f));
            crate.SetState("label", "Example crate");
            TestingFloor.Tick(0);
            MovementTracker.TrackMovementEvent(new MovementEvent("start"));
            TestingFloor.RecordTeleport(Vector3.zero, Vector3.one);
            crate.Remove(null);
            TestingFloor.Tick(1);

            var sdkNames = TelemetryContract.Keys(TelemetryContract.Rows("platform_records",
                    row => TelemetryContract.Text(row, "sent_by") == "sdk" && TelemetryContract.Text(row, "status") == "current"), "name")
                .Concat(TelemetryContract.Keys(TelemetryContract.Rows("sdk_events"), "name"))
                .Append("level_started")
                .ToArray();
            var objectFields = TelemetryContract.Keys(TelemetryContract.Rows("tracked_object_fields"));
            var properties = ConventionalProperties();
            foreach (var record in _records) {
                CollectionAssert.Contains(sdkNames, record.EventType, "Record names are declared in the contract");
                var wire = Wire(record);
                AssertDeclaredEnvelope(wire);
                if (record.Kind != RecordKind.Event) continue;
                foreach (var key in Properties(wire)) {
                    Assert.IsTrue(properties.ContainsKey(key), $"{record.EventType} sends {key}, which the contract does not declare");
                    AssertValue(properties[key], wire[key], "Property");
                }
            }
            CollectionAssert.IsSubsetOf(_records.Where(record => record.Kind == RecordKind.Objects)
                .SelectMany(record => Properties(Wire(record)))
                .Where(key => !key.StartsWith("game_level.", StringComparison.Ordinal)).Distinct().ToArray(), objectFields);
        }

        void UseExampleSources() {
            _camera = new GameObject("example-camera");
            var camera = _camera.AddComponent<Camera>();
            _camera.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 0f));
            TestingFloor.SetCameraSource(() => camera);
            TestingFloor.SetPositionSource(() => new Vector3(4f, 5f, 6f));
        }

        TelemetryEvent First(string name) {
            var matches = _records.Where(record => record.EventType == name).ToArray();
            Assert.IsNotEmpty(matches, $"The SDK sends {name}");
            return matches[0];
        }

        // A session state keyframe in a recording: it carries every $tf key the SDK sets.
        static TelemetryEvent FullRecord() {
            var platform = new PlatformContext(
                os: "macOS",
                locale: "en-US",
                appVersion: "1.2.3",
                unityVersion: "6000.0.0f1",
                screenWidth: 1920,
                screenHeight: 1080,
                deviceModel: "example-device",
                sdkVersion: TestingFloorVersion.Value,
                editor: true);
            return new TelemetryEvent(TestingFloor.StateRecordName, new Dictionary<string, object> { ["example.setting"] = "value" },
                ContextSnapshot.Create(platform), "example-device-id", "example-user-id", 1711234567890, "example-play",
                new TestingFloorRecording("example-recording", 42, true), seq: 7, keyframe: true, kind: RecordKind.State);
        }

        static TelemetryEvent MinimalEvent() =>
            new("example_event", null, ContextSnapshot.Create(default), null, null, 1711234567890, null);

        static byte[] Serialize(TelemetryEvent[] records, out int written) => new JsonPayloadWriter().BuildBatch(
            records, "example-write-key", "example-play",
            JsonPayloadWriter.CollectorBodyByteCap, JsonPayloadWriter.CollectorEventByteCap, null, out _, out written);

        static Dictionary<string, object> Request(params TelemetryEvent[] records) {
            var bytes = Serialize(records, out var written);
            Assert.AreEqual(records.Length, written, "Every record serializes");
            return (Dictionary<string, object>)Json.Parse(Encoding.UTF8.GetString(bytes));
        }

        static IEnumerable<Dictionary<string, object>> Events(byte[] request) =>
            ((List<object>)((Dictionary<string, object>)Json.Parse(Encoding.UTF8.GetString(request)))["events"])
            .Cast<Dictionary<string, object>>();

        static Dictionary<string, object> Wire(TelemetryEvent record) =>
            (Dictionary<string, object>)((List<object>)Request(record)["events"]).Single();

        static string[] Envelope(Dictionary<string, object> wire) =>
            wire.Keys.Where(key => key.StartsWith("$", StringComparison.Ordinal)).ToArray();

        static string[] Properties(Dictionary<string, object> wire) =>
            wire.Keys.Where(key => !key.StartsWith("$", StringComparison.Ordinal)).ToArray();

        static Dictionary<string, Dictionary<string, object>> ConventionalProperties() =>
            TelemetryContract.Rows("conventional_properties").ToDictionary(row => (string)row["key"]);

        // Envelope and $tf keys are declared in the contract, with the declared types.
        static void AssertDeclaredEnvelope(Dictionary<string, object> wire) {
            var fields = TelemetryContract.Rows("event_fields").ToDictionary(row => (string)row["key"]);
            foreach (var key in Envelope(wire)) {
                Assert.IsTrue(fields.ContainsKey(key), $"{wire["$name"]} sends {key}, which the contract does not declare");
                AssertValue(fields[key], wire[key], "Event field");
            }
            if (!wire.TryGetValue("$tf", out var value)) return;
            var context = TelemetryContract.Rows("context_fields", row => TelemetryContract.Text(row, "set_by") == "sdk")
                .ToDictionary(row => (string)row["key"]);
            foreach (var kvp in (Dictionary<string, object>)value) {
                Assert.IsTrue(context.ContainsKey(kvp.Key), $"{wire["$name"]} sends $tf.{kvp.Key}, which the contract does not declare as SDK context");
                AssertValue(context[kvp.Key], kvp.Value, "$tf field");
            }
        }

        static void AssertDeclaredProperties(Dictionary<string, object> wire) {
            AssertDeclaredEnvelope(wire);
            var properties = ConventionalProperties();
            foreach (var key in Properties(wire)) {
                Assert.IsTrue(properties.ContainsKey(key), $"{wire["$name"]} sends {key}, which the contract does not declare");
                AssertValue(properties[key], wire[key], "Property");
            }
        }

        static void AssertValue(Dictionary<string, object> row, object value, string what) {
            var type = (string)row["type"];
            Assert.IsTrue(TelemetryContract.HasType(type, value),
                $"{what} {row["key"]} is {TelemetryContract.TypeOf(value)} on the wire; the contract says {type}");
            if (type == "timestamp") {
                StringAssert.IsMatch(UtcTimestamp.ToString(), (string)value, $"{what} {row["key"]} is an ISO 8601 UTC time with milliseconds");
            }
        }
    }
}
