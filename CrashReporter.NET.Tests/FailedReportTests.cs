using System;
using System.IO;
using System.Linq;
using Xunit;

namespace CrashReporterDotNET.Tests
{
    [Collection(QueueCollection.Name)]
    public sealed class FailedReportTests : IDisposable
    {
        private readonly TemporaryDirectory _directory = new TemporaryDirectory();

        public void Dispose() => _directory.Dispose();

        private static Exception Throw(Func<Exception> create)
        {
            try
            {
                throw create();
            }
            catch (Exception e)
            {
                return e;
            }
        }

        [Fact]
        public void RoundTrip_PreservesControlCharactersMarkupAndNestedExceptions()
        {
            const string hostile = "ctl\u0001\u001F nul\u0000 <tag attr=\"x\"> &amp; ]]> ￾ end \U0001F600";
            var exception = Throw(() => new InvalidOperationException("outer " + hostile,
                Throw(() => new FormatException("inner " + hostile, Throw(() => new ArgumentException("innermost"))))));

            var path = Path.Combine(_directory.Path, "report.xml");
            var original = new FailedReport
            {
                FormatVersion = FailedReport.CurrentFormatVersion,
                Exception = ExceptionData.FromException(exception),
                ScreenShot = new byte[] { 0, 1, 2, 255 },
                ApplicationTitle = "App " + hostile,
                ApplicationVersion = "1.2.3.4",
                DeveloperMessage = "dev " + hostile,
                UserEmail = "user@example.com",
                UserMessage = "user " + hostile,
                IncludeScreenshot = true,
                CrashDateUtc = new DateTime(2026, 9, 25, 1, 2, 3, DateTimeKind.Utc)
            };

            original.Save(path);
            var loaded = FailedReport.Load(path);

            Assert.Equal(FailedReport.CurrentFormatVersion, loaded.FormatVersion);
            Assert.Equal(original.ScreenShot, loaded.ScreenShot);
            Assert.Equal(original.ApplicationTitle, loaded.ApplicationTitle);
            Assert.Equal(original.ApplicationVersion, loaded.ApplicationVersion);
            Assert.Equal(original.DeveloperMessage, loaded.DeveloperMessage);
            Assert.Equal(original.UserEmail, loaded.UserEmail);
            Assert.Equal(original.UserMessage, loaded.UserMessage);
            Assert.True(loaded.IncludeScreenshot);
            Assert.Equal(original.CrashDateUtc, loaded.CrashDateUtc);

            var replayed = loaded.Exception.ToException();
            Assert.Equal("System.InvalidOperationException", ReplayedException.GetTypeName(replayed));
            Assert.Equal(exception.Message, replayed.Message);
            Assert.Equal(exception.StackTrace, replayed.StackTrace);
            Assert.Equal(exception.ToString(), replayed.ToString());
            Assert.Equal(exception.HResult, replayed.HResult);
            Assert.Equal("System.FormatException", ReplayedException.GetTypeName(replayed.InnerException));
            Assert.Equal(exception.InnerException.Message, replayed.InnerException.Message);
            Assert.Equal("System.ArgumentException", ReplayedException.GetTypeName(replayed.InnerException.InnerException));
        }

        [Fact]
        public void Save_ReplacesUnpairedSurrogatesInsteadOfFailing()
        {
            var path = Path.Combine(_directory.Path, "report.xml");
            var report = new FailedReport
            {
                Exception = ExceptionData.FromException(new Exception("broken \uD800 text \uDC00 end")),
                UserMessage = "\uD83D"
            };

            report.Save(path);
            var loaded = FailedReport.Load(path);

            Assert.Equal("broken � text � end", loaded.Exception.Message);
            Assert.Equal("�", loaded.UserMessage);
            // The report being saved is not modified.
            Assert.Equal("\uD83D", report.UserMessage);
        }

        [Fact]
        public void Load_ReadsVersion1ReportsWrittenBy20()
        {
            // Format written by CrashReporter.NET 2.0.x.
            var path = Path.Combine(_directory.Path, "failed-report-v1.xml");
            File.WriteAllText(path,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<FailedReport xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"https://github.com/ravibpatel/CrashReporter.NET/failed-report\">" +
                "<Exception><TypeName>System.InvalidOperationException</TypeName><Message>old</Message><Source>Src</Source>" +
                "<StackTrace>   at X.Y()</StackTrace><HResult>-2146233079</HResult><Text>System.InvalidOperationException: old</Text>" +
                "<InnerException i:nil=\"true\" /></Exception><ScreenShot>AQID</ScreenShot></FailedReport>");

            var loaded = FailedReport.Load(path);

            Assert.Equal(0, loaded.FormatVersion);
            Assert.Equal("old", loaded.Exception.Message);
            Assert.Equal(new byte[] { 1, 2, 3 }, loaded.ScreenShot);
            Assert.Null(loaded.ApplicationTitle);
            Assert.Null(loaded.IncludeScreenshot);
        }

        [Fact]
        public void AtomicWrite_InterruptedWriteLeavesNoFile()
        {
            var path = Path.Combine(_directory.Path, "failed-report-x.xml");

            Assert.Throws<IOException>(() => AtomicFile.Write(path, stream =>
            {
                stream.Write(new byte[1024], 0, 1024);
                throw new IOException("disk full");
            }));

            Assert.False(File.Exists(path));
            Assert.Empty(Directory.GetFiles(_directory.Path));
        }

        [Fact]
        public void AtomicWrite_NeverOverwritesAnExistingReport()
        {
            var path = Path.Combine(_directory.Path, "failed-report-x.xml");
            File.WriteAllText(path, "existing");

            Assert.ThrowsAny<IOException>(() => AtomicFile.Write(path, stream => stream.WriteByte(1)));

            Assert.Equal("existing", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(_directory.Path));
        }

        [Fact]
        public void Queue_FileNamesAreUniqueEvenWithinTheSameMillisecond()
        {
            var now = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
            var names = Enumerable.Range(0, 1000).Select(_ => FailedReportQueue.CreateFileName(now)).ToList();

            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.All(names, name => Assert.StartsWith("failed-report-20260925T100000000-", name));
        }

        [Fact]
        public void Queue_IsSeparatedPerApplication()
        {
            FailedReportQueue.RootDirectory = _directory.Path;

            var first = FailedReportQueue.GetDirectory("App One");
            var second = FailedReportQueue.GetDirectory("App:Two?");

            Assert.NotEqual(first.FullName, second.FullName);
            Assert.Equal(_directory.Path, first.Parent.FullName);
            Assert.Equal("App_Two_", second.Name);
            Assert.Equal("default", FailedReportQueue.GetSafeName("  "));
        }

        [Fact]
        public void Queue_ListsOnlyCompletedReportsOldestFirst()
        {
            var queue = new DirectoryInfo(_directory.Path);
            var older = FailedReportQueue.CreateFileName(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var newer = FailedReportQueue.CreateFileName(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            File.WriteAllText(Path.Combine(queue.FullName, newer), "");
            File.WriteAllText(Path.Combine(queue.FullName, older), "");
            File.WriteAllText(Path.Combine(queue.FullName, newer + ".abc" + AtomicFile.TemporaryExtension), "");
            File.WriteAllText(Path.Combine(queue.FullName, "failed-report-legacy.bin"), "");
            File.WriteAllText(Path.Combine(queue.FullName, "unrelated.xml"), "");

            var files = FailedReportQueue.GetFiles(queue).Select(f => f.Name).ToList();

            Assert.Equal(new[] { older, newer }, files);
        }
    }
}
