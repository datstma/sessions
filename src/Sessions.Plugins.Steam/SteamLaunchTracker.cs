using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Sessions.Core;

namespace Sessions.Plugins.Steam;

public sealed record SteamProcessEvent(int ProcessId, DateTime AddedAtUtc);
public sealed record SteamProcessCapture(string Path, uint SessionId, long Created, ITrackedProcess Process);

/// <summary>A read-only log cursor and process snapshot captured before a launch request.</summary>
public interface ISteamLaunchObservation : IDisposable
{
    uint SessionId { get; }
    bool HasExistingAppProcesses => false;
    IReadOnlySet<int> ExistingProcessIds { get; }
    IReadOnlyList<SteamProcessEvent> ReadAddedProcesses(uint appId);
    SteamProcessCapture? Capture(int processId);
}

public static class SteamProcessLog
{
    // Ignore command lines entirely. Steam association is supplemented by retained Windows identity checks.
    public static IReadOnlyList<SteamProcessEvent> ReadAdditions(string text, uint appId) => ReadEntries(text, appId, false);
    public static IReadOnlyList<SteamProcessEvent> ReadCurrentProcesses(string text, uint appId) => ReadEntries(text, appId, true);

    private static IReadOnlyList<SteamProcessEvent> ReadEntries(string text, uint appId, bool currentOnly)
    {
        var result = new List<SteamProcessEvent>();
        foreach (var line in text.Split('\n'))
        {
            if (currentOnly)
            {
                var removed = Regex.Match(line, @"^\[[^\]]+\] AppID (\d+) no longer tracking PID (\d+)(?:,|\s|$)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if (removed.Success && uint.TryParse(removed.Groups[1].Value, out var removedApp) && removedApp == appId &&
                    int.TryParse(removed.Groups[2].Value, out var removedPid)) result.RemoveAll(item => item.ProcessId == removedPid);
                var ended = Regex.Match(line, @"^\[[^\]]+\] Remove (\d+) from running list(?:\s|$)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if (ended.Success && uint.TryParse(ended.Groups[1].Value, out var endedApp) && endedApp == appId) result.Clear();
            }
            var match = Regex.Match(line, @"^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)\] AppID (\d+) adding PID (\d+) as a tracked process(?:\s|$)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success || !uint.TryParse(match.Groups[2].Value, out var id) || id != appId ||
                !int.TryParse(match.Groups[3].Value, out var pid) || pid <= 0 ||
                !DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var timestamp)) continue;
            if (currentOnly) result.RemoveAll(item => item.ProcessId == pid);
            result.Add(new(pid, timestamp.ToUniversalTime()));
        }
        return result;
    }
}

/// <summary>Bounded launch correlation, never continuous process adoption or client/tree termination.</summary>
public sealed class SteamLaunchTracker(TimeSpan? captureTimeout = null, TimeSpan? settleTime = null)
{
    public async Task<ProcessAcquisition> LaunchAsync(uint appId, string appDirectory, ISteamLaunchObservation observation,
        Func<Task> launch, CancellationToken cancellationToken)
    {
        var retained = new List<ITrackedProcess>();
        var seen = new HashSet<(int, long)>();
        var root = Path.GetFullPath(appDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var requestedAt = DateTime.UtcNow.ToFileTimeUtc();
        cancellationToken.ThrowIfCancellationRequested();
        if (observation.HasExistingAppProcesses)
            return new([], false, "App processes were already open · left open independently of this Session");
        await launch().ConfigureAwait(false);
        // An issued launch must return retained ownership even if End/cancellation arrives during capture.
        var timer = Stopwatch.StartNew();
        string? error = null;
        try
        {
            do
            {
                foreach (var added in observation.ReadAddedProcesses(appId))
                {
                    if (observation.ExistingProcessIds.Contains(added.ProcessId)) continue;
                    var candidate = observation.Capture(added.ProcessId);
                    if (candidate is null) continue;
                    var accepted = false;
                    try
                    {
                        var created = DateTime.FromFileTimeUtc(candidate.Created);
                        if (candidate.SessionId != observation.SessionId || candidate.Created < requestedAt ||
                            !candidate.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                            created > added.AddedAtUtc.AddSeconds(1) || created < added.AddedAtUtc.AddSeconds(-10) ||
                            candidate.Process.HasExited || !seen.Add((added.ProcessId, candidate.Created))) continue;
                        retained.Add(candidate.Process);
                        accepted = true;
                    }
                    finally { if (!accepted) candidate.Process.Dispose(); }
                }
                if (timer.Elapsed >= (settleTime ?? TimeSpan.FromSeconds(5)) && retained.Any(process => process.HasWindow)) break;
                await Task.Delay(100).ConfigureAwait(false);
            } while (timer.Elapsed < (captureTimeout ?? TimeSpan.FromSeconds(30)));
        }
        catch (Exception exception) { error = exception.Message; }
        // Close window-owning children before their launchers. All targets are fixed at this boundary.
        retained.Reverse();
        return new(retained, retained.Count > 0, retained.Count > 0
            ? "Opened through Steam · verified processes will be asked to close when this Session ends" +
                (error is null ? "" : " · Some tracking was unavailable; check the app after ending")
            : "Launch requested through Steam · ownership could not be verified; close this app manually" +
                (error is null ? "" : ". " + error));
    }
}
