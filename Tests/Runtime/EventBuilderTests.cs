using System;
using NUnit.Framework;

namespace TestingFloorSdk.Tests {
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

            Assert.AreNotSame(strs, b._properties["tags"]);
            CollectionAssert.AreEqual(strs, (string[])b._properties["tags"]);
            Assert.AreNotSame(ints, b._properties["levels"]);
            CollectionAssert.AreEqual(ints, (int[])b._properties["levels"]);
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
        [Test]
        public void ReservedKeysAndNonfiniteValuesAreIgnored() {
            var builder = new EventBuilder().Set("$name", "spoof").Set("", "empty")
                .Set("nan", double.NaN).Set("infinite", double.PositiveInfinity);
            Assert.IsNull(builder._properties);
            var snapshot = ContextSnapshot.Create(default);
            snapshot.Set("$schema", 99L);
            snapshot.Set("nan", double.NaN);
            Assert.IsNull(snapshot.Properties);
        }

        [Test]
        public void BuilderCannotBeSentTwiceOrMutatedAfterSending() {
            var builder = global::TestingFloorSdk.TestingFloor.Track("example").Set("value", 42);
            Assert.IsFalse(builder.Send(), "Automation cannot enqueue live telemetry");
            Assert.Throws<InvalidOperationException>(() => builder.Send());
            Assert.Throws<InvalidOperationException>(() => builder.Set("value", 43));
        }

        [Test]
        public void PropertyArraysOwnTheirCapturedValues() {
            var values = new[] { "before" };
            var builder = new EventBuilder().Set("values", values);
            var snapshot = ContextSnapshot.Create(default);
            snapshot.Set("values", values);
            values[0] = "after";
            Assert.AreEqual("before", ((string[])builder._properties["values"])[0]);
            Assert.AreEqual("before", ((string[])snapshot.Properties["values"])[0]);
        }
    }
}
