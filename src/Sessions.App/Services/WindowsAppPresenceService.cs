using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Sessions.App.Services;

public sealed class WindowsAppPresenceService : IAppPresenceService
{
    public Task<IReadOnlyDictionary<string, AppPresence>> GetPresenceAsync(
        IReadOnlyList<string> executablePaths, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyDictionary<string, AppPresence>>(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var windows = WindowsAppWindows.Enumerate();
            var windowProcesses = windows.Select(window => window.ProcessId).ToHashSet();
            using var currentProcess = Process.GetCurrentProcess();
            var desktopSessionId = currentProcess.SessionId;
            var result = new Dictionary<string, AppPresence>(StringComparer.OrdinalIgnoreCase);
            // Grouping avoids querying the same process family for duplicate configured paths.
            foreach (var group in executablePaths.Distinct(StringComparer.OrdinalIgnoreCase)
                         .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var paths = group.ToArray();
                foreach (var path in paths) result[path] = AppPresence.NotRunning;
                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    foreach (var path in paths) result[path] = AppPresence.Unknown;
                    continue;
                }
                var inaccessible = false;
                var processes = Process.GetProcessesByName(group.Key);
                try
                {
                    foreach (var process in processes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if (process.SessionId != desktopSessionId || process.HasExited) continue;
                        }
                        catch (InvalidOperationException) { continue; }
                        catch (Win32Exception) { inaccessible = true; continue; }
                        using var handle = OpenProcess(0x1000, false, (uint)process.Id);
                        var actualPath = ReadPath(handle);
                        if (actualPath is null)
                        {
                            // An unreadable process of this name may be the configured app.
                            try { inaccessible |= !process.HasExited; }
                            catch (InvalidOperationException) { }
                            catch (Win32Exception) { inaccessible = true; }
                            continue;
                        }
                        foreach (var path in paths.Where(path => SamePath(path, actualPath)))
                        {
                            if (windowProcesses.Contains((uint)process.Id)) result[path] = AppPresence.Window;
                            else if (result[path] != AppPresence.Window) result[path] = AppPresence.Background;
                        }
                    }
                }
                finally { foreach (var process in processes) process.Dispose(); }
                if (inaccessible)
                    foreach (var path in paths.Where(path => result[path] == AppPresence.NotRunning))
                        result[path] = AppPresence.Unknown;
            }
            return result;
        }, cancellationToken);

    public Task<AppFocusResult> FocusAsync(string executablePath, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            // Resolve a fresh window on each click. Never persist or trust a cached PID/HWND.
            foreach (var window in WindowsAppWindows.Enumerate())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var process = OpenProcess(0x1000, false, window.ProcessId);
                if (ReadPath(process) is not { } path || !SamePath(path, executablePath)) continue;
                WindowsAppWindows.GetWindowThreadProcessId(window.Handle, out var currentId);
                if (currentId != window.ProcessId) continue;
                if (IsIconic(window.Handle))
                {
                    if (!ShowWindowAsync(window.Handle, 9)) return AppFocusResult.Denied; // SW_RESTORE
                    // Restore is queued in the other app; wait briefly without blocking either UI.
                    for (var attempt = 0; attempt < 10 && IsIconic(window.Handle); attempt++)
                        await Task.Delay(30, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                WindowsAppWindows.GetWindowThreadProcessId(window.Handle, out currentId);
                if (currentId != window.ProcessId || ReadPath(process) is not { } currentPath ||
                    !SamePath(currentPath, executablePath)) continue;
                return SetForegroundWindow(window.Handle) ? AppFocusResult.Focused : AppFocusResult.Denied;
            }
            return AppFocusResult.NoWindow;
        }, cancellationToken);

    private static bool SamePath(string first, string second)
    {
        try { return string.Equals(Path.GetFullPath(first.Trim()), Path.GetFullPath(second.Trim()), StringComparison.OrdinalIgnoreCase); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    internal static string? GetProcessPath(int processId)
    {
        using var handle = OpenProcess(0x1000, false, (uint)processId);
        return ReadPath(handle);
    }

    private static string? ReadPath(SafeProcessHandle process)
    {
        if (process.IsInvalid) return null;
        var buffer = new StringBuilder(32768);
        var length = buffer.Capacity;
        return QueryFullProcessImageName(process, 0, buffer, ref length) ? buffer.ToString() : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder path, ref int size);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
