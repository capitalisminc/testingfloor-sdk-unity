using System;
using System.IO;
using UnityEngine;

namespace TestingFloorSdk.Internal {
    internal static class Identity {
        const string DeviceIdKey = "TestingFloor.DeviceId";
        const string ProfileIdFileName = "profile_id";

        static string _deviceId;
        static string _profileId;
        static string _gameDeviceId;
        static string _gameUserId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() {
            _deviceId = null;
            _profileId = null;
            _gameDeviceId = null;
            _gameUserId = null;
        }

        // An id the game sets wins over the generated one; null or blank restores it.
        internal static void SetGameUserId(string userId) => _gameUserId = GameId(userId);
        internal static void SetGameDeviceId(string deviceId) => _gameDeviceId = GameId(deviceId);

        static string GameId(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        public static string DeviceId {
            get {
                if (_gameDeviceId != null) return _gameDeviceId;
                if (!string.IsNullOrWhiteSpace(_deviceId)) return _deviceId;
                _deviceId = GetOrCreateDeviceId();
                return _deviceId;
            }
        }

        public static string ProfileId {
            get {
                if (_gameUserId != null) return _gameUserId;
                if (!string.IsNullOrWhiteSpace(_profileId)) return _profileId;
                _profileId = GetOrCreateProfileId();
                return _profileId;
            }
        }

        public static string PlayerId => ResolvePlayerId(ProfileId, DeviceId);

        // Mirrors how Testing Floor picks the Player ID for an event: the
        // `$user_id` when present, else the `$device_id`.
        internal static string ResolvePlayerId(string profileId, string deviceId) {
            return !string.IsNullOrWhiteSpace(profileId) ? profileId : deviceId;
        }

        static string GetOrCreateDeviceId() {
            var value = PlayerPrefs.GetString(DeviceIdKey, string.Empty);
            if (string.IsNullOrWhiteSpace(value)) {
                value = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceIdKey, value);
                PlayerPrefs.Save();
            }
            return value;
        }

        static string GetOrCreateProfileId() {
            var path = GetProfileIdPath();
            try {
                if (File.Exists(path)) {
                    var existing = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(existing)) return existing;
                }
                var value = Guid.NewGuid().ToString("N");
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(path, value);
                return value;
            }
            catch (Exception ex) {
                Debug.LogWarning($"[TestingFloor] Failed to read/write profile ID at {path}: {ex.Message}");
                return null;
            }
        }

        static string GetProfileIdPath() {
            var basePath = Application.persistentDataPath;
            return Path.Combine(basePath, "synced", ProfileIdFileName);
        }
    }
}
