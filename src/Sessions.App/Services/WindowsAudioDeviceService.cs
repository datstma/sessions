using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Sessions.Core;

namespace Sessions.App.Services;

/// <summary>Core Audio enumeration and Windows default-device policy, isolated from the UI and domain.</summary>
public sealed class WindowsAudioDeviceService : IAudioDeviceService
{
    public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<AudioDevice>>(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Audio switching requires Windows.");
            var result = new List<AudioDevice>();
            WithEnumerator(enumerator =>
            {
                foreach (var flow in Enum.GetValues<AudioFlow>())
                {
                    Check(enumerator.EnumAudioEndpoints((int)flow, 1, out var collection)); // DEVICE_STATE_ACTIVE
                    try
                    {
                        Check(collection.GetCount(out var count));
                        for (uint index = 0; index < count; index++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            Check(collection.Item(index, out var device));
                            try
                            {
                                Check(device.GetId(out var id));
                                Check(device.OpenPropertyStore(0, out var properties));
                                try
                                {
                                    var key = new PropertyKey { Format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
                                    Check(properties.GetValue(ref key, out var value));
                                    try { result.Add(new(id, value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? id : id, flow)); }
                                    finally { PropVariantClear(ref value); }
                                }
                                finally { Release(properties); }
                            }
                            finally { Release(device); }
                        }
                    }
                    finally { Release(collection); }
                }
            });
            return result;
        }, cancellationToken);

    public Task<string?> GetDefaultAsync(AudioFlow flow, AudioRole role) => Task.Run(() =>
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Audio switching requires Windows.");
        string? id = null;
        WithEnumerator(enumerator =>
        {
            var status = enumerator.GetDefaultAudioEndpoint((int)flow, (int)role, out var device);
            if (status == unchecked((int)0x80070490)) return; // No default endpoint.
            Check(status);
            try { Check(device.GetId(out id)); }
            finally { Release(device); }
        });
        return id;
    });

    public Task SetDefaultAsync(AudioFlow flow, AudioRole role, string deviceId) => Task.Run(() =>
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Audio switching requires Windows.");
        WithEnumerator(enumerator =>
        {
            Check(enumerator.GetDevice(deviceId, out var device));
            try
            {
                Check(device.GetState(out var state));
                Check(((IMMEndpoint)device).GetDataFlow(out var actualFlow));
                if (state != 1 || actualFlow != (int)flow)
                    throw new InvalidOperationException("The selected audio device is no longer available.");
            }
            finally { Release(device); }
        });
        var instance = Create("870af99c-171d-4f9e-af0d-e63df40c2bc9");
        try { Check(((IPolicyConfig)instance).SetDefaultEndpoint(deviceId, (int)role)); }
        finally { Release(instance); }
    });

    private static object Create(string id)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Audio switching requires Windows.");
        return Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid(id), throwOnError: true)!)!;
    }
    private static void WithEnumerator(Action<IMMDeviceEnumerator> action)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Audio switching requires Windows.");
        var instance = Create("bcde0395-e52f-467c-8e3d-c4579291692e");
        try { action((IMMDeviceEnumerator)instance); }
        finally { Release(instance); }
    }
    private static void Release(object instance)
    {
        if (OperatingSystem.IsWindows()) Marshal.ReleaseComObject(instance);
    }
    private static void Check(int status) => Marshal.ThrowExceptionForHR(status);

    [ComImport, Guid("a95664d2-9614-4f35-a746-de8db63617e6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }
    [ComImport, Guid("0bd7a1be-7a1a-44db-8397-cc5392387b5e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("d666063f-1587-4e43-81f1-b948e807363f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, uint context, IntPtr parameters, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("1be09788-6894-4089-8586-9a2a6c265ac5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMEndpoint { [PreserveSig] int GetDataFlow(out int flow); }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    // Undocumented Windows policy interface used for default switching (also used by EarTrumpet).
    // Only SetDefaultEndpoint is called; preceding declarations preserve COM vtable slot order.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
        void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode();
        void GetPropertyValue(); void SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
}
