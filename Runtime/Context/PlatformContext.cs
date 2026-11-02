using System;
using System.Globalization;
using UnityEngine;

namespace TestingFloorSdk {
    internal readonly struct PlatformContext {
        public readonly string Os;
        public readonly string Locale;
        public readonly string AppVersion;
        public readonly string UnityVersion;
        public readonly int ScreenWidth;
        public readonly int ScreenHeight;
        public readonly string DeviceModel;
        public readonly string SdkVersion;
        public readonly bool Editor;

        internal PlatformContext(
            string os,
            string locale,
            string appVersion,
            string unityVersion,
            int screenWidth,
            int screenHeight,
            string deviceModel,
            string sdkVersion,
            bool editor = false) {
            Os = os;
            Locale = locale;
            AppVersion = appVersion;
            UnityVersion = unityVersion;
            ScreenWidth = screenWidth;
            ScreenHeight = screenHeight;
            DeviceModel = deviceModel;
            SdkVersion = sdkVersion;
            Editor = editor;
        }

        public PlatformContext WithScreenSize(int width, int height) => new PlatformContext(
            Os, Locale, AppVersion, UnityVersion, width, height, DeviceModel, SdkVersion, Editor);

        public static PlatformContext Capture() {
            return new PlatformContext(
                os: PlatformNames.OsFamily(Application.platform.ToString()),
                locale: PlatformNames.Locale(Application.systemLanguage, CultureInfo.CurrentUICulture),
                appVersion: Application.version,
                unityVersion: Application.unityVersion,
                screenWidth: Screen.width,
                screenHeight: Screen.height,
                deviceModel: SystemInfo.deviceModel,
                sdkVersion: TestingFloorVersion.Value,
                editor: Application.isEditor
            );
        }
    }

    /// <summary>The OS family and BCP 47 locale the telemetry contract asks for in <c>$tf</c>.</summary>
    internal static class PlatformNames {
        // Matches RuntimePlatform names, so platforms added in later Unity versions still map.
        internal static string OsFamily(string platform) {
            if (string.IsNullOrWhiteSpace(platform)) return null;
            if (Starts(platform, "Windows") || Starts(platform, "WSAPlayer")) return "Windows";
            if (Starts(platform, "OSX")) return "macOS";
            if (Starts(platform, "Linux") || Starts(platform, "EmbeddedLinux")) return "Linux";
            if (platform == "IPhonePlayer") return "iOS";
            if (platform == "Android") return "Android";
            if (platform == "tvOS") return "tvOS";
            if (platform == "VisionOS") return "visionOS";
            if (platform == "WebGLPlayer") return "Web";
            if (Starts(platform, "PS")) return "PlayStation";
            if (platform.IndexOf("Xbox", StringComparison.Ordinal) >= 0) return "Xbox";
            if (Starts(platform, "Switch")) return "Switch";
            return platform;
        }

        /// <summary>
        /// The UI culture's tag, such as <c>en-US</c>, when its language is the one Unity reports;
        /// otherwise a language-only tag from <paramref name="language"/>, or null when unknown.
        /// </summary>
        internal static string Locale(SystemLanguage language, CultureInfo culture) {
            var languageTag = LanguageTag(language);
            var cultureTag = culture != null && !string.IsNullOrEmpty(culture.Name) ? culture.Name.Replace('_', '-') : null;
            if (cultureTag == null) return languageTag;
            if (languageTag == null) return cultureTag;
            var languageOnly = languageTag.Split('-')[0];
            return string.Equals(culture.TwoLetterISOLanguageName, languageOnly, StringComparison.OrdinalIgnoreCase) ? cultureTag : languageTag;
        }

        internal static string LanguageTag(SystemLanguage language) {
            switch (language) {
                case SystemLanguage.Afrikaans: return "af";
                case SystemLanguage.Arabic: return "ar";
                case SystemLanguage.Basque: return "eu";
                case SystemLanguage.Belarusian: return "be";
                case SystemLanguage.Bulgarian: return "bg";
                case SystemLanguage.Catalan: return "ca";
                case SystemLanguage.Chinese: return "zh";
                case SystemLanguage.ChineseSimplified: return "zh-Hans";
                case SystemLanguage.ChineseTraditional: return "zh-Hant";
                case SystemLanguage.Czech: return "cs";
                case SystemLanguage.Danish: return "da";
                case SystemLanguage.Dutch: return "nl";
                case SystemLanguage.English: return "en";
                case SystemLanguage.Estonian: return "et";
                case SystemLanguage.Faroese: return "fo";
                case SystemLanguage.Finnish: return "fi";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Greek: return "el";
                case SystemLanguage.Hebrew: return "he";
                case SystemLanguage.Hindi: return "hi";
                case SystemLanguage.Hungarian: return "hu";
                case SystemLanguage.Icelandic: return "is";
                case SystemLanguage.Indonesian: return "id";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Latvian: return "lv";
                case SystemLanguage.Lithuanian: return "lt";
                case SystemLanguage.Norwegian: return "no";
                case SystemLanguage.Polish: return "pl";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Romanian: return "ro";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.SerboCroatian: return "hbs";
                case SystemLanguage.Slovak: return "sk";
                case SystemLanguage.Slovenian: return "sl";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Swedish: return "sv";
                case SystemLanguage.Thai: return "th";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Ukrainian: return "uk";
                case SystemLanguage.Vietnamese: return "vi";
                default: return null;
            }
        }

        static bool Starts(string value, string prefix) => value.StartsWith(prefix, StringComparison.Ordinal);
    }

    internal static class TestingFloorVersion {
        public const string Value = "0.7.0";
    }
}
