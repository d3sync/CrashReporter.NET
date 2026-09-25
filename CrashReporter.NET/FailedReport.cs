using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;

namespace CrashReporterDotNET
{
    /// <summary>
    /// Snapshot of a crash report persisted to disk when sending fails, so exactly the same report can be retried later.
    /// Replaces the BinaryFormatter based format, which is not supported on .NET 9+.
    /// </summary>
    [DataContract(Name = "FailedReport", Namespace = Namespace)]
    internal sealed class FailedReport
    {
        internal const string Namespace = "https://github.com/ravibpatel/CrashReporter.NET/failed-report";

        /// <summary>
        /// Version 1 (2.0.x) only contained <see cref="Exception"/> and <see cref="ScreenShot"/>.
        /// Version 2 (2.1+) also stores the report metadata, so retries do not recapture or replace it.
        /// </summary>
        internal const int CurrentFormatVersion = 2;

        private static readonly DataContractSerializer Serializer = new DataContractSerializer(typeof(FailedReport));

        [DataMember(Order = 1)]
        public ExceptionData Exception { get; set; }

        [DataMember(Order = 2)]
        public byte[] ScreenShot { get; set; }

        [DataMember(Order = 3, EmitDefaultValue = false)]
        public int FormatVersion { get; set; }

        [DataMember(Order = 4, EmitDefaultValue = false)]
        public string ApplicationTitle { get; set; }

        [DataMember(Order = 5, EmitDefaultValue = false)]
        public string ApplicationVersion { get; set; }

        [DataMember(Order = 6, EmitDefaultValue = false)]
        public string DeveloperMessage { get; set; }

        [DataMember(Order = 7, EmitDefaultValue = false)]
        public string UserEmail { get; set; }

        [DataMember(Order = 8, EmitDefaultValue = false)]
        public string UserMessage { get; set; }

        [DataMember(Order = 9, EmitDefaultValue = false)]
        public bool? IncludeScreenshot { get; set; }

        [DataMember(Order = 10, EmitDefaultValue = false)]
        public DateTime? CrashDateUtc { get; set; }

        /// <summary>
        /// Entry assembly version at the time of the crash, reported to Doctor Dump.
        /// </summary>
        [DataMember(Order = 11, EmitDefaultValue = false)]
        public string ApplicationAssemblyVersion { get; set; }

        /// <summary>
        /// Writes the report atomically: the file only appears under <paramref name="path"/> once it has been written completely.
        /// </summary>
        public void Save(string path)
        {
            AtomicFile.Write(path, stream =>
            {
                // CheckCharacters = false writes characters that are invalid in XML (e.g. U+0001 in an exception message)
                // as character references instead of throwing, and NewLineHandling.Entitize writes \r as &#xD; so that
                // XML line-break normalization does not turn \r\n into \n. Arbitrary text round-trips exactly.
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    CheckCharacters = false,
                    NewLineHandling = NewLineHandling.Entitize
                };
                using (var writer = XmlWriter.Create(stream, settings))
                {
                    Serializer.WriteObject(writer, Sanitized());
                }
            });
        }

        public static FailedReport Load(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings
                   {
                       CheckCharacters = false,
                       DtdProcessing = DtdProcessing.Prohibit,
                       XmlResolver = null
                   }))
            {
                return (FailedReport) Serializer.ReadObject(reader);
            }
        }

        private FailedReport Sanitized()
        {
            var copy = (FailedReport) MemberwiseClone();
            copy.Exception = Exception?.Sanitized();
            copy.ApplicationTitle = XmlText.ReplaceLoneSurrogates(ApplicationTitle);
            copy.ApplicationVersion = XmlText.ReplaceLoneSurrogates(ApplicationVersion);
            copy.ApplicationAssemblyVersion = XmlText.ReplaceLoneSurrogates(ApplicationAssemblyVersion);
            copy.DeveloperMessage = XmlText.ReplaceLoneSurrogates(DeveloperMessage);
            copy.UserEmail = XmlText.ReplaceLoneSurrogates(UserEmail);
            copy.UserMessage = XmlText.ReplaceLoneSurrogates(UserMessage);
            return copy;
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

        internal ExceptionData Sanitized()
        {
            return new ExceptionData
            {
                TypeName = XmlText.ReplaceLoneSurrogates(TypeName),
                Message = XmlText.ReplaceLoneSurrogates(Message),
                Source = XmlText.ReplaceLoneSurrogates(Source),
                StackTrace = XmlText.ReplaceLoneSurrogates(StackTrace),
                HResult = HResult,
                Text = XmlText.ReplaceLoneSurrogates(Text),
                InnerException = InnerException?.Sanitized()
            };
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

    /// <summary>
    /// Per-application queue of failed reports in the user's temporary directory.
    /// </summary>
    internal static class FailedReportQueue
    {
        private const string FilePrefix = "failed-report-";

        private const string FileExtension = ".xml";

        /// <summary>
        /// Root directory of all queues. Can be overridden for tests.
        /// </summary>
        internal static string RootDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "CrashReporterNET");

        /// <summary>
        /// Queue directory of the current application, so applications never retry each other's reports.
        /// </summary>
        public static DirectoryInfo GetDirectory(string applicationName = null)
        {
            return new DirectoryInfo(Path.Combine(RootDirectory, GetSafeName(applicationName ?? GetApplicationName())));
        }

        /// <summary>
        /// Returns a new, unique file name. Reports saved in the same millisecond, or by several processes, never collide.
        /// </summary>
        public static string CreateFileName(DateTime utcNow)
        {
            return $"{FilePrefix}{utcNow:yyyyMMdd'T'HHmmssfff}-{Guid.NewGuid():N}{FileExtension}";
        }

        /// <summary>
        /// Queued reports, oldest first. Files still being written (see <see cref="AtomicFile"/>) are not included.
        /// </summary>
        public static IEnumerable<FileInfo> GetFiles(DirectoryInfo directory)
        {
            if (!directory.Exists)
                return Enumerable.Empty<FileInfo>();

            return directory.GetFiles(FilePrefix + "*" + FileExtension)
                .Where(file => file.Extension.Equals(FileExtension, StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.Name, StringComparer.Ordinal);
        }

        internal const string LockFileName = ".retry.lock";

        /// <summary>
        /// Takes the exclusive retry lock of a queue, or returns null if another retry (in this or another process) holds it.
        /// The lock is an open file handle without sharing, so the operating system releases it when the owning process
        /// exits or crashes; a lock file left behind by an interrupted retry never blocks later retries.
        /// </summary>
        public static IDisposable TryLock(DirectoryInfo directory)
        {
            try
            {
                return new FileStream(Path.Combine(directory.FullName, LockFileName), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            }
            catch (IOException) when (directory.Exists)
            {
                // Sharing violation: the queue is locked by another retry.
                return null;
            }
        }

        internal static string GetApplicationName()
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            return entryAssembly?.GetName().Name ?? AppDomain.CurrentDomain.FriendlyName;
        }

        internal static string GetSafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "default";

            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }
    }

    /// <summary>
    /// Writes a file via a temporary file and a rename, so readers never see partially written content.
    /// </summary>
    internal static class AtomicFile
    {
        internal const string TemporaryExtension = ".tmp";

        public static void Write(string path, Action<Stream> write)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + TemporaryExtension;
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    write(stream);
                    stream.Flush(true);
                }

                File.Move(temporaryPath, path);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
