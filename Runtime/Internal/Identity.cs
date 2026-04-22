using System;
using System.IO;
using UnityEngine;

namespace TestingFloor.Internal {
    internal static class Identity {
        const string DeviceIdKey = "TestingFloor.DeviceId";
        const string ProfileIdFileName = "profile_id";

        static string _deviceId;
        static string _profileId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            _deviceId = null;
            _profileId = null;
        }

        public static string DeviceId {
            get {
                if (!string.IsNullOrWhiteSpace(_deviceId)) return _deviceId;
                _deviceId = GetOrCreateDeviceId();
                return _deviceId;
            }
        }

        public static string ProfileId {
            get {
                if (!string.IsNullOrWhiteSpace(_profileId)) return _profileId;
                _profileId = GetOrCreateProfileId();
                return _profileId;
            }
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
