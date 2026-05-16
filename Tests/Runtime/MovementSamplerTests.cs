using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TestingFloor.Tests {
    public class MovementSamplerTests {
        static MovementSampler MakeSampler() {
            return new MovementSampler {
                MinPingDistance = 0.5f,
                MinPingIntervalSeconds = 0.5f,
                StartGraceSeconds = 0.05f,
                StopGraceSeconds = 0.25f,
                MinStep = 0.005f,
                MinPingRotationDegrees = 5f,
                MinStepRotationDegrees = 0.1f,
                MinPingFovDegrees = 2f,
                MinStepFovDegrees = 0.05f,
                MinPingCameraDistance = 0.5f,
                MinStepCameraDistance = 0.005f,
            };
        }

        static System.Action<MovementEvent> Recorder(List<MovementEvent> sink) {
            return ev => sink.Add(ev);
        }

        static List<string> Reasons(List<MovementEvent> events) {
            var r = new List<string>(events.Count);
            foreach (var ev in events) r.Add(ev.Reason);
            return r;
        }

        static ActivitySample PositionSample(Vector3 pos) {
            return new ActivitySample(playerPosition: pos);
        }

        static ActivitySample RotationSample(Quaternion rotation) {
            return new ActivitySample(cameraRotation: rotation);
        }

        static ActivitySample FovSample(float fov) {
            return new ActivitySample(cameraFov: fov);
        }

        static ActivitySample CameraPositionSample(Vector3 pos) {
            return new ActivitySample(cameraPosition: pos);
        }

        [Test]
        public void EmptySampleEmitsNothingAndStaysIdle() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            s.Step(0f, default, Recorder(emitted));
            s.Step(0.5f, default, Recorder(emitted));
            Assert.IsEmpty(emitted);
            Assert.IsFalse(s.IsMoving);
        }

        [Test]
        public void StationaryEmitsNothing() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var sample = PositionSample(new Vector3(0, 0, 0));
            for (var t = 0f; t < 5f; t += 0.05f) {
                s.Step(t, sample, Recorder(emitted));
            }
            Assert.IsEmpty(emitted);
        }

        [Test]
        public void StartFiresAfterStartGraceOfMotion() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            s.Step(0f, PositionSample(new Vector3(0, 0, 0)), rec);
            s.Step(0.02f, PositionSample(new Vector3(0.3f, 0, 0)), rec);
            Assert.IsEmpty(emitted, "start should not fire before StartGraceSeconds (0.05) elapsed");
            s.Step(0.1f, PositionSample(new Vector3(0.6f, 0, 0)), rec);
            Assert.Contains(MovementReasons.Start, Reasons(emitted));
            Assert.IsTrue(s.IsMoving);
        }

        [Test]
        public void StopFiresWithRunDistanceAndDuration() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            // Move steadily for ~0.5s, accumulating ~5 units of distance
            s.Step(0f, PositionSample(new Vector3(0, 0, 0)), rec);
            for (var i = 1; i <= 10; i++) {
                s.Step(i * 0.05f, PositionSample(new Vector3(i * 0.5f, 0, 0)), rec);
            }
            // Then stand still long enough for stop
            s.Step(0.85f, PositionSample(new Vector3(5f, 0, 0)), rec);

            var stop = emitted.FindLast(e => e.Reason == MovementReasons.Stop);
            Assert.AreEqual(MovementReasons.Stop, stop.Reason);
            Assert.Greater(stop.Distance, 4f, "stop should report cumulative run distance");
            Assert.Greater(stop.Duration, 0.4f, "stop should report run duration");
            Assert.IsFalse(s.IsMoving);
        }

        [Test]
        public void PingFiresWithSegmentDistanceAndDuration() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            // Steady motion 1m/step at 0.05s/step → 20 m/s. Plenty of pings.
            s.Step(0f, PositionSample(new Vector3(0, 0, 0)), rec);
            for (var i = 1; i <= 30; i++) {
                s.Step(i * 0.05f, PositionSample(new Vector3(i * 1.0f, 0, 0)), rec);
            }
            var pings = emitted.FindAll(e => e.Reason == MovementReasons.Ping);
            Assert.GreaterOrEqual(pings.Count, 1, "expected at least one ping during steady motion");
            foreach (var p in pings) {
                Assert.Greater(p.Distance, 0f, "ping should carry segment distance");
                Assert.Greater(p.Duration, 0f, "ping should carry segment duration");
            }
        }

        [Test]
        public void PingDoesNotFireWithoutMinPingDistance() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            s.Step(0f, PositionSample(new Vector3(0, 0, 0)), rec);
            // 20 steps, each adds 0.01 — accumulates only 0.2m total, under MinPingDistance=0.5
            for (var i = 0; i < 20; i++) {
                var t = (i + 1) * 0.05f;
                s.Step(t, PositionSample(new Vector3(0.01f * (i + 1), 0, 0)), rec);
            }
            Assert.IsFalse(Reasons(emitted).Contains(MovementReasons.Ping));
        }

        [Test]
        public void SubMinStepJitterDoesNotTriggerStart() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            s.Step(0f, PositionSample(new Vector3(0, 0, 0)), rec);
            for (var i = 0; i < 50; i++) {
                var t = (i + 1) * 0.05f;
                // alternating ±0.0005 — well under MinStep=0.005
                var x = 0.0005f * (i % 2 == 0 ? 1 : -1);
                s.Step(t, PositionSample(new Vector3(x, 0, 0)), rec);
            }
            Assert.IsEmpty(emitted);
        }

        [Test]
        public void ResetClearsState() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            s.Step(0f, PositionSample(new Vector3(0, 0, 0)), rec);
            s.Step(0.02f, PositionSample(new Vector3(0.3f, 0, 0)), rec);
            s.Step(0.1f, PositionSample(new Vector3(0.6f, 0, 0)), rec);
            Assert.IsTrue(s.IsMoving);
            s.Reset();
            Assert.IsFalse(s.IsMoving);
        }

        [Test]
        public void RotationAloneTriggersStartAndStop() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            s.Step(0f, RotationSample(Quaternion.Euler(0, 0, 0)), rec);
            s.Step(0.02f, RotationSample(Quaternion.Euler(0, 10, 0)), rec);
            s.Step(0.1f, RotationSample(Quaternion.Euler(0, 20, 0)), rec);
            Assert.Contains(MovementReasons.Start, Reasons(emitted));
            Assert.IsTrue(s.IsMoving);

            // Hold still long enough to stop.
            var held = Quaternion.Euler(0, 20, 0);
            s.Step(0.5f, RotationSample(held), rec);
            Assert.Contains(MovementReasons.Stop, Reasons(emitted));
            Assert.IsFalse(s.IsMoving);
        }

        [Test]
        public void FovZoomAloneTriggersStartAndStop() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            // FOV zooming from 60° to 30° fast.
            s.Step(0f, FovSample(60f), rec);
            s.Step(0.02f, FovSample(55f), rec);
            s.Step(0.1f, FovSample(30f), rec);
            Assert.Contains(MovementReasons.Start, Reasons(emitted));

            s.Step(0.5f, FovSample(30f), rec);
            Assert.Contains(MovementReasons.Stop, Reasons(emitted));
        }

        [Test]
        public void CameraPositionAloneTriggersActivity() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            s.Step(0f, CameraPositionSample(Vector3.zero), rec);
            s.Step(0.02f, CameraPositionSample(new Vector3(0.3f, 0, 0)), rec);
            s.Step(0.1f, CameraPositionSample(new Vector3(0.6f, 0, 0)), rec);
            Assert.Contains(MovementReasons.Start, Reasons(emitted));
        }

        [Test]
        public void PositionStopsButRotationContinuesPreventsStop() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);

            // Get moving via position.
            s.Step(0f, new ActivitySample(playerPosition: Vector3.zero, cameraRotation: Quaternion.identity), rec);
            for (var i = 1; i <= 5; i++) {
                s.Step(i * 0.05f, new ActivitySample(
                    playerPosition: new Vector3(i * 0.3f, 0, 0),
                    cameraRotation: Quaternion.Euler(0, i * 4f, 0)
                ), rec);
            }
            Assert.IsTrue(s.IsMoving);

            // Position freezes; camera keeps rotating well past StopGraceSeconds.
            var frozenPos = new Vector3(1.5f, 0, 0);
            for (var i = 6; i <= 30; i++) {
                s.Step(i * 0.05f, new ActivitySample(
                    playerPosition: frozenPos,
                    cameraRotation: Quaternion.Euler(0, i * 4f, 0)
                ), rec);
            }

            Assert.IsTrue(s.IsMoving, "rotation activity should keep IsMoving true");
            Assert.IsFalse(Reasons(emitted).Contains(MovementReasons.Stop), "stop must not fire while camera is still rotating");
        }

        [Test]
        public void RotationBelowMinStepIsJitter() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            s.Step(0f, RotationSample(Quaternion.identity), rec);
            // 50 steps of 0.01° alternating — sub MinStepRotationDegrees=0.1°.
            for (var i = 0; i < 50; i++) {
                var t = (i + 1) * 0.05f;
                var yaw = 0.01f * (i % 2 == 0 ? 1 : -1);
                s.Step(t, RotationSample(Quaternion.Euler(0, yaw, 0)), rec);
            }
            Assert.IsEmpty(emitted);
        }

        [Test]
        public void RotationWrapAroundDoesNotSpike() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            // Continuous yaw rotation through 360°→0° wrap. Each step is +2°.
            // A naïve euler-subtract sampler would see one step of -358°.
            // Quaternion.Angle should report 2° per step.
            s.Step(0f, RotationSample(Quaternion.Euler(0, 359f, 0)), rec);
            var yaw = 359f;
            var t = 0f;
            for (var i = 0; i < 30; i++) {
                t += 0.05f;
                yaw += 2f;
                if (yaw >= 360f) yaw -= 360f;
                s.Step(t, RotationSample(Quaternion.Euler(0, yaw, 0)), rec);
            }
            // Total true rotation across the run is ~60°. A spike from wraparound would
            // dwarf any individual ping or the stop event with ~358°.
            float maxReportedRotation = 0f;
            foreach (var ev in emitted) {
                if (ev.RotationDegrees > maxReportedRotation) maxReportedRotation = ev.RotationDegrees;
            }
            Assert.Less(maxReportedRotation, 90f, "wraparound should not produce ~358° rotation spikes");
        }

        [Test]
        public void PingReportsRotationAndFovDelta() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);

            s.Step(0f, new ActivitySample(
                playerPosition: Vector3.zero,
                cameraRotation: Quaternion.identity,
                cameraFov: 60f
            ), rec);
            for (var i = 1; i <= 30; i++) {
                s.Step(i * 0.05f, new ActivitySample(
                    playerPosition: new Vector3(i * 1.0f, 0, 0),
                    cameraRotation: Quaternion.Euler(0, i * 3f, 0),
                    cameraFov: 60f + i * 0.5f
                ), rec);
            }
            var pings = emitted.FindAll(e => e.Reason == MovementReasons.Ping);
            Assert.GreaterOrEqual(pings.Count, 1, "expected at least one ping");
            var anyRotation = false;
            var anyFov = false;
            foreach (var p in pings) {
                if (p.RotationDegrees > 0f) anyRotation = true;
                if (p.FovDelta > 0f) anyFov = true;
            }
            Assert.IsTrue(anyRotation, "at least one ping should carry rotation_degrees");
            Assert.IsTrue(anyFov, "at least one ping should carry fov_delta");
        }

        [Test]
        public void StopReportsCumulativeRunRotationAndFov() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);

            s.Step(0f, new ActivitySample(
                playerPosition: Vector3.zero,
                cameraRotation: Quaternion.identity,
                cameraFov: 60f
            ), rec);
            for (var i = 1; i <= 10; i++) {
                s.Step(i * 0.05f, new ActivitySample(
                    playerPosition: new Vector3(i * 0.5f, 0, 0),
                    cameraRotation: Quaternion.Euler(0, i * 6f, 0),
                    cameraFov: 60f + i * 1f
                ), rec);
            }
            // Hold still long enough to stop.
            var lastSample = new ActivitySample(
                playerPosition: new Vector3(5f, 0, 0),
                cameraRotation: Quaternion.Euler(0, 60f, 0),
                cameraFov: 70f
            );
            s.Step(0.85f, lastSample, rec);

            var stop = emitted.FindLast(e => e.Reason == MovementReasons.Stop);
            Assert.AreEqual(MovementReasons.Stop, stop.Reason);
            Assert.Greater(stop.Distance, 4f, "stop carries cumulative player distance");
            Assert.Greater(stop.RotationDegrees, 40f, "stop carries cumulative rotation degrees");
            Assert.Greater(stop.FovDelta, 8f, "stop carries cumulative FOV delta");
        }
    }
}
