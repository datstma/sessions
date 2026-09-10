using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sessions.Core;

namespace Sessions.App.Services;

public interface IIndividualAppCloser
{
    Task<IPreparedAppClose> PrepareAsync(StartProcessAction app, CancellationToken cancellationToken = default);
}

/// <summary>Explicit manual closing is independent of automatic Session ownership.</summary>
public sealed class WindowsIndividualAppCloser(ISessionPluginHost? plugins = null) : IIndividualAppCloser
{
    public Task<IPreparedAppClose> PrepareAsync(StartProcessAction app, CancellationToken cancellationToken = default)
    {
        if (app.Plugin is { } reference)
            return (plugins?.Capture() ?? throw new InvalidOperationException("This plugin is unavailable."))
                .PrepareCloseAsync(reference, cancellationToken);
        return Task.Run<IPreparedAppClose>(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var path = Path.GetFullPath(app.ExecutablePath.Trim());
            if (string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use the Sessions window controls to close Sessions.");
            using var self = Process.GetCurrentProcess();
            var retained = new List<ITrackedProcess>();
            var candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path));
            var unreadable = false;
            try
            {
                foreach (var candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WindowsProcessIdentity? identity = null;
                    try
                    {
                        identity = WindowsProcessIdentity.Open(candidate.Id);
                        if (identity.SessionId != self.SessionId || identity.HasExited ||
                            !string.Equals(identity.Path, path, StringComparison.OrdinalIgnoreCase)) continue;
                        retained.Add(new WindowsTrackedApp(identity, owned: false));
                        identity = null;
                    }
                    catch (Win32Exception exception) when (exception.NativeErrorCode == 87) { }
                    catch (Win32Exception) { unreadable = true; }
                    finally { identity?.Dispose(); }
                }
                if (retained.Count == 0 && unreadable) throw new InvalidOperationException("Windows couldn't verify this app. Close it from its own window.");
                return new PreparedAppClose(retained);
            }
            catch { foreach (var process in retained) process.Dispose(); throw; }
            finally { foreach (var candidate in candidates) candidate.Dispose(); }
        }, cancellationToken);
    }
}
