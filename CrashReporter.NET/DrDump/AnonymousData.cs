using System;

namespace CrashReporterDotNET.DrDump
{
    internal class AnonymousData
    {
        public Exception Exception { get; set; }
        public string ToEmail { get; set; }
        public Guid? ApplicationID { get; set; }

        /// <summary>
        /// Time of the crash. When not set (a new report), the time the report is sent is used.
        /// </summary>
        public DateTime? CrashDateUtc { get; set; }

        /// <summary>
        /// Application title saved with a failed report. When not set, the running application's title is used.
        /// </summary>
        public string ApplicationTitle { get; set; }

        /// <summary>
        /// Entry assembly version saved with a failed report. When not set, the running application's version is used,
        /// so a report retried after an upgrade is still attributed to the version that crashed.
        /// </summary>
        public string ApplicationVersion { get; set; }
    }
}