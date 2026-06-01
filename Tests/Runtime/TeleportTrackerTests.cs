using NUnit.Framework;
using UnityEngine;

namespace TestingFloor.Tests {
    public class TeleportTrackerTests {
        [SetUp]
        public void SetUp() {
            while (TeleportTracker.ConsumeMovementResetRequested()) { }
            global::TestingFloor.TestingFloor.ClearPositionSource();
        }

        [TearDown]
        public void TearDown() {
            global::TestingFloor.TestingFloor.ClearPositionSource();
            while (TeleportTracker.ConsumeMovementResetRequested()) { }
        }

        [Test]
        public void RecordTeleportRequestsMovementReset() {
            global::TestingFloor.TestingFloor.RecordTeleport(
                Vector3.zero,
                new Vector3(10f, 0f, 0f),
                "test",
                0.25);

            Assert.IsTrue(TeleportTracker.ConsumeMovementResetRequested());
            Assert.IsFalse(TeleportTracker.ConsumeMovementResetRequested());
        }

        [Test]
        public void ScopeStaysActiveUntilDisposed() {
            var position = Vector3.zero;
            global::TestingFloor.TestingFloor.SetPositionSource(() => position);

            var scope = global::TestingFloor.TestingFloor.BeginTeleport("async");
            Assert.IsTrue(TeleportTracker.IsTeleporting);

            position = new Vector3(5f, 0f, 0f);
            scope.Cancel();
            scope.Dispose();

            Assert.IsFalse(TeleportTracker.IsTeleporting);
            Assert.IsTrue(TeleportTracker.ConsumeMovementResetRequested());
        }
    }
}
