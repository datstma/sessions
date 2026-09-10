using System.Text.Json;

namespace Sessions.Core;

/// <summary>Portable plugin identity and opaque, versioned configuration; never a process identity.</summary>
public sealed record PluginAppReference(string PluginId, string TargetId, int Version = 1, JsonElement? Settings = null,
    bool CloseOnEnd = false)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PluginId) || string.IsNullOrWhiteSpace(TargetId) || Version < 1)
            throw new ArgumentException("A plugin app needs a plugin identity, target identity and settings version.");
        if (Settings is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("Plugin settings must contain valid JSON.");
    }

    public PluginAppReference Capture() => this with { Settings = Settings?.Clone() };
}

/// <summary>Captures enabled plugins and preferences once for the lifetime of a run.</summary>
public interface ISessionPluginHost
{
    ISessionPluginLaunches Capture();
}

public enum PluginAppPresence { Unknown, NotRunning, Running }

/// <summary>Captured plugin operations. Cleanup requires opt-in and verified process lifetimes.</summary>
public interface ISessionPluginLaunches
{
    Task ValidateAsync(PluginAppReference app, CancellationToken cancellationToken);
    Task<string> LaunchAsync(PluginAppReference app, CancellationToken cancellationToken);
    Task<IPreparedAppClose> PrepareCloseAsync(PluginAppReference app, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This plugin can't verify this app for manual closing. Close it from its own window.");
    Task<PluginAppPresence> GetPresenceAsync(PluginAppReference app, CancellationToken cancellationToken) =>
        Task.FromResult(PluginAppPresence.Unknown);
    async Task<ProcessAcquisition> OpenAsync(PluginAppReference app, CancellationToken cancellationToken)
    {
        if (app.CloseOnEnd) throw new InvalidOperationException("This plugin doesn't support closing apps. Turn off Close when this Session ends.");
        return new([], false, await LaunchAsync(app, cancellationToken).ConfigureAwait(false));
    }
}
