using System;
using System.ComponentModel;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using CrashReporterDotNET.com.drdump;

namespace CrashReporterDotNET.DrDump
{
    internal class DrDumpService
    {
        public delegate void SendRequestCompletedEventHandler(object sender, SendRequestCompletedEventArgs args);

        public event SendRequestCompletedEventHandler SendRequestCompleted;

        private SendRequestState _sendRequestState;

        private readonly DrDumpSoapClient _uploader;

        public DrDumpService(IWebProxy webProxy = null)
        {
            _uploader = new DrDumpSoapClient(webProxy);

            var configOverride =
                Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Idol Software\DumpUploader",
                    "ServiceURL", null) as string;
            if (!string.IsNullOrEmpty(configOverride))
            {
                var t = new Uri(configOverride);
                var newUrl = new UriBuilder(_uploader.Url)
                {
                    Scheme = t.Scheme,
                    Host = t.Host,
                    Port = t.Port
                };
                _uploader.Url = newUrl.ToString();
            }
        }

        public void SendAnonymousReportAsync(Exception exception, string toEmail, Guid? applicationId)
        {
            _sendRequestState = new SendRequestState
            {
                AnonymousData = new AnonymousData
                {
                    Exception = exception,
                    ToEmail = toEmail,
                    ApplicationID = applicationId
                }
            };

            var state = _sendRequestState;
            var clientLib = SendRequestState.GetClientLib();
            var application = state.GetApplication();
            RunAsync(() => _uploader.SendAnonymousReportAsync(clientLib, application,
                    state.GetExceptionDescription(true)),
                result => OnSendAnonymousReportCompleted(state, result));
        }

        public string SendReportSilently(Exception exception, string toEmail, Guid? applicationId, string developerMessage, string from,
            string userMessage, byte[] screenshot)
        {
            _sendRequestState = new SendRequestState
            {
                AnonymousData = new AnonymousData
                {
                    Exception = exception,
                    ToEmail = toEmail,
                    ApplicationID = applicationId
                },
                PrivateData = new PrivateData
                {
                    UserEmail = from,
                    UserMessage = userMessage,
                    DeveloperMessage = developerMessage,
                    Screenshot = screenshot
                }
            };

            var clientLib = SendRequestState.GetClientLib();
            var application = _sendRequestState.GetApplication();
            var exceptionDescription = _sendRequestState.GetExceptionDescription(true);
            var response = RunSynchronously(() =>
                _uploader.SendAnonymousReportAsync(clientLib, application, exceptionDescription));
            if (response is ErrorResponse errorResponse)
                throw new Exception(errorResponse.Error);

            if (response is NeedReportResponse)
            {
                var detailedExceptionDescription = _sendRequestState.GetDetailedExceptionDescription();
                var additionalDataResponse = RunSynchronously(() =>
                    _uploader.SendAdditionalDataAsync(response.Context, detailedExceptionDescription));
                if (additionalDataResponse is ErrorResponse errorAdditionalDataResponse)
                    throw new Exception(errorAdditionalDataResponse.Error);
                return additionalDataResponse.UrlToProblem;
            }

            return response.UrlToProblem;
        }

        public void SendAdditionalDataAsync(Control control, string developerMessage, string userEmail,
            string userMessage, byte[] screenshot)
        {
            bool needToSend;
            lock (_sendRequestState)
            {
                _sendRequestState.PrivateData = new PrivateData
                {
                    UserEmail = userEmail,
                    UserMessage = userMessage,
                    DeveloperMessage = developerMessage,
                    Screenshot = screenshot
                };

                needToSend = _sendRequestState.SendAnonymousReportResult != null;
            }

            if (needToSend)
                SendAdditionalDataAsync(control, _sendRequestState);
        }

        private void SendAdditionalDataAsync(Control control, SendRequestState sendRequestState)
        {
            SendRequestCompletedEventArgs e;
            try
            {
                var res = sendRequestState.SendAnonymousReportResult;
                if (res.Error != null || res.Cancelled)
                {
                    e = new SendRequestCompletedEventArgs(null, res.Error, res.Cancelled);
                }
                else
                {
                    Response response = res.Result;
                    if (response is ErrorResponse errorResponse)
                        throw new Exception(errorResponse.Error);

                    if (response is NeedReportResponse)
                    {
                        var detailedExceptionDescription = sendRequestState.GetDetailedExceptionDescription();
                        RunAsync(() => _uploader.SendAdditionalDataAsync(response.Context, detailedExceptionDescription),
                            OnSendAdditionalDataCompleted);
                        return;
                    }

                    e = new SendRequestCompletedEventArgs(response, null, false);
                }
            }
            catch (Exception ex)
            {
                e = new SendRequestCompletedEventArgs(null, ex, false);
            }

            if (SendRequestCompleted != null)
            {
                if (control != null)
                {
                    control.BeginInvoke(SendRequestCompleted, new object[] {this, e});
                }
                else
                {
                    SendRequestCompleted.Invoke(this, e);
                }
            }
        }

        private void OnSendAnonymousReportCompleted(SendRequestState state, RequestResult result)
        {
            bool needToSend;

            lock (state)
            {
                state.SendAnonymousReportResult = result;

                needToSend = state.PrivateData != null;
            }

            if (needToSend)
                SendAdditionalDataAsync(null, state);
        }

        private void OnSendAdditionalDataCompleted(RequestResult result)
        {
            try
            {
                if (result.Error != null || result.Cancelled)
                {
                    SendRequestCompleted?.Invoke(this, new SendRequestCompletedEventArgs(null, result.Error, result.Cancelled));
                    return;
                }

                Response response = result.Result;
                if (response is ErrorResponse errorResponse)
                    throw new Exception(errorResponse.Error);

                SendRequestCompleted?.Invoke(this, new SendRequestCompletedEventArgs(response, null, false));
            }
            catch (Exception ex)
            {
                SendRequestCompleted?.Invoke(this, new SendRequestCompletedEventArgs(null, ex, false));
            }
        }

        /// <summary>
        /// Starts the request on the thread pool and raises <paramref name="completed"/> on the synchronization context
        /// of the caller (the UI thread for WinForms/WPF), matching the behaviour of the old SoapHttpClientProtocol proxy.
        /// </summary>
        private static void RunAsync(Func<Task<Response>> request, Action<RequestResult> completed)
        {
            var asyncOperation = AsyncOperationManager.CreateOperation(null);
            Task.Run(request).ContinueWith(task =>
            {
                RequestResult result;
                if (task.IsFaulted)
                    result = new RequestResult(null, task.Exception?.GetBaseException(), false);
                else if (task.IsCanceled)
                    result = new RequestResult(null,
                        new TimeoutException("The request to the Doctor Dump service timed out."), false);
                else
                    result = new RequestResult(task.Result, null, false);

                asyncOperation.PostOperationCompleted(_ => completed(result), null);
            }, TaskScheduler.Default);
        }

        private static Response RunSynchronously(Func<Task<Response>> request)
        {
            // Task.Run avoids deadlocks when called from a thread with a synchronization context (e.g. the UI thread).
            return Task.Run(request).GetAwaiter().GetResult();
        }

        internal sealed class RequestResult
        {
            public RequestResult(Response result, Exception error, bool cancelled)
            {
                Result = result;
                Error = error;
                Cancelled = cancelled;
            }

            public Response Result { get; }

            public Exception Error { get; }

            public bool Cancelled { get; }
        }

        public class SendRequestCompletedEventArgs : AsyncCompletedEventArgs
        {
            private readonly Response _result;

            internal SendRequestCompletedEventArgs(Response result, Exception exception, bool cancelled) :
                base(exception, cancelled, null)
            {
                _result = result;
            }

            public Response Result
            {
                get
                {
                    RaiseExceptionIfNecessary();
                    return _result;
                }
            }
        }
    }
}
