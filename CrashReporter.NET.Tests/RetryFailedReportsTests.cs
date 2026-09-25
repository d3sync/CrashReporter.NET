using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CrashReporterDotNET.Tests
{
    [Collection(QueueCollection.Name)]
    public sealed class RetryFailedReportsTests : IDisposable
    {
        private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(10);

        private readonly TemporaryDirectory _root = new TemporaryDirectory();

        private readonly string _originalRoot = FailedReportQueue.RootDirectory;

        public RetryFailedReportsTests()
        {
            FailedReportQueue.RootDirectory = _root.Path;
        }

        public void Dispose()
        {
            FailedReportQueue.RootDirectory = _originalRoot;
            _root.Dispose();
        }

        private static ReportCrash CreateReporter(int smtpPort) => new ReportCrash("to@example.com")
        {
            AnalyzeWithDoctorDump = false,
            SmtpHost = "127.0.0.1",
            Port = smtpPort,
            FromEmail = "from@example.com"
        };

        private static Exception CreateException(string message)
        {
            try
            {
                throw new InvalidOperationException(message);
            }
            catch (Exception e)
            {
                return e;
            }
        }

        private static void SaveReport(int smtpPort, string message, byte[] screenshot)
        {
            var reporter = CreateReporter(smtpPort);
            reporter.Exception = CreateException(message);
            reporter.ApplicationTitle = "Original App";
            reporter.ApplicationVersion = "9.8.7.6";
            reporter.DeveloperMessage = "original developer message";
            reporter.ScreenShotBinary = screenshot;
            reporter.SaveFailedReport("user@example.com", "what the user typed", includeScreenshot: true);
        }

        private static string[] QueuedFiles() =>
            FailedReportQueue.GetFiles(FailedReportQueue.GetDirectory()).Select(f => f.FullName).ToArray();

        [Fact]
        public async Task RetryAsync_SendsTheSavedSnapshotWithoutRecapturing()
        {
            using (var smtp = new SmtpStub())
            {
                var screenshot = Enumerable.Range(0, 48).Select(i => (byte) (i * 5)).ToArray();
                SaveReport(smtp.Port, "saved crash", screenshot);

                // Simulates the next start of the application: different settings, no screenshot of its own.
                var reporter = CreateReporter(smtp.Port);
                reporter.DeveloperMessage = "message of the new session";
                reporter.IncludeScreenshot = false;

                var result = await reporter.RetryFailedReportsAsync();

                Assert.Equal(1, result.FailedReports);
                Assert.Equal(1, result.FailedReportsSent);
                Assert.True(result.AnyReportSent);
                Assert.Empty(QueuedFiles());
                Assert.Null(reporter.ScreenShotBinary);

                Assert.True(smtp.Messages.TryTake(out var raw, MailTimeout), "no e-mail received");
                var mail = SmtpStub.Decode(raw);
                Assert.Contains("saved crash", mail);
                Assert.Contains("Original App 9.8.7.6", mail);
                Assert.Contains("original developer message", mail);
                Assert.DoesNotContain("message of the new session", mail);
                Assert.Contains("what the user typed", mail);
                Assert.Contains("Crash Report by user@example.com", mail);
                Assert.Contains("System.InvalidOperationException", mail);
                // The original screenshot is attached, even though the new session disabled screenshots.
                Assert.Contains(Convert.ToBase64String(screenshot), raw.Replace("\r\n", ""));
            }
        }

        private sealed class RecordingSynchronizationContext : SynchronizationContext
        {
            public int Calls;

            public override void Post(SendOrPostCallback d, object state)
            {
                Interlocked.Increment(ref Calls);
                base.Post(d, state);
            }

            public override void Send(SendOrPostCallback d, object state)
            {
                Interlocked.Increment(ref Calls);
                base.Send(d, state);
            }

            public override SynchronizationContext CreateCopy() => this;
        }

        [Fact]
        public async Task RetryAsync_NeverUsesTheCallersSynchronizationContext()
        {
            using (var smtp = new SmtpStub())
            {
                SaveReport(smtp.Port, "from ui thread", null);
                var reporter = CreateReporter(smtp.Port);

                // Simulates calling from a UI thread (e.g. WPF Application.OnStartup) without awaiting.
                var uiContext = new RecordingSynchronizationContext();
                var previous = SynchronizationContext.Current;
                Task<FailedReportsRetryResult> retry;
                SynchronizationContext.SetSynchronizationContext(uiContext);
                try
                {
                    retry = reporter.RetryFailedReportsAsync();
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }

                var result = await retry;

                Assert.Equal(1, result.FailedReportsSent);
                Assert.Equal(0, uiContext.Calls);
            }
        }

        [Fact]
        public async Task RetryAsync_StopsAtFirstFailureAndKeepsTheReports()
        {
            var closedPort = SmtpStub.GetClosedPort();
            SaveReport(closedPort, "first", null);
            SaveReport(closedPort, "second", null);

            var result = await CreateReporter(closedPort).RetryFailedReportsAsync();

            Assert.Equal(2, result.FailedReports);
            Assert.Equal(0, result.FailedReportsSent);
            Assert.False(result.AnyReportSent);
            Assert.Equal(2, QueuedFiles().Length);
        }

        [Fact]
        public async Task RetryAsync_CancellationKeepsTheReports()
        {
            SaveReport(SmtpStub.GetClosedPort(), "queued", null);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    CreateReporter(SmtpStub.GetClosedPort()).RetryFailedReportsAsync(cancellation.Token));
            }

            Assert.Single(QueuedFiles());
        }

        [Fact]
        public async Task RetryAsync_DoesNotTouchReportsOfOtherApplications()
        {
            var otherApplication = FailedReportQueue.GetDirectory("Some Other App");
            otherApplication.Create();
            var otherReport = Path.Combine(otherApplication.FullName, FailedReportQueue.CreateFileName(DateTime.UtcNow));
            new FailedReport { Exception = ExceptionData.FromException(new Exception("not mine")) }.Save(otherReport);

            using (var smtp = new SmtpStub())
            {
                var result = await CreateReporter(smtp.Port).RetryFailedReportsAsync();

                Assert.Equal(0, result.FailedReports);
                Assert.True(File.Exists(otherReport));
                Assert.Empty(smtp.Messages);
            }
        }

        [Fact]
        public void Retry_SynchronousWrapperReportsCounts()
        {
            using (var smtp = new SmtpStub())
            {
                SaveReport(smtp.Port, "sync retry", null);

                var anySent = CreateReporter(smtp.Port).RetryFailedReports(out var found, out var sent);

                Assert.True(anySent);
                Assert.Equal(1, found);
                Assert.Equal(1, sent);
                Assert.True(smtp.Messages.TryTake(out _, MailTimeout));
            }
        }

        [Fact]
        public async Task RetryAsync_SendsVersion1ReportsWithCurrentSettings()
        {
            // A report saved by 2.0.x only contains the exception and screenshot.
            var queue = FailedReportQueue.GetDirectory();
            queue.Create();
            new FailedReport { Exception = ExceptionData.FromException(CreateException("from 2.0")) }
                .Save(Path.Combine(queue.FullName, FailedReportQueue.CreateFileName(DateTime.UtcNow)));

            using (var smtp = new SmtpStub())
            {
                var reporter = CreateReporter(smtp.Port);
                reporter.DeveloperMessage = "current developer message";

                var result = await reporter.RetryFailedReportsAsync();

                Assert.Equal(1, result.FailedReportsSent);
                Assert.True(smtp.Messages.TryTake(out var raw, MailTimeout));
                var mail = SmtpStub.Decode(raw);
                Assert.Contains("from 2.0", mail);
                Assert.Contains("current developer message", mail);
            }
        }

        [Fact]
        public async Task RetryAsync_DeletesCorruptReports()
        {
            var queue = FailedReportQueue.GetDirectory();
            queue.Create();
            var corrupt = Path.Combine(queue.FullName, FailedReportQueue.CreateFileName(DateTime.UtcNow));
            File.WriteAllText(corrupt, "<not a report");

            var result = await CreateReporter(SmtpStub.GetClosedPort()).RetryFailedReportsAsync();

            Assert.Equal(0, result.FailedReports);
            Assert.False(File.Exists(corrupt));
        }
    }
}
