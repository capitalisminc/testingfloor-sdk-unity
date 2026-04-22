using NUnit.Framework;

namespace TestingFloor.Tests {
    public class EventBuilderTests {
        [Test]
        public void SetAccumulatesProperties() {
            var b = new EventBuilder { _eventType = "test" }
                .Set("s", "hello")
                .Set("n", 42L)
                .Set("f", 3.14)
                .Set("b", true);

            Assert.IsNotNull(b._properties);
            Assert.AreEqual("hello", b._properties["s"]);
            Assert.AreEqual(42L, b._properties["n"]);
            Assert.AreEqual(3.14, b._properties["f"]);
            Assert.AreEqual(true, b._properties["b"]);
        }

        [Test]
        public void SetIfPresentSkipsEmpty() {
            var b = new EventBuilder { _eventType = "test" }
                .SetIfPresent("empty", "")
                .SetIfPresent("whitespace", "   ")
                .SetIfPresent("filled", "value");

            Assert.IsNotNull(b._properties);
            Assert.IsFalse(b._properties.ContainsKey("empty"));
            Assert.IsFalse(b._properties.ContainsKey("whitespace"));
            Assert.AreEqual("value", b._properties["filled"]);
        }

        [Test]
        public void SetNullObjectDoesNotAllocate() {
            var b = new EventBuilder { _eventType = "test" }.Set("key", (object)null);
            Assert.IsNull(b._properties);
        }

        [Test]
        public void ChainedOverridesLastWins() {
            var b = new EventBuilder { _eventType = "test" }
                .Set("k", "first")
                .Set("k", "second");

            Assert.AreEqual("second", b._properties["k"]);
        }
    }
}
