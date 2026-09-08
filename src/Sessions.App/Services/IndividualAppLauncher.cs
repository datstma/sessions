using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sessions.Core;

namespace Sessions.App.Services;

public sealed class IndividualAppLauncher(IAppPresenceService presenceService, IProcessStarter processStarter,
    TimeProvider? timeProvider = null) : IIndividualAppLauncher
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _pending = new(StringComparer.OrdinalIgnoreCase);

    public async Task<AppLaunchResult> LaunchAsync(StartProcessAction app, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(app.ExecutablePath.Trim());
        await _launchGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await presenceService.GetPresenceAsync([path], cancellationToken);
            var presence = snapshot.TryGetValue(path, out var found) ? found : AppPresence.Unknown;
            if (presence is AppPresence.Window or AppPresence.Background)
            {
                _pending.Remove(path);
                return new AppLaunchResult(presence);
            }
            foreach (var expired in _pending.Where(item => item.Value <= _time.GetUtcNow()).Select(item => item.Key).ToArray())
                _pending.Remove(expired);
            if (_pending.TryGetValue(path, out var pendingUntil))
                return new AppLaunchResult(presence, pendingUntil);
            if (presence != AppPresence.NotRunning)
                throw new InvalidOperationException("Couldn't check whether this app is already running. Try again shortly.");

            await processStarter.StartAsync(app, cancellationToken);
            // A launcher may hand off to another process. Allow discovery to catch up before another launch.
            pendingUntil = _time.GetUtcNow().AddSeconds(10);
            _pending[path] = pendingUntil;
            return new AppLaunchResult(AppPresence.Checking, pendingUntil);
        }
        finally { _launchGate.Release(); }
    }
}
