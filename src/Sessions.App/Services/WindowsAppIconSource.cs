using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sessions.App.Services;

/// <summary>Small, process-local PNG cache; file inspection never runs on the UI thread.</summary>
public sealed class WindowsAppIconSource : IAppIconSource
{
    public static WindowsAppIconSource Shared { get; } = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, Task<byte[]?>> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, byte[]?> _readIcon;

    public WindowsAppIconSource() : this(path => OperatingSystem.IsWindows()
        ? WindowsRunningAppSource.ReadIcon(path) : null) { }

    internal WindowsAppIconSource(Func<string, byte[]?> readIcon) => _readIcon = readIcon;

    public Task<byte[]?> GetIconAsync(string executablePath)
    {
        string path;
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return Task.FromResult<byte[]?>(null);
            path = Path.GetFullPath(executablePath.Trim());
            if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<byte[]?>(null);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        { return Task.FromResult<byte[]?>(null); }

        lock (_sync)
        {
            if (_icons.TryGetValue(path, out var pending)) return pending;
            // Bound retained bytes even when browsing large libraries; controls own their decoded bitmaps.
            if (_icons.Count >= 128) _icons.Remove(_icons.Keys.First());
            return _icons[path] = Task.Run(() => _readIcon(path));
        }
    }
}
