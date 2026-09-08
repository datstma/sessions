using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Sessions.App.Services;

/// <summary>Normal close, optional force quit, and a bounded UAC helper for one verified process.</summary>
internal static class WindowsProcessCleanup
{
    internal const string HelperArgument = "--close-session-app";

    public static async Task<bool> CloseAsync(WindowsProcessIdentity process, TimeSpan timeout, bool force, bool allowElevation = true)
    {
        if (process.HasExited) return true;
        try
        {
            // Hidden main windows can represent tray apps, but internal helper/dispatcher windows
            // must never receive WM_CLOSE. Destroying those can strand a still-running application.
            var denied = false;
            if (!EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var id);
                if (id != process.Id || !IsAppWindowForClose(window)) return true;
                // Recheck the recipient after inspecting its styles; the retained process must still be alive.
                GetWindowThreadProcessId(window, out id);
                if (id == process.Id && !process.HasExited && !PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero))
                    denied |= Marshal.GetLastWin32Error() == 5;
                return true;
            }, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows couldn't inspect the app's windows for cleanup.");
            if (denied) throw new Win32Exception(5);
            if (await process.WaitAsync(timeout).ConfigureAwait(false)) return true;
            if (!force) return false;
            using var target = WindowsProcessIdentity.Open(process.Id, terminate: true);
            if (!target.Matches(process.Created, process.Path, process.SessionId))
                throw new InvalidOperationException("The app's process identity changed. It was left alone.");
            target.Terminate();
            return await target.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5 && allowElevation)
        {
            if (process.HasExited) return true;
            return await CloseElevatedAsync(process, timeout, force).ConfigureAwait(false);
        }
    }

    private static async Task<bool> CloseElevatedAsync(WindowsProcessIdentity target, TimeSpan timeout, bool force)
    {
        // The UI remains unelevated. Only this one operation requests UAC approval.
        var executable = Path.Combine(AppContext.BaseDirectory, "Sessions.App.exe");
        var info = CreateHelperStartInfo(executable, target.Id, target.Created, target.Path, target.SessionId, force, timeout);
        info.Verb = "runas";
        try
        {
            using var helper = Process.Start(info) ?? throw new InvalidOperationException("Windows didn't return the cleanup helper.");
            // Includes margin for helper startup; UAC consent itself is handled by Windows during Start.
            using var deadline = new System.Threading.CancellationTokenSource(timeout + timeout + TimeSpan.FromSeconds(15));
            await helper.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            if (target.HasExited) return true;
            throw new InvalidOperationException(helper.ExitCode == 2
                ? "The elevated app stayed open. Retry End or quit it manually."
                : "Administrator cleanup couldn't verify or close this app. It was left alone.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        { throw new InvalidOperationException("Administrator approval was cancelled. Retry End to try again, or quit the app yourself.", exception); }
    }

    internal static ProcessStartInfo CreateHelperStartInfo(string executable, int id, long created, string path, uint session, bool force, TimeSpan timeout)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { HelperArgument, id.ToString(CultureInfo.InvariantCulture), created.ToString(CultureInfo.InvariantCulture),
                     path, session.ToString(CultureInfo.InvariantCulture), force ? "force" : "normal",
                     Math.Clamp((int)timeout.TotalMilliseconds, 50, 10000).ToString(CultureInfo.InvariantCulture) })
            info.ArgumentList.Add(argument);
        return info;
    }

    // Runs before Avalonia/single-instance/library initialization. No persisted process names are read.
    internal static async Task<int> RunHelperAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 7 || args[0] != HelperArgument ||
            !int.TryParse(args[1], out var id) || id <= 0 || id == Environment.ProcessId ||
            !long.TryParse(args[2], out var created) || created <= 0 || !Path.IsPathFullyQualified(args[3]) ||
            !uint.TryParse(args[4], out var session) || args[5] is not ("normal" or "force") ||
            !int.TryParse(args[6], out var milliseconds) || milliseconds is < 50 or > 10000) return 3;
        try
        {
            using var self = WindowsProcessIdentity.Open(Environment.ProcessId);
            if (self.SessionId != session) return 3;
            using var target = WindowsProcessIdentity.Open(id);
            if (!target.Matches(created, args[3], session)) return 3;
            return await CloseAsync(target, TimeSpan.FromMilliseconds(milliseconds), args[5] == "force", allowElevation: false).ConfigureAwait(false) ? 0 : 2;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException) { return 3; }
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    private static bool IsAppWindowForClose(IntPtr window) => GetWindowTextLength(window) > 0 &&
        (GetWindowLong(window, -16) & 0x00080000) != 0 && // WS_SYSMENU: an application window with a Close action.
        (GetWindowLong(window, -20) & 0x00000080) == 0; // Exclude WS_EX_TOOLWINDOW infrastructure/utility windows.

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
