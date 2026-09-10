using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Sessions.Plugins.Steam;
using Sessions.Core;

namespace Sessions.App.Services;

public sealed class WindowsSteamClient : ISteamClient
{
    public async Task<IPreparedAppClose> PrepareCloseAsync(string installation, string appDirectory, uint appId, CancellationToken cancellationToken)
    {
        var presence = await GetPresenceAsync(appId, cancellationToken).ConfigureAwait(false);
        if (presence == PluginAppPresence.NotRunning) return new PreparedAppClose([]);
        if (presence != PluginAppPresence.Running) throw new InvalidOperationException("Steam's running state couldn't be checked. Close this app from its own window.");
        return await Task.Run(() => WindowsSteamManualClose.Prepare(installation, appDirectory, appId, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public Task<PluginAppPresence> GetPresenceAsync(uint appId, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (!OperatingSystem.IsWindows()) return PluginAppPresence.Unknown;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\Apps\" + appId.ToString(CultureInfo.InvariantCulture));
            return key?.GetValue("Running") switch { 1 => PluginAppPresence.Running, 0 => PluginAppPresence.NotRunning, _ => PluginAppPresence.Unknown };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return PluginAppPresence.Unknown; }
    }, cancellationToken);

    public async Task<ProcessAcquisition> OpenAsync(string installation, string appDirectory, uint appId, bool closeOnEnd, CancellationToken cancellationToken)
    {
        var presence = await GetPresenceAsync(appId, cancellationToken).ConfigureAwait(false);
        if (presence == PluginAppPresence.Running)
            return new([], false, "Already running in Steam · stays open when this Session ends");
        if (!closeOnEnd || presence != PluginAppPresence.NotRunning)
        {
            await LaunchAsync(installation, appId, cancellationToken).ConfigureAwait(false);
            return new([], false, closeOnEnd ? "Launch requested · previous running state was unavailable; close this app manually"
                : "Launch requested through Steam · close this app manually");
        }
        ISteamLaunchObservation observation;
        try { observation = await Task.Run(() => new WindowsSteamLaunchObservation(installation, appDirectory), cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            await LaunchAsync(installation, appId, cancellationToken).ConfigureAwait(false);
            return new([], false, "Launch requested · process tracking was unavailable; close this app manually");
        }
        using (observation)
            return await new SteamLaunchTracker().LaunchAsync(appId, appDirectory, observation,
                () => LaunchAsync(installation, appId, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public string? FindInstallation()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") as string;
    }

    public Task LaunchAsync(string installation, uint appId, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Steam launching currently requires Windows.");
        cancellationToken.ThrowIfCancellationRequested();
        // Desktop shell activation preserves launch independence. The returned client handle is never a game handle.
        using var process = Process.Start(CreateStartInfo(installation, appId));
    }, cancellationToken);

    internal static ProcessStartInfo CreateStartInfo(string installation, uint appId)
    {
        if (appId == 0 || !Path.IsPathFullyQualified(installation)) throw new ArgumentException("Choose a valid Steam app and installation folder.");
        return new ProcessStartInfo
        {
            FileName = Path.Combine(installation, "steam.exe"),
            Arguments = "steam://run/" + appId.ToString(CultureInfo.InvariantCulture),
            WorkingDirectory = installation,
            UseShellExecute = true,
            Verb = "open"
        };
    }
}
