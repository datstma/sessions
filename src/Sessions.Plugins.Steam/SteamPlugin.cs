using System.Globalization;
using Sessions.Core;

namespace Sessions.Plugins.Steam;

public interface ISteamClient
{
    string? FindInstallation();
    Task LaunchAsync(string installation, uint appId, CancellationToken cancellationToken);
    Task<IPreparedAppClose> PrepareCloseAsync(string installation, string appDirectory, uint appId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Steam process identities are unavailable. Close the app from Steam.");
    Task<PluginAppPresence> GetPresenceAsync(uint appId, CancellationToken cancellationToken) => Task.FromResult(PluginAppPresence.Unknown);
    async Task<ProcessAcquisition> OpenAsync(string installation, string appDirectory, uint appId, bool closeOnEnd, CancellationToken cancellationToken)
    {
        await LaunchAsync(installation, appId, cancellationToken).ConfigureAwait(false);
        return new([], false, "Launch requested through Steam · close this app manually");
    }
}

/// <summary>Reads local manifests only. Steam performs activation; its process is never owned as a game.</summary>
public sealed class SteamPlugin(ISteamClient client) : IApplicationPlugin
{
    public const string Id = "sessions.steam";
    public PluginDescriptor Descriptor { get; } = new(Id, "Steam", "1.1.0", EnabledByDefault: true, SupportsClose: true);
    public IReadOnlyList<PluginSetting> Settings { get; } =
        [new("installationPath", "Steam installation folder", "Leave blank to find Steam automatically. Choose the folder containing steam.exe.")];

    private string Installation(IReadOnlyDictionary<string, string> settings)
    {
        var root = settings.TryGetValue("installationPath", out var path) && !string.IsNullOrWhiteSpace(path)
            ? path.Trim() : client.FindInstallation();
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || !File.Exists(Path.Combine(root, "steam.exe")))
            throw new InvalidOperationException("Steam could not be found. Install Steam or set its installation folder in Settings → Plugins.");
        return Path.GetFullPath(root);
    }

    public Task<PluginDiscovery> DiscoverAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) =>
        Task.Run(() => Discover(Installation(settings), cancellationToken), cancellationToken);

    private static PluginDiscovery Discover(string root, CancellationToken token, Dictionary<string, string>? directories = null)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var warnings = new List<string>();
        var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            try
            {
                var folders = SteamKeyValues.Read(libraryFile).Object("libraryfolders");
                if (folders is null) throw new InvalidDataException("Missing libraryfolders section.");
                foreach (var (key, value) in folders.Values)
                {
                    token.ThrowIfCancellationRequested();
                    if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out _)) continue;
                    var path = value is SteamKeyValues node ? node.Text("path") : value as string;
                    if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)) libraries.Add(Path.GetFullPath(path));
                    else warnings.Add("A Steam library path could not be read.");
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            { warnings.Add("Some Steam libraries could not be read. " + exception.Message); }
        }
        var apps = new List<PluginApp>();
        foreach (var library in libraries)
        {
            token.ThrowIfCancellationRequested();
            var steamapps = Path.Combine(library, "steamapps");
            try
            {
                if (!Directory.Exists(steamapps)) { warnings.Add("A Steam library is unavailable. Reconnect its drive and refresh."); continue; }
                foreach (var file in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf").Take(4096))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var state = SteamKeyValues.Read(file).Object("AppState") ?? throw new InvalidDataException("Missing AppState section.");
                        var id = state.Text("appid");
                        if (!uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) || appId == 0 ||
                            Path.GetFileName(file) != $"appmanifest_{appId}.acf") throw new InvalidDataException("Invalid Steam app identity.");
                        var name = state.Text("name");
                        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Missing Steam app name.");
                        var directory = state.Text("installdir");
                        var common = Path.GetFullPath(Path.Combine(steamapps, "common")) + Path.DirectorySeparatorChar;
                        var installed = string.IsNullOrWhiteSpace(directory) ? "" : Path.GetFullPath(Path.Combine(common, directory));
                        var available = installed.StartsWith(common, StringComparison.OrdinalIgnoreCase) && Directory.Exists(installed) &&
                            uint.TryParse(state.Text("StateFlags"), NumberStyles.None, CultureInfo.InvariantCulture, out var flags) && (flags & 4) != 0;
                        apps.Add(new(name, new(Id, appId.ToString(CultureInfo.InvariantCulture)), available ? null :
                            "Steam installation is incomplete or unavailable. Finish installing or reconnect the library, then refresh."));
                        if (available) directories?.TryAdd(appId.ToString(CultureInfo.InvariantCulture), installed);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                    { warnings.Add("A Steam app manifest could not be read. " + exception.Message); }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { warnings.Add("A Steam library could not be read. " + exception.Message); }
        }
        return new(apps.GroupBy(app => app.Reference.TargetId).Select(group => group.OrderBy(app => app.UnavailableReason is not null).First())
                .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            warnings.Count == 0 ? null : string.Join(" ", warnings.Distinct().Take(5)));
    }

    public async Task ValidateAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        if (app.PluginId != Id || app.Version != 1 || app.Settings is not null ||
            !uint.TryParse(app.TargetId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0 ||
            app.TargetId != id.ToString(CultureInfo.InvariantCulture))
            throw new InvalidOperationException("This Steam app uses unsupported settings. Your saved entry is kept; use a compatible plugin version.");
        var found = await DiscoverAsync(settings, cancellationToken).ConfigureAwait(false);
        var game = found.Apps.FirstOrDefault(item => item.Reference.TargetId == app.TargetId);
        if (game is null) throw new InvalidOperationException("This Steam app is not installed in an available library. Install it or reconnect its drive, then retry.");
        if (game.UnavailableReason is { } reason) throw new InvalidOperationException(reason);
    }

    public async Task<string> LaunchAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        await ValidateAsync(app, settings, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await client.LaunchAsync(Installation(settings), uint.Parse(app.TargetId, CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        return "Launch requested through Steam · waiting for Steam's running status";
    }

    public Task<PluginAppPresence> GetPresenceAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) =>
        app.PluginId == Id && app.Version == 1 && uint.TryParse(app.TargetId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? client.GetPresenceAsync(id, cancellationToken) : Task.FromResult(PluginAppPresence.Unknown);

    public async Task<ProcessAcquisition> OpenAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        await ValidateAsync(app, settings, cancellationToken).ConfigureAwait(false);
        var root = Installation(settings);
        var directories = new Dictionary<string, string>();
        await Task.Run(() => Discover(root, cancellationToken, directories), cancellationToken).ConfigureAwait(false);
        if (!directories.TryGetValue(app.TargetId, out var directory)) throw new InvalidOperationException("This Steam app is no longer available. Refresh its library.");
        return await client.OpenAsync(root, directory, uint.Parse(app.TargetId, CultureInfo.InvariantCulture), app.CloseOnEnd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IPreparedAppClose> PrepareCloseAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        await ValidateAsync(app, settings, cancellationToken).ConfigureAwait(false);
        var root = Installation(settings);
        var directories = new Dictionary<string, string>();
        await Task.Run(() => Discover(root, cancellationToken, directories), cancellationToken).ConfigureAwait(false);
        if (!directories.TryGetValue(app.TargetId, out var directory)) throw new InvalidOperationException("This Steam app is no longer available.");
        return await client.PrepareCloseAsync(root, directory, uint.Parse(app.TargetId, CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
    }
}
