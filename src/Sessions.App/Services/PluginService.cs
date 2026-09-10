using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Sessions.Core;
using Sessions.Plugins;

namespace Sessions.App.Services;

/// <summary>Serializes local configuration writes. Captured runs keep their settings across later edits.</summary>
public sealed partial class PluginService(PluginCatalog catalog, IPluginPreferencesStore? store = null) : ObservableObject, ISessionPluginHost, IAppSource
{
    public PluginCatalog Catalog { get; } = catalog;
    [ObservableProperty] private IReadOnlyDictionary<string, PluginConfiguration> _current = new Dictionary<string, PluginConfiguration>();
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _hasLoadError;
    [ObservableProperty] private string? _errorMessage;

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Current = store is null ? new Dictionary<string, PluginConfiguration>() : await store.LoadAsync();
            IsLoaded = true;
            HasLoadError = false;
            ErrorMessage = null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            HasLoadError = true;
            ErrorMessage = "Plugin preferences couldn't be loaded. Plugin launching is unavailable. Try loading again, or reset plugin preferences to keep a backup and start fresh. " + exception.Message;
        }
        finally { IsBusy = false; }
    }

    public async Task<bool> SaveAsync(IReadOnlyDictionary<string, PluginConfiguration> configurations, bool reset = false)
    {
        // A reset before the first read must not overwrite an unreadable file without preserving it.
        if (IsBusy || !IsLoaded && !HasLoadError || HasLoadError && !reset) return false;
        IsBusy = true;
        try
        {
            var captured = configurations.ToDictionary(pair => pair.Key, pair => pair.Value with { Settings = pair.Value.CaptureSettings() }, StringComparer.Ordinal);
            if (store is not null) await store.SaveAsync(captured, HasLoadError);
            Current = captured;
            IsLoaded = true;
            HasLoadError = false;
            ErrorMessage = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ErrorMessage = "Plugin preferences couldn't be saved. Your applied settings and active run are unchanged. " + exception.Message;
            return false;
        }
        finally { IsBusy = false; }
    }

    public ISessionPluginLaunches Capture() => Catalog.Capture(Current,
        !IsLoaded || HasLoadError ? "Plugin preferences are unavailable. Open Settings → Plugins and try loading again." : null);

    public async Task<IReadOnlyList<DiscoveredApp>> GetAppsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsLoaded || HasLoadError) throw new IOException("Plugin preferences are unavailable. Open Settings → Plugins and try loading again.");
        var result = await Catalog.DiscoverAsync(Current, cancellationToken);
        var apps = result.Apps.Select(app => new DiscoveredApp(app.Name, null, UnavailableReason: app.UnavailableReason,
            Origin: AppDiscoveryOrigin.Plugin, Location: app.Reference.PluginId, Plugin: app.Reference)).ToList();
        if (result.Message is { } message) apps.Add(new("Plugin discovery", null, UnavailableReason: message, Origin: AppDiscoveryOrigin.Plugin));
        return apps;
    }
}
