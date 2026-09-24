using System;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace CrashReporterDotNET
{
    /// <summary>
    /// Crash report persisted to disk when sending fails, so it can be retried later.
    /// Replaces the BinaryFormatter based format, which is not supported on .NET 9+.
    /// </summary>
    [DataContract(Name = "FailedReport", Namespace = Namespace)]
    internal sealed class FailedReport
    {
        internal const string Namespace = "https://github.com/ravibpatel/CrashReporter.NET/failed-report";

        private static readonly DataContractSerializer Serializer = new DataContractSerializer(typeof(FailedReport));

        [DataMember(Order = 1)]
        public ExceptionData Exception { get; set; }

        [DataMember(Order = 2)]
        public byte[] ScreenShot { get; set; }

        public void Save(string path)
        {
            using (var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true }))
            {
                Serializer.WriteObject(writer, this);
            }
        }

        public static FailedReport Load(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                return (FailedReport) Serializer.ReadObject(reader);
            }
        }
    }

    [DataContract(Name = "Exception", Namespace = FailedReport.Namespace)]
    internal sealed class ExceptionData
    {
        [DataMember(Order = 1)]
        public string TypeName { get; set; }

        [DataMember(Order = 2)]
        public string Message { get; set; }

        [DataMember(Order = 3)]
        public string Source { get; set; }

        [DataMember(Order = 4)]
        public string StackTrace { get; set; }

        [DataMember(Order = 5)]
        public int HResult { get; set; }

        [DataMember(Order = 6)]
        public string Text { get; set; }

        [DataMember(Order = 7)]
        public ExceptionData InnerException { get; set; }

        public static ExceptionData FromException(Exception exception)
        {
            if (exception == null)
                return null;

            return new ExceptionData
            {
                TypeName = ReplayedException.GetTypeName(exception),
                Message = exception.Message,
                Source = exception.Source,
                StackTrace = exception.StackTrace,
                HResult = exception.HResult,
                Text = exception.ToString(),
                InnerException = FromException(exception.InnerException)
            };
        }

        public Exception ToException()
        {
            return new ReplayedException(this);
        }
    }

    /// <summary>
    /// Exception recreated from a <see cref="FailedReport"/>. Reports the original type name, message, source and stack trace.
    /// </summary>
    internal sealed class ReplayedException : Exception
    {
        private readonly string _stackTrace;

        private readonly string _text;

        public ReplayedException(ExceptionData data)
            : base(data.Message, data.InnerException?.ToException())
        {
            OriginalTypeName = data.TypeName;
            Source = data.Source;
            HResult = data.HResult;
            _stackTrace = data.StackTrace;
            _text = data.Text;
        }

        public string OriginalTypeName { get; }

        public override string StackTrace => _stackTrace;

        public override string ToString() => _text ?? base.ToString();

        /// <summary>
        /// Returns the full type name of the exception, or the original type name if it was recreated from a failed report.
        /// </summary>
        public static string GetTypeName(Exception exception)
        {
            return exception is ReplayedException replayed && !string.IsNullOrEmpty(replayed.OriginalTypeName)
                ? replayed.OriginalTypeName
                : exception.GetType().ToString();
        }
    }
}
