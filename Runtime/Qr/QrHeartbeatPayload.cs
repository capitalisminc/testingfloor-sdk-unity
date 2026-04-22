using System;

namespace TestingFloor.Internal {
    internal static class QrHeartbeatPayload {
        public const string Prefix = "tfqr://sync/v1";

        public static string Build(string sessionId, long unixMs, long sequence) {
            if (string.IsNullOrWhiteSpace(sessionId)) {
                throw new ArgumentException("QR heartbeat payload requires a session id.", nameof(sessionId));
            }

            var escapedSessionId = Uri.EscapeDataString(sessionId);
            return $"{Prefix}?s={escapedSessionId}&t={unixMs}&q={sequence}";
        }
    }
}
