using Sessions.Core;
using Sessions.Plugins.Steam;

namespace Sessions.Core.Tests;

public sealed class SteamPluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SessionsSteamTests", Guid.NewGuid().ToString("N"));
    private readonly Client _client = new();
    private SteamPlugin Plugin => new(_client);

    public SteamPluginTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "steamapps"));
        File.WriteAllText(Path.Combine(_root, "steam.exe"), "fixture, never executed");
        _client.Root = _root;
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static void Manifest(string root, string id, string name = "A game", string flags = "4", string? directory = null)
    {
        Directory.CreateDirectory(Path.Combine(root, "steamapps", "common", id));
        File.WriteAllText(Path.Combine(root, "steamapps", $"appmanifest_{id}.acf"),
            $"\"AppState\" {{ \"appid\" \"{id}\" \"name\" \"{Escape(name)}\" \"StateFlags\" \"{flags}\" \"installdir\" \"{Escape(directory ?? id)}\" }}");
    }

    [Fact]
    public async Task DiscoveryReadsMultipleLibrariesNamesAndDeduplicatesWithoutLaunchingOrWriting()
    {
        var other = Path.Combine(_root, "Other Library");
        Manifest(_root, "10", "Game \"One\"");
        Manifest(other, "20", "}");
        Manifest(other, "10", "Duplicate");
        var file = Path.Combine(_root, "steamapps", "libraryfolders.vdf");
        File.WriteAllText(file, $"// comment\n\"libraryfolders\" {{ \"0\" {{ \"path\" \"{Escape(_root)}\" }} \"1\" {{ \"path\" \"{Escape(other)}\" \"apps\" {{ \"20\" \"100\" }} }} }}");
        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var result = await Plugin.DiscoverAsync(new Dictionary<string, string>(), default);
        Assert.Equal(2, result.Apps.Count);
        Assert.Contains(result.Apps, app => app.Name == "Game \"One\"");
        Assert.Contains(result.Apps, app => app.Name == "}");
        Assert.All(result.Apps, app => Assert.Null(app.UnavailableReason));
        Assert.Empty(_client.Launches);
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task MalformedManifestAndMissingLibraryDoNotHideHealthyGame()
    {
        Manifest(_root, "10");
        File.WriteAllText(Path.Combine(_root, "steamapps", "appmanifest_20.acf"), "\"AppState\" {");
        File.WriteAllText(Path.Combine(_root, "steamapps", "libraryfolders.vdf"),
            $"libraryfolders {{ 1 \"{Escape(Path.Combine(_root, "disconnected"))}\" }}");
        var result = await Plugin.DiscoverAsync(new Dictionary<string, string>(), default);
        Assert.Equal("10", Assert.Single(result.Apps).Reference.TargetId);
        Assert.Contains("manifest", result.Message);
        Assert.Contains("unavailable", result.Message);
    }

    [Theory]
    [InlineData("../outside", "4")]
    [InlineData("10", "2")]
    public async Task UnavailableOrEscapingInstallationCannotLaunch(string directory, string flags)
    {
        Manifest(_root, "10", directory: directory, flags: flags);
        var result = await Plugin.DiscoverAsync(new Dictionary<string, string>(), default);
        Assert.NotNull(Assert.Single(result.Apps).UnavailableReason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Plugin.LaunchAsync(new(SteamPlugin.Id, "10"), new Dictionary<string, string>(), default));
        Assert.Empty(_client.Launches);
    }

    [Fact]
    public async Task ValidLaunchPassesOnlyStableAppIdentityAndHandlesUninstallMoveCancellationAndMissingClient()
    {
        Manifest(_root, "10");
        var plugin = Plugin;
        var reference = new PluginAppReference(SteamPlugin.Id, "10");
        var settings = new Dictionary<string, string>();
        Assert.Contains("running status", await plugin.LaunchAsync(reference, settings, default));
        Assert.Equal((_root, 10u), Assert.Single(_client.Launches));
        File.Delete(Path.Combine(_root, "steamapps", "appmanifest_10.acf"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.LaunchAsync(reference, settings, default));
        var moved = Path.Combine(_root, "moved");
        Manifest(moved, "10");
        File.WriteAllText(Path.Combine(_root, "steamapps", "libraryfolders.vdf"), $"libraryfolders {{ 1 {{ path \"{Escape(moved)}\" }} }}");
        await plugin.LaunchAsync(reference, settings, default);
        Assert.Equal(2, _client.Launches.Count);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plugin.LaunchAsync(reference, settings, cancelled.Token));
        File.Delete(Path.Combine(_root, "steam.exe"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.LaunchAsync(reference, settings, default));
        Assert.Equal(2, _client.Launches.Count);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("10 --other", 1)]
    [InlineData("010", 1)]
    [InlineData("10", 2)]
    public async Task UnsupportedTargetsAndVersionsNeverReachClient(string id, int version)
    {
        Manifest(_root, "10");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Plugin.LaunchAsync(new(SteamPlugin.Id, id, version), new Dictionary<string, string>(), default));
        Assert.Empty(_client.Launches);
    }

    [Fact]
    public async Task ExplicitInstallationFolderWorksWithoutRegistryDiscovery()
    {
        Manifest(_root, "10");
        _client.Root = null;
        var result = await Plugin.DiscoverAsync(new Dictionary<string, string> { ["installationPath"] = _root }, default);
        Assert.Single(result.Apps);
        Assert.Empty(_client.Launches);
    }

    private sealed class Client : ISteamClient
    {
        public string? Root;
        public List<(string, uint)> Launches = [];
        public string? FindInstallation() => Root;
        public Task LaunchAsync(string installation, uint appId, CancellationToken cancellationToken)
        { Launches.Add((installation, appId)); return Task.CompletedTask; }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
