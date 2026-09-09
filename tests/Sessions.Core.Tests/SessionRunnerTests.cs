using Sessions.Core;

namespace Sessions.Core.Tests;

public sealed class SessionRunnerTests
{
    [Fact]
    public async Task OpensInOrderAndClosesOnlyOwnedProcessesInReverseOrder()
    {
        var log = new List<string>();
        var a = new FakeProcess("A", log);
        var b = new FakeProcess("B", log);
        var c = new FakeProcess("C", log);
        var host = new FakeHost(log, new ProcessAcquisition([a], true, "owned"), new ProcessAcquisition([b], false, "pre-existing"), new ProcessAcquisition([c], true, "owned"));
        var runner = new SessionRunner(host);
        await runner.StartAsync(Definition("A", "B", "C"));
        Assert.Equal(SessionRunState.Running, runner.Snapshot!.State);
        Assert.Equal(new[] { "open A", "open B", "open C" }, log);
        await runner.EndAsync();
        Assert.Equal(new[] { "open A", "open B", "open C", "close C", "close A" }, log);
        Assert.False(b.Exited);
        Assert.All(new[] { a, b, c }, p => Assert.True(p.Disposed));
        Assert.Equal(SessionRunState.Completed, runner.Snapshot.State);
        await runner.EndAsync();
        Assert.Equal(5, log.Count);
    }

    [Fact]
    public async Task FailureStopsFurtherStartupAndRollsBackOnlyOwnedApps()
    {
        var log = new List<string>();
        var process = new FakeProcess("A", log);
        var host = new FakeHost(log, new ProcessAcquisition([process], true, "owned")) { FailAt = 2 };
        var runner = new SessionRunner(host);
        await runner.StartAsync(Definition("A", "B", "C"));
        Assert.Equal(new[] { "open A", "open B" }, log);
        Assert.Equal(SessionRunState.AwaitingEndConfirmation, runner.Snapshot!.State);
        Assert.False(process.Exited);
        await runner.EndAsync();
        Assert.Equal(new[] { "open A", "open B", "close A" }, log);
        Assert.Equal(SessionRunState.Failed, runner.Snapshot!.State);
        Assert.Equal(SessionAppState.Failed, runner.Snapshot.Apps[1].State);
        Assert.Equal(SessionAppState.Waiting, runner.Snapshot.Apps[2].State);
    }

    [Fact]
    public async Task EndDuringStartupWaitsForAcquisitionThenClosesItExactlyOnce()
    {
        var log = new List<string>();
        var process = new FakeProcess("A", log);
        var host = new FakeHost(log, new ProcessAcquisition([process], true, "owned")) { Gate = new() };
        var runner = new SessionRunner(host);
        var start = runner.StartAsync(Definition("A", "B"));
        var end = runner.EndAsync();
        Assert.Same(end, runner.EndAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.StartAsync(Definition("Other")));
        host.Gate.SetResult(); // Simulates an OS launch that finished after End was requested.
        await Task.WhenAll(start, end).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { "open A", "close A" }, log);
        Assert.Equal(SessionRunState.Completed, runner.Snapshot!.State);
    }

    [Fact]
    public async Task MainExitEndsSessionButSupportingExitDoesNot()
    {
        var log = new List<string>();
        var support = new FakeProcess("Support", log);
        var main = new FakeProcess("Main", log);
        var definition = Definition("Support", "Main");
        definition = definition with { MainAppId = definition.Apps[1].Id };
        var runner = new SessionRunner(new FakeHost(log, new ProcessAcquisition([support], true, "owned"), new ProcessAcquisition([main], true, "owned")));
        await runner.StartAsync(definition);
        support.Exited = true;
        await runner.RefreshAsync();
        Assert.Equal(SessionRunState.Running, runner.Snapshot!.State);
        main.Exited = true;
        await runner.RefreshAsync();
        Assert.Equal(SessionRunState.AwaitingEndConfirmation, runner.Snapshot!.State);
        await runner.EndAsync();
        Assert.Equal(SessionRunState.Completed, runner.Snapshot.State);
        Assert.DoesNotContain(log, item => item.StartsWith("close"));
    }

    [Fact]
    public async Task AlreadyOpenMainTracksAllExistingInstancesWithoutTakingOwnership()
    {
        var log = new List<string>();
        var one = new FakeProcess("one", log);
        var two = new FakeProcess("two", log);
        var definition = Definition("Main");
        var runner = new SessionRunner(new FakeHost(log, new ProcessAcquisition([one, two], false, "already open")));
        await runner.StartAsync(definition with { MainAppId = definition.Apps[0].Id });
        one.Exited = true;
        await runner.RefreshAsync();
        Assert.True(runner.Snapshot!.IsActive);
        two.Exited = true;
        await runner.RefreshAsync();
        Assert.Equal(SessionRunState.AwaitingEndConfirmation, runner.Snapshot!.State);
        await runner.EndAsync();
        Assert.False(runner.Snapshot.IsActive);
        Assert.DoesNotContain(log, item => item.StartsWith("close"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefusalKeepsOwnershipUntilRetryOrExplicitLeaveOpen(bool leaveOpen)
    {
        var log = new List<string>();
        var process = new FakeProcess("A", log) { AcceptClose = false };
        var runner = new SessionRunner(new FakeHost(log, new ProcessAcquisition([process], true, "owned")));
        await runner.StartAsync(Definition("A"));
        await runner.EndAsync();
        Assert.Equal(SessionRunState.NeedsAttention, runner.Snapshot!.State);
        Assert.False(process.Disposed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.StartAsync(Definition("Other")));
        if (leaveOpen) runner.LeaveAppsOpen();
        else { process.AcceptClose = true; await runner.EndAsync(); }
        Assert.False(runner.Snapshot.IsActive);
        Assert.Equal(!leaveOpen, process.Exited);
        Assert.True(process.Disposed);
        Assert.Equal(leaveOpen ? 1 : 2, log.Count(item => item == "close A"));
    }

    [Fact]
    public async Task CleanupErrorDoesNotPreventOtherOwnedAppsFromClosing()
    {
        var log = new List<string>();
        var one = new FakeProcess("one", log);
        var two = new FakeProcess("two", log) { CloseError = new IOException("Cannot reach app") };
        var runner = new SessionRunner(new FakeHost(log, new ProcessAcquisition([one], true, "owned"), new ProcessAcquisition([two], true, "owned")));
        await runner.StartAsync(Definition("one", "two"));
        await runner.EndAsync();
        Assert.True(one.Exited);
        Assert.Equal(SessionRunState.NeedsAttention, runner.Snapshot!.State);
        Assert.Contains("Cannot reach app", runner.Snapshot.Apps[1].Message);
        runner.LeaveAppsOpen();
    }

    [Fact]
    public async Task UntrackedMainNeedsManualEndAndEmptyUserBoundSessionCanRun()
    {
        var runner = new SessionRunner(new FakeHost([], new ProcessAcquisition([], false, "untracked")));
        var definition = Definition("Launcher");
        await runner.StartAsync(definition with { MainAppId = definition.Apps[0].Id });
        await runner.RefreshAsync();
        Assert.True(runner.Snapshot!.IsActive);
        Assert.Contains("couldn't be tracked", runner.Snapshot.Message);
        await runner.EndAsync();
        await runner.StartAsync(Definition());
        Assert.Equal(SessionRunState.Running, runner.Snapshot.State);
        await runner.EndAsync();
        Assert.Equal(SessionRunState.Completed, runner.Snapshot.State);
    }

    [Fact]
    public async Task CancellingMainExitPromptKeepsAppsAndDoesNotPromptAgain()
    {
        var support = new FakeProcess("Support", []);
        var main = new FakeProcess("Main", []);
        var definition = Definition("Support", "Main");
        var runner = new SessionRunner(new FakeHost([], new([support], true, "owned"), new([main], true, "owned")));
        await runner.StartAsync(definition with { MainAppId = definition.Apps[1].Id });
        main.Exited = true;
        await runner.RefreshAsync();
        Assert.Equal(SessionRunState.AwaitingEndConfirmation, runner.Snapshot!.State);
        Assert.False(support.Exited);
        runner.DismissEndRequest();
        await runner.RefreshAsync();
        await runner.RefreshAsync();
        Assert.Equal(SessionRunState.Running, runner.Snapshot.State);
        Assert.False(support.Exited);
        await runner.EndAsync();
        Assert.True(support.Exited);
    }

    [Fact]
    public async Task CancelledFailureCleanupRetainsOwnershipForLaterConfirmedEnd()
    {
        var process = new FakeProcess("A", []);
        var runner = new SessionRunner(new FakeHost([], new ProcessAcquisition([process], true, "owned")) { FailAt = 2 });
        await runner.StartAsync(Definition("A", "B"));
        runner.DismissEndRequest();
        Assert.Equal(SessionRunState.NeedsAttention, runner.Snapshot!.State);
        Assert.False(process.Exited);
        Assert.False(process.Disposed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.StartAsync(Definition("Other")));
        await runner.EndAsync();
        Assert.True(process.Exited);
        Assert.Equal(SessionRunState.Failed, runner.Snapshot.State);
    }

    [Fact]
    public async Task DefaultClosePreservesUnsavedAppsAndOnlyExplicitTargetCanBeForced()
    {
        var one = new FakeProcess("one", []) { AcceptClose = false };
        var two = new FakeProcess("two", []) { AcceptClose = false };
        var existing = new FakeProcess("existing", []) { AcceptClose = false };
        var definition = Definition("one", "two", "existing");
        var runner = new SessionRunner(new FakeHost([], new([one], true, "owned"), new([two], true, "owned"), new([existing], false, "existing")));
        await runner.StartAsync(definition);
        var runId = runner.Snapshot!.RunId;
        await runner.ForceQuitAppAsync(runId, definition.Apps[0].Id); // No pending cleanup.
        Assert.Empty(one.ForceRequests);
        await runner.EndAsync();
        Assert.Equal(SessionRunState.NeedsAttention, runner.Snapshot.State);
        Assert.Equal(new[] { false }, one.ForceRequests);
        Assert.Equal(new[] { false }, two.ForceRequests);
        await runner.ForceQuitAppAsync(Guid.NewGuid(), definition.Apps[0].Id);
        await runner.ForceQuitAppAsync(runId, definition.Apps[2].Id);
        await runner.ForceQuitAppAsync(runId, Guid.NewGuid());
        Assert.Single(one.ForceRequests);
        Assert.Empty(existing.ForceRequests);
        await runner.ForceQuitAppAsync(runId, definition.Apps[0].Id);
        Assert.True(one.Exited);
        Assert.False(two.Exited);
        Assert.Equal(new[] { false, true }, one.ForceRequests);
        Assert.Single(two.ForceRequests);
        Assert.Equal(SessionRunState.NeedsAttention, runner.Snapshot.State);
        two.Exited = true; // User saves or discards and closes the other app.
        await runner.RefreshAsync();
        Assert.Equal(SessionRunState.Completed, runner.Snapshot.State);
        Assert.False(existing.Exited);
        Assert.True(two.Disposed);
        await runner.StartAsync(Definition());
        await runner.ForceQuitAppAsync(runId, definition.Apps[0].Id);
        Assert.Equal(SessionRunState.Running, runner.Snapshot.State);
        await runner.EndAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedFailureCleanupUsesEachAppsForcePermission(bool force)
    {
        var process = new FakeProcess("A", []) { AcceptClose = false };
        var definition = Definition("A", "B");
        definition = definition with { Apps = [definition.Apps[0] with { AllowForceQuit = force }, definition.Apps[1]] };
        var runner = new SessionRunner(new FakeHost([], new ProcessAcquisition([process], true, "owned")) { FailAt = 2 });
        await runner.StartAsync(definition);
        Assert.Empty(process.ForceRequests);
        await runner.EndAsync();
        Assert.Equal(new[] { force }, process.ForceRequests);
        Assert.Equal(force, process.Exited);
        if (!force)
        {
            Assert.Equal(SessionRunState.NeedsAttention, runner.Snapshot!.State);
            await runner.RefreshAsync(); // A cancelled save prompt must not be retried automatically.
            Assert.Single(process.ForceRequests);
            process.Exited = true;
            await runner.RefreshAsync();
        }
        Assert.Equal(SessionRunState.Failed, runner.Snapshot!.State);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MainExitAndLateStartupAcquisitionPreserveTheSameForcePolicy(bool lateStartup, bool force)
    {
        var support = new FakeProcess("Support", []) { AcceptClose = false };
        var main = new FakeProcess("Main", []);
        var definition = Definition("Support", "Main");
        definition = definition with { MainAppId = definition.Apps[1].Id,
            Apps = [definition.Apps[0] with { AllowForceQuit = force }, definition.Apps[1]] };
        var host = new FakeHost([], new([support], true, "owned"), new([main], true, "owned"))
            { Gate = lateStartup ? new() : null };
        var runner = new SessionRunner(host);
        var start = runner.StartAsync(definition);
        if (lateStartup)
        {
            var end = runner.EndAsync();
            host.Gate!.SetResult();
            await Task.WhenAll(start, end).WaitAsync(TimeSpan.FromSeconds(3));
        }
        else
        {
            await start;
            main.Exited = true;
            await runner.RefreshAsync();
            Assert.Empty(support.ForceRequests);
            await runner.EndAsync();
        }
        Assert.Equal(new[] { force }, support.ForceRequests);
        Assert.Equal(force, support.Exited);
        if (!force) runner.LeaveAppsOpen();
    }

    private static SessionDefinition Definition(params string[] names) => new(Guid.NewGuid(), "Test", "",
        names.Select(name => new StartProcessAction(Guid.NewGuid(), name, name + ".exe")).ToArray());

    private sealed class FakeHost(List<string> log, params ProcessAcquisition[] acquisitions) : ISessionProcessHost
    {
        private int _calls;
        public int FailAt { get; init; } = -1;
        public TaskCompletionSource? Gate { get; init; }
        public async Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken token)
        {
            log.Add("open " + app.Name);
            var call = ++_calls;
            if (Gate is not null) await Gate.Task;
            if (call == FailAt) throw new IOException("Missing executable");
            return acquisitions[call - 1];
        }
    }

    private sealed class FakeProcess(string name, List<string> log) : ITrackedProcess
    {
        public bool Exited { get; set; }
        public bool Disposed { get; private set; }
        public bool AcceptClose { get; set; } = true;
        public Exception? CloseError { get; init; }
        public bool HasExited => Exited;
        public List<bool> ForceRequests { get; } = [];
        public Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false)
        {
            log.Add("close " + name);
            ForceRequests.Add(allowForceQuit);
            if (CloseError is not null) throw CloseError;
            if (AcceptClose || allowForceQuit) Exited = true;
            return Task.FromResult(Exited);
        }
        public void Dispose() => Disposed = true;
    }
}
