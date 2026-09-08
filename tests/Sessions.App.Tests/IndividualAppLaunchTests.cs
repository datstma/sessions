using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class IndividualAppLaunchTests
{
    private static readonly StartProcessAction App = new(Guid.NewGuid(), "Notes", @"C:\Apps\Notes.exe",
        "--profile \"My work\"", @"C:\My work");

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyboardLaunchUsesSavedSettingsThenShowsRealPresence(bool dark)
    {
        var presence = new FakePresence();
        var starter = new FakeStarter { Gate = new() };
        var model = new MainViewModel(new NoWriteStore(), presence, new IndividualAppLauncher(presence, starter));
        var window = new MainWindow { DataContext = model, Width = 860, Height = 620,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await model.RefreshPresenceAsync();
            Dispatcher.UIThread.RunJobs();
            var row = model.SelectedSession!.Apps[0];
            var button = window.GetVisualDescendants().OfType<Button>()
                .Single(control => ReferenceEquals(control.Command, row.LaunchCommand));
            Assert.True(button.IsVisible);
            Capture(window, $"launch-{(dark ? "dark" : "light")}");
            Assert.True(button.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.True(row.IsStarting);
            Assert.False(row.LaunchCommand.CanExecute(null));
            var launchTask = row.LaunchCommand.ExecutionTask!;
            await row.LaunchCommand.ExecuteAsync(null); // Even direct repeated execution is guarded.
            Assert.Single(starter.Started);
            Capture(window, $"starting-{(dark ? "dark" : "light")}");
            starter.Gate.SetResult();
            await launchTask;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(App, Assert.Single(starter.Started));
            Assert.True(row.IsStarting); // Process.Start success is not proof of ongoing presence.
            presence.Presence = AppPresence.Window;
            await model.RefreshPresenceAsync();
            Assert.True(row.IsRunning);
            Assert.False(row.IsStarting);
            Assert.True(row.FocusCommand.CanExecute(null));
        }
        finally { starter.Gate.TrySetResult(); window.Close(); }
    }

    [Theory]
    [InlineData(AppPresence.Window)]
    [InlineData(AppPresence.Background)]
    public async Task FreshPresencePreventsLaunchingAnAppThatAlreadyOpened(AppPresence state)
    {
        var starter = new FakeStarter();
        var launcher = new IndividualAppLauncher(new FakePresence { Presence = state }, starter);
        var result = await launcher.LaunchAsync(App, TestContext.Current.CancellationToken);
        Assert.Equal(state, result.Presence);
        Assert.Empty(starter.Started);
    }

    [Theory]
    [InlineData(AppPresence.Unknown)]
    [InlineData(AppPresence.Checking)]
    public async Task UncertainPresenceDoesNotLaunch(AppPresence state)
    {
        var starter = new FakeStarter();
        var launcher = new IndividualAppLauncher(new FakePresence { Presence = state }, starter);
        await Assert.ThrowsAsync<InvalidOperationException>(() => launcher.LaunchAsync(App, TestContext.Current.CancellationToken));
        Assert.Empty(starter.Started);
    }

    [Fact]
    public async Task ConcurrentRowsAndSlowStartupDoNotCreateDuplicateProcessesAndRetryExpires()
    {
        var starter = new FakeStarter { Gate = new() };
        var time = new TestTime();
        var launcher = new IndividualAppLauncher(new FakePresence(), starter, time);
        var first = launcher.LaunchAsync(App, TestContext.Current.CancellationToken);
        var second = launcher.LaunchAsync(App with { ExecutablePath = App.ExecutablePath.ToUpperInvariant() }, TestContext.Current.CancellationToken);
        Assert.Single(starter.Started);
        starter.Gate.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Single(starter.Started);
        Assert.Equal(results[0].PendingUntil, results[1].PendingUntil);
        time.Now = time.Now.AddSeconds(11);
        await launcher.LaunchAsync(App, TestContext.Current.CancellationToken);
        Assert.Equal(2, starter.Started.Count);
    }

    [AvaloniaFact]
    public async Task LaunchFailureShowsInlineErrorAndAllowsRetry()
    {
        var presence = new FakePresence();
        var starter = new FakeStarter { Error = new Win32Exception("Access denied") };
        var row = new SessionViewModel(new(Guid.NewGuid(), "Work", "", [App]), presence,
            new IndividualAppLauncher(presence, starter)).Apps[0];
        row.ApplyPresence(AppPresence.NotRunning);
        await row.LaunchCommand.ExecuteAsync(null);
        Assert.Contains("Couldn't start Notes", row.FocusMessage);
        Assert.Contains("Access denied", row.FocusMessage);
        Assert.False(row.IsStarting);
        Assert.True(row.LaunchCommand.CanExecute(null));
        starter.Error = null;
        await row.LaunchCommand.ExecuteAsync(null);
        Assert.Null(row.FocusMessage);
        Assert.True(row.IsStarting);
        Assert.Equal(2, starter.Started.Count);
    }

    [AvaloniaFact]
    public async Task StaleNotRunningClickFocusesTheNowRunningAppWithoutLaunching()
    {
        var presence = new FakePresence { Presence = AppPresence.Window };
        var starter = new FakeStarter();
        var row = new SessionViewModel(new(Guid.NewGuid(), "Work", "", [App]), presence,
            new IndividualAppLauncher(presence, starter)).Apps[0];
        row.ApplyPresence(AppPresence.NotRunning);
        await row.LaunchCommand.ExecuteAsync(null);
        Assert.Empty(starter.Started);
        Assert.Equal(1, presence.FocusCount);
        Assert.True(row.IsRunning);
    }

    [Fact]
    public async Task WindowsStarterRejectsMissingExecutableAndInvalidWorkingFolder()
    {
        if (!OperatingSystem.IsWindows()) return;
        var starter = new WindowsProcessStarter();
        await Assert.ThrowsAsync<ArgumentException>(() => starter.StartAsync(App with { ExecutablePath = "Notes.exe" }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<FileNotFoundException>(() => starter.StartAsync(App with
            { ExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "Missing.exe") }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => starter.StartAsync(App with
            { ExecutablePath = Environment.ProcessPath!, WorkingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) }, TestContext.Current.CancellationToken));
    }

    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SESSIONS_SCREENSHOT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    private sealed class FakePresence : IAppPresenceService
    {
        public AppPresence Presence { get; set; } = AppPresence.NotRunning;
        public int FocusCount { get; private set; }
        public Task<IReadOnlyDictionary<string, AppPresence>> GetPresenceAsync(IReadOnlyList<string> paths,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, AppPresence>>(
                paths.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(path => path, _ => Presence, StringComparer.OrdinalIgnoreCase));
        public Task<AppFocusResult> FocusAsync(string path, CancellationToken cancellationToken = default)
        {
            FocusCount++;
            return Task.FromResult(AppFocusResult.Focused);
        }
    }

    private sealed class FakeStarter : IProcessStarter
    {
        public List<StartProcessAction> Started { get; } = [];
        public TaskCompletionSource? Gate { get; init; }
        public Exception? Error { get; set; }
        public async Task StartAsync(StartProcessAction app, CancellationToken cancellationToken = default)
        {
            Started.Add(app);
            if (Error is not null) throw Error;
            if (Gate is not null) await Gate.Task;
        }
    }

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class NoWriteStore : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionDefinition>>([new(Guid.NewGuid(), "Work", "Open just the app you need.", [App])]);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Individual launches must not write the library.");
    }
}
