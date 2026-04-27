using NUnit.Framework;
using UnityEngine;

namespace TestingFloor.Tests {
    public class BuiltInContextProviderTests {
        [TearDown]
        public void TearDown() {
            global::TestingFloor.TestingFloor.ClearPositionSource();
            global::TestingFloor.TestingFloor.ClearCameraSource();
        }

        [Test]
        public void FillWithNoSourcesLeavesSnapshotEmpty() {
            var snapshot = ContextSnapshot.Create(PlatformContext.Capture());
            BuiltInContextProvider.Fill(ref snapshot);
            Assert.IsNull(snapshot.Properties);
        }

        [Test]
        public void FillWithPositionSourceWritesPlayerKeys() {
            global::TestingFloor.TestingFloor.SetPositionSource(() => new Vector3(1.5f, 2.25f, -3f));
            var snapshot = ContextSnapshot.Create(PlatformContext.Capture());
            BuiltInContextProvider.Fill(ref snapshot);

            Assert.IsNotNull(snapshot.Properties);
            Assert.IsTrue(snapshot.Properties.ContainsKey("player.position.x"));
            Assert.IsTrue(snapshot.Properties.ContainsKey("player.position.y"));
            Assert.IsTrue(snapshot.Properties.ContainsKey("player.position.z"));
            Assert.AreEqual(1.5, (double)snapshot.Properties["player.position.x"], 1e-6);
            Assert.AreEqual(2.25, (double)snapshot.Properties["player.position.y"], 1e-6);
            Assert.AreEqual(-3.0, (double)snapshot.Properties["player.position.z"], 1e-6);
        }

        [Test]
        public void FillWithNullReturningPositionSourceWritesNothing() {
            global::TestingFloor.TestingFloor.SetPositionSource(() => null);
            var snapshot = ContextSnapshot.Create(PlatformContext.Capture());
            BuiltInContextProvider.Fill(ref snapshot);
            Assert.IsNull(snapshot.Properties);
        }

        [Test]
        public void FillWithThrowingPositionSourceDoesNotPropagate() {
            global::TestingFloor.TestingFloor.SetPositionSource(() => throw new System.InvalidOperationException("nope"));
            var snapshot = ContextSnapshot.Create(PlatformContext.Capture());
            Assert.DoesNotThrow(() => BuiltInContextProvider.Fill(ref snapshot));
            Assert.IsNull(snapshot.Properties);
        }
    }
}
