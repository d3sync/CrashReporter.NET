using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CrashReporterDotNET.com.drdump;
using CrashReporterDotNET.DrDump;
using Xunit;

namespace CrashReporterDotNET.Tests
{
    public sealed class DrDumpSoapClientTests
    {
        private const string Namespace = "https://www.drdump.com/services";

        private static string Envelope(string body) =>
            "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body>" + body + "</s:Body></s:Envelope>";

        private static string AnonymousResult(string type, string extra = "") => Envelope(
            $"<SendAnonymousReportResponse xmlns=\"{Namespace}\"><SendAnonymousReportResult xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\" i:type=\"{type}\">" +
            $"<ClientID>cid</ClientID><Context>AQIDBA==</Context><UrlToProblem>https://drdump.com/p/1</UrlToProblem>{extra}</SendAnonymousReportResult></SendAnonymousReportResponse>");

        private static readonly string Fault = Envelope(
            "<s:Fault><s:Code><s:Value>s:Receiver</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en-US\">Server is sad</s:Text></s:Reason></s:Fault>");

        private static DrDumpSoapClient CreateClient(FakeHttpHandler handler) =>
            new DrDumpSoapClient(new HttpClient(handler)) { Url = "https://drdump.test/Service/CrashReporterReportUploader.svc" };

        private static Task<Response> SendAnonymous(DrDumpSoapClient client, CancellationToken cancellationToken = default) =>
            client.SendAnonymousReportAsync(new ClientLib { V1 = 2, V2 = 1 },
                new Application { AppName = "App", MainModule = "App", Email = "a@b.c" },
                new ExceptionDescription { ClrVersion = "10.0", OS = "os", CrashDate = DateTime.UtcNow },
                cancellationToken);

        [Fact]
        public async Task SuccessfulResponse_IsParsed()
        {
            var handler = new FakeHttpHandler(HttpStatusCode.OK, AnonymousResult("NeedReportResponse"));

            var response = await SendAnonymous(CreateClient(handler));

            Assert.IsType<NeedReportResponse>(response);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, response.Context);
            Assert.Equal("https://drdump.com/p/1", response.UrlToProblem);
            Assert.StartsWith("application/soap+xml", handler.LastRequestContentType);
            Assert.Contains("SendAnonymousReport\"", handler.LastRequestContentType);
        }

        [Fact]
        public async Task ErrorResponse_IsReturnedAsTypedResult()
        {
            var handler = new FakeHttpHandler(HttpStatusCode.OK, AnonymousResult("ErrorResponse", "<Error>Valid e-mail is required</Error>"));

            var response = await SendAnonymous(CreateClient(handler));

            Assert.Equal("Valid e-mail is required", Assert.IsType<ErrorResponse>(response).Error);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.OK)]
        public async Task SoapFault_IsReportedWithItsReason(HttpStatusCode statusCode)
        {
            var handler = new FakeHttpHandler(statusCode, Fault);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAnonymous(CreateClient(handler)));

            Assert.Equal("Server is sad", error.Message);
        }

        [Fact]
        public async Task UnsuccessfulStatus_WithValidEnvelope_IsRejected()
        {
            var handler = new FakeHttpHandler(HttpStatusCode.InternalServerError, AnonymousResult("StopResponse"));

            var error = await Assert.ThrowsAsync<HttpRequestException>(() => SendAnonymous(CreateClient(handler)));

            Assert.Contains("HTTP 500", error.Message);
        }

        [Fact]
        public async Task UnsuccessfulStatus_WithNonSoapBody_IsRejected()
        {
            var handler = new FakeHttpHandler(HttpStatusCode.BadGateway, "<html><body>Bad gateway</body></html>", "text/html");

            var error = await Assert.ThrowsAsync<HttpRequestException>(() => SendAnonymous(CreateClient(handler)));

            Assert.Contains("HTTP 502", error.Message);
        }

        [Fact]
        public async Task NilResult_IsRejected()
        {
            var handler = new FakeHttpHandler(HttpStatusCode.OK, Envelope(
                $"<SendAnonymousReportResponse xmlns=\"{Namespace}\"><SendAnonymousReportResult xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\" i:nil=\"true\" /></SendAnonymousReportResponse>"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAnonymous(CreateClient(handler)));

            Assert.Contains("no result", error.Message);
        }

        [Fact]
        public async Task MissingResult_IsRejected()
        {
            var handler = new FakeHttpHandler(HttpStatusCode.OK, Envelope($"<SendAnonymousReportResponse xmlns=\"{Namespace}\" />"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => SendAnonymous(CreateClient(handler)));
        }

        [Fact]
        public async Task MalformedSuccessfulResponse_IsRejected()
        {
            var handler = new FakeHttpHandler(HttpStatusCode.OK, "this is not xml");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAnonymous(CreateClient(handler)));

            Assert.Contains("invalid response", error.Message);
        }

        [Fact]
        public async Task Cancellation_AbortsTheRequest()
        {
            var handler = new FakeHttpHandler(async (request, token) =>
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAnonymous(CreateClient(handler), cancellation.Token));
            }
        }

        [Fact]
        public async Task InvalidXmlCharactersInReport_AreReplacedBeforeSending()
        {
            Exception exception;
            try
            {
                throw new InvalidOperationException("bad \u0001 char \uD800 and ￿");
            }
            catch (Exception e)
            {
                exception = e;
            }

            var state = new SendRequestState
            {
                AnonymousData = new AnonymousData { Exception = exception, ToEmail = "a@b.c" },
                PrivateData = new PrivateData { UserMessage = "user \u0002 message", DeveloperMessage = "dev", UserEmail = "u@x.y" }
            };
            var handler = new FakeHttpHandler(HttpStatusCode.OK, Envelope(
                $"<SendAdditionalDataResponse xmlns=\"{Namespace}\"><SendAdditionalDataResult xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\" i:type=\"StopResponse\"><UrlToProblem>u</UrlToProblem></SendAdditionalDataResult></SendAdditionalDataResponse>"));

            var response = await CreateClient(handler).SendAdditionalDataAsync(new byte[] { 1 }, state.GetDetailedExceptionDescription());

            Assert.IsType<StopResponse>(response);
            Assert.Contains("bad � char � and �", handler.LastRequestBody);
            Assert.Contains("user � message", handler.LastRequestBody);
        }

        [Fact]
        public void HttpClients_AreSharedPerProxy()
        {
            var field = typeof(DrDumpSoapClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
            var proxy = new WebProxy("http://proxy.test:8080");

            var defaultA = field.GetValue(new DrDumpSoapClient());
            var defaultB = field.GetValue(new DrDumpSoapClient());
            var proxyA = field.GetValue(new DrDumpSoapClient(proxy));
            var proxyB = field.GetValue(new DrDumpSoapClient(proxy));
            var otherProxy = field.GetValue(new DrDumpSoapClient(new WebProxy("http://other.test:8080")));

            Assert.Same(defaultA, defaultB);
            Assert.Same(proxyA, proxyB);
            Assert.NotSame(defaultA, proxyA);
            Assert.NotSame(proxyA, otherProxy);
        }
    }
}
