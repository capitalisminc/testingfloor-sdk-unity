using NUnit.Framework;
using UnityEngine;

namespace TestingFloorSdk.Tests {
    public class TeleportTrackerTests {
        [SetUp]
        public void SetUp() {
            while (TeleportTracker.ConsumeMovementResetRequested()) { }
            global::TestingFloorSdk.TestingFloor.ClearPositionSource();
        }

        [TearDown]
        public void TearDown() {
            global::TestingFloorSdk.TestingFloor.ClearPositionSource();
            while (TeleportTracker.ConsumeMovementResetRequested()) { }
        }

        [Test]
        public void RecordTeleportRequestsMovementReset() {
            global::TestingFloorSdk.TestingFloor.RecordTeleport(
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
            global::TestingFloorSdk.TestingFloor.SetPositionSource(() => position);

            var scope = global::TestingFloorSdk.TestingFloor.BeginTeleport("async");
            Assert.IsTrue(TeleportTracker.IsTeleporting);

            position = new Vector3(5f, 0f, 0f);
            scope.Cancel();
            scope.Dispose();

            Assert.IsFalse(TeleportTracker.IsTeleporting);
            Assert.IsTrue(TeleportTracker.ConsumeMovementResetRequested());
        }
    }
}
