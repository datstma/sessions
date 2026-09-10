using Sessions.Core;

namespace Sessions.Plugins;

/// <summary>Explicit bundled registration. An unknown or incompatible identity never falls back to an executable.</summary>
public sealed class PluginCatalog
{
    public const int ApiVersion = 1;
    private readonly IReadOnlyDictionary<string, IApplicationPlugin> _plugins;
    public IReadOnlyList<IApplicationPlugin> Plugins { get; }

    public PluginCatalog(IEnumerable<IApplicationPlugin> plugins)
    {
        Plugins = plugins.ToArray();
        if (Plugins.Any(plugin => string.IsNullOrWhiteSpace(plugin.Descriptor.Id)) ||
            Plugins.Select(plugin => plugin.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != Plugins.Count)
            throw new ArgumentException("Bundled plugins must have unique, nonempty identities.");
        _plugins = Plugins.ToDictionary(plugin => plugin.Descriptor.Id, StringComparer.Ordinal);
    }

    public string? UnavailableReason(string id, PluginConfiguration? configuration)
    {
        if (!_plugins.TryGetValue(id, out var plugin)) return $"Plugin '{id}' is missing. Your saved apps are kept.";
        if (plugin.Descriptor.ApiVersion != ApiVersion) return $"{plugin.Descriptor.Name} needs a different Sessions plugin API version.";
        if (configuration is { Version: var version } && version != plugin.Descriptor.SettingsVersion)
            return $"{plugin.Descriptor.Name} settings need a compatible plugin version. Your settings are kept.";
        if (!(configuration?.Enabled ?? plugin.Descriptor.EnabledByDefault)) return $"{plugin.Descriptor.Name} is disabled. Enable it in Settings.";
        return null;
    }

    public ISessionPluginLaunches Capture(IReadOnlyDictionary<string, PluginConfiguration> configurations, string? loadError = null) =>
        new CapturedPlugins(this, configurations.ToDictionary(pair => pair.Key,
            pair => pair.Value with { Settings = pair.Value.CaptureSettings() }, StringComparer.Ordinal), loadError);

    public async Task<PluginDiscovery> DiscoverAsync(IReadOnlyDictionary<string, PluginConfiguration> configurations,
        CancellationToken cancellationToken)
    {
        var apps = new List<PluginApp>();
        var messages = new List<string>();
        foreach (var plugin in Plugins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            configurations.TryGetValue(plugin.Descriptor.Id, out var configuration);
            if (UnavailableReason(plugin.Descriptor.Id, configuration) is { } unavailable)
            { messages.Add(unavailable); continue; }
            try
            {
                var found = await plugin.DiscoverAsync(configuration?.CaptureSettings() ?? new Dictionary<string, string>(), cancellationToken).ConfigureAwait(false);
                var validated = new List<PluginApp>();
                foreach (var app in found.Apps)
                {
                    app.Reference.Validate();
                    if (app.Reference.PluginId != plugin.Descriptor.Id) throw new InvalidDataException("The plugin returned another plugin's identity.");
                    validated.Add(app with { Reference = app.Reference.Capture() });
                }
                apps.AddRange(validated);
                if (found.Message is not null) messages.Add(found.Message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { messages.Add($"Couldn't list {plugin.Descriptor.Name} apps. {exception.Message}"); }
        }
        return new(apps.DistinctBy(app => (app.Reference.PluginId, app.Reference.TargetId)).ToArray(),
            messages.Count == 0 ? null : string.Join(" ", messages));
    }

    private sealed class CapturedPlugins(PluginCatalog catalog, IReadOnlyDictionary<string, PluginConfiguration> configurations,
        string? loadError) : ISessionPluginLaunches
    {
        private (IApplicationPlugin Plugin, IReadOnlyDictionary<string, string> Settings) Resolve(PluginAppReference app)
        {
            app.Validate();
            if (loadError is not null) throw new InvalidOperationException(loadError);
            configurations.TryGetValue(app.PluginId, out var configuration);
            if (catalog.UnavailableReason(app.PluginId, configuration) is { } reason) throw new InvalidOperationException(reason);
            return (catalog._plugins[app.PluginId], configuration?.CaptureSettings() ?? new Dictionary<string, string>());
        }

        public async Task ValidateAsync(PluginAppReference app, CancellationToken cancellationToken)
        {
            var (plugin, settings) = Resolve(app);
            if (app.CloseOnEnd && !plugin.Descriptor.SupportsClose)
                throw new InvalidOperationException("This plugin doesn't support closing apps. Turn off Close when this Session ends.");
            await plugin.ValidateAsync(app, settings, cancellationToken).ConfigureAwait(false);
        }

        public async Task<PluginAppPresence> GetPresenceAsync(PluginAppReference app, CancellationToken cancellationToken)
        {
            var (plugin, settings) = Resolve(app);
            return await plugin.GetPresenceAsync(app, settings, cancellationToken).ConfigureAwait(false);
        }

        public Task<IPreparedAppClose> PrepareCloseAsync(PluginAppReference app, CancellationToken cancellationToken)
        {
            var (plugin, settings) = Resolve(app);
            return plugin.PrepareCloseAsync(app, settings, cancellationToken);
        }

        public async Task<ProcessAcquisition> OpenAsync(PluginAppReference app, CancellationToken cancellationToken)
        {
            var (plugin, settings) = Resolve(app);
            if (app.CloseOnEnd && !plugin.Descriptor.SupportsClose)
                throw new InvalidOperationException("This plugin doesn't support closing apps.");
            var acquired = await plugin.OpenAsync(app, settings, cancellationToken).ConfigureAwait(false);
            // The host enforces consent even if a bundled implementation returns ownership incorrectly.
            return acquired with { Owned = app.CloseOnEnd && acquired.Owned };
        }

        public async Task<string> LaunchAsync(PluginAppReference app, CancellationToken cancellationToken)
        {
            var (plugin, settings) = Resolve(app);
            try { return await plugin.LaunchAsync(app, settings, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { throw new InvalidOperationException($"{plugin.Descriptor.Name} couldn't launch this app. {exception.Message}", exception); }
        }
    }
}
