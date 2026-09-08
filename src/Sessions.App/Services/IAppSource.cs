using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sessions.App.Services;

// Discovery is configuration assistance, not runtime process ownership. No process identity is persisted.
public enum AppDiscoveryOrigin { Running, StartMenu, Browsed }
public sealed record DiscoveredApp(string Name, string? ExecutablePath, byte[]? IconPng = null, string? UnavailableReason = null,
    string Arguments = "", string WorkingDirectory = "", bool RunAsAdministrator = false,
    AppDiscoveryOrigin Origin = AppDiscoveryOrigin.Running, string Location = "");

public interface IAppSource
{
    Task<IReadOnlyList<DiscoveredApp>> GetAppsAsync(CancellationToken cancellationToken = default);
}
