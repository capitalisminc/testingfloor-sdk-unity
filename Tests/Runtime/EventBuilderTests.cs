using System;
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
        public void ChainedOverridesLastWins() {
            var b = new EventBuilder { _eventType = "test" }
                .Set("k", "first")
                .Set("k", "second");

            Assert.AreEqual("second", b._properties["k"]);
        }

        [Test]
        public void SetAcceptsStringAndIntArrays() {
            var strs = new[] { "a", "b" };
            var ints = new[] { 1, 2, 3 };
            var b = new EventBuilder { _eventType = "test" }
                .Set("tags", strs)
                .Set("levels", ints);

            Assert.AreSame(strs, b._properties["tags"]);
            Assert.AreSame(ints, b._properties["levels"]);
        }

        [Test]
        public void SetAcceptsGuid() {
            var id = Guid.NewGuid();
            var b = new EventBuilder { _eventType = "test" }.Set("entity.id", id);
            Assert.AreEqual(id, b._properties["entity.id"]);
        }

        [Test]
        public void SetIfPresentSkipsNullPrimitives() {
            var b = new EventBuilder { _eventType = "test" }
                .SetIfPresent("a", (int?)null)
                .SetIfPresent("b", (long?)null)
                .SetIfPresent("c", (float?)null)
                .SetIfPresent("d", (double?)null)
                .SetIfPresent("e", (bool?)null);

            Assert.IsNull(b._properties);
        }

        [Test]
        public void SetIfPresentSetsPresentPrimitives() {
            var b = new EventBuilder { _eventType = "test" }
                .SetIfPresent("i", (int?)7)
                .SetIfPresent("l", (long?)9000000000L)
                .SetIfPresent("f", (float?)1.5f)
                .SetIfPresent("d", (double?)2.25)
                .SetIfPresent("b", (bool?)true);

            Assert.AreEqual(7L, b._properties["i"]);
            Assert.AreEqual(9000000000L, b._properties["l"]);
            Assert.AreEqual(1.5, b._properties["f"]);
            Assert.AreEqual(2.25, b._properties["d"]);
            Assert.AreEqual(true, b._properties["b"]);
        }
    }
}
