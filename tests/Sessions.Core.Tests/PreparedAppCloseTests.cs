using Sessions.Plugins.Steam;

namespace Sessions.Core.Tests;

public sealed class PreparedAppCloseTests
{
    [Fact]
    public async Task PreparedCloseFreezesTargetsUsesNormalCloseAndDoesNotRetryOrAdoptLaterCopies()
    {
        var target = new Process();
        var later = new Process();
        var input = new List<ITrackedProcess> { target };
        using var request = new PreparedAppClose(input, TimeSpan.Zero);
        input.Add(later);
        Assert.True(await request.CloseAsync());
        Assert.Equal(1, target.Calls);
        Assert.False(target.Force);
        Assert.Equal(0, later.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(request.CloseAsync);
        Assert.Equal(1, target.Calls);
    }

    [Fact]
    public async Task CancelAndExitedTargetsDoNotReceiveCloseRequests()
    {
        var cancelled = new Process();
        var request = new PreparedAppClose([cancelled]);
        request.Dispose();
        Assert.True(cancelled.Disposed);
        Assert.Equal(0, cancelled.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(request.CloseAsync);
        var exited = new Process { Exited = true };
        using var stale = new PreparedAppClose([exited]);
        Assert.True(await stale.CloseAsync());
        Assert.Equal(0, exited.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefusalOrFailureDoesNotForceQuitOrSkipRemainingCapturedTargets(bool throws)
    {
        var refusing = new Process { Refuse = true, Throws = throws };
        var other = new Process();
        using var request = new PreparedAppClose([refusing, other]);
        if (throws) await Assert.ThrowsAsync<InvalidOperationException>(request.CloseAsync);
        else Assert.False(await request.CloseAsync());
        Assert.False(refusing.Force);
        Assert.Equal(1, other.Calls);
    }

    [Fact]
    public void SteamManualSnapshotRemovesEndedProcessesAndKeepsOnlyLatestMatchingAssociations()
    {
        var events = SteamProcessLog.ReadCurrentProcesses("""
            [2026-09-10 19:09:15] AppID 10 adding PID 1 as a tracked process
            [2026-09-10 19:09:16] Remove 10 from running list
            [2026-09-10 19:09:17] AppID 10 adding PID 2 as a tracked process
            [2026-09-10 19:09:18] AppID 10 no longer tracking PID 2, exit code 0
            [2026-09-10 19:09:19] AppID 20 adding PID 3 as a tracked process
            [2026-09-10 19:09:20] AppID 10 adding PID 4 as a tracked process
            [2026-09-10 19:09:21] AppID 10 adding PID 4 as a tracked process
            """, 10);
        Assert.Equal(4, Assert.Single(events).ProcessId);
        Assert.Equal(21, events[0].AddedAtUtc.Second);
    }

    private sealed class Process : ITrackedProcess
    {
        public int Calls;
        public bool Force, Disposed, Exited, Refuse, Throws;
        public bool HasExited => Exited;
        public Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false)
        {
            Calls++; Force = allowForceQuit;
            if (Throws) throw new IOException("fixture failure");
            return Task.FromResult(!Refuse);
        }
        public void Dispose() => Disposed = true;
    }
}
