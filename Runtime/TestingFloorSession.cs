using System;
using System.IO;
using UnityEngine;

namespace TestingFloor {
    public sealed class TestingFloorSession {
        public string SessionId { get; }
        public long? PlaytestId { get; }

        internal TestingFloorSession(string sessionId, long? playtestId) {
            SessionId = sessionId;
            PlaytestId = playtestId;
        }

        const string CliPrefix = "--testing-floor=";
        const int MaxPayloadAgeHours = 12;

        static bool _resolved;
        static TestingFloorSession _current;
        static string _fallbackSessionId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            _resolved = false;
            _current = null;
            _fallbackSessionId = null;
        }

        public static TestingFloorSession Current {
            get {
                if (_resolved) return _current;
                _resolved = true;
                _current = ResolveFromArgs() ?? ResolveFromSidecar();
                return _current;
            }
        }

        internal static string EffectiveSessionId {
            get {
                var current = Current;
                if (!string.IsNullOrWhiteSpace(current?.SessionId)) return current.SessionId;
                if (!string.IsNullOrWhiteSpace(_fallbackSessionId)) return _fallbackSessionId;
                _fallbackSessionId = Guid.NewGuid().ToString();
                return _fallbackSessionId;
            }
        }

        static TestingFloorSession ResolveFromArgs() {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++) {
                var arg = args[i];
                if (!arg.StartsWith(CliPrefix, StringComparison.Ordinal)) continue;
                var json = arg.Substring(CliPrefix.Length);
                try {
                    var payload = JsonUtility.FromJson<Payload>(json);
                    if (payload == null || string.IsNullOrWhiteSpace(payload.session_id)) return null;
                    return new TestingFloorSession(payload.session_id, PayloadPlaytestId(payload));
                }
                catch (Exception e) {
                    Debug.LogWarning($"[TestingFloor] Failed to parse --testing-floor JSON: {e.Message}");
                    return null;
                }
            }
            return null;
        }

        static TestingFloorSession ResolveFromSidecar() {
            var path = GetSidecarPath();
            if (string.IsNullOrWhiteSpace(path)) return null;

            try {
                if (!File.Exists(path)) return null;

                var json = File.ReadAllText(path);
                var payload = JsonUtility.FromJson<Payload>(json);
                if (payload == null || string.IsNullOrWhiteSpace(payload.session_id)) {
                    Debug.LogWarning("[TestingFloor] Sidecar payload missing session_id; ignoring.");
                    SafeDelete(path);
                    return null;
                }

                if (payload.created_at_unix_ms > 0) {
                    var ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - payload.created_at_unix_ms;
                    if (ageMs > TimeSpan.FromHours(MaxPayloadAgeHours).TotalMilliseconds) {
                        Debug.LogWarning("[TestingFloor] Sidecar payload is stale (>12h); ignoring.");
                        SafeDelete(path);
                        return null;
                    }
                }

                SafeDelete(path);
                return new TestingFloorSession(payload.session_id, PayloadPlaytestId(payload));
            }
            catch (Exception e) {
                Debug.LogWarning($"[TestingFloor] Failed to parse sidecar payload: {e.Message}");
                SafeDelete(path);
                return null;
            }
        }

        static string GetSidecarPath() {
            try {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(projectRoot, "Library", "TestingFloor", "session-payload.json");
            }
            catch {
                return null;
            }
        }

        static void SafeDelete(string path) {
            try {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) {
                Debug.LogWarning($"[TestingFloor] Failed to delete sidecar payload: {e.Message}");
            }
        }

        static long? PayloadPlaytestId(Payload payload) {
            return payload.playtest_id > 0 ? payload.playtest_id : null;
        }

        [Serializable]
        sealed class Payload {
            public string session_id;
            public long playtest_id;
            public long created_at_unix_ms;
            public int schema;
            public string source;
        }
    }
}
