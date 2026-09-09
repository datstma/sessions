using Sessions.Core;

namespace Sessions.Core.Tests;

public sealed class SessionAudioTests
{
    [Theory]
    [InlineData(false, SessionLaunchMode.InOrder)]
    [InlineData(true, SessionLaunchMode.InOrder)]
    [InlineData(false, SessionLaunchMode.Together)]
    [InlineData(true, SessionLaunchMode.Together)]
    public async Task SwitchesBeforeLaunchingAndRestoresEachOriginalRole(bool leaveOpen, SessionLaunchMode mode)
    {
        var audio = new Devices();
        var original = audio.Defaults.ToDictionary();
        var process = new Process();
        var host = new Host(() =>
        {
            Assert.All(audio.Defaults, pair => Assert.Equal(pair.Key.Item1 == AudioFlow.Output ? "headset" : "mic", pair.Value));
            return new([process], true, "owned");
        });
        var runner = new SessionRunner(host, audioDevices: audio);
        await runner.StartAsync(Definition() with { LaunchMode = mode });
        Assert.Equal(SessionRunState.Running, runner.Snapshot!.State);
        if (leaveOpen) await runner.LeaveAppsOpenAsync(); else await runner.EndAsync();
        Assert.Equal(original.OrderBy(pair => pair.Key), audio.Defaults.OrderBy(pair => pair.Key));
        Assert.Equal(!leaveOpen, process.HasExited);
        Assert.False(runner.Snapshot.IsActive);
    }

    [Fact]
    public async Task CoupledWindowsRolesAreCapturedBeforeSetterSideEffects()
    {
        var audio = new Devices { LinkOrdinaryRoles = true };
        audio.Defaults[(AudioFlow.Output, AudioRole.Console)] = "original-output";
        audio.Defaults[(AudioFlow.Output, AudioRole.Multimedia)] = "original-output";
        var runner = new SessionRunner(new Host(), audioDevices: audio);
        await runner.StartAsync(Definition() with { InputAudioDevice = null });
        Assert.True(runner.Snapshot!.StartupSucceeded);
        Assert.Equal("headset", audio.Defaults[(AudioFlow.Output, AudioRole.Multimedia)]);
        await runner.EndAsync();
        Assert.Equal("original-output", audio.Defaults[(AudioFlow.Output, AudioRole.Console)]);
        Assert.Equal("original-output", audio.Defaults[(AudioFlow.Output, AudioRole.Multimedia)]);
    }

    [Fact]
    public async Task FailedPartialRollbackKeepsRunActiveUntilRestorationSucceeds()
    {
        var audio = new Devices { FailAtWrite = 2, BlockRestore = true };
        var host = new Host();
        var runner = new SessionRunner(host, audioDevices: audio);
        await runner.StartAsync(Definition());
        Assert.Equal(0, host.Calls);
        Assert.Equal(SessionRunState.NeedsAttention, runner.Snapshot!.State);
        Assert.Contains("Audio restoration", runner.Snapshot.Message);
        audio.BlockRestore = false;
        await runner.EndAsync();
        Assert.False(runner.Snapshot.IsActive);
        Assert.All(audio.Defaults, pair => Assert.StartsWith("original-", pair.Value));
    }

    [Fact]
    public async Task MainExitDoesNotRestoreUntilEndIsConfirmed()
    {
        var audio = new Devices();
        var process = new Process();
        var runner = new SessionRunner(new Host(() => new([process], true, "owned")), audioDevices: audio);
        var definition = Definition();
        await runner.StartAsync(definition with { MainAppId = definition.Apps[0].Id });
        process.HasExited = true;
        await runner.RefreshAsync();
        Assert.Equal(SessionRunState.AwaitingEndConfirmation, runner.Snapshot!.State);
        Assert.Equal("headset", audio.Defaults[(AudioFlow.Output, AudioRole.Console)]);
        await runner.EndAsync();
        Assert.All(audio.Defaults, pair => Assert.StartsWith("original-", pair.Value));
    }

    [Fact]
    public async Task ConcurrentExternalChoiceDuringApplyIsPreservedAndLaunchStops()
    {
        var audio = new Devices();
        audio.BeforeFirstWrite = () =>
        {
            audio.Defaults[(AudioFlow.Output, AudioRole.Multimedia)] = "manual-choice";
            return Task.CompletedTask;
        };
        var host = new Host();
        var runner = new SessionRunner(host, audioDevices: audio);
        await runner.StartAsync(Definition());
        Assert.Equal(0, host.Calls);
        Assert.False(runner.Snapshot!.IsActive);
        Assert.Equal("manual-choice", audio.Defaults[(AudioFlow.Output, AudioRole.Multimedia)]);
        Assert.Equal("headset", audio.Defaults[(AudioFlow.Output, AudioRole.Console)]);
    }

    [Fact]
    public async Task NoSelectionsNeverTouchesAudioAndInputOnlyLeavesOutputAlone()
    {
        var audio = new Devices { ReadError = true };
        var runner = new SessionRunner(new Host(), audioDevices: audio);
        await runner.StartAsync(Definition() with { OutputAudioDevice = null, InputAudioDevice = null });
        await runner.EndAsync();
        Assert.Empty(audio.Writes);
        audio.ReadError = false;
        await runner.StartAsync(Definition() with { OutputAudioDevice = null });
        await runner.EndAsync();
        Assert.All(audio.Writes, write => Assert.Equal(AudioFlow.Input, write.Flow));
    }

    [Fact]
    public async Task MissingDevicePreventsAllChangesAndLaunches()
    {
        var audio = new Devices();
        var host = new Host();
        var runner = new SessionRunner(host, audioDevices: audio);
        await runner.StartAsync(Definition() with { InputAudioDevice = new("missing", "Disconnected microphone") });
        Assert.Empty(audio.Writes);
        Assert.Equal(0, host.Calls);
        Assert.Equal(SessionRunState.Failed, runner.Snapshot!.State);
        Assert.Contains("Disconnected microphone", runner.Snapshot.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialSwitchFailureRollsBackEvenIfSetterChangedDeviceBeforeThrowing(bool appliedBeforeFailure)
    {
        var audio = new Devices { FailAtWrite = 2, ApplyBeforeError = appliedBeforeFailure };
        var original = audio.Defaults.ToDictionary();
        var host = new Host();
        var runner = new SessionRunner(host, audioDevices: audio);
        await runner.StartAsync(Definition());
        Assert.Equal(original.OrderBy(pair => pair.Key), audio.Defaults.OrderBy(pair => pair.Key));
        Assert.Equal(0, host.Calls);
        Assert.Equal(SessionRunState.Failed, runner.Snapshot!.State);
    }

    [Fact]
    public async Task LaterDeviceChoiceIsPreservedAndUnavailableRestoreCanBeRetried()
    {
        var audio = new Devices();
        var runner = new SessionRunner(new Host(), audioDevices: audio);
        await runner.StartAsync(Definition());
        audio.Defaults[(AudioFlow.Output, AudioRole.Console)] = "manual-choice";
        audio.BlockRestore = true;
        await runner.EndAsync();
        Assert.Equal(SessionRunState.NeedsAttention, runner.Snapshot!.State);
        Assert.Contains("Audio restoration needs attention", runner.Snapshot.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.StartAsync(Definition()));
        await runner.RefreshAsync();
        Assert.True(runner.Snapshot.IsActive);
        audio.BlockRestore = false;
        await runner.EndAsync();
        Assert.False(runner.Snapshot.IsActive);
        Assert.Equal("manual-choice", audio.Defaults[(AudioFlow.Output, AudioRole.Console)]);
        Assert.Equal("headset", audio.Defaults[(AudioFlow.Output, AudioRole.Multimedia)]); // Linked ordinary roles preserve the later choice together.
    }

    [Fact]
    public async Task RefusingAppDoesNotDelayAudioRestorationAndMonitorDoesNotRestoreTwice()
    {
        var audio = new Devices();
        var process = new Process { Refuses = true };
        var runner = new SessionRunner(new Host(() => new([process], true, "owned")), audioDevices: audio);
        await runner.StartAsync(Definition());
        await runner.EndAsync();
        Assert.Equal(SessionRunState.NeedsAttention, runner.Snapshot!.State);
        Assert.All(audio.Defaults, pair => Assert.StartsWith("original-", pair.Value));
        var count = audio.Writes.Count;
        process.HasExited = true;
        await runner.RefreshAsync();
        Assert.False(runner.Snapshot.IsActive);
        Assert.Equal(count, audio.Writes.Count);
    }

    [Fact]
    public async Task EndDuringAudioSwitchWaitsForLateWriteAndRestoresWithoutLaunching()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audio = new Devices { BeforeFirstWrite = async () => { entered.SetResult(); await gate.Task; } };
        var host = new Host();
        var runner = new SessionRunner(host, audioDevices: audio);
        var start = runner.StartAsync(Definition());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var end = runner.EndAsync();
        var secondEnd = runner.EndAsync();
        Assert.Same(end, secondEnd);
        Assert.False(end.IsCompleted);
        gate.SetResult();
        await Task.WhenAll(start, end);
        Assert.Equal(0, host.Calls);
        Assert.All(audio.Defaults, pair => Assert.StartsWith("original-", pair.Value));
        Assert.False(runner.Snapshot!.IsActive);
    }

    [Fact]
    public async Task LaunchFailureRestoresAudioWhileOwnedAppsStillRequireConfirmation()
    {
        var audio = new Devices();
        var process = new Process();
        var count = 0;
        var runner = new SessionRunner(new Host(() => ++count == 1 ? new([process], true, "owned") : throw new IOException("Launch failed")), audioDevices: audio);
        var definition = Definition();
        await runner.StartAsync(definition with { Apps = [definition.Apps[0], new(Guid.NewGuid(), "Second", "second.exe")] });
        Assert.Equal(SessionRunState.AwaitingEndConfirmation, runner.Snapshot!.State);
        Assert.All(audio.Defaults, pair => Assert.StartsWith("original-", pair.Value));
        Assert.False(process.HasExited);
        await runner.EndAsync();
        Assert.True(process.HasExited);
    }

    private static SessionDefinition Definition() => new(Guid.NewGuid(), "Flight", "", [new(Guid.NewGuid(), "Game", "game.exe")],
        OutputAudioDevice: new("headset", "Headset"), InputAudioDevice: new("mic", "Microphone"));
    private sealed class Host(Func<ProcessAcquisition>? open = null) : ISessionProcessHost
    {
        public int Calls;
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(open?.Invoke() ?? new([], false, "untracked")); }
    }
    private sealed class Process : ITrackedProcess
    {
        public bool HasExited { get; set; }
        public bool Refuses;
        public Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false) { HasExited = !Refuses; return Task.FromResult(!Refuses); }
        public void Dispose() { }
    }
    private sealed class Devices : IAudioDeviceService
    {
        public Dictionary<(AudioFlow, AudioRole), string> Defaults = Enum.GetValues<AudioFlow>()
            .SelectMany(flow => Enum.GetValues<AudioRole>().Select(role => (flow, role)))
            .ToDictionary(pair => pair, pair => $"original-{pair.flow}-{pair.role}");
        public List<(AudioFlow Flow, AudioRole Role, string Id)> Writes = [];
        public bool ReadError, ApplyBeforeError, BlockRestore, LinkOrdinaryRoles;
        public int FailAtWrite;
        public Func<Task>? BeforeFirstWrite;
        public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) => ReadError
            ? throw new IOException("Device list failed")
            : Task.FromResult<IReadOnlyList<AudioDevice>>([new("headset", "Headset", AudioFlow.Output), new("mic", "Microphone", AudioFlow.Input),
                .. Defaults.Select(pair => new AudioDevice(pair.Value, pair.Value, pair.Key.Item1))]);
        public Task<string?> GetDefaultAsync(AudioFlow flow, AudioRole role) => Task.FromResult<string?>(Defaults[(flow, role)]);
        public async Task SetDefaultAsync(AudioFlow flow, AudioRole role, string deviceId)
        {
            Writes.Add((flow, role, deviceId));
            if (Writes.Count == 1 && BeforeFirstWrite is not null) await BeforeFirstWrite();
            if (BlockRestore && deviceId.StartsWith("original-")) throw new IOException("Previous device disconnected");
            if (Writes.Count == FailAtWrite)
            {
                if (ApplyBeforeError) Defaults[(flow, role)] = deviceId;
                throw new IOException("Device switch failed");
            }
            Defaults[(flow, role)] = deviceId;
            if (LinkOrdinaryRoles && role != AudioRole.Communications)
            {
                Defaults[(flow, AudioRole.Console)] = deviceId;
                Defaults[(flow, AudioRole.Multimedia)] = deviceId;
            }
        }
    }
}
