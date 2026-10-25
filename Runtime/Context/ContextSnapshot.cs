using System;
using System.Collections.Generic;
using TestingFloorSdk.Internal;

namespace TestingFloorSdk {
    public struct ContextSnapshot {
        internal PlatformContext Platform;
        internal Dictionary<string, object> Properties;

        internal static ContextSnapshot Create(PlatformContext platform) {
            return new ContextSnapshot {
                Platform = platform,
                Properties = null,
            };
        }

        internal static bool IsPropertyKey(string key) => !string.IsNullOrWhiteSpace(key) && !key.StartsWith("$", StringComparison.Ordinal);

        internal static void Put(ref Dictionary<string, object> properties, string key, object value) {
            if (!IsPropertyKey(key) || value == null) return;
            if (value is double d && (double.IsNaN(d) || double.IsInfinity(d))) return;
            if (value is float f && (float.IsNaN(f) || float.IsInfinity(f))) return;
            if (value is Array array) value = array.Clone();
            properties ??= TelemetryQueue.RentProperties();
            properties[key] = value;
        }

        public void Set(string key, string value) => Put(ref Properties, key, value);
        public void Set(string key, long value) => Put(ref Properties, key, value);
        public void Set(string key, double value) => Put(ref Properties, key, value);
        public void Set(string key, bool value) => Put(ref Properties, key, value);
        public void Set(string key, string[] value) => Put(ref Properties, key, value);
        public void Set(string key, int[] value) => Put(ref Properties, key, value);
        public void Set(string key, Guid value) => Put(ref Properties, key, value);
        public void SetVector(string prefix, UnityEngine.Vector3 value) {
            Set(prefix + ".x", value.x); Set(prefix + ".y", value.y); Set(prefix + ".z", value.z);
        }
        public void SetIfPresent(string key, string value) { if (!string.IsNullOrWhiteSpace(value)) Set(key, value); }
        public void SetIfPresent(string key, int? value) { if (value.HasValue) Set(key, value.Value); }
        public void SetIfPresent(string key, long? value) { if (value.HasValue) Set(key, value.Value); }
        public void SetIfPresent(string key, float? value) { if (value.HasValue) Set(key, value.Value); }
        public void SetIfPresent(string key, double? value) { if (value.HasValue) Set(key, value.Value); }
        public void SetIfPresent(string key, bool? value) { if (value.HasValue) Set(key, value.Value); }

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
                case Guid g:
                    writer.WriteString(key, g);
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
                default:
                    throw new InvalidOperationException(
                        $"Unsupported TestingFloor property type for '{key}': {value.GetType()}.");
            }
        }
    }
}
