using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;

namespace Sessions.App.Services;

[SupportedOSPlatform("windows")]
internal static class WindowsShellShortcut
{
    internal sealed record Target(string Path, string Arguments, string WorkingDirectory, bool RunAsAdministrator);

    // Read persisted shortcut metadata only. Resolve/Save/activation are deliberately not called.
    public static Target Read(string file)
    {
        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
        try
        {
            ((IPersistFile)instance).Load(file, 0); // STGM_READ
            var link = (IShellLinkW)instance;
            var path = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            var directory = new StringBuilder(32768);
            link.GetPath(path, path.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH; expand without resolving/repairing.
            link.GetArguments(arguments, arguments.Capacity);
            link.GetWorkingDirectory(directory, directory.Capacity);
            ((IShellLinkDataList)instance).GetFlags(out var flags);
            return new Target(Environment.ExpandEnvironmentVariables(path.ToString()), arguments.ToString(),
                Environment.ExpandEnvironmentVariables(directory.ToString()), (flags & 0x2000) != 0);
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport, Guid("45E2B4AE-B1C3-11D0-B92F-00A0C90312E1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkDataList
    {
        void AddDataBlock(IntPtr block);
        void CopyDataBlock(uint signature, out IntPtr block);
        void RemoveDataBlock(uint signature);
        void GetFlags(out uint flags);
        void SetFlags(uint flags);
    }
}
