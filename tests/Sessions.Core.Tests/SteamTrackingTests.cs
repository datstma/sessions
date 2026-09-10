using Sessions.Plugins.Steam;

namespace Sessions.Core.Tests;

public sealed class SteamTrackingTests
{
    [Fact]
    public async Task ExistingAppProcessesBlockOwnershipEvenWhenSteamFlagWasClear()
    {
        using var observation = new Observation { HasExistingAppProcesses = true };
        var result = await new SteamLaunchTracker().LaunchAsync(10, Path.GetTempPath(), observation,
            () => throw new InvalidOperationException("Must not launch a duplicate"), default);
        Assert.False(result.Owned);
        Assert.Empty(result.Processes);
        Assert.Contains("already open", result.Message);
    }
    [Fact]
    public void LogParserUsesOnlyMatchingAppAdditionsAndIgnoresCommandLines()
    {
        var result = SteamProcessLog.ReadAdditions("""
            [2026-09-10 19:09:15] AppID 223850 adding PID 46732 as a tracked process "anything here"
            [2026-09-10 19:09:16] AppID 20 adding PID 22 as a tracked process
            [2026-09-10 19:09:17] AppID 223850 no longer tracking PID 46732, exit code 0
            [2026-09-10 19:09:18] AppID 223850 adding PID 48604 as a tracked process
            [bad] AppID 223850 adding PID 999 as a tracked process
            """, 223850);
        Assert.Equal(new[] { 46732, 48604 }, result.Select(item => item.ProcessId));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("preexisting")]
    [InlineData("older")]
    [InlineData("outside")]
    [InlineData("prefix")]
    [InlineData("other-session")]
    [InlineData("reused-pid")]
    [InlineData("exited")]
    public async Task CleanupCaptureRequiresNewMatchingProcessLifetime(string condition)
    {
        var process = new Tracked { Exited = condition == "exited" };
        var root = Path.Combine(Path.GetTempPath(), "steam-game");
        using var observation = new Observation();
        if (condition == "preexisting") observation.Existing.Add(42);
        var tracker = new SteamLaunchTracker(TimeSpan.FromMilliseconds(20), TimeSpan.Zero);
        var acquired = await tracker.LaunchAsync(10, root, observation, () =>
        {
            var now = DateTime.UtcNow;
            observation.Events.Add(new(42, condition == "reused-pid" ? now.AddMinutes(-1) : now));
            observation.Candidate = new(condition == "outside" ? Path.Combine(Path.GetTempPath(), "steam.exe") :
                Path.Combine(condition == "prefix" ? root + "-other" : root, "app.exe"),
                condition == "other-session" ? 99u : 1u, condition == "older" ? now.AddSeconds(-2).ToFileTimeUtc() : now.ToFileTimeUtc(), process);
            return Task.CompletedTask;
        }, default);
        Assert.Equal(condition == "valid", acquired.Owned);
        if (condition == "valid")
        {
            Assert.Same(process, Assert.Single(acquired.Processes));
            await process.RequestCloseAsync(TimeSpan.Zero);
            Assert.True(process.Closed);
        }
        else { Assert.Empty(acquired.Processes); Assert.False(process.Closed); }
        foreach (var captured in acquired.Processes) captured.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrObservationFailureAfterLaunchRetainsAcquiredCleanup(bool fail)
    {
        using var cancellation = new CancellationTokenSource();
        using var observation = new Observation();
        var process = new Tracked { Window = false };
        var root = Path.Combine(Path.GetTempPath(), "steam-game");
        var acquired = await new SteamLaunchTracker(TimeSpan.FromMilliseconds(250), TimeSpan.Zero).LaunchAsync(10, root, observation, () =>
        {
            var now = DateTime.UtcNow;
            observation.Events.Add(new(42, now));
            observation.Candidate = new(Path.Combine(root, "app.exe"), 1, now.ToFileTimeUtc(), process);
            if (fail) observation.FailAfterRead = true; else cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token);
        Assert.True(acquired.Owned);
        Assert.Same(process, Assert.Single(acquired.Processes));
        Assert.False(process.Disposed);
        process.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunnerEnforcesOptInAndWaitsForPluginCleanup(bool optIn)
    {
        var process = new Tracked();
        var runner = new SessionRunner(new NoProcesses(), plugins: new Host(process));
        await runner.StartAsync(new(Guid.NewGuid(), "Steam", "", [new(Guid.NewGuid(), "Game", "", Plugin: new("test", "10", CloseOnEnd: optIn))]));
        Assert.Equal(optIn, Assert.Single(runner.Snapshot!.Apps).Owned);
        await runner.EndAsync();
        Assert.Equal(optIn, process.Closed);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task CloseChoiceRoundTripsAndDefaultsOffForExistingPluginLibraries()
    {
        var path = Path.Combine(Path.GetTempPath(), "Sessions-plugin-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new JsonSessionStore(path);
            await store.SaveAsync([new(Guid.NewGuid(), "Steam", "", [new(Guid.NewGuid(), "Game", "", Plugin: new("sessions.steam", "223850", CloseOnEnd: true))])]);
            Assert.True((await store.LoadAsync())[0].Apps[0].Plugin!.CloseOnEnd);
            var text = await File.ReadAllTextAsync(path);
            // JSON whitespace is incidental; remove the optional field structurally for the legacy fixture.
            var json = System.Text.Json.Nodes.JsonNode.Parse(text)!;
            json["sessions"]![0]!["apps"]![0]!["plugin"]!.AsObject().Remove("closeOnEnd");
            await File.WriteAllTextAsync(path, json.ToJsonString());
            Assert.False((await store.LoadAsync())[0].Apps[0].Plugin!.CloseOnEnd);
        }
        finally { File.Delete(path); }
    }

    private sealed class Observation : ISteamLaunchObservation
    {
        public uint SessionId => 1;
        public bool HasExistingAppProcesses { get; set; }
        public HashSet<int> Existing = [];
        public IReadOnlySet<int> ExistingProcessIds => Existing;
        public List<SteamProcessEvent> Events = [];
        public SteamProcessCapture? Candidate;
        public bool FailAfterRead;
        private bool _read;
        public IReadOnlyList<SteamProcessEvent> ReadAddedProcesses(uint appId)
        { if (_read && FailAfterRead) throw new IOException("log unavailable"); if (_read) return []; _read = true; return Events; }
        public SteamProcessCapture? Capture(int processId) => Candidate;
        public void Dispose() { }
    }
    private sealed class Tracked : ITrackedProcess
    {
        public bool Closed, Exited, Disposed;
        public bool Window = true;
        public bool HasExited => Exited || Closed;
        public bool HasWindow => Window;
        public Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false) { Closed = true; return Task.FromResult(true); }
        public void Dispose() => Disposed = true;
    }
    private sealed class Host(Tracked process) : ISessionPluginHost, ISessionPluginLaunches
    {
        public ISessionPluginLaunches Capture() => this;
        public Task ValidateAsync(PluginAppReference app, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> LaunchAsync(PluginAppReference app, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<ProcessAcquisition> OpenAsync(PluginAppReference app, CancellationToken cancellationToken) => Task.FromResult(new ProcessAcquisition([process], true, "tracked"));
    }
    private sealed class NoProcesses : ISessionProcessHost
    { public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
}
