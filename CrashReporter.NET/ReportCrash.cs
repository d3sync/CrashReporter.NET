using System;
using System.Collections.Generic;
#if NETFRAMEWORK
using System.Deployment.Application;
#endif
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using CrashReporterDotNET.DrDump;
using Application = System.Windows.Forms.Application;

namespace CrashReporterDotNET
{
    /// <summary>
    /// Set SMTP server details and receiver email fields of this class instance to send crash reports directly in your inbox.
    /// </summary>
    [Serializable]
    public class ReportCrash
    {
        /// <summary>
        /// Set it to true if you want to send whole crash report silently.
        /// </summary>
        public bool Silent = false;

        /// <summary>
        /// Set it to true if you want to show screenshot tab.
        /// </summary>
        public bool ShowScreenshotTab = false;

        /// <summary>
        /// Gets or Sets name or IP address of the Host used for SMTP transactions.
        /// </summary>
        public String SmtpHost;

        /// <summary>
        /// Specify whether the SMTP client uses the Secure Socket Layer (SSL) to encrypt the connection.
        /// </summary>
        public Boolean EnableSSL;

        /// <summary>
        /// Gets or Sets the port used for SMTP transactions.
        /// </summary>
        public int Port = 25;

        /// <summary>
        /// Gets or Sets the username used for SMTP transactions.
        /// </summary>
        public String UserName = "";

        /// <summary>
        /// Gets or Sets the password used for SMTP transactions. 
        /// </summary>
        public String Password = "";

        /// <summary>
        /// Gets or Sets email address where you want to receive crash reports.
        /// </summary>
        public String ToEmail;

        /// <summary>
        /// Gets or Sets email address used by crash reporter if user don't provide her email address.
        /// </summary>
        public String FromEmail;

        /// <summary>
        /// Gets or Sets exception that occur during application execution.
        /// </summary>
        public Exception Exception;

        /// <summary>
        /// Specify whether CrashReporter.NET should take screen shot of whole screen or not.
        /// </summary>
        public bool CaptureScreen = false;

        /// <summary>
        /// Gets or Sets custom message developer wants to send. It can be something like value of variables or other details you want to send.
        /// </summary>
        public String DeveloperMessage = "";

        /// <summary>
        ///  Gets or Sets if email is required to send the crash report.
        /// </summary>
        public bool EmailRequired = false;

        /// <summary>
        /// Gets or Sets "Include screenshot" start value.
        /// </summary>
        public bool IncludeScreenshot = true;

        /// <summary>
        /// Specify whether CrashReporter.NET should send crash reports only for new problems (duplicates detected by Doctor Dump free cloud service).
        /// </summary>
        public bool AnalyzeWithDoctorDump = true;

        /// <summary>
        /// Specify a proxy for a web request.
        /// </summary>
        public IWebProxy WebProxy;

        /// <summary>
        /// Specify Doctor Dump processing settings. Used only when AnalyzeWithDoctorDump is true.
        /// </summary>
        [NonSerialized]
        public DoctorDumpSettings DoctorDumpSettings = new DoctorDumpSettings();

        internal string ApplicationTitle;

        internal string ApplicationVersion;

        /// <summary>
        /// Version of the entry assembly, which Doctor Dump uses to identify the application version.
        /// Can differ from <see cref="ApplicationVersion"/>, which is the ClickOnce version when deployed with ClickOnce.
        /// </summary>
        internal string ApplicationAssemblyVersion;

        internal byte[] ScreenShotBinary;

        internal DateTime? CrashDateUtc;

        /// <summary>
        /// Maximum time to deliver one failed report when retrying (<see cref="RetryFailedReportsAsync"/>).
        /// A report that is not delivered in time stays queued, and retrying stops until the next attempt.
        /// </summary>
        public TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(100);

        /// <summary>
        /// Creates the Doctor Dump service used for retries. Replaced by tests.
        /// </summary>
        [NonSerialized]
        internal Func<DrDumpService> DrDumpServiceFactory;

        /// <summary>
        /// Opens the Doctor Dump problem page. Replaced by tests.
        /// </summary>
        [NonSerialized]
        internal Action<string> ReportUrlOpener = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        [NonSerialized]
        private DrDumpService _doctorDumpService;

        /// <summary>
        /// Object use to send exception report to your Inbox.
        /// </summary>
        /// <param name="toEmail">Email where you want to receive crash reports.</param>
        public ReportCrash(string toEmail)
        {
            ToEmail = toEmail;
        }
        /// <summary>
        /// Save the exception to the user's temporary directory for later retry.
        /// Reports are kept per application in %TEMP%\CrashReporterNET\&lt;application name&gt;.
        /// </summary>
        public void SaveFailedReport()
        {
            SaveFailedReport(null, null, IncludeScreenshot);
        }

        internal void SaveFailedReport(string userEmail, string userMessage, bool includeScreenshot)
        {
            if (ApplicationTitle == null || ApplicationVersion == null)
                CaptureApplicationInfo();

            var directory = FailedReportQueue.GetDirectory();
            var path = Path.Combine(directory.FullName, FailedReportQueue.CreateFileName(DateTime.UtcNow));
            new FailedReport
            {
                FormatVersion = FailedReport.CurrentFormatVersion,
                Exception = ExceptionData.FromException(Exception),
                ScreenShot = ScreenShotBinary,
                ApplicationTitle = ApplicationTitle,
                ApplicationVersion = ApplicationVersion,
                ApplicationAssemblyVersion = ApplicationAssemblyVersion,
                DeveloperMessage = DeveloperMessage,
                UserEmail = userEmail,
                UserMessage = userMessage,
                IncludeScreenshot = includeScreenshot,
                CrashDateUtc = CrashDateUtc ?? DateTime.UtcNow
            }.Save(path);
        }

        /// <summary>
        /// Retries any previously failed report silently. If the first fails, it will stop.
        /// This method blocks until all reports are sent; use <see cref="RetryFailedReportsAsync"/> to avoid blocking the UI thread.
        /// </summary>
        /// <returns>Whether any report has been sent.</returns>
        public bool RetryFailedReports() => RetryFailedReports(out _, out _);

        /// <summary>
        /// Retries any previously failed report silently. If the first fails, it will stop.
        /// This method blocks until all reports are sent; use <see cref="RetryFailedReportsAsync"/> to avoid blocking the UI thread.
        /// </summary>
        /// <param name="failedReports">The amount of failed reports found.</param>
        /// <param name="failedReportsSent">The amount of failed reports sent.</param>
        /// <returns>Whether any report has been sent.</returns>
        public bool RetryFailedReports(out int failedReports, out int failedReportsSent)
        {
            // RetryFailedReportsAsync runs on the thread pool, so blocking on it cannot deadlock a UI thread.
            var result = RetryFailedReportsAsync(CancellationToken.None).GetAwaiter().GetResult();
            failedReports = result.FailedReports;
            failedReportsSent = result.FailedReportsSent;
            return result.AnyReportSent;
        }

        /// <summary>
        /// Retries any previously failed report of this application in the background, without blocking the calling thread.
        /// Each report is sent exactly as it was saved (same exception, screenshot, messages and application version).
        /// Retrying stops at the first report that fails to send, or is not delivered within <see cref="DeliveryTimeout"/>;
        /// it stays queued for the next attempt.
        /// Only one retry per application runs at a time, also across processes: if another retry is already in progress,
        /// this call returns immediately without sending anything, so no report is ever sent twice.
        /// </summary>
        /// <param name="cancellationToken">Cancels the retry. Reports that were not sent stay queued.</param>
        /// <returns>The number of failed reports found and sent.</returns>
        /// <exception cref="OperationCanceledException">The retry was cancelled.</exception>
        public Task<FailedReportsRetryResult> RetryFailedReportsAsync(CancellationToken cancellationToken = default)
        {
            // All work, including reading the queue, runs on the thread pool: nothing blocks or is posted back to the
            // calling (UI) thread, so it is safe to call from e.g. Application.OnStartup without awaiting it.
            return Task.Run(() => RetryFailedReportsCoreAsync(cancellationToken), cancellationToken);
        }

        private async Task<FailedReportsRetryResult> RetryFailedReportsCoreAsync(CancellationToken cancellationToken)
        {
            var directory = FailedReportQueue.GetDirectory();
            if (!directory.Exists)
                return new FailedReportsRetryResult(0, 0);

            var queued = new List<KeyValuePair<FileInfo, FailedReport>>();
            IDisposable queueLock = null;
            try
            {
                // Exclusive for the whole retry (across processes too), and released by the OS if the process dies.
                queueLock = FailedReportQueue.TryLock(directory);
                if (queueLock == null)
                    return new FailedReportsRetryResult(0, 0);

                foreach (var file in FailedReportQueue.GetFiles(directory))
                {
                    var report = LoadFailedReport(file);
                    if (report != null)
                        queued.Add(new KeyValuePair<FileInfo, FailedReport>(file, report));
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                // The queue can't be read right now (e.g. access denied); the reports stay queued for the next attempt.
                Debug.WriteLine(e);
                queueLock?.Dispose();
                return new FailedReportsRetryResult(0, 0);
            }

            using (queueLock)
            {
                var sent = 0;
                foreach (var entry in queued)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string reportUrl;
                    using (var delivery = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        delivery.CancelAfter(DeliveryTimeout);
                        try
                        {
                            reportUrl = await SendFailedReportAsync(entry.Value, delivery.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception)
                        {
                            // Delivery failed or timed out: keep this and all later reports for the next attempt.
                            break;
                        }
                    }

                    // The report is delivered: remove it before doing anything optional, so it is never sent twice.
                    sent++;
                    TryDelete(entry.Key);
                    TryOpenReportInBrowser(reportUrl);
                }

                return new FailedReportsRetryResult(queued.Count, sent);
            }
        }

        private static FailedReport LoadFailedReport(FileInfo file)
        {
            FailedReport report;
            try
            {
                report = FailedReport.Load(file.FullName);
            }
            catch (Exception e) when (e is SerializationException || e is XmlException)
            {
                report = null;
            }
            catch (IOException)
            {
                // Locked by another process of the same application; try again next time.
                return null;
            }

            if (report?.Exception == null)
            {
                // Corrupt or empty report: it can never be sent.
                TryDelete(file);
                return null;
            }

            return report;
        }

        private static void TryDelete(FileInfo file)
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Sends a saved report as it was captured, without capturing a new screenshot or changing this instance.
        /// </summary>
        /// <returns>The Doctor Dump problem page, or null.</returns>
        private async Task<string> SendFailedReportAsync(FailedReport report, CancellationToken cancellationToken)
        {
            var exception = report.Exception.ToException();
            var isCurrentFormat = report.FormatVersion >= 2;
            var developerMessage = isCurrentFormat ? report.DeveloperMessage : DeveloperMessage;
            var includeScreenshot = report.IncludeScreenshot ?? IncludeScreenshot;
            var screenshot = includeScreenshot && report.ScreenShot?.Length > 0 ? report.ScreenShot : null;
            var from = !string.IsNullOrEmpty(report.UserEmail) ? report.UserEmail : FromEmail;

            if (AnalyzeWithDoctorDump)
            {
                // The saved application identity is reported, not the (possibly upgraded) running application.
                var service = DrDumpServiceFactory?.Invoke() ?? new DrDumpService(WebProxy);
                return await service.SendReportAsync(exception, ToEmail,
                    DoctorDumpSettings?.ApplicationID, developerMessage, from, report.UserMessage, screenshot,
                    report.CrashDateUtc, report.ApplicationTitle, report.ApplicationAssemblyVersion,
                    cancellationToken).ConfigureAwait(false);
            }

            ValidateSmtpSettings();
            string applicationTitle = report.ApplicationTitle, applicationVersion = report.ApplicationVersion;
            if (applicationTitle == null || applicationVersion == null)
            {
                GetApplicationInfo(out var currentTitle, out var currentVersion);
                applicationTitle = applicationTitle ?? currentTitle;
                applicationVersion = applicationVersion ?? currentVersion;
            }

            var subject = string.IsNullOrEmpty(report.UserEmail)
                ? $"{applicationTitle} {applicationVersion} Crash Report"
                : $"{applicationTitle} {applicationVersion} Crash Report by {report.UserEmail}";
            var html = CreateHtmlReport(applicationTitle, applicationVersion, exception, developerMessage,
                report.UserMessage, null);

            using (var smtpClient = CreateSmtpClient())
            using (var message = CreateMailMessage(subject, html, screenshot))
            {
                await SendMailAsync(smtpClient, message, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        /// <summary>
        /// Sends the message and aborts the delivery when <paramref name="cancellationToken"/> is cancelled,
        /// including while connecting or waiting for the server.
        /// </summary>
        internal static async Task SendMailAsync(SmtpClient smtpClient, MailMessage message, CancellationToken cancellationToken)
        {
#if NETFRAMEWORK
            // SendMailAsync on .NET Framework has no cancellation: use SendAsync and SendAsyncCancel instead.
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SendCompletedEventHandler completed = null;
            completed = (sender, e) =>
            {
                smtpClient.SendCompleted -= completed;
                if (e.Cancelled)
                    completion.TrySetCanceled(cancellationToken);
                else if (e.Error != null)
                    completion.TrySetException(e.Error);
                else
                    completion.TrySetResult(true);
            };
            smtpClient.SendCompleted += completed;
            try
            {
                smtpClient.SendAsync(message, null);
            }
            catch
            {
                smtpClient.SendCompleted -= completed;
                throw;
            }

            // Registered after the send started: a cancellation that already happened runs the callback immediately.
            // The task is cancelled right away, because SendAsyncCancel does not interrupt a send that is still connecting
            // (e.g. waiting for the server greeting). The caller then disposes the SmtpClient, which aborts the pending
            // operation and closes its connection. Both calls are ignored if the send completed in the meantime.
            using (cancellationToken.Register(() =>
                   {
                       completion.TrySetCanceled(cancellationToken);
                       try
                       {
                           smtpClient.SendAsyncCancel();
                       }
                       catch (ObjectDisposedException)
                       {
                       }
                   }))
            {
                await completion.Task.ConfigureAwait(false);
            }
#else
            await smtpClient.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
#endif
        }

        /// <summary>
        /// Sends exception report silently to receiver email address provided in ToEmail.
        /// </summary>
        /// <param name="exception">Exception object that contains details of the exception.</param>
        public void SendSilently(Exception exception)
        {
            Send(exception, true);
        }

        /// <summary>
        /// Sends exception report directly to receiver email address provided in ToEmail.
        /// </summary>
        /// <param name="exception">Exception object that contains details of the exception.</param>
        public void Send(Exception exception)
        {
            Send(exception, Silent);
        }

        private void Send(Exception exception, bool silent)
        {
            Exception = exception;
            CrashDateUtc = DateTime.UtcNow;
            CaptureApplicationInfo();
            try
            {
                if (CaptureScreen)
                    ScreenShotBinary = CaptureScreenshot.CaptureScreen(ImageFormat.Png);
                else
                    ScreenShotBinary = CaptureScreenshot.CaptureActiveWindow(ImageFormat.Png);
            }
            catch (Exception e)
            {
                Debug.Write(e.Message);
            }
            
            if (!AnalyzeWithDoctorDump)
            {
                ValidateSmtpSettings();
            }

            if (!Application.MessageLoop)
            {
                Application.EnableVisualStyles();
            }

            if (silent)
            {
                SendReport(IncludeScreenshot);
            }
            else
            {
                if (Thread.CurrentThread.GetApartmentState().Equals(ApartmentState.MTA))
                {
                    var thread = new Thread(() => new CrashReport(this).ShowDialog()) { IsBackground = false };
                    thread.CurrentCulture = thread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join();
                }
                else
                {
                    new CrashReport(this).ShowDialog();
                }
            }
        }

        private void ValidateSmtpSettings()
        {
            if (string.IsNullOrEmpty(FromEmail))
            {
                throw new ArgumentNullException(@"FromEmail");
            }

            if (string.IsNullOrEmpty(SmtpHost))
            {
                throw new ArgumentNullException("SmtpHost");
            }
        }

        /// <summary>
        /// Whether the crash dialog should send an anonymous report to Doctor Dump as soon as it opens.
        /// Never when reports are delivered by SMTP only.
        /// </summary>
        internal bool SendAnonymousReportWhenDialogOpens =>
            AnalyzeWithDoctorDump && DoctorDumpSettings != null && DoctorDumpSettings.SendAnonymousReportSilently;

        private void CaptureApplicationInfo()
        {
            GetApplicationInfo(out ApplicationTitle, out ApplicationVersion);
            ApplicationAssemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
        }

        private static void GetApplicationInfo(out string title, out string version)
        {
            var mainAssembly = Assembly.GetEntryAssembly();
            if (mainAssembly == null)
            {
                title = FailedReportQueue.GetApplicationName();
                version = GetClickOnceVersion() ?? string.Empty;
                return;
            }

            string appTitle = null;
            var attributes = mainAssembly.GetCustomAttributes(typeof(AssemblyTitleAttribute), true);
            if (attributes.Length > 0)
            {
                appTitle = ((AssemblyTitleAttribute)attributes[0]).Title;
            }

            title = !string.IsNullOrEmpty(appTitle) ? appTitle : mainAssembly.GetName().Name;
            version = GetClickOnceVersion() ?? mainAssembly.GetName().Version.ToString();
        }

        private void OpenReportInBrowser(string reportUrl)
        {
            if (DoctorDumpSettings != null && DoctorDumpSettings.OpenReportInBrowser && !string.IsNullOrEmpty(reportUrl))
                ReportUrlOpener(reportUrl);
        }

        /// <summary>
        /// Opening the problem page is optional: failing to do so (e.g. no default browser) never affects delivery.
        /// </summary>
        private void TryOpenReportInBrowser(string reportUrl)
        {
            try
            {
                OpenReportInBrowser(reportUrl);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }

        private static string GetClickOnceVersion()
        {
#if NETFRAMEWORK
            return Type.GetType("Mono.Runtime") == null && ApplicationDeployment.IsNetworkDeployed
                ? ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString()
                : null;
#else
            // ClickOnce for .NET 5+ exposes deployment information through environment variables.
            return bool.TryParse(Environment.GetEnvironmentVariable("ClickOnce_IsNetworkDeployed"), out var isNetworkDeployed) &&
                   isNetworkDeployed
                ? Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion")
                : null;
#endif
        }

        internal void SendReport(bool includeScreenshot,
            DrDumpService.SendRequestCompletedEventHandler sendRequestCompleted = null,
            SendCompletedEventHandler smtpClientSendCompleted = null, Control form = null, string from = "",
            string userMessage = "")
        {
            string subject = String.Empty;
            
            if (string.IsNullOrEmpty(from))
            {
                from = !string.IsNullOrEmpty(FromEmail)
                    ? FromEmail
                    : null;
            }
            else
            {
                subject = $"{ApplicationTitle} {ApplicationVersion} Crash Report by {from}";
            }

            if (AnalyzeWithDoctorDump)
            {
                SendFullReport(includeScreenshot, sendRequestCompleted, form, from, userMessage);
            }
            else
            {
                SendEmail(includeScreenshot, smtpClientSendCompleted, subject, userMessage);
            }
        }

        #region Send Email Using SMTP

        private void SendEmail(bool includeScreenshot, SendCompletedEventHandler smtpClientSendCompleted, string subject, string userMessage)
        {
            if (string.IsNullOrEmpty(subject))
            {
                subject = $"{ApplicationTitle} {ApplicationVersion} Crash Report";
            }

            var smtpClient = CreateSmtpClient();
            var message = CreateMailMessage(subject, CreateHtmlReport(userMessage),
                includeScreenshot ? ScreenShotBinary : null);

            if (smtpClientSendCompleted != null)
            {
                try
                {
                    smtpClient.SendCompleted += smtpClientSendCompleted;
                    smtpClient.SendAsync(message, "Crash Report");
                }
                catch (SmtpException smtpException)
                {
                    smtpClientSendCompleted(this, new System.ComponentModel.AsyncCompletedEventArgs(smtpException, true, null));
                }
            }
            else
            {
                smtpClient.Send(message);
            }
        }

        private SmtpClient CreateSmtpClient()
        {
            return new SmtpClient
            {
                Host = SmtpHost,
                Port = Port,
                EnableSsl = EnableSSL,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(UserName, Password)
            };
        }

        private MailMessage CreateMailMessage(string subject, string htmlReport, byte[] screenshot)
        {
            var message = new MailMessage(new MailAddress(FromEmail), new MailAddress(ToEmail))
                {IsBodyHtml = true, Subject = subject, Body = htmlReport};

            if (screenshot?.Length > 0)
            {
                message.Attachments.Add(new Attachment(new MemoryStream(screenshot), "Screenshot.png", "image/png"));
            }

            return message;
        }

        #endregion

        #region HTML Report Generator

        /// <summary>
        /// Creates the HTML report of the current crash.
        /// </summary>
        /// <param name="userMessage">Comment entered by the user.</param>
        /// <param name="embedScreenshot">Embed the screenshot as an inline image, for reports saved to a file.
        /// E-mails attach the screenshot instead, because many mail clients block inline data images.</param>
        internal string CreateHtmlReport(string userMessage, bool embedScreenshot = false)
        {
            return CreateHtmlReport(ApplicationTitle, ApplicationVersion, Exception, DeveloperMessage, userMessage,
                embedScreenshot ? ScreenShotBinary : null);
        }

        internal static string CreateHtmlReport(string applicationTitle, string applicationVersion, Exception exception,
            string developerMessage, string userMessage, byte[] embeddedScreenshot)
        {
            string report =
                string.Format(
                    @"<!DOCTYPE html PUBLIC ""-//W3C//DTD XHTML 1.0 Transitional//EN"" ""http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd"">
                    <html xmlns=""http://www.w3.org/1999/xhtml"">
                    <head>
                    <meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"" />
                    <title>{0} {1} Crash Report</title>
                    <style type=""text/css"">
                    .message {{
                    padding-top:5px;
                    padding-bottom:5px;
                    padding-right:20px;
                    padding-left:20px;
                    font-family:Sans-serif;
                    }}
                    .content
                    {{
                    border-style:dashed;
                    border-width:1px;
                    }}
                    .title
                    {{
                    padding-top:1px;
                    padding-bottom:1px;
                    padding-right:10px;
                    padding-left:10px;
                    font-family:Arial;
                    }}
                    </style>
                    </head>
                    <body>
                    <div class=""title"" style=""background-color: #FFCC99"">
                    <h2>{0} {1} Crash Report</h2>
                    </div>
                    <br/>
                    <div class=""content"">
                    <div class=""title"" style=""background-color: #66CCFF;"">
                    <h3>Windows Version</h3>
                    </div>
                    <div class=""message"">
                    <p>{2}</p>
                    </div>
                    </div>
                    <br/>
                    <div class=""content"">
                    <div class=""title"" style=""background-color: #66CCFF;"">
                    <h3>CLR Version</h3>
                    </div>
                    <div class=""message"">
                    <p>{3}</p>
                    </div>
                    </div>
                    <br/>    
                    <div class=""content"">
                    <div class=""title"" style=""background-color: #66CCFF;"">
                    <h3>Exception</h3>
                    </div>
                    <div class=""message"">
                    {4}
                    </div>
                    </div>", WebUtility.HtmlEncode(applicationTitle),
                    WebUtility.HtmlEncode(applicationVersion),
                    WebUtility.HtmlEncode(HelperMethods.GetWindowsVersion()),
                    WebUtility.HtmlEncode(Environment.Version.ToString()),
                    CreateReport(exception));
            if (!String.IsNullOrEmpty(userMessage))
            {
                report += $@"<br/>
                            <div class=""content"">
                            <div class=""title"" style=""background-color: #66FF99;"">
                            <h3>User Comment</h3>
                            </div>
                            <div class=""message"">
                            <p>{WebUtility.HtmlEncode(userMessage)}</p>
                            </div>
                            </div>";
            }

            if (!String.IsNullOrEmpty(developerMessage?.Trim()))
            {
                report += $@"<br/>
                            <div class=""content"">
                            <div class=""title"" style=""background-color: #66FF99;"">
                            <h3>Developer Message</h3>
                            </div>
                            <div class=""message"">
                            <p>{WebUtility.HtmlEncode(developerMessage.Trim())}</p>
                            </div>
                            </div>";
            }

            if (embeddedScreenshot?.Length > 0)
            {
                report += $@"<br/>
                            <div class=""content"">
                            <div class=""title"" style=""background-color: #66CCFF;"">
                            <h3>Screenshot</h3>
                            </div>
                            <div class=""message"">
                            <p><img alt=""Screenshot"" style=""max-width: 100%;"" src=""data:image/png;base64,{Convert.ToBase64String(embeddedScreenshot)}"" /></p>
                            </div>
                            </div>";
            }

            report += "</body></html>";
            return report;
        }

        private static string CreateReport(Exception exception)
        {
            string report = $@"<br/>
                        <div class=""content"">
                        <div class=""title"" style=""background-color: #66CCFF;"">
                        <h3>Exception Type</h3>
                        </div>
                        <div class=""message"">
                        <p>{WebUtility.HtmlEncode(ReplayedException.GetTypeName(exception))}</p>
                        </div>
                        </div><br/>
                        <div class=""content"">
                        <div class=""title"" style=""background-color: #66CCFF;"">
                        <h3>Error Message</h3>
                        </div>
                        <div class=""message"">
                        <p>{WebUtility.HtmlEncode(exception.Message)}</p>
                        </div>
                        </div><br/>
                        <div class=""content"">
                        <div class=""title"" style=""background-color: #66CCFF;"">
                        <h3>Source</h3>
                        </div>
                        <div class=""message"">
                        <p>{WebUtility.HtmlEncode(exception.Source ?? "No source")}</p>
                        </div>
                        </div><br/>
                        <div class=""content"">
                        <div class=""title"" style=""background-color: #66CCFF;"">
                        <h3>Stack Trace</h3>
                        </div>
                        <div class=""message"">
                        <p>{
                    WebUtility.HtmlEncode(exception.StackTrace ?? "No stack trace").Replace("\r\n", "\n").Replace("\n", "<br/>")
                }</p>
                        </div>
                        </div>";
            if (exception.InnerException != null)
            {
                report += $@"<br/>
                        <div class=""content"">
                        <div class=""title"" style=""background-color: #66CCFF;"">
                        <h3>Inner Exception</h3>
                        </div>
                        <div class=""message"">
                        {CreateReport(exception.InnerException)}
                        </div>
                        </div>";
            }

            report += "<br/>";
            return report;
        }

        #endregion

        #region DrDump Functions

        internal void SendAnonymousReport(DrDumpService.SendRequestCompletedEventHandler sendRequestCompleted)
        {
            try
            {
                _doctorDumpService = new DrDumpService(WebProxy);

                _doctorDumpService.SendRequestCompleted += sendRequestCompleted;

                _doctorDumpService.SendAnonymousReportAsync(
                    Exception,
                    ToEmail,
                    DoctorDumpSettings?.ApplicationID);
            }
            catch (SocketException)
            {
                _doctorDumpService = null;
            }
        }

        private void SendFullReport(bool includeScreenshot,
            DrDumpService.SendRequestCompletedEventHandler sendRequestCompleted, Control form, string from,
            string userMessage)
        {
            byte[] screenshot = null;
            if (ScreenShotBinary?.Length > 0 && includeScreenshot)
                screenshot = ScreenShotBinary;

            if (sendRequestCompleted != null)
            {
                if (_doctorDumpService == null)
                {
                    SendAnonymousReport(sendRequestCompleted);
                }

                if (_doctorDumpService == null)
                {
                    throw new SocketException();
                }

                _doctorDumpService.SendAdditionalDataAsync(form, DeveloperMessage, from,
                    userMessage, screenshot);
            }
            else
            {
                _doctorDumpService = new DrDumpService(WebProxy);
                var reportUrl = _doctorDumpService.SendReportSilently(Exception, ToEmail, DoctorDumpSettings?.ApplicationID, DeveloperMessage, from, userMessage, screenshot);
                OpenReportInBrowser(reportUrl);
            }
        }

        #endregion
    }

    /// <summary>
    /// Result of <see cref="ReportCrash.RetryFailedReportsAsync"/>.
    /// </summary>
    public sealed class FailedReportsRetryResult
    {
        internal FailedReportsRetryResult(int failedReports, int failedReportsSent)
        {
            FailedReports = failedReports;
            FailedReportsSent = failedReportsSent;
        }

        /// <summary>
        /// The amount of failed reports found.
        /// </summary>
        public int FailedReports { get; }

        /// <summary>
        /// The amount of failed reports sent (and removed from the queue).
        /// </summary>
        public int FailedReportsSent { get; }

        /// <summary>
        /// Whether any report has been sent.
        /// </summary>
        public bool AnyReportSent => FailedReportsSent > 0;
    }

    /// <summary>
    /// Set Doctor Dump processing settings.
    /// </summary>
    public class DoctorDumpSettings
    {
        /// <summary>
        /// Gets or Sets application ID.
        /// </summary>
        public Guid? ApplicationID;

        /// <summary>
        /// Specify whether CrashReporter.NET should send anonymous crash report to Doctor Dump that doesn't contain private information.
        /// Only about 1/10 of users press "Send" button on crash reporting dialogs. And even less if there are required fields to fill.
        /// Without sending anonymous reports most of the problems are hidden from the developer.
        /// </summary>
        public bool SendAnonymousReportSilently = true;

        /// <summary>
        /// Specify whether CrashReporter.NET should open the web page in browser about crash report that contains report ID and may contain steps to fix the problem.
        /// </summary>
        public bool OpenReportInBrowser = true;
    }
}
