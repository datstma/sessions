using System.Text.Json;
using Sessions.Plugins;

namespace Sessions.Core.Tests;

public sealed class PluginExecutionTests
{
    private static StartProcessAction PluginApp(string target = "10") => new(Guid.NewGuid(), "Game", "", Plugin: new("test", target));
    private static SessionDefinition Definition(params StartProcessAction[] apps) => new(Guid.NewGuid(), "Session", "", apps);

    [Theory]
    [InlineData(SessionLaunchMode.InOrder)]
    [InlineData(SessionLaunchMode.Together)]
    public async Task PluginRequestsNeverReachProcessHostOrCleanup(SessionLaunchMode mode)
    {
        var launched = new List<string>();
        var plugin = new Plugin { Launch = (app, _, _) => { launched.Add(app.TargetId); return Task.FromResult("requested"); } };
        var process = new OwnedProcess();
        var host = new Host { Result = new([process], true, "owned") };
        var runner = new SessionRunner(host, plugins: new PluginHost(plugin));
        await runner.StartAsync(Definition(PluginApp(), new(Guid.NewGuid(), "Utility", "utility.exe"), PluginApp("20")) with { LaunchMode = mode });
        Assert.Equal(new[] { "10", "20" }, launched);
        Assert.Equal(1, host.Calls);
        Assert.All(runner.Snapshot!.Apps.Where(app => app.Name == "Game"), app =>
        { Assert.False(app.Owned); Assert.Equal(SessionAppState.Untracked, app.State); });
        await runner.EndAsync();
        Assert.True(process.Closed);
        Assert.Equal(SessionRunState.Completed, runner.Snapshot!.State);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("api")]
    [InlineData("settings")]
    [InlineData("load")]
    [InlineData("closing")]
    public async Task UnavailablePluginFailsBeforeOrdinaryAppsOrAudioChanges(string failure)
    {
        var plugin = new Plugin { Descriptor = new("test", "Test", "1", ApiVersion: failure == "api" ? 2 : 1, EnabledByDefault: true) };
        var catalog = new PluginCatalog(failure == "missing" ? [] : [plugin]);
        var host = new Host();
        var audio = new Audio();
        var configuration = new Dictionary<string, PluginConfiguration> { ["test"] = new(failure != "disabled", failure == "settings" ? 2 : 1) };
        var runner = new SessionRunner(host, audioDevices: audio,
            plugins: new CapturedHost(catalog.Capture(configuration, failure == "load" ? "Preferences unavailable" : null)));
        var pluginApp = PluginApp();
        if (failure == "closing") pluginApp = pluginApp with { Plugin = pluginApp.Plugin! with { CloseOnEnd = true } };
        await runner.StartAsync(Definition(new(Guid.NewGuid(), "Utility", "utility.exe"), pluginApp) with { OutputAudioDevice = new("output", "Output") });
        Assert.Equal(SessionRunState.Failed, runner.Snapshot!.State);
        Assert.Equal(0, host.Calls);
        Assert.Equal(0, audio.Calls);
    }

    [Fact]
    public async Task CapturedPluginSettingsSurviveDisableDuringStartupAndNextRunUsesNewSettings()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? observed = null;
        var plugin = new Plugin { Launch = async (_, settings, _) => { entered.SetResult(); await release.Task; observed = settings["profile"]; return "requested"; } };
        var source = new PluginHost(plugin);
        source.Configurations["test"] = new(true, Settings: new Dictionary<string, string> { ["profile"] = "original" });
        var runner = new SessionRunner(new Host(), plugins: source);
        var start = runner.StartAsync(Definition(PluginApp()));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        source.Configurations["test"] = new(false, Settings: new Dictionary<string, string> { ["profile"] = "later" });
        release.SetResult();
        await start;
        Assert.Equal("original", observed);
        await runner.EndAsync();
        await runner.StartAsync(Definition(PluginApp()));
        Assert.Equal(SessionRunState.Failed, runner.Snapshot!.State);
        Assert.Contains("disabled", runner.Snapshot.Message);
    }

    [Fact]
    public async Task EndWaitsForIssuedPluginLaunchAndStillClosesOnlyOwnedUtility()
    {
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plugin = new Plugin { Launch = (_, _, _) => { entered.SetResult(); return gate.Task; } };
        var process = new OwnedProcess();
        var runner = new SessionRunner(new Host { Result = new([process], true, "owned") }, plugins: new PluginHost(plugin));
        var start = runner.StartAsync(Definition(new(Guid.NewGuid(), "Utility", "utility.exe"), PluginApp()));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var end = runner.EndAsync();
        Assert.False(end.IsCompleted);
        Assert.False(process.Closed);
        gate.SetResult("requested");
        await Task.WhenAll(start, end).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(process.Closed);
        Assert.False(runner.Snapshot!.IsActive);
        Assert.False(runner.Snapshot.Apps[1].Owned);
    }

    [Fact]
    public async Task PluginFailureRetainsOwnedAppsUntilCleanupConfirmation()
    {
        var plugin = new Plugin { Launch = (_, _, _) => throw new IOException("provider failed") };
        var process = new OwnedProcess();
        var runner = new SessionRunner(new Host { Result = new([process], true, "owned") }, plugins: new PluginHost(plugin));
        await runner.StartAsync(Definition(new(Guid.NewGuid(), "Utility", "utility.exe"), PluginApp()));
        Assert.Equal(SessionRunState.AwaitingEndConfirmation, runner.Snapshot!.State);
        Assert.False(process.Closed);
        Assert.Contains("provider failed", runner.Snapshot.Apps[1].Message);
        await runner.EndAsync();
        Assert.True(process.Closed);
    }

    [Fact]
    public void PluginTargetsRejectExecutableCleanupReadinessMainFocusAndDuplicateRequests()
    {
        var app = PluginApp();
        foreach (var invalid in new[] { app with { ExecutablePath = "steam.exe" }, app with { Arguments = "--unsafe" },
            app with { WorkingDirectory = "folder" }, app with { RunAsAdministrator = true }, app with { AllowForceQuit = true },
            app with { Readiness = AppReadiness.ProcessRunning }, app with { Readiness = AppReadiness.WindowAppeared } })
            Assert.Throws<ArgumentException>(() => Definition(invalid).Validate());
        Assert.Throws<ArgumentException>(() => (Definition(app) with { MainAppId = app.Id }).Validate());
        Assert.Throws<ArgumentException>(() => (Definition(app) with { FocusAfterStartup = StartupFocus.App, FocusAppId = app.Id }).Validate());
        Assert.Throws<ArgumentException>(() => Definition(app, PluginApp()).Validate());
    }

    [Fact]
    public async Task UnknownPluginAndOpaqueFutureConfigurationRoundTripWithoutResolvingPlugin()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SessionsPluginTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "sessions.json");
        try
        {
            using var json = JsonDocument.Parse("{\"profile\":{\"future\":[1,true,\"keep\"]}}");
            var reference = new PluginAppReference("missing.future", "target", 9, json.RootElement.Clone());
            var store = new JsonSessionStore(path);
            await store.SaveAsync([Definition(PluginApp() with { Plugin = reference })]);
            var before = await File.ReadAllTextAsync(path);
            var loaded = Assert.Single(await store.LoadAsync());
            var retained = Assert.Single(loaded.Apps).Plugin!;
            Assert.Equal(reference.PluginId, retained.PluginId);
            Assert.Equal(9, retained.Version);
            Assert.True(JsonElement.DeepEquals(reference.Settings!.Value, retained.Settings!.Value));
            Assert.Equal(before, await File.ReadAllTextAsync(path));
            await store.SaveAsync([loaded with { Name = "Renamed" }]);
            Assert.Contains("\"version\": 6", await File.ReadAllTextAsync(path));
            await File.WriteAllTextAsync(path, before.Replace("\"version\": 6", "\"version\": 4"));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiscoveryFailureDoesNotHideHealthyPluginAndRejectsForeignIdentities()
    {
        var bad = new Plugin { Discover = (_, _) => throw new IOException("broken") };
        var good = new Plugin { Descriptor = new("good", "Good", "1", EnabledByDefault: true),
            Discover = (_, _) => Task.FromResult(new PluginDiscovery([new("Healthy", new("good", "1"))])) };
        var result = await new PluginCatalog([bad, good]).DiscoverAsync(new Dictionary<string, PluginConfiguration>(), default);
        Assert.Equal("Healthy", Assert.Single(result.Apps).Name);
        Assert.Contains("broken", result.Message);
        bad.Discover = (_, _) => Task.FromResult(new PluginDiscovery([new("Invalid", new("another", "1"))]));
        result = await new PluginCatalog([bad]).DiscoverAsync(new Dictionary<string, PluginConfiguration>(), default);
        Assert.Empty(result.Apps);
        Assert.Contains("identity", result.Message);
    }

    private sealed class Host : ISessionProcessHost
    {
        public int Calls;
        public ProcessAcquisition Result = new([], false, "untracked");
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken)
        { Calls++; Assert.Null(app.Plugin); return Task.FromResult(Result); }
    }
    private sealed class OwnedProcess : ITrackedProcess
    {
        public bool Closed;
        public bool HasExited => Closed;
        public Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false) { Closed = true; return Task.FromResult(true); }
        public void Dispose() { }
    }
    private sealed class PluginHost(Plugin plugin) : ISessionPluginHost
    {
        public Dictionary<string, PluginConfiguration> Configurations = [];
        public ISessionPluginLaunches Capture() => new PluginCatalog([plugin]).Capture(Configurations);
    }
    private sealed class CapturedHost(ISessionPluginLaunches launches) : ISessionPluginHost
    { public ISessionPluginLaunches Capture() => launches; }
    private sealed class Plugin : IApplicationPlugin
    {
        public PluginDescriptor Descriptor { get; set; } = new("test", "Test", "1", EnabledByDefault: true);
        public IReadOnlyList<PluginSetting> Settings => [];
        public Func<PluginAppReference, IReadOnlyDictionary<string, string>, CancellationToken, Task<string>> Launch = (_, _, _) => Task.FromResult("requested");
        public Func<IReadOnlyDictionary<string, string>, CancellationToken, Task<PluginDiscovery>> Discover = (_, _) => Task.FromResult(new PluginDiscovery([]));
        public Task<PluginDiscovery> DiscoverAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) => Discover(settings, cancellationToken);
        public Task ValidateAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> LaunchAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) => Launch(app, settings, cancellationToken);
    }
    private sealed class Audio : IAudioDeviceService
    {
        public int Calls;
        public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task<string?> GetDefaultAsync(AudioFlow flow, AudioRole role) { Calls++; throw new InvalidOperationException(); }
        public Task SetDefaultAsync(AudioFlow flow, AudioRole role, string deviceId) { Calls++; throw new InvalidOperationException(); }
    }
}
