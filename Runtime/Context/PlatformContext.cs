using UnityEngine;

namespace TestingFloor {
    public readonly struct PlatformContext {
        public readonly string Os;
        public readonly string Locale;
        public readonly string AppVersion;
        public readonly string UnityVersion;
        public readonly int ScreenWidth;
        public readonly int ScreenHeight;
        public readonly string DeviceModel;
        public readonly string SdkVersion;

        PlatformContext(
            string os,
            string locale,
            string appVersion,
            string unityVersion,
            int screenWidth,
            int screenHeight,
            string deviceModel,
            string sdkVersion) {
            Os = os;
            Locale = locale;
            AppVersion = appVersion;
            UnityVersion = unityVersion;
            ScreenWidth = screenWidth;
            ScreenHeight = screenHeight;
            DeviceModel = deviceModel;
            SdkVersion = sdkVersion;
        }

        public static PlatformContext Capture() {
            return new PlatformContext(
                os: Application.platform.ToString(),
                locale: Application.systemLanguage.ToString(),
                appVersion: Application.version,
                unityVersion: Application.unityVersion,
                screenWidth: Screen.width,
                screenHeight: Screen.height,
                deviceModel: SystemInfo.deviceModel,
                sdkVersion: TestingFloorVersion.Value
            );
        }
    }

    internal static class TestingFloorVersion {
        public const string Value = "0.1.0";
    }
}
