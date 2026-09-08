using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Sessions.App.Services;

/// <summary>A retained process lifetime, opened with only query/synchronize rights.</summary>
internal sealed class WindowsProcessIdentity : IDisposable
{
    private readonly SafeProcessHandle _handle;
    public int Id { get; }
    public long Created { get; }
    public string Path { get; }
    public uint SessionId { get; }

    private WindowsProcessIdentity(int id, SafeProcessHandle handle)
    {
        Id = id;
        _handle = handle;
        if (!GetProcessTimes(handle, out var created, out _, out _, out _)) throw new Win32Exception();
        Created = created;
        var path = new StringBuilder(32768);
        var length = path.Capacity;
        if (!QueryFullProcessImageName(handle, 0, path, ref length)) throw new Win32Exception();
        Path = path.ToString();
        if (!ProcessIdToSessionId(id, out var session)) throw new Win32Exception();
        SessionId = session;
    }

    public static WindowsProcessIdentity Open(int id, bool terminate = false)
    {
        var handle = OpenProcess(0x00101000u | (terminate ? 1u : 0u), false, id);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        try { return new WindowsProcessIdentity(id, handle); }
        catch { handle.Dispose(); throw; }
    }

    public bool HasExited => WaitForSingleObject(_handle, 0) switch
    {
        0 => true,
        258 => false,
        _ => throw new Win32Exception()
    };

    public long? ExitedAt
    {
        get
        {
            if (!HasExited) return null;
            if (!GetProcessTimes(_handle, out _, out var exited, out _, out _)) throw new Win32Exception();
            return exited;
        }
    }

    public bool Matches(long created, string path, uint session) => Created == created && SessionId == session &&
        string.Equals(Path, path, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> WaitAsync(TimeSpan timeout)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!HasExited && deadline.Elapsed < timeout) await Task.Delay(50).ConfigureAwait(false);
        return HasExited;
    }

    public void Terminate()
    {
        if (!HasExited && !TerminateProcess(_handle, 1)) throw new Win32Exception();
    }

    public void Dispose() => _handle.Dispose();

    // Parent IDs are only candidates. Callers also verify path, login session and lifetime bounds.
    public static IReadOnlyList<(int Id, int Parent, string Name)> Snapshot()
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception();
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), ExeFile = "" };
        var results = new List<(int, int, string)>();
        if (!Process32First(snapshot, ref entry)) throw new Win32Exception();
        do { results.Add(((int)entry.Id, (int)entry.Parent, entry.ExeFile)); } while (Process32Next(snapshot, ref entry));
        if (Marshal.GetLastWin32Error() != 18) throw new Win32Exception(); // ERROR_NO_MORE_FILES
        return results;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id;
        public UIntPtr Heap;
        public uint Module, Threads, Parent;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle handle, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle handle, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ProcessIdToSessionId(int id, out uint session);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(SafeProcessHandle handle, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
}
