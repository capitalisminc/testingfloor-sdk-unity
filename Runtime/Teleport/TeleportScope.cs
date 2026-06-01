using System;

namespace TestingFloor {
    public sealed class TeleportScope : IDisposable {
        readonly int _id;
        bool _cancelled;
        bool _disposed;

        internal TeleportScope(int id) {
            _id = id;
        }

        public void Cancel() {
            _cancelled = true;
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            TeleportTracker.End(_id, _cancelled);
        }
    }
}
