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

        [Test]
        public void NullPositionEmitsNothingAndStaysIdle() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            s.Step(0f, null, Recorder(emitted));
            s.Step(0.5f, null, Recorder(emitted));
            Assert.IsEmpty(emitted);
            Assert.IsFalse(s.IsMoving);
        }

        [Test]
        public void StationaryEmitsNothing() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var pos = new Vector3(0, 0, 0);
            for (var t = 0f; t < 5f; t += 0.05f) {
                s.Step(t, pos, Recorder(emitted));
            }
            Assert.IsEmpty(emitted);
        }

        [Test]
        public void StartFiresAfterStartGraceOfMotion() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            s.Step(0f, new Vector3(0, 0, 0), rec);
            s.Step(0.02f, new Vector3(0.3f, 0, 0), rec);
            Assert.IsEmpty(emitted, "start should not fire before StartGraceSeconds (0.05) elapsed");
            s.Step(0.1f, new Vector3(0.6f, 0, 0), rec);
            Assert.Contains(MovementReasons.Start, Reasons(emitted));
            Assert.IsTrue(s.IsMoving);
        }

        [Test]
        public void StopFiresWithRunDistanceAndDuration() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            // Move steadily for ~0.5s, accumulating ~5 units of distance
            s.Step(0f, new Vector3(0, 0, 0), rec);
            for (var i = 1; i <= 10; i++) {
                s.Step(i * 0.05f, new Vector3(i * 0.5f, 0, 0), rec);
            }
            // Then stand still long enough for stop
            s.Step(0.85f, new Vector3(5f, 0, 0), rec);

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
            s.Step(0f, new Vector3(0, 0, 0), rec);
            for (var i = 1; i <= 30; i++) {
                s.Step(i * 0.05f, new Vector3(i * 1.0f, 0, 0), rec);
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
            s.Step(0f, new Vector3(0, 0, 0), rec);
            // 20 steps, each adds 0.01 — accumulates only 0.2m total, under MinPingDistance=0.5
            for (var i = 0; i < 20; i++) {
                var t = (i + 1) * 0.05f;
                s.Step(t, new Vector3(0.01f * (i + 1), 0, 0), rec);
            }
            Assert.IsFalse(Reasons(emitted).Contains(MovementReasons.Ping));
        }

        [Test]
        public void SubMinStepJitterDoesNotTriggerStart() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            s.Step(0f, new Vector3(0, 0, 0), rec);
            for (var i = 0; i < 50; i++) {
                var t = (i + 1) * 0.05f;
                // alternating ±0.0005 — well under MinStep=0.005
                var x = 0.0005f * (i % 2 == 0 ? 1 : -1);
                s.Step(t, new Vector3(x, 0, 0), rec);
            }
            Assert.IsEmpty(emitted);
        }

        [Test]
        public void ResetClearsState() {
            var s = MakeSampler();
            var emitted = new List<MovementEvent>();
            var rec = Recorder(emitted);
            s.Step(0f, new Vector3(0, 0, 0), rec);
            s.Step(0.02f, new Vector3(0.3f, 0, 0), rec);
            s.Step(0.1f, new Vector3(0.6f, 0, 0), rec);
            Assert.IsTrue(s.IsMoving);
            s.Reset();
            Assert.IsFalse(s.IsMoving);
        }
    }
}
