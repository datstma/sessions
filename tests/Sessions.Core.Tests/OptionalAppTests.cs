using System.Collections.Concurrent;
using Sessions.Plugins;

namespace Sessions.Core.Tests;

public sealed class OptionalAppTests
{
    [Fact]
    public async Task OptionalLaunchFailureIsSkippedWithoutItsPauseAndLaterAppsStart()
    {
        var opened = new ConcurrentQueue<string>();
        var runner = new SessionRunner(new Host((app, _) =>
        {
            opened.Enqueue(app.Name);
            return app.Name == "Tracker" ? throw new IOException("The executable was not found.") : Owned();
        }));

        await runner.StartAsync(Definition(App("Tracker") with { Optional = true, PauseAfterSeconds = 300 }, App("Radio")))
            .WaitAsync(TimeSpan.FromSeconds(3));

        var snapshot = runner.Snapshot!;
        Assert.Equal(["Tracker", "Radio"], opened);
        Assert.Equal(SessionRunState.Running, snapshot.State);
        Assert.True(snapshot.StartupSucceeded);
        Assert.Equal(SessionAppState.Skipped, snapshot.Apps[0].State);
        Assert.Equal("Skipped: The executable was not found.", snapshot.Apps[0].Message);
        Assert.Equal("Your Session is active. Tracker didn't finish starting; its status explains why.", snapshot.Message);
        await runner.EndAsync();
        Assert.Equal(SessionRunState.Completed, runner.Snapshot!.State);
    }

    [Fact]
    public async Task OptionalAppThatOpensButMissesItsConditionStaysTrackedAndClosesAtEnd()
    {
        var slow = new Process { HasWindow = false };
        var runner = new SessionRunner(new Host((app, _) => app.Name == "Overlay" ? Task.FromResult(new ProcessAcquisition([slow], true, "Opened by this Session")) : Owned()));

        await runner.StartAsync(Definition(
                App("Overlay") with { Optional = true, Readiness = AppReadiness.WindowAppeared, ReadinessTimeoutSeconds = 1 },
                App("Radio"), App("Voice")))
            .WaitAsync(TimeSpan.FromSeconds(4));

        var overlay = runner.Snapshot!.Apps[0];
        Assert.Equal(SessionRunState.Running, runner.Snapshot.State);
        Assert.Equal(SessionAppState.Running, overlay.State);
        Assert.True(overlay.Owned);
        Assert.Equal("Opened by this Session · Didn't finish starting: No window appeared for Overlay within 1 seconds.", overlay.Message);
        Assert.Equal([SessionAppState.Running, SessionAppState.Running], runner.Snapshot.Apps.Skip(1).Select(app => app.State));

        await runner.EndAsync();
        Assert.True(slow.Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkippedMainAppMakesEndingManual(bool exitsBeforeReady)
    {
        var main = new Process { HasWindow = false, HasExited = exitsBeforeReady };
        var runner = new SessionRunner(new Host((app, _) => app.Name != "Sim" ? Owned()
            : exitsBeforeReady ? Task.FromResult(new ProcessAcquisition([main], true, "Opened by this Session"))
            : throw new InvalidOperationException("Windows couldn't open the app.")));
        var sim = App("Sim") with { Optional = true, Readiness = exitsBeforeReady ? AppReadiness.ProcessRunning : AppReadiness.LaunchCompleted };

        await runner.StartAsync(Definition(App("Radio"), sim) with { MainAppId = sim.Id }).WaitAsync(TimeSpan.FromSeconds(3));
        await runner.RefreshAsync();

        Assert.Equal(SessionRunState.Running, runner.Snapshot!.State);
        Assert.Equal(SessionAppState.Skipped, runner.Snapshot.Apps[1].State);
        Assert.Equal("Your Session is active. Sim didn't start, so it won't end this Session. Use End Session when you're finished.", runner.Snapshot.Message);
        await runner.EndAsync();
        Assert.Equal(SessionRunState.Completed, runner.Snapshot!.State);
    }

    [Fact]
    public async Task OptionalMainAppThatStartsStillEndsTheSession()
    {
        var main = new Process();
        var sim = App("Sim") with { Optional = true };
        var runner = new SessionRunner(new Host((_, _) => Task.FromResult(new ProcessAcquisition([main], true, "owned"))));

        await runner.StartAsync(Definition(sim) with { MainAppId = sim.Id });
        main.HasExited = true;
        await runner.RefreshAsync();

        Assert.Equal(SessionRunState.AwaitingEndConfirmation, runner.Snapshot!.State);
        await runner.EndAsync();
    }

    [Fact]
    public async Task RequiredFailureAfterAnOptionalSkipStillStopsAndCleansUpWhatOpened()
    {
        var overlay = new Process { HasWindow = false };
        var radio = new Process();
        var runner = new SessionRunner(new Host((app, _) => app.Name switch
        {
            "Overlay" => Task.FromResult(new ProcessAcquisition([overlay], true, "owned")),
            "Radio" => Task.FromResult(new ProcessAcquisition([radio], true, "owned")),
            _ => throw new IOException("Required app missing.")
        }));

        await runner.StartAsync(Definition(
                App("Overlay") with { Optional = true, Readiness = AppReadiness.WindowAppeared, ReadinessTimeoutSeconds = 1 },
                App("Radio"), App("Sim"), App("Later")))
            .WaitAsync(TimeSpan.FromSeconds(4));

        Assert.Equal(SessionRunState.AwaitingEndConfirmation, runner.Snapshot!.State);
        Assert.Equal(SessionAppState.Failed, runner.Snapshot.Apps[2].State);
        Assert.Equal(SessionAppState.Waiting, runner.Snapshot.Apps[3].State);
        await runner.EndAsync();
        Assert.True(overlay.Closed);
        Assert.True(radio.Closed);
        Assert.Equal(SessionRunState.Failed, runner.Snapshot!.State);
    }

    [Fact]
    public async Task TogetherOptionalFailureDoesNotCancelOtherLaunches()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new SessionRunner(new Host(async (app, token) =>
        {
            if (app.Name == "Tracker") throw new IOException("missing");
            await gate.Task.WaitAsync(token);
            return new ProcessAcquisition([new Process()], true, "owned");
        }));

        var start = runner.StartAsync(Definition(App("Tracker") with { Optional = true }, App("Radio"), App("Voice")) with { LaunchMode = SessionLaunchMode.Together });
        await Observe(runner, snapshot => snapshot.Apps[0].State == SessionAppState.Skipped);
        gate.SetResult();
        await start.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(SessionRunState.Running, runner.Snapshot!.State);
        Assert.Equal("Your Session is active. Tracker didn't finish starting; its status explains why.", runner.Snapshot.Message);
        await runner.EndAsync();
    }

    [Fact]
    public async Task UntrackedAndRepeatedOptionalAppsContinueWithoutAmbiguousLaunches()
    {
        var opened = new ConcurrentQueue<string>();
        var runner = new SessionRunner(new Host((app, _) =>
        {
            opened.Enqueue(app.Name);
            return Task.FromResult(new ProcessAcquisition([], false, "Launch request sent"));
        }));
        var first = App("Launcher") with { Optional = true, Readiness = AppReadiness.WindowAppeared };
        var repeat = App("Launcher again") with { Optional = true, ExecutablePath = first.ExecutablePath };

        await runner.StartAsync(Definition(first, repeat, App("Chat")) with { LaunchMode = SessionLaunchMode.Together })
            .WaitAsync(TimeSpan.FromSeconds(3));

        var apps = runner.Snapshot!.Apps;
        Assert.Equal(["Chat", "Launcher"], opened.OrderBy(name => name));
        Assert.Equal(SessionAppState.Untracked, apps[0].State);
        Assert.StartsWith("Launch request sent · Didn't finish starting: Readiness cannot be verified", apps[0].Message);
        Assert.Equal(SessionAppState.Skipped, apps[1].State);
        Assert.Contains("earlier launch of this executable was untracked", apps[1].Message);
        Assert.Equal("Your Session is active. 2 optional apps didn't finish starting: Launcher, Launcher again.", runner.Snapshot.Message);
        await runner.EndAsync();
    }

    [Fact]
    public async Task OptionalPluginAppIsSkippedWhenItsPluginIsUnavailable()
    {
        var host = new Host((_, _) => Owned());
        var runner = new SessionRunner(host, plugins: new MissingPlugins());
        var game = new StartProcessAction(Guid.NewGuid(), "3DMark", "", Plugin: new("steam", "223850"), Optional: true);

        await runner.StartAsync(Definition(App("Radio"), game));

        Assert.Equal(SessionRunState.Running, runner.Snapshot!.State);
        Assert.Equal(1, host.Calls);
        Assert.Equal(SessionAppState.Skipped, runner.Snapshot.Apps[1].State);
        Assert.Contains("Skipped: ", runner.Snapshot.Apps[1].Message);
        await runner.EndAsync();
    }

    [Fact]
    public async Task EndDuringAnOptionalWaitIsCancellationNotASkip()
    {
        var slow = new Process { HasWindow = false };
        var runner = new SessionRunner(new Host((_, _) => Task.FromResult(new ProcessAcquisition([slow], true, "owned"))));
        var start = runner.StartAsync(Definition(App("Overlay") with { Optional = true, Readiness = AppReadiness.WindowAppeared, ReadinessTimeoutSeconds = 60 }, App("Later")));
        await Observe(runner, snapshot => snapshot.Apps[0].Message.StartsWith("Waiting for"));

        await runner.EndAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await start;

        Assert.Equal(SessionRunState.Completed, runner.Snapshot!.State);
        Assert.NotEqual(SessionAppState.Skipped, runner.Snapshot.Apps[0].State);
        Assert.True(slow.Closed);
    }

    private static Task<ProcessAcquisition> Owned() => Task.FromResult(new ProcessAcquisition([new Process()], true, "owned"));
    private static StartProcessAction App(string name) => new(Guid.NewGuid(), name, Path.Combine(Path.GetTempPath(), name + ".exe"));
    private static SessionDefinition Definition(params StartProcessAction[] apps) => new(Guid.NewGuid(), "Test", "", apps);

    private static Task Observe(SessionRunner runner, Func<SessionRunSnapshot, bool> predicate)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs e)
        {
            if (runner.Snapshot is { } snapshot && predicate(snapshot)) { runner.Changed -= Changed; gate.TrySetResult(); }
        }
        runner.Changed += Changed;
        Changed(null, EventArgs.Empty);
        return gate.Task.WaitAsync(TimeSpan.FromSeconds(4));
    }

    private sealed class Host(Func<StartProcessAction, CancellationToken, Task<ProcessAcquisition>> open) : ISessionProcessHost
    {
        public int Calls;
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return open(app, cancellationToken);
        }
    }

    private sealed class MissingPlugins : ISessionPluginHost
    {
        public ISessionPluginLaunches Capture() => new PluginCatalog([]).Capture(new Dictionary<string, PluginConfiguration>());
    }

    private sealed class Process : ITrackedProcess
    {
        public bool HasWindow { get; set; } = true;
        public bool HasExited { get; set; }
        public bool Closed { get; private set; }
        public Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false) { Closed = true; HasExited = true; return Task.FromResult(true); }
        public void Dispose() { }
    }
}
