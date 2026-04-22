using NUnit.Framework;
using TestingFloor.Internal;

namespace TestingFloor.Tests {
    public class TelemetryQueueTests {
        [Test]
        public void RentAndReturnReusesDictionary() {
            var a = TelemetryQueue.RentProperties();
            Assert.IsNotNull(a);
            a["k"] = "v";
            TelemetryQueue.ReturnProperties(a);

            var b = TelemetryQueue.RentProperties();
            Assert.AreSame(a, b, "pool should hand back the same instance");
            Assert.AreEqual(0, b.Count, "returned dict must be cleared");
            TelemetryQueue.ReturnProperties(b);
        }

        [Test]
        public void ReturnNullIsSafe() {
            Assert.DoesNotThrow(() => TelemetryQueue.ReturnProperties(null));
        }

        [Test]
        public void RentAfterPoolExhaustionReturnsFreshInstance() {
            var first = TelemetryQueue.RentProperties();
            var second = TelemetryQueue.RentProperties();
            Assert.AreNotSame(first, second);
            TelemetryQueue.ReturnProperties(first);
            TelemetryQueue.ReturnProperties(second);
        }
    }
}
