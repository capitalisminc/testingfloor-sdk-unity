namespace TestingFloor.Internal {
    /// GF(256) with primitive polynomial 0x11D (x^8 + x^4 + x^3 + x^2 + 1), per ISO/IEC 18004.
    internal static class QrReedSolomon {
        static readonly byte[] Exp = new byte[512];
        static readonly byte[] Log = new byte[256];
        static QrReedSolomon() {
            byte x = 1;
            for (var i = 0; i < 255; i++) {
                Exp[i] = x;
                Log[x] = (byte)i;
                var next = x << 1;
                if ((next & 0x100) != 0) next ^= 0x11D;
                x = (byte)next;
            }
            for (var i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
        }

        public static byte Mul(byte a, byte b) {
            if (a == 0 || b == 0) return 0;
            return Exp[Log[a] + Log[b]];
        }

        /// Returns the generator polynomial of degree `degree`, coefficients high-order first.
        /// g(x) = (x - a^0)(x - a^1)...(x - a^(degree-1))
        public static byte[] GeneratorPolynomial(int degree) {
            var result = new byte[] { 1 };
            for (var i = 0; i < degree; i++) {
                result = PolyMul(result, new byte[] { 1, Exp[i] });
            }
            return result;
        }

        static byte[] PolyMul(byte[] p, byte[] q) {
            var r = new byte[p.Length + q.Length - 1];
            for (var i = 0; i < p.Length; i++) {
                if (p[i] == 0) continue;
                for (var j = 0; j < q.Length; j++) {
                    r[i + j] ^= Mul(p[i], q[j]);
                }
            }
            return r;
        }

        /// Returns `ecLen` error-correction codewords for `data`.
        public static byte[] Encode(byte[] data, int ecLen) {
            var gen = GeneratorPolynomial(ecLen);
            var buffer = new byte[data.Length + ecLen];
            System.Array.Copy(data, buffer, data.Length);
            for (var i = 0; i < data.Length; i++) {
                var coeff = buffer[i];
                if (coeff == 0) continue;
                for (var j = 0; j < gen.Length; j++) {
                    buffer[i + j] ^= Mul(gen[j], coeff);
                }
            }
            var ec = new byte[ecLen];
            System.Array.Copy(buffer, data.Length, ec, 0, ecLen);
            return ec;
        }
    }
}
