using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using CrashReporterDotNET.com.drdump;

namespace CrashReporterDotNET.DrDump
{
    /// <summary>
    /// Minimal SOAP 1.2 client for the Doctor Dump CrashReporterReportUploader service.
    /// Replaces the legacy System.Web.Services "Web Reference" proxy, which is not available on .NET (Core).
    /// Messages are produced with the same XmlSerializer members mappings that SoapHttpClientProtocol used,
    /// so the wire format is unchanged.
    /// </summary>
    internal class DrDumpSoapClient
    {
        public const string DefaultUrl = "https://drdump.com/Service/CrashReporterReportUploader.svc";

        private const string ServiceNamespace = "https://www.drdump.com/services";

        private const string SoapNamespace = "http://www.w3.org/2003/05/soap-envelope";

        private const string ActionPrefix =
            "https://www.drdump.com/services/IdolSoftware.DoctorDump.CrashReporterGate.CrashReporterReportUploader/";

        private static readonly Operation SendAnonymousReportOperation = new Operation("SendAnonymousReport",
            Member("clientLib", typeof(ClientLib)),
            Member("app", typeof(Application)),
            Member("exception", typeof(ExceptionDescription)));

        private static readonly Operation SendAdditionalDataOperation = new Operation("SendAdditionalData",
            Member("context", typeof(byte[]), "base64Binary"),
            Member("addInfo", typeof(DetailedExceptionDescription)));

        // HttpClient instances are long-lived and shared, as recommended by
        // https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines.
        // One client without a custom proxy, plus one per IWebProxy instance (released together with the proxy).
        private static readonly Lazy<HttpClient> SharedClient = new Lazy<HttpClient>(() => CreateHttpClient(null));

        private static readonly ConditionalWeakTable<IWebProxy, HttpClient> ProxyClients =
            new ConditionalWeakTable<IWebProxy, HttpClient>();

        private readonly HttpClient _httpClient;

        public DrDumpSoapClient(IWebProxy webProxy = null)
            : this(webProxy == null ? SharedClient.Value : ProxyClients.GetValue(webProxy, CreateHttpClient))
        {
        }

        /// <summary>
        /// Uses the given client. The caller owns it; this class never disposes it.
        /// </summary>
        internal DrDumpSoapClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public string Url { get; set; } = DefaultUrl;

        public Task<Response> SendAnonymousReportAsync(ClientLib clientLib, Application app,
            ExceptionDescription exception, CancellationToken cancellationToken = default)
        {
            return InvokeAsync(SendAnonymousReportOperation, cancellationToken, clientLib, app, exception);
        }

        public Task<Response> SendAdditionalDataAsync(byte[] context, DetailedExceptionDescription addInfo,
            CancellationToken cancellationToken = default)
        {
            return InvokeAsync(SendAdditionalDataOperation, cancellationToken, context, addInfo);
        }

        private static HttpClient CreateHttpClient(IWebProxy webProxy)
        {
#if NETFRAMEWORK
            var handler = new HttpClientHandler();
#else
            // Recycle pooled connections periodically so DNS changes are picked up by the long-lived client.
            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
#endif
            if (webProxy != null)
            {
                handler.Proxy = webProxy;
                handler.UseProxy = true;
            }

            return new HttpClient(handler);
        }

        private async Task<Response> InvokeAsync(Operation operation, CancellationToken cancellationToken,
            params object[] parameters)
        {
            using (var content = new ByteArrayContent(operation.WriteRequest(parameters)))
            {
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                    $"application/soap+xml; charset=utf-8; action=\"{ActionPrefix}{operation.Name}\"");

                using (var httpResponse = await _httpClient.PostAsync(Url, content, cancellationToken).ConfigureAwait(false))
                {
                    var body = await httpResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    return operation.ReadResponse(body, httpResponse);
                }
            }
        }

        private static XmlReflectionMember Member(string name, Type type, string dataType = null)
        {
            var element = new XmlElementAttribute { IsNullable = true };
            if (dataType != null)
            {
                element.DataType = dataType;
            }

            var attributes = new XmlAttributes();
            attributes.XmlElements.Add(element);
            return new XmlReflectionMember { MemberName = name, MemberType = type, XmlAttributes = attributes };
        }

        private sealed class Operation
        {
            private readonly XmlSerializer _requestSerializer;

            private readonly XmlSerializer _responseSerializer;

            public Operation(string name, params XmlReflectionMember[] parameters)
            {
                Name = name;
                var importer = new XmlReflectionImporter();
                var request = importer.ImportMembersMapping(name, ServiceNamespace, parameters, true);
                var response = importer.ImportMembersMapping(name + "Response", ServiceNamespace,
                    new[] { Member(name + "Result", typeof(Response)) }, true);
                var serializers = XmlSerializer.FromMappings(new XmlMapping[] { request, response });
                _requestSerializer = serializers[0];
                _responseSerializer = serializers[1];
            }

            public string Name { get; }

            public byte[] WriteRequest(object[] parameters)
            {
                using (var stream = new MemoryStream())
                {
                    using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
                    {
                        writer.WriteStartDocument();
                        writer.WriteStartElement("soap", "Envelope", SoapNamespace);
                        writer.WriteAttributeString("xmlns", "soap", null, SoapNamespace);
                        writer.WriteAttributeString("xmlns", "xsi", null, XmlSchema.InstanceNamespace);
                        writer.WriteAttributeString("xmlns", "xsd", null, XmlSchema.Namespace);
                        writer.WriteStartElement("Body", SoapNamespace);
                        _requestSerializer.Serialize(writer, parameters);
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                        writer.WriteEndDocument();
                    }

                    return stream.ToArray();
                }
            }

            /// <summary>
            /// Validates and reads the response:
            /// SOAP faults are reported with their reason (whatever the HTTP status), other unsuccessful HTTP statuses
            /// are reported as <see cref="HttpRequestException"/>, and a missing or nil result is rejected.
            /// </summary>
            public Response ReadResponse(byte[] body, HttpResponseMessage httpResponse)
            {
                Envelope envelope;
                try
                {
                    envelope = ReadEnvelope(body);
                }
                catch (XmlException e)
                {
                    if (!httpResponse.IsSuccessStatusCode)
                        throw CreateHttpError(httpResponse, e);
                    throw new InvalidOperationException(
                        $"The Doctor Dump service returned an invalid response to {Name}.", e);
                }

                if (envelope.IsFault)
                    throw new InvalidOperationException(string.IsNullOrEmpty(envelope.FaultReason)
                        ? $"Doctor Dump service returned a SOAP fault ({envelope.FaultCode})."
                        : envelope.FaultReason);

                if (!httpResponse.IsSuccessStatusCode)
                    throw CreateHttpError(httpResponse, null);

                if (!(envelope.Result is Response response))
                    throw new InvalidOperationException(
                        $"The Doctor Dump service returned no result for {Name}.");

                return response;
            }

            private Envelope ReadEnvelope(byte[] body)
            {
                var settings = new XmlReaderSettings
                {
                    IgnoreWhitespace = true,
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                using (var reader = XmlReader.Create(new MemoryStream(body), settings))
                {
                    reader.MoveToContent();
                    reader.ReadStartElement("Envelope", SoapNamespace);
                    reader.MoveToContent();
                    if (reader.IsStartElement("Header", SoapNamespace))
                    {
                        reader.Skip();
                        reader.MoveToContent();
                    }

                    reader.ReadStartElement("Body", SoapNamespace);
                    reader.MoveToContent();
                    if (reader.IsStartElement("Fault", SoapNamespace))
                        return ReadFault(reader);

                    object[] results;
                    try
                    {
                        results = (object[]) _responseSerializer.Deserialize(reader);
                    }
                    catch (InvalidOperationException e)
                    {
                        // XmlSerializer reports malformed or unexpected content as InvalidOperationException.
                        throw new XmlException(e.InnerException?.Message ?? e.Message, e);
                    }

                    return new Envelope { Result = results != null && results.Length > 0 ? results[0] : null };
                }
            }

            private static Envelope ReadFault(XmlReader reader)
            {
                var envelope = new Envelope { IsFault = true };
                using (var fault = reader.ReadSubtree())
                {
                    while (fault.Read())
                    {
                        if (fault.NodeType != XmlNodeType.Element || fault.NamespaceURI != SoapNamespace)
                            continue;
                        if (fault.LocalName == "Value" && envelope.FaultCode == null)
                            envelope.FaultCode = fault.ReadElementContentAsString();
                        else if (fault.LocalName == "Text" && envelope.FaultReason == null)
                            envelope.FaultReason = fault.ReadElementContentAsString();
                    }
                }

                return envelope;
            }

            private static HttpRequestException CreateHttpError(HttpResponseMessage httpResponse, Exception inner)
            {
                return new HttpRequestException(
                    $"The Doctor Dump service returned HTTP {(int) httpResponse.StatusCode} ({httpResponse.ReasonPhrase}).",
                    inner);
            }

            private sealed class Envelope
            {
                public bool IsFault;
                public string FaultCode;
                public string FaultReason;
                public object Result;
            }
        }
    }
}
