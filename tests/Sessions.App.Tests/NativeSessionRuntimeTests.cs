using System.Diagnostics;
using Sessions.App.Services;
using Sessions.Core;

namespace Sessions.App.Tests;

[Collection("Native desktop")]
public sealed class NativeSessionRuntimeTests
{
    public static bool RunNativeRuntime => OperatingSystem.IsWindows() &&
        Environment.GetEnvironmentVariable("SESSIONS_RUN_RUNTIME_SMOKE") == "1";

    [Theory(Skip = "Opt-in readiness checks using isolated fixture windows only.", SkipUnless = nameof(RunNativeRuntime))]
    [InlineData(SessionLaunchMode.InOrder)]
    [InlineData(SessionLaunchMode.Together)]
    public async Task StartupWindowReadinessWorksForOwnedAndPreExistingApps(SessionLaunchMode mode)
    {
        using var test = new NativeFixture();
        var owned = test.App("ready-owned") with { Readiness = AppReadiness.WindowAppeared, ReadinessTimeoutSeconds = 10 };
        var existing = test.App("ready-existing") with { Readiness = AppReadiness.WindowAppeared, ReadinessTimeoutSeconds = 10 };
        await new WindowsProcessStarter().StartAsync(existing, test.Token);
        var existingProcess = await test.RetainWindow(existing);
        var runner = new SessionRunner(new WindowsSessionProcessHost(), TimeSpan.FromMilliseconds(500));
        try
        {
            await runner.StartAsync(new(Guid.NewGuid(), "Readiness fixture", "", [owned, existing], LaunchMode: mode)).WaitAsync(test.Token);
            Assert.True(runner.Snapshot!.StartupSucceeded);
            Assert.Equal(SessionRunState.Running, runner.Snapshot.State);
            Assert.True(runner.Snapshot.Apps[0].Owned);
            Assert.False(runner.Snapshot.Apps[1].Owned);
            var ownedProcess = await test.RetainWindow(owned);
            await runner.EndAsync();
            Assert.True(ownedProcess.HasExited);
            Assert.False(existingProcess.HasExited);
        }
        finally { if (runner.Snapshot?.IsActive == true) await runner.LeaveAppsOpenAsync(); }
    }

    [Theory(Skip = "Opt-in isolated off-screen test windows; never touches user apps.", SkipUnless = nameof(RunNativeRuntime))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndClosesOwnedWindowButPreservesPreExistingApp(bool refuse)
    {
        using var test = new NativeFixture();
        var owned = test.App("owned", refuse) with { AllowForceQuit = refuse };
        var existing = test.App("existing");
        await new WindowsProcessStarter().StartAsync(existing, test.Token);
        var existingProcess = await test.RetainWindow(existing);
        var runner = new SessionRunner(new WindowsSessionProcessHost(), TimeSpan.FromMilliseconds(500));
        try
        {
            await runner.StartAsync(new(Guid.NewGuid(), "Native test", "", [owned, existing]));
            var ownedProcess = await test.RetainWindow(owned);
            Assert.True(runner.Snapshot!.Apps[0].Owned);
            Assert.False(runner.Snapshot.Apps[1].Owned);
            await runner.EndAsync();
            Assert.False(existingProcess.HasExited);
            Assert.True(ownedProcess.HasExited);
            Assert.Equal(SessionRunState.Completed, runner.Snapshot.State);
        }
        finally { if (runner.Snapshot?.IsActive == true) await runner.LeaveAppsOpenAsync(); }
    }

    [Fact(Skip = "Opt-in isolated off-screen main-app lifetime test.", SkipUnless = nameof(RunNativeRuntime))]
    public async Task MainProcessExitWaitsForConfirmationBeforeClosingOwnedSupportApp()
    {
        using var test = new NativeFixture();
        var support = test.App("support");
        var main = test.App("main");
        var runner = new SessionRunner(new WindowsSessionProcessHost(), TimeSpan.FromMilliseconds(500));
        try
        {
            await runner.StartAsync(new(Guid.NewGuid(), "Main lifetime", "", [support, main], main.Id));
            var supportProcess = await test.RetainWindow(support);
            var mainProcess = await test.RetainWindow(main);
            mainProcess.Kill(); // This exact test-owned process simulates the main app exiting.
            await mainProcess.WaitForExitAsync(test.Token);
            while (runner.Snapshot!.State != SessionRunState.AwaitingEndConfirmation) await Task.Delay(25, test.Token);
            Assert.False(supportProcess.HasExited);
            await runner.EndAsync();
            Assert.True(supportProcess.HasExited);
            Assert.Equal(SessionRunState.Completed, runner.Snapshot.State);
        }
        finally { if (runner.Snapshot?.IsActive == true) await runner.LeaveAppsOpenAsync(); }
    }

    [Fact(Skip = "Opt-in isolated single-instance/activation test processes.", SkipUnless = nameof(RunNativeRuntime))]
    public async Task SecondInstanceSignalsOwnerAndCannotAcquireLibraryGuard()
    {
        using var test = new NativeFixture();
        var name = Guid.NewGuid().ToString("N");
        var first = test.StartInstance(name);
        await test.WaitForFile("owner");
        var second = test.StartInstance(name);
        await second.WaitForExitAsync(test.Token);
        await test.WaitForFile("secondary");
        await test.WaitForFile("activated");
        Assert.False(first.HasExited);
        Assert.Equal(0, second.ExitCode);
        File.WriteAllText(Path.Combine(test.DirectoryPath, "release-instance"), "release");
        await first.WaitForExitAsync(test.Token);
        File.Delete(Path.Combine(test.DirectoryPath, "release-instance"));
        File.Delete(Path.Combine(test.DirectoryPath, "owner"));
        var third = test.StartInstance(name);
        await test.WaitForFile("owner");
        Assert.Equal(third.Id.ToString(), File.ReadAllText(Path.Combine(test.DirectoryPath, "owner")));
        File.WriteAllText(Path.Combine(test.DirectoryPath, "release-instance"), "release");
        await third.WaitForExitAsync(test.Token);
        Assert.Equal(0, third.ExitCode);
    }

    [Fact(Skip = "Opt-in isolated force-quit test; never closes user apps.", SkipUnless = nameof(RunNativeRuntime))]
    public async Task DefaultStopPreservesRefusingAppUntilTargetedForceQuit()
    {
        using var test = new NativeFixture();
        var owned = test.App("force-owned", refuse: true);
        var existing = test.App("force-existing", refuse: true);
        await new WindowsProcessStarter().StartAsync(existing, test.Token);
        var existingProcess = await test.RetainWindow(existing);
        var runner = new SessionRunner(new WindowsSessionProcessHost(), TimeSpan.FromMilliseconds(200));
        try
        {
            await runner.StartAsync(new(Guid.NewGuid(), "Force quit", "", [owned, existing]));
            var ownedProcess = await test.RetainWindow(owned);
            await runner.EndAsync();
            Assert.False(ownedProcess.HasExited);
            Assert.False(existingProcess.HasExited);
            Assert.Equal(SessionRunState.NeedsAttention, runner.Snapshot!.State);
            await runner.ForceQuitAppAsync(runner.Snapshot.RunId, existing.Id);
            Assert.False(existingProcess.HasExited);
            await runner.ForceQuitAppAsync(runner.Snapshot.RunId, owned.Id);
            Assert.True(ownedProcess.HasExited);
            Assert.False(existingProcess.HasExited);
            Assert.Equal(SessionRunState.Completed, runner.Snapshot!.State);
        }
        finally { if (runner.Snapshot?.IsActive == true) await runner.LeaveAppsOpenAsync(); }
    }

    [Theory(Skip = "Opt-in isolated self-restart tests; no real UAC prompts.", SkipUnless = nameof(RunNativeRuntime))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TracksSelfRestartAfterOriginalExitWithoutEndingMainOrAdoptingUnrelatedCopy(bool exitBeforeFirstMonitor)
    {
        using var test = new NativeFixture();
        var app = test.App("restart");
        var runner = new SessionRunner(new WindowsSessionProcessHost(), TimeSpan.FromMilliseconds(200));
        try
        {
            if (exitBeforeFirstMonitor) File.WriteAllText(Path.Combine(app.WorkingDirectory, "approve-handoff"), "go");
            var launch = app with { Arguments = $"handoff \"{app.WorkingDirectory}\"" };
            await runner.StartAsync(new(Guid.NewGuid(), "Restart", "", [launch], app.Id));
            if (!exitBeforeFirstMonitor)
            {
                await runner.RefreshAsync();
                Assert.Equal(SessionRunState.Running, runner.Snapshot!.State);
                File.WriteAllText(Path.Combine(app.WorkingDirectory, "approve-handoff"), "go");
            }
            var replacement = await test.RetainWindow(app);
            do
            {
                await Task.Delay(25, test.Token);
                await runner.RefreshAsync();
            } while (runner.Snapshot!.State == SessionRunState.Running && !runner.Snapshot.Apps[0].Message.Contains("restarted"));
            Assert.Equal(SessionRunState.Running, runner.Snapshot!.State);
            Assert.Contains("restarted", runner.Snapshot.Apps[0].Message);

            // Same executable path, but launched independently after this run started.
            var otherDirectory = Path.Combine(test.DirectoryPath, "independent");
            Directory.CreateDirectory(otherDirectory);
            var unrelated = app with { Arguments = $"window \"{otherDirectory}\" accept", WorkingDirectory = otherDirectory };
            await new WindowsProcessStarter().StartAsync(unrelated, test.Token);
            var unrelatedProcess = await test.RetainWindow(unrelated);
            await runner.EndAsync();
            Assert.True(replacement.HasExited);
            Assert.False(unrelatedProcess.HasExited);
            Assert.Equal(SessionRunState.Completed, runner.Snapshot.State);
        }
        finally { if (runner.Snapshot?.IsActive == true) await runner.LeaveAppsOpenAsync(); }
    }

    [Theory(Skip = "Opt-in helper identity tests; helper runs without elevation and touches only its own fixture.", SkipUnless = nameof(RunNativeRuntime))]
    [InlineData("correct")]
    [InlineData("wrong-time")]
    [InlineData("wrong-path")]
    [InlineData("wrong-session")]
    [InlineData("normal")]
    public async Task CleanupHelperRequiresExactIdentity(string identityCase)
    {
        using var test = new NativeFixture();
        var app = test.App("helper-target", refuse: true);
        await new WindowsProcessStarter().StartAsync(app, test.Token);
        var process = await test.RetainWindow(app);
        using var identity = WindowsProcessIdentity.Open(process.Id);
        var info = WindowsProcessCleanup.CreateHelperStartInfo(Path.Combine(AppContext.BaseDirectory, "Sessions.App.exe"),
            identity.Id, identity.Created + (identityCase == "wrong-time" ? 1 : 0),
            identityCase == "wrong-path" ? app.ExecutablePath + ".different" : identity.Path,
            identityCase == "wrong-session" ? identity.SessionId + 1 : identity.SessionId, identityCase != "normal", TimeSpan.FromMilliseconds(100));
        using var helper = Process.Start(info)!;
        await helper.WaitForExitAsync(test.Token);
        Assert.Equal(identityCase == "correct" ? 0 : identityCase == "normal" ? 2 : 3, helper.ExitCode);
        Assert.Equal(identityCase == "correct", process.HasExited);
    }

    [Theory(Skip = "Opt-in window cleanup regressions; only use isolated helper apps.", SkipUnless = nameof(RunNativeRuntime))]
    [InlineData("guarded")]
    [InlineData("hidden")]
    public async Task NormalCloseDoesNotDestroyInternalHelperWindows(string mode)
    {
        using var test = new NativeFixture();
        var app = test.App("internal-windows");
        app = app with { Arguments = $"window \"{app.WorkingDirectory}\" {mode}" };
        var runner = new SessionRunner(new WindowsSessionProcessHost(), TimeSpan.FromMilliseconds(500));
        try
        {
            await runner.StartAsync(new(Guid.NewGuid(), "Guarded windows", "", [app]));
            var process = await test.RetainWindow(app);
            await runner.EndAsync();
            Assert.False(File.Exists(Path.Combine(app.WorkingDirectory, "internal-window-closed")));
            Assert.True(File.Exists(Path.Combine(app.WorkingDirectory, "main-close-requested")));
            Assert.True(process.HasExited);
            Assert.Equal(SessionRunState.Completed, runner.Snapshot!.State);
        }
        finally { if (runner.Snapshot?.IsActive == true) await runner.LeaveAppsOpenAsync(); }
    }

    [Theory(Skip = "Opt-in lingering-process regression; only uses an isolated helper app.", SkipUnless = nameof(RunNativeRuntime))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowDisappearanceIsNotExitAndForceQuitTerminatesExactProcess(bool force)
    {
        using var test = new NativeFixture();
        var app = test.App("lingering");
        app = app with { Arguments = $"window \"{app.WorkingDirectory}\" linger" };
        await new WindowsProcessStarter().StartAsync(app, test.Token);
        var process = await test.RetainWindow(app);
        using var identity = WindowsProcessIdentity.Open(process.Id);
        var closed = await WindowsProcessCleanup.CloseAsync(identity, TimeSpan.FromMilliseconds(200), force);
        Assert.True(File.Exists(Path.Combine(app.WorkingDirectory, "main-close-requested")));
        Assert.Equal(force, process.HasExited);
        Assert.Equal(force, closed);
    }

    [Theory(Skip = "Opt-in isolated unsaved-document dialog; never opens Word or user documents.", SkipUnless = nameof(RunNativeRuntime))]
    [InlineData("save")]
    [InlineData("discard")]
    public async Task SavePromptSurvivesDefaultTimeoutAndCancelUntilUserCloses(string choice)
    {
        using var test = new NativeFixture();
        var app = test.App("document");
        app = app with { Arguments = $"window \"{app.WorkingDirectory}\" save-prompt" };
        var runner = new SessionRunner(new WindowsSessionProcessHost()); // Real default three-second timeout.
        try
        {
            await runner.StartAsync(new(Guid.NewGuid(), "Unsaved work", "", [app]));
            var process = await test.RetainWindow(app);
            await runner.EndAsync();
            Assert.True(File.Exists(Path.Combine(app.WorkingDirectory, "save-prompt-open")));
            await Task.Delay(500, test.Token); // Still alive beyond the complete grace period.
            Assert.False(process.HasExited);
            Assert.Equal(SessionRunState.NeedsAttention, runner.Snapshot!.State);
            File.WriteAllText(Path.Combine(app.WorkingDirectory, "save-choice"), "cancel");
            while (!File.Exists(Path.Combine(app.WorkingDirectory, "choice-cancel"))) await Task.Delay(25, test.Token);
            await Task.Delay(1100, test.Token); // Background observation must not reissue close.
            Assert.False(process.HasExited);
            Assert.False(File.Exists(Path.Combine(app.WorkingDirectory, "save-prompt-open")));
            Assert.Equal(SessionRunState.NeedsAttention, runner.Snapshot.State);
            await runner.EndAsync(); // User explicitly retries End and the app asks again.
            Assert.True(File.Exists(Path.Combine(app.WorkingDirectory, "save-prompt-open")));
            File.WriteAllText(Path.Combine(app.WorkingDirectory, "save-choice"), choice);
            await process.WaitForExitAsync(test.Token);
            while (runner.Snapshot.IsActive) await Task.Delay(25, test.Token); // Auto-finish, without another End.
            Assert.Equal(SessionRunState.Completed, runner.Snapshot.State);
            Assert.Equal(choice == "save", File.Exists(Path.Combine(app.WorkingDirectory, "saved-document")));
        }
        finally { if (runner.Snapshot?.IsActive == true) await runner.LeaveAppsOpenAsync(); }
    }

    private sealed class NativeFixture : IDisposable
    {
        private readonly CancellationTokenSource _timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private readonly List<Process> _processes = [];
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "Sessions runtime test " + Guid.NewGuid().ToString("N"));
        public CancellationToken Token => _timeout.Token;
        public NativeFixture() { Directory.CreateDirectory(DirectoryPath); _timeout.CancelAfter(TimeSpan.FromSeconds(12)); }
        public StartProcessAction App(string name, bool refuse = false)
        {
            var directory = Path.Combine(DirectoryPath, name);
            Directory.CreateDirectory(directory);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "launch-probe")))
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            return new(Guid.NewGuid(), name, Path.Combine(directory, "Sessions.LaunchProbe.exe"),
                $"window \"{directory}\" {(refuse ? "refuse" : "accept")}", directory);
        }
        public async Task<Process> RetainWindow(StartProcessAction app)
        {
            var ready = Path.Combine(app.WorkingDirectory, "window-ready");
            while (!File.Exists(ready)) await Task.Delay(25, Token);
            var process = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(ready, Token)));
            _ = process.SafeHandle;
            _processes.Add(process);
            return process;
        }
        public Process StartInstance(string name)
        {
            var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "launch-probe", "Sessions.LaunchProbe.exe"))
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            info.ArgumentList.Add("instance");
            info.ArgumentList.Add(DirectoryPath);
            info.ArgumentList.Add(name);
            var process = Process.Start(info)!;
            _processes.Add(process);
            return process;
        }
        public async Task WaitForFile(string name)
        {
            while (!File.Exists(Path.Combine(DirectoryPath, name))) await Task.Delay(25, Token);
        }
        public void Dispose()
        {
            foreach (var process in _processes)
            {
                if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); }
                process.Dispose();
            }
            _timeout.Dispose();
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
