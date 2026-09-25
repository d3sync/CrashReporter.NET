using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CrashReporterDotNET
{
    internal static class HelperMethods
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32")]
        static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll")]
        static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

        private static bool Is64BitOperatingSystem()
        {
            // Check if this process is natively an x64 process. If it is, it will only run on x64 environments, thus, the environment must be x64.
            if (IntPtr.Size == 8)
                return true;
            // Check if this process is an x86 process running on an x64 environment.
            IntPtr moduleHandle = GetModuleHandle("kernel32");
            if (moduleHandle != IntPtr.Zero)
            {
                IntPtr processAddress = GetProcAddress(moduleHandle, "IsWow64Process");
                if (processAddress != IntPtr.Zero)
                {
                    if (IsWow64Process(GetCurrentProcess(), out var result) && result)
                        return true;
                }
            }

            // The environment must be an x86 environment.
            return false;
        }

        private static string HKLM_GetString(string key, string value)
        {
            try
            {
                RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(key);
                return registryKey?.GetValue(value)?.ToString() ?? String.Empty;
            }
            catch
            {
                return String.Empty;
            }
        }

        public static string GetOSVersion()
        {
            if (!string.IsNullOrEmpty(HKLM_GetString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                "CurrentMajorVersionNumber")))
            {
                return
                    $"{HKLM_GetString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentMajorVersionNumber")}.{HKLM_GetString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentMinorVersionNumber")}.{HKLM_GetString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber")}.0";
            }

            return
                $"{HKLM_GetString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentVersion")}.{HKLM_GetString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber")}.0";
        }

        public static string GetWindowsVersion()
        {
            string osArchitecture;
            try
            {
                osArchitecture = Is64BitOperatingSystem() ? "64-bit" : "32-bit";
            }
            catch (Exception)
            {
                osArchitecture = "32/64-bit (Undetermined)";
            }

            const string currentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            string productName = HKLM_GetString(currentVersionKey, "ProductName");
            string csdVersion = HKLM_GetString(currentVersionKey, "CSDVersion");
            string currentBuild = HKLM_GetString(currentVersionKey, "CurrentBuildNumber");
            string updateBuildRevision = HKLM_GetString(currentVersionKey, "UBR");
            string installationType = HKLM_GetString(currentVersionKey, "InstallationType");
            if (!string.IsNullOrEmpty(productName))
            {
                int.TryParse(currentBuild, out var build);
                productName = GetWindowsProductName(productName, build, installationType);
                var osBuild = string.IsNullOrEmpty(updateBuildRevision) ? currentBuild : $"{currentBuild}.{updateBuildRevision}";
                return
                    $"{(productName.StartsWith("Microsoft") ? "" : "Microsoft ")}{productName}{(!string.IsNullOrEmpty(csdVersion) ? " " + csdVersion : String.Empty)} {osArchitecture} (OS Build {osBuild})";
            }

            return String.Empty;
        }

        /// <summary>
        /// First build number of Windows 11. Windows 11 still reports version 10.0 and its registry ProductName
        /// still says "Windows 10", so the build number is the only reliable way to tell them apart.
        /// </summary>
        internal const int Windows11FirstBuild = 22000;

        /// <summary>
        /// Corrects the registry ProductName of Windows 11 workstations, which Windows reports as "Windows 10 ...".
        /// Server editions (e.g. Windows Server 2022, build 20348; Windows Server 2025, build 26100) are left untouched.
        /// </summary>
        internal static string GetWindowsProductName(string productName, int build, string installationType)
        {
            if (string.IsNullOrEmpty(productName))
                return productName;

            var isServer = string.IsNullOrEmpty(installationType)
                ? productName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0
                : installationType.StartsWith("Server", StringComparison.OrdinalIgnoreCase);

            const string windows10 = "Windows 10";
            var index = productName.IndexOf(windows10, StringComparison.Ordinal);
            if (!isServer && build >= Windows11FirstBuild && index >= 0)
            {
                return productName.Substring(0, index) + "Windows 11" + productName.Substring(index + windows10.Length);
            }

            return productName;
        }
    }
}