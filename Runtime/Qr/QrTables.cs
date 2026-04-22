namespace TestingFloor.Internal {
    /// Version capacity + block structure for ECC level M, byte mode, versions 1..10.
    /// Rows: { dataCodewords, ecCodewordsPerBlock, blocksG1, codewordsG1, blocksG2, codewordsG2 }
    /// Derived from ISO/IEC 18004 Table 9 / Table 13.
    internal static class QrTables {
        public const int MinVersion = 1;
        public const int MaxVersion = 10;

        public static readonly int[,] EccM = {
            /* V1  */ { 16,  10, 1, 16, 0, 0  },
            /* V2  */ { 28,  16, 1, 28, 0, 0  },
            /* V3  */ { 44,  26, 1, 44, 0, 0  },
            /* V4  */ { 64,  18, 2, 32, 0, 0  },
            /* V5  */ { 86,  24, 2, 43, 0, 0  },
            /* V6  */ { 108, 16, 4, 27, 0, 0  },
            /* V7  */ { 124, 18, 4, 31, 0, 0  },
            /* V8  */ { 154, 22, 2, 38, 2, 39 },
            /* V9  */ { 182, 22, 3, 36, 2, 37 },
            /* V10 */ { 216, 26, 4, 43, 1, 44 },
        };

        /// Alignment pattern center coordinates. V1 has none; V2..V6 have two; V7..V10 have three.
        public static readonly int[][] AlignmentCenters = {
            /* V1  */ new int[] { },
            /* V2  */ new[] { 6, 18 },
            /* V3  */ new[] { 6, 22 },
            /* V4  */ new[] { 6, 26 },
            /* V5  */ new[] { 6, 30 },
            /* V6  */ new[] { 6, 34 },
            /* V7  */ new[] { 6, 22, 38 },
            /* V8  */ new[] { 6, 24, 42 },
            /* V9  */ new[] { 6, 26, 46 },
            /* V10 */ new[] { 6, 28, 50 },
        };

        public static int SideLength(int version) => 4 * version + 17;

        public static int DataCodewords(int version) => EccM[version - 1, 0];

        public static int EcCodewordsPerBlock(int version) => EccM[version - 1, 1];

        public static int NumBlocks(int version) => EccM[version - 1, 2] + EccM[version - 1, 4];

        public static (int blocksG1, int codewordsG1, int blocksG2, int codewordsG2) BlockStructure(int version) {
            return (EccM[version - 1, 2], EccM[version - 1, 3], EccM[version - 1, 4], EccM[version - 1, 5]);
        }

        /// Byte-mode payload capacity. Subtracts 4-bit mode indicator and 8- or 16-bit character count.
        public static int BytePayloadCapacity(int version) {
            var dataBits = DataCodewords(version) * 8;
            var countBits = version < 10 ? 8 : 16;
            return (dataBits - 4 - countBits) / 8;
        }

        public static int PickSmallestVersionFor(int byteCount) {
            for (var v = MinVersion; v <= MaxVersion; v++) {
                if (BytePayloadCapacity(v) >= byteCount) return v;
            }
            return -1;
        }
    }
}
