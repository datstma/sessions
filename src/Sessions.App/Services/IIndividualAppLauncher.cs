using System;
using System.Threading;
using System.Threading.Tasks;
using Sessions.Core;

namespace Sessions.App.Services;

public sealed record AppLaunchResult(AppPresence Presence, DateTimeOffset? PendingUntil = null, string? Message = null);

// A manual launch does not create a Session run or confer cleanup ownership.
public interface IIndividualAppLauncher
{
    Task<AppLaunchResult> LaunchAsync(StartProcessAction app, CancellationToken cancellationToken = default);
}

public interface IProcessStarter
{
    Task StartAsync(StartProcessAction app, CancellationToken cancellationToken = default);
}
