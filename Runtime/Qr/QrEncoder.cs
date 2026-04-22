using System;
using System.Collections.Generic;
using System.Text;

namespace TestingFloor.Internal {
    /// Pure-C# QR Code encoder. Byte mode, ECC level M, versions 1..10.
    ///
    /// Implements ISO/IEC 18004:2015 end-to-end:
    ///   - Data encoding (mode indicator, char count, bit stream, terminator, padding)
    ///   - Reed-Solomon ECC over GF(256)
    ///   - Block interleaving per version
    ///   - Matrix construction (finder + timing + alignment + data zigzag)
    ///   - Mask scoring (all 8 masks, lowest penalty wins)
    ///   - Format + version info with BCH correction
    internal static class QrEncoder {
        const byte ByteMode = 0b0100;
        const byte EcLevelM = 0b00;

        public static QrMatrix Encode(string text) {
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            return Encode(bytes);
        }

        public static QrMatrix Encode(byte[] data) {
            var version = QrTables.PickSmallestVersionFor(data.Length);
            if (version < 0) {
                throw new ArgumentException(
                    $"QR payload of {data.Length} bytes exceeds max capacity ({QrTables.BytePayloadCapacity(QrTables.MaxVersion)} bytes at V{QrTables.MaxVersion}, ECC-M).");
            }

            var codewords = BuildDataCodewords(data, version);
            var interleaved = InterleaveBlocks(codewords, version);
            var modules = BuildModuleMatrix(interleaved, version);
            return new QrMatrix(modules, version);
        }

        // ---- Step 1: Data codewords ----

        static byte[] BuildDataCodewords(byte[] data, int version) {
            var totalData = QrTables.DataCodewords(version);
            var bits = new BitStream(totalData * 8);

            bits.Append(ByteMode, 4);
            bits.Append((uint)data.Length, version < 10 ? 8 : 16);
            for (var i = 0; i < data.Length; i++) bits.Append(data[i], 8);

            var remainingBits = totalData * 8 - bits.Length;
            bits.Append(0, Math.Min(4, remainingBits));

            while (bits.Length % 8 != 0) bits.Append(0, 1);

            var pad = (byte)0xEC;
            while (bits.Length < totalData * 8) {
                bits.Append(pad, 8);
                pad = (byte)(pad == 0xEC ? 0x11 : 0xEC);
            }

            return bits.ToBytes();
        }

        // ---- Step 2: Reed-Solomon per block + interleave ----

        static byte[] InterleaveBlocks(byte[] data, int version) {
            var (blocksG1, codewordsG1, blocksG2, codewordsG2) = QrTables.BlockStructure(version);
            var ecPerBlock = QrTables.EcCodewordsPerBlock(version);
            var numBlocks = blocksG1 + blocksG2;

            var dataBlocks = new byte[numBlocks][];
            var ecBlocks = new byte[numBlocks][];

            var offset = 0;
            for (var i = 0; i < blocksG1; i++) {
                dataBlocks[i] = new byte[codewordsG1];
                Array.Copy(data, offset, dataBlocks[i], 0, codewordsG1);
                offset += codewordsG1;
                ecBlocks[i] = QrReedSolomon.Encode(dataBlocks[i], ecPerBlock);
            }
            for (var i = 0; i < blocksG2; i++) {
                var idx = blocksG1 + i;
                dataBlocks[idx] = new byte[codewordsG2];
                Array.Copy(data, offset, dataBlocks[idx], 0, codewordsG2);
                offset += codewordsG2;
                ecBlocks[idx] = QrReedSolomon.Encode(dataBlocks[idx], ecPerBlock);
            }

            var maxDataLen = Math.Max(codewordsG1, codewordsG2);
            var result = new List<byte>(data.Length + numBlocks * ecPerBlock);

            for (var i = 0; i < maxDataLen; i++) {
                for (var b = 0; b < numBlocks; b++) {
                    if (i < dataBlocks[b].Length) result.Add(dataBlocks[b][i]);
                }
            }
            for (var i = 0; i < ecPerBlock; i++) {
                for (var b = 0; b < numBlocks; b++) result.Add(ecBlocks[b][i]);
            }

            return result.ToArray();
        }

        // ---- Step 3: Build the matrix ----

        static bool[,] BuildModuleMatrix(byte[] codewords, int version) {
            var size = QrTables.SideLength(version);
            var modules = new bool[size, size];
            var reserved = new bool[size, size];

            PlaceFinderPatterns(modules, reserved, size);
            PlaceSeparators(reserved, size);
            PlaceTimingPatterns(modules, reserved, size);
            PlaceAlignmentPatterns(modules, reserved, version, size);
            ReserveFormatArea(reserved, size);
            if (version >= 7) ReserveVersionArea(reserved, size);

            PlaceData(modules, reserved, codewords, size);

            var bestMask = SelectMask(modules, reserved, size);
            ApplyMask(modules, reserved, bestMask, size);

            PlaceFormatInfo(modules, bestMask, size);
            if (version >= 7) PlaceVersionInfo(modules, version, size);

            // Dark module: (4V+9, 8). Always true, placed after format info so it overrides
            // whatever format-info bit happened to land there (per ISO/IEC 18004).
            modules[size - 8, 8] = true;

            return modules;
        }

        static void PlaceFinderPatterns(bool[,] modules, bool[,] reserved, int size) {
            var corners = new[] { (0, 0), (0, size - 7), (size - 7, 0) };
            foreach (var (r0, c0) in corners) {
                for (var dr = 0; dr < 7; dr++) {
                    for (var dc = 0; dc < 7; dc++) {
                        var dark = dr == 0 || dr == 6 || dc == 0 || dc == 6
                                   || (dr >= 2 && dr <= 4 && dc >= 2 && dc <= 4);
                        modules[r0 + dr, c0 + dc] = dark;
                        reserved[r0 + dr, c0 + dc] = true;
                    }
                }
            }
        }

        static void PlaceSeparators(bool[,] reserved, int size) {
            for (var i = 0; i < 8; i++) {
                reserved[7, i] = true;
                reserved[i, 7] = true;
                reserved[7, size - 1 - i] = true;
                reserved[i, size - 8] = true;
                reserved[size - 8, i] = true;
                reserved[size - 1 - i, 7] = true;
            }
        }

        static void PlaceTimingPatterns(bool[,] modules, bool[,] reserved, int size) {
            for (var i = 8; i < size - 8; i++) {
                var dark = (i % 2) == 0;
                modules[6, i] = dark;
                modules[i, 6] = dark;
                reserved[6, i] = true;
                reserved[i, 6] = true;
            }
        }

        static void PlaceAlignmentPatterns(bool[,] modules, bool[,] reserved, int version, int size) {
            var centers = QrTables.AlignmentCenters[version - 1];
            if (centers.Length == 0) return;
            foreach (var r in centers) {
                foreach (var c in centers) {
                    if (reserved[r, c]) continue;
                    for (var dr = -2; dr <= 2; dr++) {
                        for (var dc = -2; dc <= 2; dc++) {
                            var edge = Math.Abs(dr) == 2 || Math.Abs(dc) == 2;
                            var center = dr == 0 && dc == 0;
                            modules[r + dr, c + dc] = edge || center;
                            reserved[r + dr, c + dc] = true;
                        }
                    }
                }
            }
        }

        static void ReserveFormatArea(bool[,] reserved, int size) {
            // Around top-left finder: row 8 cols 0..8 and col 8 rows 0..8 (col 6 / row 6 are timing but
            // keep them reserved in the format band; timing already reserved via placement).
            for (var i = 0; i <= 8; i++) {
                reserved[8, i] = true;
                reserved[i, 8] = true;
            }
            // Around bottom-left finder: col 8 rows size-7..size-1.
            for (var i = 0; i < 7; i++) {
                reserved[size - 1 - i, 8] = true;
            }
            // Around top-right finder: row 8 cols size-8..size-1 (and dark module cell size-8).
            for (var i = 0; i < 8; i++) {
                reserved[8, size - 1 - i] = true;
            }
            // Dark module position.
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

        static void PlaceData(bool[,] modules, bool[,] reserved, byte[] codewords, int size) {
            var bit = 0;
            var totalBits = codewords.Length * 8;
            var upward = true;

            for (var col = size - 1; col > 0; col -= 2) {
                if (col == 6) col--; // skip vertical timing column
                for (var step = 0; step < size; step++) {
                    var row = upward ? size - 1 - step : step;
                    for (var c = 0; c < 2; c++) {
                        var cc = col - c;
                        if (reserved[row, cc]) continue;
                        var dark = false;
                        if (bit < totalBits) {
                            var b = codewords[bit / 8];
                            dark = ((b >> (7 - (bit % 8))) & 1) == 1;
                            bit++;
                        }
                        modules[row, cc] = dark;
                    }
                }
                upward = !upward;
            }
        }

        // ---- Masks ----

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

        static void ApplyMask(bool[,] modules, bool[,] reserved, int mask, int size) {
            for (var r = 0; r < size; r++) {
                for (var c = 0; c < size; c++) {
                    if (reserved[r, c]) continue;
                    if (MaskBit(mask, r, c)) modules[r, c] = !modules[r, c];
                }
            }
        }

        static int SelectMask(bool[,] modules, bool[,] reserved, int size) {
            var bestMask = 0;
            var bestPenalty = int.MaxValue;
            for (var m = 0; m < 8; m++) {
                ApplyMask(modules, reserved, m, size);
                PlaceFormatInfo(modules, m, size);
                modules[size - 8, 8] = true; // dark module affects penalty scoring
                var penalty = ComputePenalty(modules, size);
                ClearReservedFormatCells(modules, size);
                ApplyMask(modules, reserved, m, size); // XOR is self-inverse for non-reserved cells
                if (penalty < bestPenalty) {
                    bestPenalty = penalty;
                    bestMask = m;
                }
            }
            return bestMask;
        }

        static void ClearReservedFormatCells(bool[,] modules, int size) {
            for (var i = 0; i <= 8; i++) {
                modules[8, i] = false;
                modules[i, 8] = false;
            }
            for (var i = 0; i < 7; i++) modules[size - 1 - i, 8] = false;
            for (var i = 0; i < 8; i++) modules[8, size - 1 - i] = false;
            modules[size - 8, 8] = false;
        }

        static int ComputePenalty(bool[,] m, int size) {
            var p = 0;

            for (var r = 0; r < size; r++) p += RunPenalty(m, size, r, rowScan: true);
            for (var c = 0; c < size; c++) p += RunPenalty(m, size, c, rowScan: false);

            for (var r = 0; r < size - 1; r++) {
                for (var c = 0; c < size - 1; c++) {
                    if (m[r, c] == m[r, c + 1] && m[r, c] == m[r + 1, c] && m[r, c] == m[r + 1, c + 1]) {
                        p += 3;
                    }
                }
            }

            var pattern1 = new[] { true, false, true, true, true, false, true, false, false, false, false };
            var pattern2 = new[] { false, false, false, false, true, false, true, true, true, false, true };
            for (var r = 0; r < size; r++) {
                for (var c = 0; c <= size - 11; c++) {
                    if (MatchesRow(m, r, c, pattern1) || MatchesRow(m, r, c, pattern2)) p += 40;
                }
            }
            for (var c = 0; c < size; c++) {
                for (var r = 0; r <= size - 11; r++) {
                    if (MatchesCol(m, r, c, pattern1) || MatchesCol(m, r, c, pattern2)) p += 40;
                }
            }

            var dark = 0;
            for (var r = 0; r < size; r++) {
                for (var c = 0; c < size; c++) {
                    if (m[r, c]) dark++;
                }
            }
            var total = size * size;
            var percent = dark * 100 / total;
            p += Math.Abs(percent - 50) / 5 * 10;

            return p;
        }

        static int RunPenalty(bool[,] m, int size, int fixedIdx, bool rowScan) {
            var p = 0;
            var runLen = 1;
            var prev = rowScan ? m[fixedIdx, 0] : m[0, fixedIdx];
            for (var i = 1; i < size; i++) {
                var cur = rowScan ? m[fixedIdx, i] : m[i, fixedIdx];
                if (cur == prev) {
                    runLen++;
                }
                else {
                    if (runLen >= 5) p += 3 + (runLen - 5);
                    runLen = 1;
                    prev = cur;
                }
            }
            if (runLen >= 5) p += 3 + (runLen - 5);
            return p;
        }

        static bool MatchesRow(bool[,] m, int r, int c, bool[] pattern) {
            for (var i = 0; i < pattern.Length; i++) {
                if (m[r, c + i] != pattern[i]) return false;
            }
            return true;
        }

        static bool MatchesCol(bool[,] m, int r, int c, bool[] pattern) {
            for (var i = 0; i < pattern.Length; i++) {
                if (m[r + i, c] != pattern[i]) return false;
            }
            return true;
        }

        // ---- Format + version info ----

        static void PlaceFormatInfo(bool[,] modules, int mask, int size) {
            var info = ComputeFormatInfo(mask);

            // First copy around the top-left finder.
            for (var i = 0; i <= 5; i++) modules[8, i] = Bit(info, i);
            modules[8, 7] = Bit(info, 6);
            modules[8, 8] = Bit(info, 7);
            modules[7, 8] = Bit(info, 8);
            for (var i = 9; i < 15; i++) modules[14 - i, 8] = Bit(info, i);

            // Second copy: col 8 rows size-1..size-8 for bits 0..7, row 8 cols size-7..size-1 for bits 8..14.
            for (var i = 0; i < 8; i++) modules[size - 1 - i, 8] = Bit(info, i);
            for (var i = 8; i < 15; i++) modules[8, size - 15 + i] = Bit(info, i);
        }

        static int ComputeFormatInfo(int mask) {
            var data = (EcLevelM << 3) | mask; // 5 bits: ecc[2] | mask[3]
            var bch = data << 10;
            const int poly = 0b10100110111; // 0x537 — BCH(15,5) generator
            for (var i = 14; i >= 10; i--) {
                if ((bch & (1 << i)) != 0) bch ^= poly << (i - 10);
            }
            var info = ((data << 10) | (bch & 0x3FF)) ^ 0x5412;
            return info & 0x7FFF;
        }

        static void PlaceVersionInfo(bool[,] modules, int version, int size) {
            var info = ComputeVersionInfo(version);
            for (var i = 0; i < 18; i++) {
                var bit = Bit(info, i);
                var a = size - 11 + i % 3;
                var b = i / 3;
                modules[a, b] = bit;
                modules[b, a] = bit;
            }
        }

        static int ComputeVersionInfo(int version) {
            var bch = version << 12;
            const int poly = 0b1111100100101; // 0x1F25 — BCH(18,6) generator
            for (var i = 17; i >= 12; i--) {
                if ((bch & (1 << i)) != 0) bch ^= poly << (i - 12);
            }
            return (version << 12) | (bch & 0xFFF);
        }

        static bool Bit(int value, int i) => ((value >> i) & 1) == 1;

        // ---- Bit stream helper ----

        sealed class BitStream {
            readonly List<byte> _bytes;
            int _bitLength;

            public BitStream(int capacityBits) {
                _bytes = new List<byte>((capacityBits + 7) / 8);
                _bitLength = 0;
            }

            public int Length => _bitLength;

            public void Append(uint value, int bitCount) {
                for (var i = bitCount - 1; i >= 0; i--) {
                    var bit = (value >> i) & 1u;
                    if (_bitLength % 8 == 0) _bytes.Add(0);
                    if (bit == 1) {
                        _bytes[_bitLength / 8] |= (byte)(1 << (7 - (_bitLength % 8)));
                    }
                    _bitLength++;
                }
            }

            public byte[] ToBytes() => _bytes.ToArray();
        }
    }
}
