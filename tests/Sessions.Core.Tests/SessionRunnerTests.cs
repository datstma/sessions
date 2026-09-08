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
        public Task<bool> RequestCloseAsync(TimeSpan timeout)
        {
            log.Add("close " + name);
            if (CloseError is not null) throw CloseError;
            if (AcceptClose) Exited = true;
            return Task.FromResult(Exited);
        }
        public void Dispose() => Disposed = true;
    }
}
