using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sessions.Core;

namespace Sessions.App.Services;

public sealed class WindowsProcessStarter : IProcessStarter
{
    public Task StartAsync(StartProcessAction app, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var info = CreateStartInfo(app);
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(info);
        // A successful shell handoff may return no handle; dispose never terminates the app.
    }, cancellationToken);

    internal static ProcessStartInfo CreateStartInfo(StartProcessAction app)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Opening apps is currently supported on Windows.");
        var path = app.ExecutablePath.Trim();
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an executable (.exe) with a full path in Edit Session.");
        if (!File.Exists(path)) throw new FileNotFoundException("The app's executable couldn't be found. Check its location in Edit Session.", path);
        var directory = string.IsNullOrWhiteSpace(app.WorkingDirectory) ? Path.GetDirectoryName(path)! : app.WorkingDirectory.Trim();
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("The app's working folder couldn't be found. Check it in Edit Session.");
        return new ProcessStartInfo
        {
            FileName = path,
            Arguments = app.Arguments,
            WorkingDirectory = directory,
            // Preserve independent desktop activation (including the Discord pipe-lifetime fix).
            UseShellExecute = true,
            Verb = app.RunAsAdministrator ? "runas" : "open"
        };
    }
}
