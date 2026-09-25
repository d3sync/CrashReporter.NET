using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CrashReporterDotNET.Tests
{
    /// <summary>
    /// Tests that change <see cref="FailedReportQueue.RootDirectory"/> must not run in parallel.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class QueueCollection
    {
        public const string Name = "Failed report queue";
    }

    internal sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CrashReporterNET.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Minimal SMTP server on the loopback interface that records received messages.
    /// </summary>
    internal sealed class SmtpStub : IDisposable
    {
        private readonly TcpListener _listener;

        public SmtpStub()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint) _listener.LocalEndpoint).Port;
            Task.Run(AcceptLoop);
        }

        public int Port { get; }

        public BlockingCollection<string> Messages { get; } = new BlockingCollection<string>();

        /// <summary>
        /// A server that accepts connections but never answers (no greeting), to simulate a stalled SMTP server.
        /// </summary>
        public static TcpListener StartStalledServer(out int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            port = ((IPEndPoint) listener.LocalEndpoint).Port;
            return listener;
        }

        /// <summary>
        /// A port on which nothing listens, to simulate an unreachable SMTP server.
        /// </summary>
        public static int GetClosedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint) listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose() => _listener.Stop();

        private async Task AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                try
                {
                    Serve(client);
                }
                catch (IOException)
                {
                }
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII))
            using (var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true })
            {
                writer.WriteLine("220 localhost ESMTP");
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var command = line.ToUpperInvariant();
                    if (command.StartsWith("DATA"))
                    {
                        writer.WriteLine("354 End data with <CR><LF>.<CR><LF>");
                        var message = new StringBuilder();
                        while ((line = reader.ReadLine()) != null && line != ".")
                            message.Append(line).Append("\r\n");
                        Messages.Add(message.ToString());
                        writer.WriteLine("250 OK");
                    }
                    else if (command.StartsWith("QUIT"))
                    {
                        writer.WriteLine("221 Bye");
                        return;
                    }
                    else
                    {
                        writer.WriteLine("250 OK");
                    }
                }
            }
        }

        /// <summary>
        /// Returns the raw message plus the decoded content of all base64 and quoted-printable MIME parts.
        /// </summary>
        public static string Decode(string rawMessage)
        {
            var text = new StringBuilder(rawMessage);
            foreach (var part in Regex.Split(rawMessage, "\r\n\r\n"))
            {
                var candidate = part.Replace("\r\n", "").Trim();
                if (candidate.Length < 8 || !Regex.IsMatch(candidate, "^[A-Za-z0-9+/=]+$"))
                    continue;
                try
                {
                    text.Append(Encoding.UTF8.GetString(Convert.FromBase64String(candidate)));
                }
                catch (FormatException)
                {
                }
            }

            text.Append(rawMessage.Replace("=\r\n", "").Replace("=3D", "="));
            return text.ToString();
        }
    }

    /// <summary>
    /// HttpMessageHandler that returns a fixed response and records the request.
    /// </summary>
    internal sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public FakeHttpHandler(HttpStatusCode statusCode, string body, string contentType = "application/soap+xml; charset=utf-8")
            : this((request, token) => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8)
                {
                    Headers = { ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType) }
                }
            }))
        {
        }

        public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public string LastRequestBody { get; private set; }

        public string LastRequestContentType { get; private set; }

        public ConcurrentQueue<string> RequestBodies { get; } = new ConcurrentQueue<string>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            LastRequestContentType = request.Content.Headers.ContentType?.ToString();
            RequestBodies.Enqueue(LastRequestBody);
            return await _respond(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
