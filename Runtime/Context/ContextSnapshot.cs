using System;
using System.Collections.Generic;
using TestingFloor.Internal;

namespace TestingFloor {
    public struct ContextSnapshot {
        public PlatformContext Platform;
        internal Dictionary<string, object> Properties;

        internal static ContextSnapshot Create(PlatformContext platform) {
            return new ContextSnapshot {
                Platform = platform,
                Properties = null,
            };
        }

        public void Set(string key, string value) {
            if (string.IsNullOrWhiteSpace(key) || value == null) return;
            Properties ??= TelemetryQueue.RentProperties();
            Properties[key] = value;
        }

        public void Set(string key, long value) {
            if (string.IsNullOrWhiteSpace(key)) return;
            Properties ??= TelemetryQueue.RentProperties();
            Properties[key] = value;
        }

        public void Set(string key, double value) {
            if (string.IsNullOrWhiteSpace(key)) return;
            Properties ??= TelemetryQueue.RentProperties();
            Properties[key] = value;
        }

        public void Set(string key, bool value) {
            if (string.IsNullOrWhiteSpace(key)) return;
            Properties ??= TelemetryQueue.RentProperties();
            Properties[key] = value;
        }

        internal static void WriteObjectValue(TelemetryJsonWriter writer, string key, object value) {
            switch (value) {
                case null:
                    return;
                case string s:
                    writer.WriteString(key, s);
                    break;
                case int i:
                    writer.WriteNumber(key, i);
                    break;
                case long l:
                    writer.WriteNumber(key, l);
                    break;
                case float f:
                    writer.WriteNumber(key, f);
                    break;
                case double d:
                    writer.WriteNumber(key, d);
                    break;
                case bool b:
                    writer.WriteBoolean(key, b);
                    break;
                case string[] arr:
                    writer.WritePropertyName(key);
                    writer.WriteStartArray();
                    for (var i2 = 0; i2 < arr.Length; i2++) writer.WriteStringValue(arr[i2]);
                    writer.WriteEndArray();
                    break;
                case int[] intArr:
                    writer.WritePropertyName(key);
                    writer.WriteStartArray();
                    for (var i2 = 0; i2 < intArr.Length; i2++) writer.WriteNumberValue(intArr[i2]);
                    writer.WriteEndArray();
                    break;
                case IReadOnlyList<string> strList:
                    writer.WritePropertyName(key);
                    writer.WriteStartArray();
                    for (var i2 = 0; i2 < strList.Count; i2++) writer.WriteStringValue(strList[i2]);
                    writer.WriteEndArray();
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported TestingFloor property type for '{key}': {value.GetType()}.");
            }
        }
    }
}
