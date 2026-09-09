using System.Collections.Concurrent;
using System.Diagnostics;
using Sessions.Core;

namespace Sessions.Core.Tests;

public sealed class AdvancedStartupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrderedStartupWaitsForTrackedWindowAndLeavesPreExistingAppsIndependent(bool owned)
    {
        var process = new Process { HasWindow = false };
        var opened = new ConcurrentQueue<string>();
        var host = new Host((app, _) =>
        {
            opened.Enqueue(app.Name);
            return Task.FromResult(new ProcessAcquisition([app.Name == "A" ? process : new Process()], owned, "acquired"));
        });
        var runner = new SessionRunner(host);
        var waiting = Observe(runner, s => s.Apps[0].Message.StartsWith("Waiting for"));
        var definition = Definition(App("A") with { Readiness = AppReadiness.WindowAppeared }, App("B"));
        var start = runner.StartAsync(definition);
        await waiting;
        Assert.Equal(new[] { "A" }, opened.ToArray());
        Assert.False(runner.Snapshot!.StartupSucceeded);
        process.HasWindow = true;
        await start.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { "A", "B" }, opened.ToArray());
        Assert.True(runner.Snapshot.StartupSucceeded);
        await runner.EndAsync();
        Assert.Equal(owned, process.Closed);
    }

    [Fact]
    public async Task TogetherStartsIndependentAppsAndEndDrainsAllInFlightAcquisitionsInDefinitionReverseOrder()
    {
        var gates = new[] { Gate(), Gate(), Gate() };
        var opened = new ConcurrentQueue<string>();
        var closed = new List<string>();
        var processes = Enumerable.Range(0, 3).Select(i => new Process { Closing = () => closed.Add(i.ToString()) }).ToArray();
        var runner = new SessionRunner(new Host(async (app, _) =>
        {
            var i = int.Parse(app.Name);
            opened.Enqueue(app.Name);
            await gates[i].Task;
            return new([processes[i]], true, "owned");
        }));
        var definition = Definition(App("0"), App("1"), App("2")) with { LaunchMode = SessionLaunchMode.Together };
        var start = runner.StartAsync(definition);
        Assert.Equal(3, opened.Count);
        var end = runner.EndAsync();
        gates[2].SetResult();
        gates[0].SetResult();
        Assert.False(end.IsCompleted);
        gates[1].SetResult();
        await Task.WhenAll(start, end).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { "2", "1", "0" }, closed);
        Assert.False(runner.Snapshot!.StartupSucceeded);
        Assert.All(processes, p => Assert.True(p.Disposed));
    }

    [Fact]
    public async Task ParallelFailureCancelsWaitsButRetainsLateAcquisitionUntilCleanupIsConfirmed()
    {
        var failure = Gate();
        var late = Gate();
        var process = new Process();
        var runner = new SessionRunner(new Host(async (app, _) =>
        {
            if (app.Name == "A") { await failure.Task; throw new IOException("missing executable"); }
            await late.Task; // Windows may finish creating a process after cancellation.
            return new([process], true, "owned");
        }));
        var start = runner.StartAsync(Definition(App("A"), App("B")) with { LaunchMode = SessionLaunchMode.Together });
        var failed = Observe(runner, s => s.Apps[0].State == SessionAppState.Failed);
        failure.SetResult();
        await failed;
        Assert.False(start.IsCompleted);
        late.SetResult();
        await start.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(SessionRunState.AwaitingEndConfirmation, runner.Snapshot!.State);
        Assert.False(process.Closed);
        Assert.False(process.Disposed);
        Assert.True(runner.Snapshot.Apps[1].Owned);
        await runner.EndAsync();
        Assert.True(process.Closed);
    }

    [Fact]
    public async Task SameExecutableIsSerializedAndUntrackedHandoffPreventsAnotherLaunch()
    {
        var gate = Gate();
        var opened = new ConcurrentQueue<string>();
        var runner = new SessionRunner(new Host(async (app, _) =>
        {
            opened.Enqueue(app.Name);
            if (app.Name == "A") await gate.Task;
            return new([], false, "untracked");
        }));
        var first = App("A");
        var second = App("B") with { ExecutablePath = first.ExecutablePath.ToUpperInvariant() };
        var start = runner.StartAsync(Definition(first, second, App("C")) with { LaunchMode = SessionLaunchMode.Together });
        Assert.Equal(new[] { "A", "C" }, opened.ToArray());
        gate.SetResult();
        await start.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.DoesNotContain("B", opened);
        Assert.Equal(SessionAppState.Failed, runner.Snapshot!.Apps[1].State);
        Assert.False(runner.Snapshot.StartupSucceeded);
    }

    [Theory]
    [InlineData(AppReadiness.ProcessRunning)]
    [InlineData(AppReadiness.WindowAppeared)]
    public async Task UntrackedLaunchCannotClaimVerifiedReadiness(AppReadiness readiness)
    {
        var runner = new SessionRunner(new Host((_, _) => Task.FromResult(new ProcessAcquisition([], false, "untracked"))));
        await runner.StartAsync(Definition(App("A") with { Readiness = readiness }));
        Assert.Equal(SessionRunState.Failed, runner.Snapshot!.State);
        Assert.Contains("untracked", runner.Snapshot.Apps[0].Message);
    }

    [Fact]
    public async Task WindowTimeoutStopsLaterAppsAndRequiresConfirmationForOwnedCleanup()
    {
        var process = new Process { HasWindow = false };
        var calls = 0;
        var runner = new SessionRunner(new Host((_, _) =>
        {
            calls++;
            return Task.FromResult(new ProcessAcquisition([process], true, "owned"));
        }));
        await runner.StartAsync(Definition(App("A") with { Readiness = AppReadiness.WindowAppeared, ReadinessTimeoutSeconds = 1 }, App("B")))
            .WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Equal(1, calls);
        Assert.Equal(SessionRunState.AwaitingEndConfirmation, runner.Snapshot!.State);
        Assert.Contains("within 1 seconds", runner.Snapshot.Apps[0].Message);
        Assert.False(process.Closed);
        await runner.EndAsync();
        Assert.True(process.Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndCancelsReadinessOrLongPauseWithoutWaitingForTheirTimeout(bool pause)
    {
        var process = new Process { HasWindow = false };
        var runner = new SessionRunner(new Host((_, _) => Task.FromResult(new ProcessAcquisition([process], true, "owned"))));
        var waiting = Observe(runner, s => s.Apps[0].Message.StartsWith(pause ? "Pausing" : "Waiting for"));
        var start = runner.StartAsync(Definition(App("A") with
        {
            Readiness = pause ? AppReadiness.LaunchCompleted : AppReadiness.WindowAppeared,
            PauseAfterSeconds = pause ? 300 : null,
            ReadinessTimeoutSeconds = 600
        }));
        await waiting;
        await Task.WhenAll(start, runner.EndAsync()).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(process.Closed);
        Assert.False(runner.Snapshot!.StartupSucceeded);
    }

    [Fact]
    public async Task OrderedPauseIsBetweenAppsAndAnExplicitZeroOverridesIt()
    {
        var starts = new List<TimeSpan>();
        var timer = Stopwatch.StartNew();
        var runner = new SessionRunner(new Host((_, _) =>
        {
            starts.Add(timer.Elapsed);
            return Task.FromResult(new ProcessAcquisition([], false, "untracked"));
        }));
        var pausing = Observe(runner, s => s.Apps.Any(app => app.Message.StartsWith("Pausing")));
        var start = runner.StartAsync(Definition(App("A") with { PauseAfterSeconds = 0 }, App("B"), App("C")) with { PauseBetweenAppsSeconds = 1 });
        await pausing;
        Assert.Equal(2, starts.Count);
        await start.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.True(starts[2] - starts[1] >= TimeSpan.FromMilliseconds(900));
        Assert.True(runner.Snapshot!.StartupSucceeded);
        await runner.LeaveAppsOpenAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessRunningReadinessRequiresALiveTrackedProcessButNoWindow(bool exited)
    {
        var process = new Process { HasWindow = false, HasExited = exited };
        var runner = new SessionRunner(new Host((_, _) => Task.FromResult(new ProcessAcquisition([process], false, "already open"))));
        await runner.StartAsync(Definition(App("A") with { Readiness = AppReadiness.ProcessRunning }));
        Assert.Equal(!exited, runner.Snapshot!.StartupSucceeded);
        Assert.Equal(exited ? SessionRunState.Failed : SessionRunState.Running, runner.Snapshot.State);
        if (!exited) await runner.LeaveAppsOpenAsync();
        Assert.False(process.Closed);
    }

    [Fact]
    public async Task TogetherIgnoresAllPausesButWaitsForEveryWindow()
    {
        var a = new Process();
        var b = new Process { HasWindow = false };
        var runner = new SessionRunner(new Host((app, _) => Task.FromResult(new ProcessAcquisition([app.Name == "A" ? a : b], true, "owned"))));
        var waiting = Observe(runner, s => s.Apps[1].Message.StartsWith("Waiting for"));
        var start = runner.StartAsync(Definition(App("A") with { PauseAfterSeconds = 300 },
            App("B") with { Readiness = AppReadiness.WindowAppeared, PauseAfterSeconds = 300 }) with
            { LaunchMode = SessionLaunchMode.Together, PauseBetweenAppsSeconds = 300 });
        await waiting;
        Assert.False(start.IsCompleted);
        b.HasWindow = true;
        await start.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(runner.Snapshot!.StartupSucceeded);
        await runner.EndAsync();
    }

    [Fact]
    public async Task CapturedSettingsAndRunIdentityDoNotChangeWithLaterDefinitionEdits()
    {
        var gate = Gate();
        var process = new Process();
        var runner = new SessionRunner(new Host(async (_, _) => { await gate.Task; return new([process], true, "owned"); }));
        var apps = new[] { App("A") };
        var definition = Definition(apps);
        var start = runner.StartAsync(definition);
        var firstId = runner.Snapshot!.RunId;
        apps[0] = apps[0] with { Readiness = AppReadiness.WindowAppeared, PauseAfterSeconds = 300 };
        gate.SetResult();
        await start.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(runner.Snapshot.StartupSucceeded);
        await runner.EndAsync();
        await runner.StartAsync(Definition());
        Assert.NotEqual(firstId, runner.Snapshot.RunId);
        await runner.LeaveAppsOpenAsync();
    }

    private static StartProcessAction App(string name) => new(Guid.NewGuid(), name, Path.Combine(Path.GetTempPath(), name + ".exe"));
    private static SessionDefinition Definition(params StartProcessAction[] apps) => new(Guid.NewGuid(), "Test", "", apps);
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Observe(SessionRunner runner, Func<SessionRunSnapshot, bool> predicate)
    {
        var gate = Gate();
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
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken) => open(app, cancellationToken);
    }
    private sealed class Process : ITrackedProcess
    {
        public bool HasWindow { get; set; } = true;
        public bool HasExited { get; set; }
        public bool Closed { get; private set; }
        public bool Disposed { get; private set; }
        public Action? Closing { get; init; }
        public Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false) { Closing?.Invoke(); Closed = true; HasExited = true; return Task.FromResult(true); }
        public void Dispose() => Disposed = true;
    }
}
