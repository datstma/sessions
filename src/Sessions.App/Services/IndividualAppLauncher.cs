using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sessions.Core;

namespace Sessions.App.Services;

public sealed class IndividualAppLauncher(IAppPresenceService presenceService, IProcessStarter processStarter,
    TimeProvider? timeProvider = null, ISessionPluginHost? plugins = null) : IIndividualAppLauncher
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Plugin, string Target), DateTimeOffset> _pluginPending = [];

    public async Task<AppLaunchResult> LaunchAsync(StartProcessAction app, CancellationToken cancellationToken = default)
    {
        var path = app.Plugin is not null ? "" : Path.GetFullPath(app.ExecutablePath.Trim());
        await _launchGate.WaitAsync(cancellationToken);
        try
        {
            if (app.Plugin is { } reference)
            {
                foreach (var expired in _pluginPending.Where(item => item.Value <= _time.GetUtcNow()).Select(item => item.Key).ToArray())
                    _pluginPending.Remove(expired);
                var target = (reference.PluginId, reference.TargetId);
                if (_pluginPending.TryGetValue(target, out var waitingUntil))
                    return new(AppPresence.Unknown, waitingUntil, "Launch already requested. Check the provider before trying again.");
                var captured = plugins?.Capture() ?? throw new InvalidOperationException("This plugin is unavailable. Open Settings → Plugins.");
                // Use the same validated model constraints as Session startup, even for direct callers.
                new SessionDefinition(Guid.NewGuid(), "Individual launch", "", [app]).Validate();
                await captured.ValidateAsync(reference, cancellationToken);
                if (await captured.GetPresenceAsync(reference, cancellationToken) == PluginAppPresence.Running)
                    return new(AppPresence.Background, Message: "Already running in Steam · left open independently of this Session");
                var message = await captured.LaunchAsync(reference, cancellationToken);
                var until = _time.GetUtcNow().AddSeconds(10);
                _pluginPending[target] = until;
                return new(AppPresence.Unknown, until, message);
            }
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
