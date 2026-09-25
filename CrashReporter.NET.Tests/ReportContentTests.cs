using System;
using Microsoft.Win32;
using Xunit;

namespace CrashReporterDotNET.Tests
{
    public sealed class WindowsVersionTests
    {
        [Theory]
        [InlineData("Windows 10 Pro", 22631, "Client", "Windows 11 Pro")]
        [InlineData("Windows 10 Education", 22631, null, "Windows 11 Education")]
        [InlineData("Windows 10 Enterprise", 22000, "Client", "Windows 11 Enterprise")]
        [InlineData("Windows 10 Pro", 26100, "Client", "Windows 11 Pro")]
        [InlineData("Windows 10 Pro", 19045, "Client", "Windows 10 Pro")]
        [InlineData("Windows 10 Enterprise LTSC 2021", 19044, "Client", "Windows 10 Enterprise LTSC 2021")]
        [InlineData("Windows 11 Pro", 26100, "Client", "Windows 11 Pro")]
        [InlineData("Windows Server 2022 Datacenter", 20348, "Server", "Windows Server 2022 Datacenter")]
        [InlineData("Windows Server 2025 Datacenter", 26100, "Server Core", "Windows Server 2025 Datacenter")]
        [InlineData("Windows 10 Server Something", 26100, null, "Windows 10 Server Something")]
        [InlineData("Windows 7 Professional", 7601, "Client", "Windows 7 Professional")]
        public void ProductName_IsCorrectedOnlyForWindows11Workstations(string registryName, int build, string installationType, string expected)
        {
            Assert.Equal(expected, HelperMethods.GetWindowsProductName(registryName, build, installationType));
        }

        [Fact]
        public void GetWindowsVersion_ReportsWindows11OnWindows11Workstations()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var build);
                var installationType = key?.GetValue("InstallationType") as string;
                var version = HelperMethods.GetWindowsVersion();

                Assert.Contains($"(OS Build {build}", version);
                if (build >= HelperMethods.Windows11FirstBuild && installationType == "Client")
                    Assert.Contains("Windows 11", version);
            }
        }
    }

    public sealed class XmlTextTests
    {
        [Fact]
        public void ToValidXml_ReplacesAllInvalidCharacters()
        {
            Assert.Equal("a�b�c�d�\t\r\n\U0001F600",
                XmlText.ToValidXml("a\u0001b\u0000c￾d\uD800\t\r\n\U0001F600"));
        }

        [Fact]
        public void ReplaceLoneSurrogates_KeepsControlCharacters()
        {
            Assert.Equal("a\u0001��\U0001F600", XmlText.ReplaceLoneSurrogates("a\u0001\uDC00\uD800\U0001F600"));
        }

        [Fact]
        public void ValidText_IsReturnedUnchanged()
        {
            const string text = "plain text <&> \U0001F600";
            Assert.Same(text, XmlText.ToValidXml(text));
            Assert.Null(XmlText.ToValidXml(null));
        }
    }

    public sealed class HtmlReportTests
    {
        private static Exception CreateException()
        {
            try
            {
                throw new InvalidOperationException("boom <b>");
            }
            catch (Exception e)
            {
                return e;
            }
        }

        [Fact]
        public void SavedReport_EmbedsTheScreenshot()
        {
            var screenshot = new byte[] { 137, 80, 78, 71, 1, 2, 3 };

            var html = ReportCrash.CreateHtmlReport("App", "1.0", CreateException(), "dev", "user", screenshot);

            Assert.Contains("src=\"data:image/png;base64," + Convert.ToBase64String(screenshot) + "\"", html);
            Assert.Contains("boom &lt;b&gt;", html);
        }

        [Theory]
        [InlineData("   at A.B()\r\n   at C.D()")]
        [InlineData("   at A.B()\n   at C.D()")]
        public void StackTrace_LineBreaksAreKept(string stackTrace)
        {
            var exception = new ExceptionData { TypeName = "System.Exception", Message = "m", StackTrace = stackTrace }.ToException();

            var html = ReportCrash.CreateHtmlReport("App", "1.0", exception, null, null, null);

            Assert.Contains("   at A.B()<br/>   at C.D()", html);
        }

        [Fact]
        public void Report_WithoutScreenshot_HasNoImage()
        {
            var html = ReportCrash.CreateHtmlReport("App", "1.0", CreateException(), null, null, null);

            Assert.DoesNotContain("<img", html);
        }

        [Fact]
        public void DialogExport_EmbedsScreenshotOnlyWhenRequested()
        {
            var reporter = new ReportCrash("to@example.com")
            {
                Exception = CreateException(),
                ApplicationTitle = "App",
                ApplicationVersion = "1.0",
                ScreenShotBinary = new byte[] { 1, 2, 3 }
            };

            Assert.Contains("data:image/png;base64,AQID", reporter.CreateHtmlReport("user", embedScreenshot: true));
            Assert.DoesNotContain("<img", reporter.CreateHtmlReport("user"));
        }
    }

    public sealed class DoctorDumpGateTests
    {
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void AnonymousReport_IsOnlySentWhenDoctorDumpIsUsed(bool analyzeWithDoctorDump, bool sendAnonymously, bool expected)
        {
            var reporter = new ReportCrash("to@example.com")
            {
                AnalyzeWithDoctorDump = analyzeWithDoctorDump,
                DoctorDumpSettings = new DoctorDumpSettings { SendAnonymousReportSilently = sendAnonymously }
            };

            Assert.Equal(expected, reporter.SendAnonymousReportWhenDialogOpens);
        }

        [Fact]
        public void AnonymousReport_IsNotSentWithoutSettings()
        {
            Assert.False(new ReportCrash("to@example.com") { DoctorDumpSettings = null }.SendAnonymousReportWhenDialogOpens);
        }
    }
}
