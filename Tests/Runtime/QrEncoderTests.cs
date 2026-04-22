using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using TestingFloor.Internal;
using UnityEngine;

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
        public void HeartbeatPayloadRoundTripsThroughQrReader() {
            var payload = QrHeartbeatPayload.Build("0f4c6f65-49d5-4c40-b4d4-c2e7acb1f7a8", 1711234567890, 42);
            var matrix = QrEncoder.Encode(payload);
            Assert.AreEqual(5, matrix.Version);
            Assert.AreEqual(payload, QrRoundTripReader.Decode(matrix));
        }

        [Test]
        public void RenderedTextureRoundTripsThroughQrReader() {
            var payload = QrHeartbeatPayload.Build("0f4c6f65-49d5-4c40-b4d4-c2e7acb1f7a8", 1711234567890, 42);
            var matrix = QrEncoder.Encode(payload);
            var texture = matrix.ToTexture(scale: 3, quiet: 4);

            try {
                Assert.AreEqual(payload, QrRoundTripReader.Decode(texture, matrix.Size, scale: 3, quiet: 4));
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

        static class QrRoundTripReader {
            const byte ByteMode = 0b0100;

            public static string Decode(QrMatrix matrix) {
                var modules = new bool[matrix.Size, matrix.Size];
                for (var r = 0; r < matrix.Size; r++) {
                    for (var c = 0; c < matrix.Size; c++) {
                        modules[r, c] = matrix[r, c];
                    }
                }
                return Decode(modules);
            }

            public static string Decode(Texture2D texture, int matrixSize, int scale, int quiet) {
                var modules = new bool[matrixSize, matrixSize];
                for (var r = 0; r < matrixSize; r++) {
                    for (var c = 0; c < matrixSize; c++) {
                        var x = (c + quiet) * scale + scale / 2;
                        var y = texture.height - 1 - ((r + quiet) * scale + scale / 2);
                        var pixel = texture.GetPixel(x, y);
                        modules[r, c] = pixel.r < 0.5f;
                    }
                }
                return Decode(modules);
            }

            static string Decode(bool[,] modules) {
                var size = modules.GetLength(0);
                Assert.AreEqual(size, modules.GetLength(1));
                Assert.AreEqual(0, (size - 17) % 4, "QR matrix size must be 4V + 17.");

                var version = (size - 17) / 4;
                Assert.GreaterOrEqual(version, QrTables.MinVersion);
                Assert.LessOrEqual(version, QrTables.MaxVersion);

                var mask = ReadMask(modules, size);
                var reserved = BuildFunctionPattern(version, size);
                var codewords = ReadCodewords(modules, reserved, version, mask);
                var data = DeinterleaveAndVerify(codewords, version);

                var bits = new BitReader(data);
                Assert.AreEqual(ByteMode, bits.ReadBits(4), "Only byte-mode QR payloads are expected.");
                var length = bits.ReadBits(version < 10 ? 8 : 16);
                var payload = new byte[length];
                for (var i = 0; i < payload.Length; i++) payload[i] = (byte)bits.ReadBits(8);
                return Encoding.UTF8.GetString(payload);
            }

            static int ReadMask(bool[,] modules, int size) {
                var first = ReadPrimaryFormatInfo(modules);
                var second = ReadSecondaryFormatInfo(modules, size);

                var firstMask = DecodeFormatMask(first, out var firstDistance);
                var secondMask = DecodeFormatMask(second, out var secondDistance);
                Assert.LessOrEqual(firstDistance, 3, "Primary format information must decode as ECC-M with a valid mask.");
                Assert.LessOrEqual(secondDistance, 3, "Secondary format information must decode as ECC-M with a valid mask.");
                Assert.AreEqual(firstMask, secondMask, "Both copies of QR format information should select the same mask.");
                return firstMask;
            }

            static int ReadPrimaryFormatInfo(bool[,] modules) {
                var format = 0;
                for (var i = 0; i <= 5; i++) {
                    if (modules[8, i]) format |= 1 << i;
                }
                if (modules[8, 7]) format |= 1 << 6;
                if (modules[8, 8]) format |= 1 << 7;
                if (modules[7, 8]) format |= 1 << 8;
                for (var i = 9; i < 15; i++) {
                    if (modules[14 - i, 8]) format |= 1 << i;
                }
                return format;
            }

            static int ReadSecondaryFormatInfo(bool[,] modules, int size) {
                var format = 0;
                for (var i = 0; i < 8; i++) {
                    if (modules[size - 1 - i, 8]) format |= 1 << i;
                }
                for (var i = 8; i < 15; i++) {
                    if (modules[8, size - 15 + i]) format |= 1 << i;
                }
                return format;
            }

            static int DecodeFormatMask(int format, out int bestDistance) {
                var bestMask = -1;
                bestDistance = int.MaxValue;
                for (var mask = 0; mask < 8; mask++) {
                    var distance = BitDistance(format, ComputeFormatInfo(mask));
                    if (distance < bestDistance) {
                        bestDistance = distance;
                        bestMask = mask;
                    }
                }
                return bestMask;
            }

            static int BitDistance(int a, int b) {
                var value = a ^ b;
                var count = 0;
                while (value != 0) {
                    count += value & 1;
                    value >>= 1;
                }
                return count;
            }

            static int ComputeFormatInfo(int mask) {
                var data = mask;
                var bch = data << 10;
                const int poly = 0b10100110111;
                for (var i = 14; i >= 10; i--) {
                    if ((bch & (1 << i)) != 0) bch ^= poly << (i - 10);
                }
                return ((data << 10) | (bch & 0x3FF)) ^ 0x5412;
            }

            static bool[,] BuildFunctionPattern(int version, int size) {
                var reserved = new bool[size, size];
                ReserveFinderPatterns(reserved, size);
                ReserveSeparators(reserved, size);
                ReserveTimingPatterns(reserved, size);
                ReserveAlignmentPatterns(reserved, version);
                ReserveFormatArea(reserved, size);
                if (version >= 7) ReserveVersionArea(reserved, size);
                return reserved;
            }

            static void ReserveFinderPatterns(bool[,] reserved, int size) {
                var corners = new[] { (0, 0), (0, size - 7), (size - 7, 0) };
                foreach (var (r0, c0) in corners) {
                    for (var dr = 0; dr < 7; dr++) {
                        for (var dc = 0; dc < 7; dc++) {
                            reserved[r0 + dr, c0 + dc] = true;
                        }
                    }
                }
            }

            static void ReserveSeparators(bool[,] reserved, int size) {
                for (var i = 0; i < 8; i++) {
                    reserved[7, i] = true;
                    reserved[i, 7] = true;
                    reserved[7, size - 1 - i] = true;
                    reserved[i, size - 8] = true;
                    reserved[size - 8, i] = true;
                    reserved[size - 1 - i, 7] = true;
                }
            }

            static void ReserveTimingPatterns(bool[,] reserved, int size) {
                for (var i = 8; i < size - 8; i++) {
                    reserved[6, i] = true;
                    reserved[i, 6] = true;
                }
            }

            static void ReserveAlignmentPatterns(bool[,] reserved, int version) {
                var centers = QrTables.AlignmentCenters[version - 1];
                if (centers.Length == 0) return;

                foreach (var r in centers) {
                    foreach (var c in centers) {
                        if (reserved[r, c]) continue;
                        for (var dr = -2; dr <= 2; dr++) {
                            for (var dc = -2; dc <= 2; dc++) {
                                reserved[r + dr, c + dc] = true;
                            }
                        }
                    }
                }
            }

            static void ReserveFormatArea(bool[,] reserved, int size) {
                for (var i = 0; i <= 8; i++) {
                    reserved[8, i] = true;
                    reserved[i, 8] = true;
                }
                for (var i = 0; i < 7; i++) reserved[size - 1 - i, 8] = true;
                for (var i = 0; i < 8; i++) reserved[8, size - 1 - i] = true;
                reserved[size - 8, 8] = true;
            }

            static void ReserveVersionArea(bool[,] reserved, int size) {
                for (var r = 0; r < 6; r++) {
                    for (var c = 0; c < 3; c++) {
                        reserved[r, size - 11 + c] = true;
                        reserved[size - 11 + c, r] = true;
                    }
                }
            }

            static byte[] ReadCodewords(bool[,] modules, bool[,] reserved, int version, int mask) {
                var totalCodewords = QrTables.DataCodewords(version)
                                     + QrTables.EcCodewordsPerBlock(version) * QrTables.NumBlocks(version);
                var result = new byte[totalCodewords];
                var bit = 0;
                var totalBits = totalCodewords * 8;
                var size = modules.GetLength(0);
                var upward = true;

                for (var col = size - 1; col > 0 && bit < totalBits; col -= 2) {
                    if (col == 6) col--;
                    for (var step = 0; step < size && bit < totalBits; step++) {
                        var row = upward ? size - 1 - step : step;
                        for (var c = 0; c < 2 && bit < totalBits; c++) {
                            var cc = col - c;
                            if (reserved[row, cc]) continue;

                            var dark = modules[row, cc];
                            if (MaskBit(mask, row, cc)) dark = !dark;
                            if (dark) result[bit / 8] |= (byte)(1 << (7 - bit % 8));
                            bit++;
                        }
                    }
                    upward = !upward;
                }

                Assert.AreEqual(totalBits, bit, "QR data area did not contain the expected number of codeword bits.");
                return result;
            }

            static bool MaskBit(int mask, int r, int c) {
                return mask switch {
                    0 => (r + c) % 2 == 0,
                    1 => r % 2 == 0,
                    2 => c % 3 == 0,
                    3 => (r + c) % 3 == 0,
                    4 => (r / 2 + c / 3) % 2 == 0,
                    5 => (r * c) % 2 + (r * c) % 3 == 0,
                    6 => ((r * c) % 2 + (r * c) % 3) % 2 == 0,
                    7 => ((r + c) % 2 + (r * c) % 3) % 2 == 0,
                    _ => false,
                };
            }

            static byte[] DeinterleaveAndVerify(byte[] codewords, int version) {
                var (blocksG1, codewordsG1, blocksG2, codewordsG2) = QrTables.BlockStructure(version);
                var ecPerBlock = QrTables.EcCodewordsPerBlock(version);
                var numBlocks = blocksG1 + blocksG2;
                var dataBlocks = new byte[numBlocks][];
                var ecBlocks = new byte[numBlocks][];

                for (var b = 0; b < numBlocks; b++) {
                    var dataLen = b < blocksG1 ? codewordsG1 : codewordsG2;
                    dataBlocks[b] = new byte[dataLen];
                    ecBlocks[b] = new byte[ecPerBlock];
                }

                var offset = 0;
                var maxDataLen = Math.Max(codewordsG1, codewordsG2);
                for (var i = 0; i < maxDataLen; i++) {
                    for (var b = 0; b < numBlocks; b++) {
                        if (i < dataBlocks[b].Length) dataBlocks[b][i] = codewords[offset++];
                    }
                }
                for (var i = 0; i < ecPerBlock; i++) {
                    for (var b = 0; b < numBlocks; b++) {
                        ecBlocks[b][i] = codewords[offset++];
                    }
                }

                for (var b = 0; b < numBlocks; b++) {
                    CollectionAssert.AreEqual(QrReedSolomon.Encode(dataBlocks[b], ecPerBlock), ecBlocks[b],
                        $"Block {b} Reed-Solomon codewords should match the decoded data.");
                }

                var data = new List<byte>(QrTables.DataCodewords(version));
                for (var b = 0; b < numBlocks; b++) data.AddRange(dataBlocks[b]);
                return data.ToArray();
            }

            sealed class BitReader {
                readonly byte[] _data;
                int _offset;

                public BitReader(byte[] data) {
                    _data = data;
                }

                public int ReadBits(int count) {
                    var value = 0;
                    for (var i = 0; i < count; i++) {
                        value <<= 1;
                        value |= (_data[_offset / 8] >> (7 - _offset % 8)) & 1;
                        _offset++;
                    }
                    return value;
                }
            }
        }
    }
}
