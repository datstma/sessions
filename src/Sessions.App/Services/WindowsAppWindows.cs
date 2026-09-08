using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sessions.App.Services;

internal static class WindowsAppWindows
{
    internal readonly record struct AppWindow(IntPtr Handle, uint ProcessId);

    public static IReadOnlyList<AppWindow> Enumerate()
    {
        var windows = new List<AppWindow>();
        // Check for a title without reading document names or browser tabs.
        if (!EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window) || GetWindowTextLength(window) == 0 ||
                    (GetWindowLong(window, -20) & 0x80) != 0) return true; // WS_EX_TOOLWINDOW
                if (DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;
                GetWindowThreadProcessId(window, out var id);
                if (id != Environment.ProcessId) windows.Add(new AppWindow(window, id));
                return true;
            }, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows couldn't read the list of open apps.");
        return windows;
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
