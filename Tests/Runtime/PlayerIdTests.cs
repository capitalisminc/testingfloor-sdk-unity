using NUnit.Framework;
using TestingFloor.Internal;

namespace TestingFloor.Tests {
    public class PlayerIdTests {
        [Test]
        public void PlayerIdIsProfileIdWhenPresent() {
            Assert.AreEqual("player-123", Identity.ResolvePlayerId("player-123", "device-abc"));
        }

        [Test]
        public void PlayerIdFallsBackToDeviceIdWithoutProfileId() {
            // Events without a profile id omit $user_id, so Testing Floor
            // shows the $device_id as the Player ID.
            Assert.AreEqual("device-abc", Identity.ResolvePlayerId(null, "device-abc"));
            Assert.AreEqual("device-abc", Identity.ResolvePlayerId("", "device-abc"));
            Assert.AreEqual("device-abc", Identity.ResolvePlayerId("   ", "device-abc"));
        }
    }
}
