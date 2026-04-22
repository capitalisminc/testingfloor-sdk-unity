using NUnit.Framework;
using TestingFloor.Internal;

namespace TestingFloor.Tests {
    public class QrEncoderTests {
        [Test]
        public void ShortPayloadFitsInVersion1() {
            var matrix = QrEncoder.Encode("hello");
            Assert.AreEqual(1, matrix.Version);
            Assert.AreEqual(21, matrix.Size);
        }

        [Test]
        public void HeartbeatPayloadEncodesAtSmallVersion() {
            var payload = QrHeartbeatPayload.Build("0f4c6f65-49d5-4c40-b4d4-c2e7acb1f7a8", 1711234567890, 42);
            var matrix = QrEncoder.Encode(payload);
            Assert.LessOrEqual(matrix.Version, 5);
        }

        [Test]
        public void HeartbeatPayloadUsesDocumentedUriContract() {
            var payload = QrHeartbeatPayload.Build("tf_7K4p9Qx2mN", 1711234567890, 42);
            Assert.AreEqual("tfqr://sync/v1?s=tf_7K4p9Qx2mN&t=1711234567890&q=42", payload);
            Assert.Less(payload.Length, 150);
        }

        [Test]
        public void HeartbeatPayloadEscapesSessionId() {
            var payload = QrHeartbeatPayload.Build("session with spaces", 1711234567890, 1);
            Assert.AreEqual("tfqr://sync/v1?s=session%20with%20spaces&t=1711234567890&q=1", payload);
        }

        [Test]
        public void FinderPatternsPlacedAtThreeCorners() {
            var matrix = QrEncoder.Encode("hello");
            AssertFinderAt(matrix, 0, 0);
            AssertFinderAt(matrix, 0, matrix.Size - 7);
            AssertFinderAt(matrix, matrix.Size - 7, 0);
        }

        [Test]
        public void DarkModuleIsAlwaysSet() {
            var matrix = QrEncoder.Encode("x");
            Assert.IsTrue(matrix[matrix.Size - 8, 8], "Dark module at (4V+9, 8) must be set.");
        }

        [Test]
        public void EncodesAreDeterministic() {
            var a = QrEncoder.Encode("deterministic");
            var b = QrEncoder.Encode("deterministic");
            Assert.AreEqual(a.Size, b.Size);
            for (var r = 0; r < a.Size; r++) {
                for (var c = 0; c < a.Size; c++) {
                    Assert.AreEqual(a[r, c], b[r, c], $"mismatch at ({r},{c})");
                }
            }
        }

        [Test]
        public void EncodesMaxCapacityPayload() {
            var capacity = QrTables.BytePayloadCapacity(QrTables.MaxVersion);
            var text = new string('a', capacity);
            var matrix = QrEncoder.Encode(text);
            Assert.AreEqual(QrTables.MaxVersion, matrix.Version);
        }

        [Test]
        public void OversizedPayloadThrows() {
            var capacity = QrTables.BytePayloadCapacity(QrTables.MaxVersion);
            var text = new string('a', capacity + 1);
            Assert.Throws<System.ArgumentException>(() => QrEncoder.Encode(text));
        }

        [Test]
        public void PicksSmallestVersionThatFits() {
            Assert.AreEqual(1, QrTables.PickSmallestVersionFor(1));
            Assert.AreEqual(1, QrTables.PickSmallestVersionFor(QrTables.BytePayloadCapacity(1)));
            Assert.AreEqual(2, QrTables.PickSmallestVersionFor(QrTables.BytePayloadCapacity(1) + 1));
            Assert.AreEqual(QrTables.MaxVersion, QrTables.PickSmallestVersionFor(QrTables.BytePayloadCapacity(QrTables.MaxVersion)));
            Assert.AreEqual(-1, QrTables.PickSmallestVersionFor(QrTables.BytePayloadCapacity(QrTables.MaxVersion) + 1));
        }

        static void AssertFinderAt(QrMatrix matrix, int r0, int c0) {
            for (var r = 0; r < 7; r++) {
                for (var c = 0; c < 7; c++) {
                    var expected = r == 0 || r == 6 || c == 0 || c == 6
                                   || (r >= 2 && r <= 4 && c >= 2 && c <= 4);
                    Assert.AreEqual(expected, matrix[r0 + r, c0 + c], $"finder mismatch at ({r0 + r},{c0 + c})");
                }
            }
        }
    }
}
