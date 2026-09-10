using Sessions.Core;

namespace Sessions.Plugins;

public sealed record PluginSetting(string Key, string Label, string Help);
public sealed record PluginDescriptor(string Id, string Name, string Version, int ApiVersion = 1,
    int SettingsVersion = 1, bool EnabledByDefault = false, bool SupportsClose = false);
public sealed record PluginConfiguration(bool Enabled, int Version = 1, IReadOnlyDictionary<string, string>? Settings = null)
{
    public IReadOnlyDictionary<string, string> CaptureSettings() =>
        (Settings ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
public sealed record PluginApp(string Name, PluginAppReference Reference, string? UnavailableReason = null);
public sealed record PluginDiscovery(IReadOnlyList<PluginApp> Apps, string? Message = null);

/// <summary>
/// Bundled plugin contract. Optional cleanup requires verified process lifetimes and explicit opt-in;
/// receive captured settings, and must honor cancellation without abandoning an issued launch.
/// No arbitrary UI or dynamic third-party assembly loading is exposed.
/// </summary>
public interface IApplicationPlugin
{
    PluginDescriptor Descriptor { get; }
    IReadOnlyList<PluginSetting> Settings { get; }
    Task<PluginDiscovery> DiscoverAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken);
    Task ValidateAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken);
    Task<string> LaunchAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken);
    Task<IPreparedAppClose> PrepareCloseAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This plugin doesn't support manual closing. Close the app from its own window.");
    Task<PluginAppPresence> GetPresenceAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) =>
        Task.FromResult(PluginAppPresence.Unknown);
    async Task<ProcessAcquisition> OpenAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        if (app.CloseOnEnd) throw new InvalidOperationException("This plugin doesn't support closing apps.");
        return new([], false, await LaunchAsync(app, settings, cancellationToken).ConfigureAwait(false));
    }
}
