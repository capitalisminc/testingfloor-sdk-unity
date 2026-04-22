using System;
using System.Collections.Generic;
using Net.Codecrete.QrCodeGenerator;
using ThirdPartyQrCode = Net.Codecrete.QrCodeGenerator.QrCode;

namespace TestingFloor.Internal {
    /// QR Code encoder backed by the vendored Codecrete/Nayuki implementation.
    internal static class QrEncoder {
        const int MaxVersion = 10;

        public static QrMatrix Encode(string text) {
            return EncodeSegments(QrSegment.MakeSegments(text ?? string.Empty));
        }

        public static QrMatrix Encode(byte[] data) {
            var bytes = data ?? Array.Empty<byte>();
            return EncodeSegments(new List<QrSegment> { QrSegment.MakeBytes(bytes) });
        }

        static QrMatrix EncodeSegments(List<QrSegment> segments) {
            try {
                var qr = ThirdPartyQrCode.EncodeSegments(
                    segments,
                    ThirdPartyQrCode.Ecc.Medium,
                    ThirdPartyQrCode.MinVersion,
                    MaxVersion,
                    mask: -1,
                    boostEcl: false);

                return ToMatrix(qr);
            }
            catch (DataTooLongException e) {
                throw new ArgumentException($"QR payload exceeds max capacity for V{MaxVersion}, ECC-M.", e);
            }
        }

        static QrMatrix ToMatrix(ThirdPartyQrCode qr) {
            var modules = new bool[qr.Size, qr.Size];
            for (var row = 0; row < qr.Size; row++) {
                for (var col = 0; col < qr.Size; col++) {
                    modules[row, col] = qr.GetModule(col, row);
                }
            }
            return new QrMatrix(modules, qr.Version);
        }
    }
}
