using System;

using System.Collections.Generic;

using System.ComponentModel;

using System.Diagnostics;

using System.IO;

using System.Threading;

using System.Threading.Tasks;

using Sessions.Core;



namespace Sessions.App.Services;



public sealed class WindowsSessionProcessHost : ISessionProcessHost

{

    public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken) => Task.Run(async () =>

    {

        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Session execution currently requires Windows.");

        var path = Path.GetFullPath(app.ExecutablePath.Trim());

        using var self = Process.GetCurrentProcess();

        var existing = new List<ITrackedProcess>();

        var candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path));

        try

        {

            var unreadable = false;

            foreach (var candidate in candidates)

            {

                cancellationToken.ThrowIfCancellationRequested();

                try

                {

                    if (candidate.HasExited || candidate.SessionId != self.SessionId) continue;

                    var actualPath = WindowsAppPresenceService.GetProcessPath(candidate.Id);

                    if (actualPath is null) { unreadable |= !candidate.HasExited; continue; }

                    if (!string.Equals(path, actualPath, StringComparison.OrdinalIgnoreCase)) continue;

                    var retained = WindowsProcessIdentity.Open(candidate.Id);

                    if (retained.HasExited || retained.SessionId != self.SessionId ||

                        !string.Equals(retained.Path, path, StringComparison.OrdinalIgnoreCase))

                    { retained.Dispose(); continue; }

                    existing.Add(new WindowsTrackedApp(retained, owned: false));

                }

                catch (InvalidOperationException) { }

                catch (Win32Exception) { unreadable = true; }

            }

            if (existing.Count > 0) return new ProcessAcquisition(existing, false, "Already open · stays open when this Session ends");

            if (unreadable) throw new InvalidOperationException("Windows couldn't verify an existing app of this name. It was left alone.");

        }

        catch { foreach (var process in existing) process.Dispose(); throw; }

        finally { foreach (var candidate in candidates) candidate.Dispose(); }



        var info = WindowsProcessStarter.CreateStartInfo(app);

        cancellationToken.ThrowIfCancellationRequested();

        var requestedAt = DateTime.UtcNow.ToFileTimeUtc();

        using var started = Process.Start(info);

        if (started is null) return new ProcessAcquisition([], false, "Opened through Windows · close this app manually");

        WindowsProcessIdentity? retainedLaunch = null;

        try

        {

            // Capture immediately, before an app can complete its startup/elevation handoff.

            retainedLaunch = WindowsProcessIdentity.Open(started.Id);

            if (retainedLaunch.SessionId != self.SessionId || !string.Equals(retainedLaunch.Path, path, StringComparison.OrdinalIgnoreCase))

            {

                retainedLaunch.Dispose();

                return new ProcessAcquisition([], false, "Windows handed off to a different app · close it manually");

            }

            var owned = retainedLaunch.Created >= requestedAt;

            var tracked = new WindowsTrackedApp(retainedLaunch, owned);

            retainedLaunch = null; // Transferred to the tracked launch.

            // End during startup still receives ownership of the launch, including a pending UAC restart.

            await Task.Delay(500).ConfigureAwait(false);

            return new ProcessAcquisition([tracked], owned, owned

                ? "Opened by this Session · stops when you confirm End Session"

                : "Windows reused an existing app · stays open when this Session ends");

        }

        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)

        {

            retainedLaunch?.Dispose();

            return new ProcessAcquisition([], false, "Opened, but Windows couldn't verify ownership · close this app manually");

        }

    }, cancellationToken);

}

