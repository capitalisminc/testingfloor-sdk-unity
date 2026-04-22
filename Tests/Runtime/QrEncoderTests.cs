using System.Collections.Generic;
using NUnit.Framework;
using TestingFloor.Internal;
using UnityEngine;
using ZXing;
using ZXing.Common;

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
        public void HeartbeatPayloadDecodesWithThirdPartyReader() {
            var payload = QrHeartbeatPayload.Build("0f4c6f65-49d5-4c40-b4d4-c2e7acb1f7a8", 1711234567890, 42);
            var matrix = QrEncoder.Encode(payload);
            var texture = Render(matrix, scale: 4, quiet: 4);

            try {
                Assert.AreEqual(payload, DecodeWithZxing(texture));
            }
            finally {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void RenderedTextureDecodesWithThirdPartyReaderAtOverlayScale() {
            var payload = QrHeartbeatPayload.Build("0f4c6f65-49d5-4c40-b4d4-c2e7acb1f7a8", 1711234567890, 42);
            var matrix = QrEncoder.Encode(payload);
            var texture = Render(matrix, scale: 3, quiet: 4);

            try {
                Assert.AreEqual(payload, DecodeWithZxing(texture));
            }
            finally {
                UnityEngine.Object.DestroyImmediate(texture);
            }
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
        public void LargeByteModePayloadFitsWithinVersionLimit() {
            var text = new string('a', 213);
            var matrix = QrEncoder.Encode(text);
            Assert.AreEqual(10, matrix.Version);
        }

        [Test]
        public void OversizedPayloadThrows() {
            var text = new string('a', 214);
            Assert.Throws<System.ArgumentException>(() => QrEncoder.Encode(text));
        }

        static Texture2D Render(QrMatrix matrix, int scale, int quiet) {
            return matrix.ToTexture(scale, quiet);
        }

        static string DecodeWithZxing(Texture2D texture) {
            var rgba = ToTopDownRgba(texture);
            var source = new RGBLuminanceSource(rgba, texture.width, texture.height, RGBLuminanceSource.BitmapFormat.RGBA32);
            var reader = new BarcodeReaderGeneric {
                Options = new DecodingOptions {
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                    TryHarder = true,
                },
            };
            var result = reader.Decode(source);
            Assert.NotNull(result, "ZXing.Net should decode the rendered QR texture.");
            return result.Text;
        }

        static byte[] ToTopDownRgba(Texture2D texture) {
            var pixels = texture.GetPixels32();
            var bytes = new byte[pixels.Length * 4];
            var width = texture.width;
            var height = texture.height;

            for (var y = 0; y < height; y++) {
                var sourceY = height - 1 - y;
                for (var x = 0; x < width; x++) {
                    var pixel = pixels[sourceY * width + x];
                    var offset = (y * width + x) * 4;
                    bytes[offset] = pixel.r;
                    bytes[offset + 1] = pixel.g;
                    bytes[offset + 2] = pixel.b;
                    bytes[offset + 3] = pixel.a;
                }
            }
            return bytes;
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
