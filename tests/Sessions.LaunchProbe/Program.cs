using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Sessions.App.Services;
using Sessions.Core;

// A bounded, windowless native regression helper. It only touches the supplied test directory.
if (args.Length < 2 || !OperatingSystem.IsWindows()) return 2;
var directory = args[1];
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
try
{
    if (args[0] == "handoff")
    {
        // Simulate SRS: keep the original alive until approval, launch a replacement, then exit.
        File.WriteAllText(Path.Combine(directory, "original-ready"), Environment.ProcessId.ToString());
        while (!File.Exists(Path.Combine(directory, "approve-handoff"))) await Task.Delay(25, timeout.Token);
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "open" };
        info.ArgumentList.Add("window");
        info.ArgumentList.Add(directory);
        info.ArgumentList.Add("accept");
        using var replacement = Process.Start(info);
        while (!File.Exists(Path.Combine(directory, "window-ready"))) await Task.Delay(25, timeout.Token);
        return 0;
    }
    if (args[0] == "window") return TestWindow.Run(directory, args.Length > 2 && args[2] == "refuse",
        guarded: args.Length > 2 && args[2] == "guarded", linger: args.Length > 2 && args[2] == "linger",
        hidden: args.Length > 2 && args[2] == "hidden");
    if (args[0] == "instance")
    {
        using var instance = new SingleInstanceGuard(@"Global\Sessions.Test." + args[2]);
        if (!instance.IsOwner) { File.WriteAllText(Path.Combine(directory, "secondary"), "blocked"); return 0; }
        instance.Listen(() => File.WriteAllText(Path.Combine(directory, "activated"), "activated"));
        File.WriteAllText(Path.Combine(directory, "owner.tmp"), Environment.ProcessId.ToString());
        File.Move(Path.Combine(directory, "owner.tmp"), Path.Combine(directory, "owner"), true);
        while (!File.Exists(Path.Combine(directory, "release-instance")))
        {
            timeout.Token.ThrowIfCancellationRequested();
            Thread.Sleep(25);
        }
        return 0;
    }
    if (args[0] == "parent")
    {
        await new WindowsProcessStarter().StartAsync(new StartProcessAction(Guid.NewGuid(), "Child probe",
            Environment.ProcessPath!, $"child \"{directory}\" \"argument with spaces\"", directory), timeout.Token);
        // The test either requests a normal exit or terminates this exact parent process.
        while (!File.Exists(Path.Combine(directory, "exit-parent"))) await Task.Delay(25, timeout.Token);
        return 0;
    }
    if (args[0] != "child" || args.Length != 3) return 2;
    var stdout = Native.GetStdHandle(-11);
    var stderr = Native.GetStdHandle(-12);
    File.WriteAllText(Path.Combine(directory, "child-ready.tmp"), Environment.ProcessId.ToString());
    File.Move(Path.Combine(directory, "child-ready.tmp"), Path.Combine(directory, "child-ready"));
    while (!File.Exists(Path.Combine(directory, "write-after-exit"))) await Task.Delay(25, timeout.Token);
    var report = new
    {
        OutputIsPipe = Native.IsPipe(stdout), ErrorIsPipe = Native.IsPipe(stderr),
        OutputWriteError = Native.TryWrite(stdout), ErrorWriteError = Native.TryWrite(stderr),
        WorkingDirectory = Environment.CurrentDirectory, Argument = args[2]
    };
    var temporaryReport = Path.Combine(directory, "report.tmp");
    File.WriteAllText(temporaryReport, JsonSerializer.Serialize(report));
    File.Move(temporaryReport, Path.Combine(directory, "report.json"));
    return 0;
}
catch (OperationCanceledException) { return 3; }

internal static class Native
{
    public static bool IsPipe(IntPtr handle) => handle != IntPtr.Zero && handle != new IntPtr(-1) && GetFileType(handle) == 3;
    public static int TryWrite(IntPtr handle)
    {
        // A GUI app launched independently may have no standard streams; that is valid.
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return 0;
        var bytes = Encoding.UTF8.GetBytes("child is still working\r\n");
        return WriteFile(handle, bytes, bytes.Length, out _, IntPtr.Zero) ? 0 : Marshal.GetLastWin32Error();
    }
    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetStdHandle(int standardHandle);
    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(IntPtr handle, byte[] buffer, int size, out int written, IntPtr overlapped);
}
