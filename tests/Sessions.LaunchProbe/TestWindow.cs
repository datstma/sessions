using System.ComponentModel;
using System.Runtime.InteropServices;

// Off-screen owned window: accepts real WM_CLOSE, but does not take focus or appear on the taskbar.
internal static class TestWindow
{
    private static bool _refuse;
    private static bool _guarded;
    private static bool _linger;
    private static bool _damaged;
    private static string _directory = "";
    private static IntPtr _main;
    private static IntPtr _auxiliary;
    private static readonly WindowProc Procedure = HandleMessage;
    public static int Run(string directory, bool refuse, bool guarded = false, bool linger = false, bool hidden = false)
    {
        _refuse = refuse;
        _guarded = guarded;
        _linger = linger;
        _directory = directory;
        var windowClass = new WindowClass { Procedure = Procedure, ClassName = "SessionsRuntimeProbe" };
        if (RegisterClass(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var owner = CreateWindowEx(0, windowClass.ClassName, "", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        var window = CreateWindowEx(0x08000000, windowClass.ClassName, "Sessions runtime test app", 0x10CF0000,
            -20000, -20000, 200, 100, owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        _main = window;
        if (hidden) ShowWindow(window, 0);
        if (guarded)
            _auxiliary = CreateWindowEx(0, windowClass.ClassName, "Internal helper (not an app window)", 0,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        SetTimer(window, 1, 15000, IntPtr.Zero); // Bounded lifetime even if a test fails.
        File.WriteAllText(Path.Combine(directory, "window-ready.tmp"), Environment.ProcessId.ToString());
        File.Move(Path.Combine(directory, "window-ready.tmp"), Path.Combine(directory, "window-ready"));
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        DestroyWindow(owner);
        return 0;
    }
    private static IntPtr HandleMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == 0x0010)
        {
            if (_guarded && window == _auxiliary)
            {
                _damaged = true;
                File.WriteAllText(Path.Combine(_directory, "internal-window-closed"), "Internal infrastructure was closed directly");
                DestroyWindow(window);
            }
            else if (!_refuse && window == _main)
            {
                File.WriteAllText(Path.Combine(_directory, "main-close-requested"), "Normal close received");
                if (_guarded) SetTimer(window, 2, 100, IntPtr.Zero); // Complete after queued window messages.
                else if (_linger) ShowWindow(window, 0); // Window disappears; the exact process remains alive.
                else DestroyWindow(window);
            }
            return IntPtr.Zero;
        }
        if (message == 0x0113)
        {
            if (wParam == (IntPtr)2 && _damaged) ShowWindow(_main, 0);
            else DestroyWindow(window);
            return IntPtr.Zero;
        }
        if (message == 0x0002) { if (window == _main) PostQuitMessage(0); return IntPtr.Zero; }
        return DefWindowProc(window, message, wParam, lParam);
    }
    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public WindowProc Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam, LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }
    [DllImport("user32.dll", EntryPoint = "RegisterClassW", SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr owner, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out Message message, IntPtr window, uint minimum, uint maximum);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll")]
    private static extern nuint SetTimer(IntPtr window, nuint id, uint milliseconds, IntPtr callback);
}
