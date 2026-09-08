using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Sessions.App.Services;

public sealed class WindowsRunningAppSource : IAppSource
{
    public Task<IReadOnlyList<DiscoveredApp>> GetAppsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Picking running apps is currently available on Windows.");
            return Scan(cancellationToken);
        }, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<DiscoveredApp> Scan(CancellationToken cancellationToken)
    {
        var processIds = WindowsAppWindows.Enumerate().Select(window => window.ProcessId).Distinct();

        var apps = new List<DiscoveredApp>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in processIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var process = Process.GetProcessById((int)id);
                if (process.HasExited) continue;
                var fallbackName = process.ProcessName;
                using var handle = OpenProcess(0x1000, false, id); // PROCESS_QUERY_LIMITED_INFORMATION only
                var pathBuffer = new StringBuilder(32768);
                var length = pathBuffer.Capacity;
                if (handle.IsInvalid || !QueryFullProcessImageName(handle, 0, pathBuffer, ref length))
                {
                    apps.Add(new DiscoveredApp(fallbackName, null, UnavailableReason: "Windows couldn't share this app's location."));
                    continue;
                }

                var path = pathBuffer.ToString();
                if (string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) || !paths.Add(path)) continue;
                var name = ReadName(path, fallbackName);
                uint packageLength = 0;
                var packageResult = GetPackageFullName(handle, ref packageLength, IntPtr.Zero);
                string? unavailable = null;
                if (packageResult != 15700 || // APPMODEL_ERROR_NO_PACKAGE
                    string.Equals(fallbackName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                    unavailable = "This app needs a launch method Sessions doesn't support yet.";
                else if (!File.Exists(path))
                    unavailable = "This app's executable is no longer available.";
                apps.Add(new DiscoveredApp(name, path, ReadIcon(path), unavailable));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                                   Win32Exception or UnauthorizedAccessException or IOException)
            {
                // Apps can close or become inaccessible between enumeration and inspection.
            }
        }
        return apps.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static string ReadName(string path, string fallback)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            return string.IsNullOrWhiteSpace(description) ? fallback : description.Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return fallback;
        }
    }

    [SupportedOSPlatform("windows")]
    internal static byte[]? ReadIcon(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or
                                               UnauthorizedAccessException or ExternalException)
        {
            return null; // Missing icons must not prevent selecting an otherwise usable app.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(SafeProcessHandle process, ref uint length, IntPtr name);
}
