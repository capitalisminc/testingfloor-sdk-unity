using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TestingFloorSdk.Tests.Editor {
    /// <summary>
    /// The vendored copy of the Testing Floor telemetry contract in
    /// <c>Tests/Contract/telemetry_contract.json</c>, found by its asset GUID.
    /// </summary>
    internal static class TelemetryContract {
        const string ContractGuid = "d8c8ba9cd62c4061a628cfb696ad0d4c";
        static Dictionary<string, object> _root;

        internal static Dictionary<string, object> Root => _root ??= Load();

        static Dictionary<string, object> Load() {
            var path = AssetDatabase.GUIDToAssetPath(ContractGuid);
            var asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.IsNotNull(asset, "The telemetry contract copy is missing from Tests/Contract");
            return (Dictionary<string, object>)Json.Parse(asset.text);
        }

        internal static Dictionary<string, object> Section(string name) => (Dictionary<string, object>)Root[name];

        internal static List<Dictionary<string, object>> Rows(string section) => Rows(Root, section);

        internal static List<Dictionary<string, object>> Rows(string section, Func<Dictionary<string, object>, bool> where) =>
            Rows(section).Where(where).ToList();

        internal static List<Dictionary<string, object>> Rows(Dictionary<string, object> parent, string field) =>
            ((List<object>)parent[field]).Cast<Dictionary<string, object>>().ToList();

        internal static string[] Keys(IEnumerable<Dictionary<string, object>> rows, string field = "key") =>
            rows.Select(row => (string)row[field]).ToArray();

        internal static string Text(Dictionary<string, object> row, string field) =>
            row.TryGetValue(field, out var value) ? value as string : null;

        /// <summary>Whether a parsed JSON value has the contract type, such as <c>integer</c> or <c>string[]</c>.</summary>
        internal static bool HasType(string type, object value) {
            switch (type) {
                case "string":
                case "timestamp":
                    return value is string;
                case "integer":
                    return value is long;
                case "number":
                    return value is long || value is double;
                case "boolean":
                    return value is bool;
                case "object":
                    return value is Dictionary<string, object>;
            }
            if (!type.EndsWith("[]", StringComparison.Ordinal) || value is not List<object> items) return false;
            var element = type.Substring(0, type.Length - 2);
            if (element == "scalar") return items.All(item => item is string || item is long || item is double || item is bool);
            return items.All(item => HasType(element, item));
        }

        /// <summary>The contract type name of a parsed JSON value.</summary>
        internal static string TypeOf(object value) {
            switch (value) {
                case string _: return "string";
                case long _: return "integer";
                case double _: return "number";
                case bool _: return "boolean";
                case Dictionary<string, object> _: return "object";
                case List<object> items when items.Count > 0:
                    var types = items.Select(TypeOf).Distinct().ToArray();
                    return types.Length == 1 ? types[0] + "[]" : "mixed[]";
                default: return value == null ? "null" : value.GetType().Name;
            }
        }
    }

    /// <summary>
    /// Reads JSON into dictionaries, lists, strings, longs, doubles, booleans
    /// and nulls. Integral numbers become longs. Duplicate keys are an error.
    /// </summary>
    internal static class Json {
        internal static object Parse(string text) {
            var reader = new Reader(text);
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd) throw reader.Error("Unexpected text after the JSON value");
            return value;
        }

        sealed class Reader {
            readonly string _text;
            int _at;

            internal Reader(string text) { _text = text; }

            internal bool AtEnd => _at >= _text.Length;

            internal FormatException Error(string message) => new($"{message} at offset {_at}.");

            internal void SkipWhitespace() {
                while (!AtEnd && char.IsWhiteSpace(_text[_at])) _at++;
            }

            void Expect(char c) {
                SkipWhitespace();
                if (AtEnd || _text[_at] != c) throw Error($"Expected '{c}'");
                _at++;
            }

            bool Accept(char c) {
                SkipWhitespace();
                if (AtEnd || _text[_at] != c) return false;
                _at++;
                return true;
            }

            void ExpectWord(string word) {
                if (string.CompareOrdinal(_text, _at, word, 0, word.Length) != 0) throw Error($"Expected {word}");
                _at += word.Length;
            }

            internal object ReadValue() {
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of JSON");
                switch (_text[_at]) {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': ExpectWord("true"); return true;
                    case 'f': ExpectWord("false"); return false;
                    case 'n': ExpectWord("null"); return null;
                    default: return ReadNumber();
                }
            }

            Dictionary<string, object> ReadObject() {
                Expect('{');
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                if (Accept('}')) return result;
                do {
                    SkipWhitespace();
                    if (AtEnd || _text[_at] != '"') throw Error("Expected a property name");
                    var key = ReadString();
                    Expect(':');
                    if (result.ContainsKey(key)) throw Error($"Duplicate key '{key}'");
                    result[key] = ReadValue();
                } while (Accept(','));
                Expect('}');
                return result;
            }

            List<object> ReadArray() {
                Expect('[');
                var result = new List<object>();
                if (Accept(']')) return result;
                do result.Add(ReadValue());
                while (Accept(','));
                Expect(']');
                return result;
            }

            string ReadString() {
                Expect('"');
                var result = new StringBuilder();
                while (true) {
                    if (AtEnd) throw Error("Unterminated string");
                    var c = _text[_at++];
                    if (c == '"') return result.ToString();
                    if (c != '\\') {
                        result.Append(c);
                        continue;
                    }
                    if (AtEnd) throw Error("Unterminated escape");
                    var escape = _text[_at++];
                    switch (escape) {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            if (_at + 4 > _text.Length) throw Error("Short unicode escape");
                            result.Append((char)int.Parse(_text.Substring(_at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _at += 4;
                            break;
                        default: throw Error($"Unknown escape '\\{escape}'");
                    }
                }
            }

            object ReadNumber() {
                var start = _at;
                while (!AtEnd && "+-0123456789.eE".IndexOf(_text[_at]) >= 0) _at++;
                var token = _text.Substring(start, _at - start);
                if (token.Length == 0) throw Error("Expected a JSON value");
                if (token.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 &&
                    long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)) {
                    return integer;
                }
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
                throw Error($"Invalid number '{token}'");
            }
        }
    }
}
