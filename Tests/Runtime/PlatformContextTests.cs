using System.Globalization;
using System.Text;
using NUnit.Framework;
using TestingFloorSdk.Internal;
using UnityEngine;

namespace TestingFloorSdk.Tests {
    public class PlatformContextTests {
        static string Json(TelemetryEvent ev) => Encoding.UTF8.GetString(new JsonPayloadWriter().BuildBatch(
            new[] { ev }, "example-write-key", "example-play",
            JsonPayloadWriter.CollectorBodyByteCap, JsonPayloadWriter.CollectorEventByteCap, null, out _, out _));

        [TestCase("WindowsPlayer", "Windows")]
        [TestCase("WindowsEditor", "Windows")]
        [TestCase("WindowsServer", "Windows")]
        [TestCase("WSAPlayerX64", "Windows")]
        [TestCase("OSXPlayer", "macOS")]
        [TestCase("OSXEditor", "macOS")]
        [TestCase("LinuxPlayer", "Linux")]
        [TestCase("LinuxServer", "Linux")]
        [TestCase("EmbeddedLinuxArm64", "Linux")]
        [TestCase("IPhonePlayer", "iOS")]
        [TestCase("Android", "Android")]
        [TestCase("tvOS", "tvOS")]
        [TestCase("VisionOS", "visionOS")]
        [TestCase("WebGLPlayer", "Web")]
        [TestCase("PS4", "PlayStation")]
        [TestCase("PS5", "PlayStation")]
        [TestCase("XboxOne", "Xbox")]
        [TestCase("GameCoreXboxSeries", "Xbox")]
        [TestCase("Switch", "Switch")]
        [TestCase("QNXArm64", "QNXArm64")]
        public void OsIsTheOperatingSystemFamily(string platform, string expected) {
            Assert.AreEqual(expected, PlatformNames.OsFamily(platform));
        }

        [Test]
        public void CaptureReportsThisPlatformAndWhetherItIsTheEditor() {
            var platform = PlatformContext.Capture();
            Assert.AreEqual(PlatformNames.OsFamily(Application.platform.ToString()), platform.Os);
            Assert.AreEqual(Application.isEditor, platform.Editor);
            Assert.AreEqual(PlatformNames.Locale(Application.systemLanguage, CultureInfo.CurrentUICulture), platform.Locale);
        }

        [Test]
        public void LocaleIsATagNotALanguageName() {
            Assert.AreEqual("en-US", PlatformNames.Locale(SystemLanguage.English, new CultureInfo("en-US")));
            Assert.AreEqual("zh-CN", PlatformNames.Locale(SystemLanguage.ChineseSimplified, new CultureInfo("zh-CN")));
            Assert.AreEqual("pt-BR", PlatformNames.Locale(SystemLanguage.Unknown, new CultureInfo("pt-BR")));
            Assert.AreEqual("de", PlatformNames.Locale(SystemLanguage.German, new CultureInfo("en-US")),
                "When the culture's language is not the system language, the system language wins");
            Assert.AreEqual("fr", PlatformNames.Locale(SystemLanguage.French, CultureInfo.InvariantCulture));
            Assert.AreEqual("zh-Hant", PlatformNames.Locale(SystemLanguage.ChineseTraditional, null));
            Assert.IsNull(PlatformNames.Locale(SystemLanguage.Unknown, CultureInfo.InvariantCulture));
        }

        [Test]
        public void EmptyContextValuesAreLeftOut() {
            var platform = new PlatformContext("", " ", "", "", 0, 0, "", TestingFloorVersion.Value, editor: true);
            var json = Json(new TelemetryEvent("example", null, ContextSnapshot.Create(platform), null, null, 0, "example-play"));
            StringAssert.Contains("\"$tf\":{\"sdk_version\":\"" + TestingFloorVersion.Value + "\",\"editor\":true}", json);
        }

        [Test]
        public void TimesHaveExactlyMillisecondsInUtc() {
            var json = Json(new TelemetryEvent("example", null, ContextSnapshot.Create(default), null, null, 1711234567890, "example-play"));
            StringAssert.Contains("\"$time\":\"2024-03-23T22:56:07.890Z\"", json);
            StringAssert.IsMatch("\"\\$sent_at\":\"\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{3}Z\"", json);
        }
    }
}
