using System;
using System.Globalization;
using System.Net;
using CrashReporterDotNET.com.drdump;

namespace CrashReporterDotNET.DrDump
{
    internal class SendRequestState
    {
        public AnonymousData AnonymousData { get; set; }

        public PrivateData PrivateData { get; set; }

        public DrDumpService.RequestResult SendAnonymousReportResult { get; set; }

        private static ExceptionInfo ConvertToExceptionInfo(Exception e, bool anonymous)
        {
            if (e == null)
                return null;
            return new ExceptionInfo
            {
                Type = XmlText.ToValidXml(ReplayedException.GetTypeName(e)),
                HResult = e.HResult,
                StackTrace = XmlText.ToValidXml(e.StackTrace),
                Source = XmlText.ToValidXml(e.Source),
                Message = anonymous ? null : XmlText.ToValidXml(e.Message),
                InnerException = ConvertToExceptionInfo(e.InnerException, anonymous)
            };
        }

        private static System.Net.NetworkInformation.PhysicalAddress GetMacAddress()
        {
            IPAddress localAddress;
            using (var googleDns = new System.Net.Sockets.UdpClient("8.8.8.8", 53))
            {
                localAddress = ((IPEndPoint) googleDns.Client.LocalEndPoint).Address;
            }

            foreach (var netInterface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var addr in netInterface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.Equals(localAddress))
                        return netInterface.GetPhysicalAddress();
                }
            }

            return null;
        }

        private int GetAnonymousMachineID()
        {
            System.Net.NetworkInformation.PhysicalAddress mac;
            try
            {
                mac = GetMacAddress();
            }
            catch (Exception e) when (e is System.Net.Sockets.SocketException ||
                                      e is System.Net.NetworkInformation.NetworkInformationException)
            {
                // No network route (e.g. offline): the machine ID is optional, the report must still be created.
                return 0;
            }

            if (mac == null)
                return 0;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                return BitConverter.ToInt32(md5.ComputeHash(mac.GetAddressBytes()), 0);
            }
        }

        internal DetailedExceptionDescription GetDetailedExceptionDescription()
        {
            return new DetailedExceptionDescription
            {
                Exception = GetExceptionDescription(false),
                DeveloperMessage = XmlText.ToValidXml(PrivateData.DeveloperMessage),
                UserDescription = XmlText.ToValidXml(PrivateData.UserMessage),
                UserEmail = XmlText.ToValidXml(PrivateData.UserEmail),
                PngScreenShot = PrivateData.Screenshot
            };
        }

        internal ExceptionDescription GetExceptionDescription(bool anonymous)
        {
            var oldCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            var oldUICulture = System.Threading.Thread.CurrentThread.CurrentUICulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            try
            {
                var osVersion = Environment.OSVersion;
                // The numeric version is reported as-is (Windows 11 is 10.0.22000+); the service maps it to a product name.
                var os = $"os={osVersion.Platform};v={HelperMethods.GetOSVersion()};spname={osVersion.ServicePack}";

                return new ExceptionDescription
                {
                    ClrVersion = Environment.Version.ToString(),
                    OS = os,
                    CrashDate = AnonymousData.CrashDateUtc ?? DateTime.UtcNow,
                    PCID = GetAnonymousMachineID(),
                    Exception = ConvertToExceptionInfo(AnonymousData.Exception, anonymous),
                    ExceptionString = anonymous ? null : XmlText.ToValidXml(AnonymousData.Exception.ToString()),
                };
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = oldCulture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = oldUICulture;
            }
        }

        internal Application GetApplication()
        {
            var mainAssembly = System.Reflection.Assembly.GetEntryAssembly();

            string moduleName = mainAssembly?.GetName().Name ?? FailedReportQueue.GetApplicationName();

            var attributes = mainAssembly?.GetCustomAttributes(typeof(System.Reflection.AssemblyCompanyAttribute), true);
            string appCompany = attributes?.Length > 0
                ? ((System.Reflection.AssemblyCompanyAttribute) attributes[0]).Company
                : AnonymousData.ToEmail;

            var attributes2 = mainAssembly?.GetCustomAttributes(typeof(System.Reflection.AssemblyTitleAttribute), true);
            string appTitle = !string.IsNullOrEmpty(AnonymousData.ApplicationTitle)
                ? AnonymousData.ApplicationTitle
                : attributes2?.Length > 0
                    ? ((System.Reflection.AssemblyTitleAttribute) attributes2[0]).Title
                    : moduleName;

            Version appVersion = null;
            if (!string.IsNullOrEmpty(AnonymousData.ApplicationVersion))
                Version.TryParse(AnonymousData.ApplicationVersion, out appVersion);
            appVersion = appVersion ?? mainAssembly?.GetName().Version ?? new Version(0, 0, 0, 0);

            return new Application
            {
                ApplicationGUID = AnonymousData.ApplicationID?.ToString("D"),
                AppName = XmlText.ToValidXml(appTitle),
                CompanyName = XmlText.ToValidXml(appCompany),
                Email = XmlText.ToValidXml(AnonymousData.ToEmail),
                V1 = (ushort) Math.Max(0, appVersion.Major),
                V2 = (ushort) Math.Max(0, appVersion.Minor),
                V3 = (ushort) Math.Max(0, appVersion.Build),
                V4 = (ushort) Math.Max(0, appVersion.Revision),
                MainModule = moduleName
            };
        }

        internal static ClientLib GetClientLib()
        {
            var clientVersion = typeof(CrashReport).Assembly.GetName().Version;
            return new ClientLib
            {
                V1 = (ushort) clientVersion.Major,
                V2 = (ushort) clientVersion.Minor,
                V3 = (ushort) clientVersion.Build,
                V4 = (ushort) clientVersion.Revision
            };
        }
    }
}