using System;
using System.Buffers;
using System.Buffers.Text;
using System.Text;

namespace TestingFloor.Internal {
    internal sealed class TelemetryJsonWriter {
        readonly ArrayBufferWriter<byte> _buffer = new(4096);
        Frame[] _frames = new Frame[8];
        int _depth;

        public void Reset() {
            _buffer.Clear();
            _depth = 0;
        }

        public ReadOnlySpan<byte> WrittenSpan => _buffer.WrittenSpan;

        public void WriteStartObject() {
            BeforeValue();
            WriteByte((byte)'{');
            Push(isArray: false);
        }

        public void WriteEndObject() {
            Pop(expectedArray: false);
            WriteByte((byte)'}');
        }

        public void WriteStartArray() {
            BeforeValue();
            WriteByte((byte)'[');
            Push(isArray: true);
        }

        public void WriteEndArray() {
            Pop(expectedArray: true);
            WriteByte((byte)']');
        }

        public void WritePropertyName(string key) {
            if (string.IsNullOrWhiteSpace(key)) {
                throw new ArgumentException("JSON property name is required.", nameof(key));
            }

            BeforeProperty();
            WriteEscapedString(key);
            WriteByte((byte)':');
            ref var frame = ref _frames[_depth - 1];
            frame.ExpectingValue = true;
        }

        public void WriteString(string key, string value) {
            if (value == null) return;
            WritePropertyName(key);
            WriteStringValue(value);
        }

        public void WriteString(string key, Guid value) {
            WritePropertyName(key);
            BeforeValue();
            WriteByte((byte)'"');
            Span<byte> bytes = stackalloc byte[36];
            if (!Utf8Formatter.TryFormat(value, bytes, out var written, new StandardFormat('D'))) {
                throw new InvalidOperationException("Failed to format GUID.");
            }
            WriteBytes(bytes.Slice(0, written));
            WriteByte((byte)'"');
        }

        public void WriteString(string key, DateTimeOffset value) {
            WritePropertyName(key);
            BeforeValue();
            WriteByte((byte)'"');
            Span<char> chars = stackalloc char[40];
            if (!value.TryFormat(chars, out var charsWritten, "O")) {
                throw new InvalidOperationException("Failed to format DateTimeOffset.");
            }
            WriteUtf8(chars.Slice(0, charsWritten));
            WriteByte((byte)'"');
        }

        public void WriteNumber(string key, int value) {
            WritePropertyName(key);
            WriteInt64(value);
        }

        public void WriteNumber(string key, long value) {
            WritePropertyName(key);
            WriteInt64(value);
        }

        public void WriteNumber(string key, float value) {
            WritePropertyName(key);
            WriteDouble(value);
        }

        public void WriteNumber(string key, double value) {
            WritePropertyName(key);
            WriteDouble(value);
        }

        public void WriteBoolean(string key, bool value) {
            WritePropertyName(key);
            WriteRawValue(value ? "true" : "false");
        }

        public void WriteStringValue(string value) {
            BeforeValue();
            WriteEscapedString(value ?? string.Empty);
        }

        public void WriteNumberValue(int value) {
            WriteInt64(value);
        }

        void WriteInt64(long value) {
            BeforeValue();
            Span<byte> bytes = stackalloc byte[32];
            if (!Utf8Formatter.TryFormat(value, bytes, out var written)) {
                throw new InvalidOperationException("Failed to format integer.");
            }
            WriteBytes(bytes.Slice(0, written));
        }

        void WriteDouble(double value) {
            BeforeValue();
            Span<byte> bytes = stackalloc byte[32];
            if (!Utf8Formatter.TryFormat(value, bytes, out var written)) {
                throw new InvalidOperationException("Failed to format floating-point value.");
            }
            WriteBytes(bytes.Slice(0, written));
        }

        void WriteRawValue(string value) {
            BeforeValue();
            WriteAscii(value);
        }

        void BeforeProperty() {
            if (_depth == 0 || _frames[_depth - 1].IsArray) {
                throw new InvalidOperationException("JSON property names can only be written inside objects.");
            }

            ref var frame = ref _frames[_depth - 1];
            if (frame.ExpectingValue) {
                throw new InvalidOperationException("Cannot write a JSON property before writing the previous value.");
            }
            if (frame.HasValue) WriteByte((byte)',');
            frame.HasValue = true;
        }

        void BeforeValue() {
            if (_depth == 0) return;

            ref var frame = ref _frames[_depth - 1];
            if (frame.IsArray) {
                if (frame.HasValue) WriteByte((byte)',');
                frame.HasValue = true;
                return;
            }

            if (!frame.ExpectingValue) {
                throw new InvalidOperationException("JSON object values must follow a property name.");
            }
            frame.ExpectingValue = false;
        }

        void Push(bool isArray) {
            if (_depth == _frames.Length) Array.Resize(ref _frames, _frames.Length * 2);
            _frames[_depth++] = new Frame { IsArray = isArray };
        }

        void Pop(bool expectedArray) {
            if (_depth == 0) throw new InvalidOperationException("JSON container stack is empty.");
            var frame = _frames[--_depth];
            if (frame.IsArray != expectedArray) {
                throw new InvalidOperationException("JSON container close type does not match open type.");
            }
            if (frame.ExpectingValue) {
                throw new InvalidOperationException("JSON object property is missing a value.");
            }
        }

        void WriteEscapedString(string value) {
            WriteByte((byte)'"');
            var runStart = 0;
            for (var i = 0; i < value.Length; i++) {
                var ch = value[i];
                if (!NeedsEscape(ch)) continue;

                if (i > runStart) WriteUtf8(value.AsSpan(runStart, i - runStart));
                WriteEscape(ch);
                runStart = i + 1;
            }

            if (runStart < value.Length) WriteUtf8(value.AsSpan(runStart));
            WriteByte((byte)'"');
        }

        static bool NeedsEscape(char ch) {
            return ch == '"' || ch == '\\' || ch < ' ';
        }

        void WriteEscape(char ch) {
            switch (ch) {
                case '"':
                    WriteAscii("\\\"");
                    break;
                case '\\':
                    WriteAscii("\\\\");
                    break;
                case '\b':
                    WriteAscii("\\b");
                    break;
                case '\f':
                    WriteAscii("\\f");
                    break;
                case '\n':
                    WriteAscii("\\n");
                    break;
                case '\r':
                    WriteAscii("\\r");
                    break;
                case '\t':
                    WriteAscii("\\t");
                    break;
                default:
                    WriteAscii("\\u00");
                    WriteHexNibble((ch >> 4) & 0xF);
                    WriteHexNibble(ch & 0xF);
                    break;
            }
        }

        void WriteHexNibble(int value) {
            WriteByte((byte)(value < 10 ? '0' + value : 'a' + value - 10));
        }

        void WriteAscii(string value) {
            var span = _buffer.GetSpan(value.Length);
            for (var i = 0; i < value.Length; i++) span[i] = (byte)value[i];
            _buffer.Advance(value.Length);
        }

        void WriteUtf8(ReadOnlySpan<char> value) {
            var span = _buffer.GetSpan(Encoding.UTF8.GetMaxByteCount(value.Length));
            var written = Encoding.UTF8.GetBytes(value, span);
            _buffer.Advance(written);
        }

        void WriteBytes(ReadOnlySpan<byte> value) {
            var span = _buffer.GetSpan(value.Length);
            value.CopyTo(span);
            _buffer.Advance(value.Length);
        }

        void WriteByte(byte value) {
            var span = _buffer.GetSpan(1);
            span[0] = value;
            _buffer.Advance(1);
        }

        struct Frame {
            public bool IsArray;
            public bool HasValue;
            public bool ExpectingValue;
        }
    }
}
