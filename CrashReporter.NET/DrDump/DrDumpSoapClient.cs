using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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

        private readonly HttpClient _httpClient;

        public DrDumpSoapClient(IWebProxy webProxy = null)
        {
            var handler = new HttpClientHandler();
            if (webProxy != null)
            {
                handler.Proxy = webProxy;
                handler.UseProxy = true;
            }

            _httpClient = new HttpClient(handler);
        }

        public string Url { get; set; } = DefaultUrl;

        public Task<Response> SendAnonymousReportAsync(ClientLib clientLib, Application app,
            ExceptionDescription exception)
        {
            return InvokeAsync(SendAnonymousReportOperation, clientLib, app, exception);
        }

        public Task<Response> SendAdditionalDataAsync(byte[] context, DetailedExceptionDescription addInfo)
        {
            return InvokeAsync(SendAdditionalDataOperation, context, addInfo);
        }

        private async Task<Response> InvokeAsync(Operation operation, params object[] parameters)
        {
            using (var content = new ByteArrayContent(operation.WriteRequest(parameters)))
            {
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                    $"application/soap+xml; charset=utf-8; action=\"{ActionPrefix}{operation.Name}\"");

                using (var httpResponse = await _httpClient.PostAsync(Url, content).ConfigureAwait(false))
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

            public Response ReadResponse(byte[] body, HttpResponseMessage httpResponse)
            {
                try
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
                        {
                            throw ReadFault(reader);
                        }

                        var results = (object[]) _responseSerializer.Deserialize(reader);
                        return (Response) results[0];
                    }
                }
                catch (XmlException) when (!httpResponse.IsSuccessStatusCode)
                {
                    // Not a SOAP message (e.g. proxy or gateway error page), report the HTTP failure instead.
                    httpResponse.EnsureSuccessStatusCode();
                    throw;
                }
                catch (InvalidOperationException e) when (e.InnerException is XmlException && !httpResponse.IsSuccessStatusCode)
                {
                    httpResponse.EnsureSuccessStatusCode();
                    throw;
                }
            }

            private static Exception ReadFault(XmlReader reader)
            {
                string code = null;
                string reason = null;
                using (var fault = reader.ReadSubtree())
                {
                    while (fault.Read())
                    {
                        if (fault.NodeType != XmlNodeType.Element || fault.NamespaceURI != SoapNamespace)
                            continue;
                        if (fault.LocalName == "Value" && code == null)
                            code = fault.ReadElementContentAsString();
                        else if (fault.LocalName == "Text" && reason == null)
                            reason = fault.ReadElementContentAsString();
                    }
                }

                return new InvalidOperationException(string.IsNullOrEmpty(reason)
                    ? $"Doctor Dump service returned a SOAP fault ({code})."
                    : reason);
            }
        }
    }
}
