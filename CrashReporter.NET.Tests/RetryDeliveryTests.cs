using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CrashReporterDotNET.DrDump;
using Xunit;

namespace CrashReporterDotNET.Tests
{
    [Collection(QueueCollection.Name)]
    public sealed class RetryDeliveryTests : IDisposable
    {
        private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(10);

        private readonly TemporaryDirectory _root = new TemporaryDirectory();

        private readonly string _originalRoot = FailedReportQueue.RootDirectory;

        public RetryDeliveryTests()
        {
            FailedReportQueue.RootDirectory = _root.Path;
        }

        public void Dispose()
        {
            FailedReportQueue.RootDirectory = _originalRoot;
            _root.Dispose();
        }

        private static ReportCrash SmtpReporter(int port) => new ReportCrash("to@example.com")
        {
            AnalyzeWithDoctorDump = false,
            SmtpHost = "127.0.0.1",
            Port = port,
            FromEmail = "from@example.com"
        };

        private static void SaveReport(string message = "queued", string title = "Original App", string assemblyVersion = "1.2.3.4")
        {
            Exception exception;
            try
            {
                throw new InvalidOperationException(message);
            }
            catch (Exception e)
            {
                exception = e;
            }

            var reporter = new ReportCrash("to@example.com")
            {
                Exception = exception,
                ApplicationTitle = title,
                ApplicationVersion = assemblyVersion,
                ApplicationAssemblyVersion = assemblyVersion
            };
            reporter.SaveFailedReport("user@example.com", "user message", includeScreenshot: false);
        }

        private static int QueuedCount() => FailedReportQueue.GetFiles(FailedReportQueue.GetDirectory()).Count();

        // ---------------- only one retry at a time ----------------

        [Fact]
        public async Task OverlappingRetries_SendEachReportOnce()
        {
            SaveReport();
            using (var smtp = new SmtpStub())
            {
                var retries = Enumerable.Range(0, 20).Select(_ => SmtpReporter(smtp.Port).RetryFailedReportsAsync()).ToArray();
                var results = await Task.WhenAll(retries);

                Assert.Equal(1, results.Sum(r => r.FailedReportsSent));
                Assert.Equal(0, QueuedCount());
                Assert.True(smtp.Messages.TryTake(out _, MailTimeout));
                // Give any duplicate delivery a chance to arrive before asserting there was none.
                Assert.False(smtp.Messages.TryTake(out _, TimeSpan.FromMilliseconds(500)), "report was sent more than once");
            }
        }

        [Fact]
        public async Task Retry_SkipsWhileAnotherProcessHoldsTheQueueLock()
        {
            SaveReport();
            var queue = FailedReportQueue.GetDirectory();
            using (var smtp = new SmtpStub())
            {
                // Same lock another process takes while retrying.
                using (var otherProcessLock = FailedReportQueue.TryLock(queue))
                {
                    Assert.NotNull(otherProcessLock);

                    var whileLocked = await SmtpReporter(smtp.Port).RetryFailedReportsAsync();

                    Assert.Equal(0, whileLocked.FailedReports);
                    Assert.Equal(0, whileLocked.FailedReportsSent);
                    Assert.Equal(1, QueuedCount());
                }

                var afterRelease = await SmtpReporter(smtp.Port).RetryFailedReportsAsync();

                Assert.Equal(1, afterRelease.FailedReportsSent);
                Assert.Equal(0, QueuedCount());
            }
        }

        [Fact]
        public async Task Retry_IsNotBlockedByALockFileLeftByAnInterruptedRetry()
        {
            SaveReport();
            var queue = FailedReportQueue.GetDirectory();
            File.WriteAllText(Path.Combine(queue.FullName, FailedReportQueue.LockFileName), "left behind by a crashed process");

            using (var smtp = new SmtpStub())
            {
                var result = await SmtpReporter(smtp.Port).RetryFailedReportsAsync();

                Assert.Equal(1, result.FailedReportsSent);
            }
        }

        // ---------------- cancellation and timeout while the SMTP server stalls ----------------

        [Theory]
        [InlineData(0)]     // right after the connection is accepted, while the client is still connecting
        [InlineData(500)]   // while the client waits for the server greeting
        public async Task Cancellation_InterruptsAStalledSmtpDelivery(int cancelAfterMilliseconds)
        {
            SaveReport();
            var stalled = SmtpStub.StartStalledServer(out var port);
            try
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    var retry = SmtpReporter(port).RetryFailedReportsAsync(cancellation.Token);
                    using (await stalled.AcceptTcpClientAsync())
                    {
                        await Task.Delay(cancelAfterMilliseconds);
                        Assert.False(retry.IsCompleted, "delivery should be waiting for the server greeting");

                        cancellation.Cancel();
                        // The connection is kept open: delivery must stop because of the cancellation, not the server.
                        var finished = await Task.WhenAny(retry, Task.Delay(TimeSpan.FromSeconds(1)));

                        Assert.Same(retry, finished);
                        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry);
                    }
                }
            }
            finally
            {
                stalled.Stop();
            }

            Assert.Equal(1, QueuedCount());
        }

        [Fact]
        public async Task Cancellation_InterruptsAStalledSmtpDelivery_ReviewerProbe()
        {
            // Regression probe from the 2.1 review: a version 1 report, cancelled as soon as the connection is accepted.
            // On .NET Framework, SendAsyncCancel alone did not stop this delivery until the server closed the connection.
            // The window in which that happens is widest on the first SMTP connection of a process, so this test reliably
            // reproduces the bug when run on its own (--filter ReviewerProbe) and may not when other SMTP tests ran first.
            var directory = FailedReportQueue.GetDirectory();
            new FailedReport { Exception = ExceptionData.FromException(new Exception("probe")) }
                .Save(Path.Combine(directory.FullName, FailedReportQueue.CreateFileName(DateTime.UtcNow)));
            var listener = SmtpStub.StartStalledServer(out var port);
            try
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    var retry = SmtpReporter(port).RetryFailedReportsAsync(cancellation.Token);
                    using (var connection = await listener.AcceptTcpClientAsync())
                    {
                        cancellation.Cancel();
                        var completed = await Task.WhenAny(retry, Task.Delay(1000));
                        connection.Close();
                        try
                        {
                            await retry;
                        }
                        catch (OperationCanceledException)
                        {
                        }

                        Assert.Same(retry, completed);
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task DeliveryTimeout_StopsAStalledDeliveryAndKeepsTheReport()
        {
            SaveReport("first");
            SaveReport("second");
            var stalled = SmtpStub.StartStalledServer(out var port);
            try
            {
                var reporter = SmtpReporter(port);
                reporter.DeliveryTimeout = TimeSpan.FromMilliseconds(300);
                var stopwatch = Stopwatch.StartNew();

                var result = await reporter.RetryFailedReportsAsync();

                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
                Assert.Equal(2, result.FailedReports);
                Assert.Equal(0, result.FailedReportsSent);
            }
            finally
            {
                stalled.Stop();
            }

            Assert.Equal(2, QueuedCount());
        }

        [Fact]
        public async Task SendMail_CancellationRacingWithCompletionIsHandled()
        {
            // Cancel right after a successful delivery completes: the send must still report success, not hang or throw.
            using (var smtp = new SmtpStub())
            {
                for (var i = 0; i < 10; i++)
                {
                    using (var cancellation = new CancellationTokenSource())
                    using (var client = new System.Net.Mail.SmtpClient("127.0.0.1", smtp.Port))
                    using (var message = new System.Net.Mail.MailMessage("from@example.com", "to@example.com", "race", "body"))
                    {
                        var send = ReportCrash.SendMailAsync(client, message, cancellation.Token);
                        var sent = await Task.WhenAny(send, Task.Delay(MailTimeout));
                        cancellation.Cancel();

                        Assert.Same(send, sent);
                        await send;
                    }
                }

                Assert.Equal(10, smtp.Messages.Count);
            }
        }

        // ---------------- Doctor Dump retries ----------------

        private static string DrDumpResponse(string operation, string type) =>
            "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body>" +
            $"<{operation}Response xmlns=\"https://www.drdump.com/services\"><{operation}Result xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\" i:type=\"{type}\">" +
            $"<Context>AQID</Context><UrlToProblem>https://drdump.test/problem/{operation}</UrlToProblem></{operation}Result></{operation}Response></s:Body></s:Envelope>";

        private static FakeHttpHandler DrDumpServer() => new FakeHttpHandler((request, token) =>
        {
            var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var response = body.Contains("<SendAnonymousReport ")
                ? DrDumpResponse("SendAnonymousReport", "NeedReportResponse")
                : DrDumpResponse("SendAdditionalData", "StopResponse");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/soap+xml")
            });
        });

        private static ReportCrash DrDumpReporter(FakeHttpHandler server, ConcurrentQueue<string> openedUrls,
            Action<string> opener = null) => new ReportCrash("to@example.com")
        {
            AnalyzeWithDoctorDump = true,
            DoctorDumpSettings = new DoctorDumpSettings { OpenReportInBrowser = true },
            DrDumpServiceFactory = () => new DrDumpService(new DrDumpSoapClient(new HttpClient(server))),
            ReportUrlOpener = opener ?? openedUrls.Enqueue
        };

        [Fact]
        public async Task DrDumpRetry_ReportsTheSavedApplicationVersionAfterAnUpgrade()
        {
            // Crashed as version 1.2.3.4; the application was upgraded (the test host has a different version) before retrying.
            SaveReport(title: "Original App", assemblyVersion: "1.2.3.4");
            var server = DrDumpServer();
            var openedUrls = new ConcurrentQueue<string>();

            var result = await DrDumpReporter(server, openedUrls).RetryFailedReportsAsync();

            Assert.Equal(1, result.FailedReportsSent);
            var anonymousRequest = server.RequestBodies.First(body => body.Contains("<SendAnonymousReport "));
            var app = Regex.Match(anonymousRequest, "<app>.*?</app>", RegexOptions.Singleline).Value;
            Assert.Contains("<AppName>Original App</AppName>", app);
            Assert.Contains("<V1>1</V1><V2>2</V2><V3>3</V3><V4>4</V4>", app);
            Assert.Equal(new[] { "https://drdump.test/problem/SendAdditionalData" }, openedUrls);
        }

        [Fact]
        public async Task DrDumpRetry_BrowserFailureDoesNotCauseASecondUpload()
        {
            SaveReport();
            var server = DrDumpServer();

            var first = await DrDumpReporter(server, null, url => throw new InvalidOperationException("no default browser"))
                .RetryFailedReportsAsync();
            var second = await DrDumpReporter(server, new ConcurrentQueue<string>()).RetryFailedReportsAsync();

            Assert.Equal(1, first.FailedReportsSent);
            Assert.Equal(0, second.FailedReports);
            Assert.Equal(0, QueuedCount());
            Assert.Equal(1, server.RequestBodies.Count(body => body.Contains("<SendAnonymousReport ")));
        }
    }
}
