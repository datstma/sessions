using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Sessions.App.Services;

public sealed class WindowsStartMenuAppSource : IAppSource
{
    private readonly string[]? _roots;
    public WindowsStartMenuAppSource() { }
    internal WindowsStartMenuAppSource(params string[] roots) => _roots = roots;

    public Task<IReadOnlyList<DiscoveredApp>> GetAppsAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Start menu apps are available on Windows.");
        var result = new TaskCompletionSource<IReadOnlyList<DiscoveredApp>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                result.TrySetResult(Scan(cancellationToken));
            }
            catch (OperationCanceledException) { result.TrySetCanceled(cancellationToken); }
            catch (Exception exception) { result.TrySetException(exception); }
        }) { IsBackground = true, Name = "Sessions Start menu discovery" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    [SupportedOSPlatform("windows")]
    private IReadOnlyList<DiscoveredApp> Scan(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var roots = _roots ?? [Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)];
        var apps = new List<DiscoveredApp>();
        foreach (var root in roots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                token.ThrowIfCancellationRequested();
                string[] files;
                try { files = Directory.GetFileSystemEntries(directory); }
                catch (DirectoryNotFoundException) { continue; }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    apps.Add(new("Start menu folder unavailable", null, UnavailableReason: "Some shortcuts couldn't be read. Try Refresh or Browse files.",
                        Origin: AppDiscoveryOrigin.StartMenu, Location: directory));
                    continue;
                }
                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(file);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue; // No junction/symlink traversal loops.
                        if ((attributes & FileAttributes.Directory) != 0) { pending.Push(file); continue; }
                        var extension = Path.GetExtension(file);
                        if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".url", StringComparison.OrdinalIgnoreCase)) continue;
                        var name = Path.GetFileNameWithoutExtension(file);
                        var location = Path.GetRelativePath(root, directory);
                        if (location == ".") location = "Start menu";
                        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                        {
                            apps.Add(new(name, null, UnavailableReason: "This shortcut opens a website or special app link. Use the app's executable instead.",
                                Origin: AppDiscoveryOrigin.StartMenu, Location: location));
                            continue;
                        }
                        var target = WindowsShellShortcut.Read(file);
                        string? unavailable = null;
                        if (string.IsNullOrWhiteSpace(target.Path) || !Path.IsPathFullyQualified(target.Path) ||
                            !Path.GetExtension(target.Path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                            unavailable = "This shortcut needs a launch method Sessions doesn't support yet.";
                        else if (!File.Exists(target.Path)) unavailable = "This shortcut's app is no longer at its saved location.";
                        else if (!string.IsNullOrWhiteSpace(target.WorkingDirectory) &&
                                 (!Path.IsPathFullyQualified(target.WorkingDirectory) || !Directory.Exists(target.WorkingDirectory)))
                            unavailable = "This shortcut's working folder is unavailable. Browse for the app instead.";
                        apps.Add(new(name, string.IsNullOrWhiteSpace(target.Path) ? null : target.Path,
                            unavailable is null ? WindowsRunningAppSource.ReadIcon(target.Path) : null, unavailable,
                            target.Arguments, target.WorkingDirectory, target.RunAsAdministrator, AppDiscoveryOrigin.StartMenu, location));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException or ArgumentException)
                    {
                        apps.Add(new(Path.GetFileNameWithoutExtension(file), null, UnavailableReason: "This Start menu shortcut couldn't be read.",
                            Origin: AppDiscoveryOrigin.StartMenu, Location: Path.GetRelativePath(root, file)));
                    }
                }
            }
        }
        token.ThrowIfCancellationRequested();
        return apps.DistinctBy(app => (app.Name.ToUpperInvariant(), app.ExecutablePath?.ToUpperInvariant(), app.Arguments,
                app.WorkingDirectory.ToUpperInvariant(), app.RunAsAdministrator, app.UnavailableReason))
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
