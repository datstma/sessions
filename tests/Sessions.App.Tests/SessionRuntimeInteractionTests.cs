using Avalonia;
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

public sealed class SessionRuntimeInteractionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartEndAndActiveNavigationWorkWithKeyboard(bool dark)
    {
        var host = new Host();
        var runner = new SessionRunner(host);
        var model = new MainViewModel(new Store(), runner: runner);
        var window = new MainWindow { DataContext = model, Width = 860, Height = 620,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var start = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, model.StartSessionCommand));
            model.SelectedSession = model.Sessions[2];
            Dispatcher.UIThread.RunJobs();
            Assert.False(start.IsVisible);
            Assert.False(model.StartSessionCommand.CanExecute(null));
            Assert.True(window.FindControl<Button>("EditSessionButton")!.IsEffectivelyVisible);
            Assert.True(model.EditSessionCommand.CanExecute(null));
            await model.StartSessionCommand.ExecuteAsync(null);
            Assert.Null(runner.Snapshot);
            model.SelectedSession = model.Sessions[0];
            Dispatcher.UIThread.RunJobs();
            Assert.True(start.IsVisible);
            Assert.True(start.IsEnabled);
            Press(window, start);
            if (model.StartSessionCommand.ExecutionTask is { } task) await task;
            Assert.True(model.HasActiveRun);
            Assert.True(model.SelectedSession!.IsActive);
            Assert.False(model.EditSessionCommand.CanExecute(null));
            Assert.False(model.RequestDeleteSessionCommand.CanExecute(null));
            Capture(window, dark ? "session-active-dark" : "session-active-light");
            model.SelectedSession = model.Sessions[1];
            Assert.False(model.StartSessionCommand.CanExecute(null));
            Assert.True(model.ShowActiveNavigation);
            Assert.True(model.Sessions[0].IsActive);
            model.ViewActiveSessionCommand.Execute(null);
            Assert.Same(model.Sessions[0], model.SelectedSession);
            model.SelectedSession = model.Sessions[1]; // End must still name the active Session.
            await model.EndSessionCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsEndConfirmation);
            Assert.Contains("Work", model.EndConfirmationTitle);
            Assert.Equal(new[] { "Notes" }, model.AppsToStop);
            Assert.True(window.FindControl<Button>("CancelEndButton")!.IsFocused);
            Assert.False(host.Process.Closed);
            Assert.False(model.IsMainContentEnabled);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Assert.False(model.IsEndConfirmation);
            Assert.True(model.HasActiveRun);
            Assert.False(host.Process.Closed);
            await model.EndSessionCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            Assert.False(model.IsEndConfirmation);
            Assert.False(host.Process.Closed);
            await model.EndSessionCommand.ExecuteAsync(null);
            Capture(window, dark ? "end-confirmation-dark" : "end-confirmation-light");
            Press(window, window.FindControl<Button>("ConfirmEndButton")!);
            if (model.ConfirmEndSessionCommand.ExecutionTask is { } endTask) await endTask;
            Assert.False(model.HasActiveRun);
            Assert.True(model.StartSessionCommand.CanExecute(null));
            Assert.True(host.Process.Closed);
        }
        finally { if (runner.Snapshot?.IsActive == true) await runner.LeaveAppsOpenAsync(); model.CancelEditCommand.Execute(null); window.Close(); }
    }

    [AvaloniaFact]
    public async Task RefusedCleanupIsVisibleAndRequiresExplicitFinishOrRetry()
    {
        var host = new Host { RefuseClose = true };
        var model = new MainViewModel(new Store(), runner: new SessionRunner(host));
        await model.LoadCommand.ExecuteAsync(null);
        await model.StartSessionCommand.ExecuteAsync(null);
        await model.EndSessionCommand.ExecuteAsync(null);
        await model.ConfirmEndSessionCommand.ExecuteAsync(null);
        Assert.True(model.NeedsCleanup);
        Assert.False(model.StartSessionCommand.CanExecute(null));
        Assert.Contains("Still open", model.SelectedSession!.Apps[0].RunMessage);
        model.FinishSessionCommand.Execute(null);
        Assert.False(model.HasActiveRun);
        Assert.False(host.Process.Closed);
        Assert.True(model.StartSessionCommand.CanExecute(null));
        model.Dispose();
    }

    [AvaloniaTheory]
    [InlineData(false, 640, 480)]
    [InlineData(true, 640, 480)]
    [InlineData(false, 1440, 900)]
    [InlineData(true, 1440, 900)]
    public async Task WaitingAppCanBeFocusedOrExplicitlyForcedWithSafeKeyboardDefaults(bool dark, int width, int height)
    {
        var host = new Host { RefuseClose = true };
        var runner = new SessionRunner(host);
        var presence = new CleanupPresence();
        var model = new MainViewModel(new Store(), presenceService: presence, runner: runner);
        var window = new MainWindow { DataContext = model, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await model.StartSessionCommand.ExecuteAsync(null);
            await model.EndSessionCommand.ExecuteAsync(null);
            await model.ConfirmEndSessionCommand.ExecuteAsync(null);
            model.SelectedSession = model.Sessions[1];
            var app = Assert.Single(model.CleanupApps);
            Assert.Equal("Notes", app.Name);
            await runner.RefreshAsync();
            Assert.Same(app, Assert.Single(model.CleanupApps));
            await model.FocusCleanupAppCommand.ExecuteAsync(app);
            Assert.Equal(@"C:\Apps\Notes.exe", presence.FocusedPath);
            Capture(window, $"waiting-app-{width}-{(dark ? "dark" : "light")}");
            var force = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, model.RequestForceQuitCommand));
            force.BringIntoView();
            Press(window, force);
            Assert.True(model.IsForceQuitConfirmation);
            Assert.Contains("Notes", model.ForceQuitTitle);
            Assert.Contains("Work", model.ForceQuitDescription);
            Assert.False(model.IsMainContentEnabled);
            Assert.False(model.RequestWindowClose());
            Assert.False(model.IsCloseConfirmation);
            Assert.True(window.FindControl<Button>("CancelForceQuitButton")!.IsFocused);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.IsForceQuitConfirmation);
            Assert.False(host.Process.Closed);
            Assert.True(force.IsFocused);
            Press(window, force);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            Assert.False(model.IsForceQuitConfirmation);
            Press(window, force);
            Capture(window, $"force-quit-{width}-{(dark ? "dark" : "light")}");
            var confirm = window.FindControl<Button>("ConfirmForceQuitButton")!;
            Assert.True(confirm.IsEffectivelyVisible);
            Assert.True(confirm.TranslatePoint(default, window)!.Value.Y + confirm.Bounds.Height <= height);
            Press(window, confirm);
            if (model.ConfirmForceQuitCommand.ExecutionTask is { } task) await task;
            Assert.True(host.Process.Closed);
            Assert.False(model.HasActiveRun);
            Assert.Empty(model.CleanupApps);
        }
        finally { if (runner.Snapshot?.IsActive == true) await runner.LeaveAppsOpenAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task SavingAndClosingDismissesStaleForceConfirmationAndFinishesRun()
    {
        var host = new Host { RefuseClose = true };
        var runner = new SessionRunner(host);
        using var model = new MainViewModel(new Store(), runner: runner);
        await model.LoadCommand.ExecuteAsync(null);
        await model.StartSessionCommand.ExecuteAsync(null);
        await model.EndSessionCommand.ExecuteAsync(null);
        await model.ConfirmEndSessionCommand.ExecuteAsync(null);
        model.RequestForceQuitCommand.Execute(Assert.Single(model.CleanupApps));
        host.Process.Exit();
        await runner.RefreshAsync();
        Assert.False(model.IsForceQuitConfirmation);
        Assert.False(model.HasActiveRun);
        Assert.False(model.ConfirmForceQuitCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndAndCloseNamesAutomaticForceAppsAndKeepsRecoveryWhenDefaultAppStaysOpen(bool dark)
    {
        var normal = new StartProcessAction(Guid.NewGuid(), "Notes", @"C:\Notes.exe");
        var forced = new StartProcessAction(Guid.NewGuid(), string.Join(" ", Enumerable.Repeat("A long utility name", 12)), @"C:\Utility.exe", AllowForceQuit: true);
        var host = new AutomaticHost(false);
        host.Support.Refuse = true;
        var runner = new SessionRunner(host);
        var model = new MainViewModel(new DefinitionStore(new(Guid.NewGuid(), "Work", "", [normal, forced])), runner: runner);
        var window = new MainWindow { DataContext = model, Width = 640, Height = 480,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await model.StartSessionCommand.ExecuteAsync(null);
            await model.EndSessionCommand.ExecuteAsync(null);
            Assert.True(model.HasAutomaticForceQuit);
            Assert.Contains(forced.Name, model.AutomaticForceQuitWarning);
            Assert.DoesNotContain(normal.Name, model.AutomaticForceQuitWarning);
            model.CancelEndSessionCommand.Execute(null);
            window.Close();
            Capture(window, $"close-force-warning-compact-{(dark ? "dark" : "light")}");
            var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible &&
                (ReferenceEquals(b.Command, model.CancelCloseCommand) || ReferenceEquals(b.Command, model.EndAndCloseCommand) ||
                 ReferenceEquals(b.Command, model.LeaveAppsAndCloseCommand))).ToArray();
            Assert.Equal(3, buttons.Length);
            Assert.All(buttons, b => Assert.InRange(b.TranslatePoint(default, window)!.Value.Y + b.Bounds.Height, 0, 480));
            await model.EndAndCloseCommand.ExecuteAsync(null);
            Assert.True(window.IsVisible);
            Assert.True(model.NeedsCleanup);
            Assert.False(host.Support.Closed);
            Assert.True(host.Main.Closed);
            host.Support.Exit();
            await runner.RefreshAsync();
            Assert.False(model.HasActiveRun);
            Assert.True(window.IsVisible);
        }
        finally { if (runner.Snapshot?.IsActive == true) await runner.LeaveAppsOpenAsync(); window.Close(); }
    }

    private sealed class CleanupPresence : IAppPresenceService
    {
        public string? FocusedPath { get; private set; }
        public Task<IReadOnlyDictionary<string, AppPresence>> GetPresenceAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, AppPresence>>(paths.ToDictionary(p => p, _ => AppPresence.Window));
        public Task<AppFocusResult> FocusAsync(string path, CancellationToken cancellationToken = default)
        { FocusedPath = path; return Task.FromResult(AppFocusResult.Focused); }
    }

    [AvaloniaFact]
    public async Task ClosingAnActiveSessionOffersCancelOrEndAndClose()
    {
        var model = new MainViewModel(new Store(), runner: new SessionRunner(new Host()));
        var window = new MainWindow { DataContext = model };
        window.Show();
        await model.StartSessionCommand.ExecuteAsync(null);
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsVisible);
        Assert.True(model.IsCloseConfirmation);
        Assert.True(window.FindControl<Button>("KeepSessionsOpenButton")!.IsFocused);
        model.CancelCloseCommand.Execute(null);
        Assert.True(model.HasActiveRun);
        window.Close();
        Assert.True(model.EndAndCloseCommand.CanExecute(null));
        await model.EndAndCloseCommand.ExecuteAsync(null);
        Assert.False(window.IsVisible);
        Assert.False(model.HasActiveRun);
    }

    [AvaloniaFact]
    public async Task KeepOpenDuringEndAndCloseCancelsWindowExitWithoutCancellingCleanup()
    {
        var host = new Host();
        host.Process.CloseGate = new();
        var model = new MainViewModel(new Store(), runner: new SessionRunner(host));
        var window = new MainWindow { DataContext = model };
        window.Show();
        await model.StartSessionCommand.ExecuteAsync(null);
        window.Close();
        var ending = model.EndAndCloseCommand.ExecuteAsync(null);
        model.CancelCloseCommand.Execute(null);
        host.Process.CloseGate.SetResult();
        await ending;
        Assert.True(window.IsVisible);
        Assert.False(model.HasActiveRun);
        Assert.True(host.Process.Closed);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ManualLaunchIsDisabledAcrossLibraryWhileSessionIsActive()
    {
        var model = new MainViewModel(new Store(), appLauncher: new NoLaunch(), runner: new SessionRunner(new Host()));
        await model.LoadCommand.ExecuteAsync(null);
        foreach (var row in model.Sessions.SelectMany(s => s.Apps)) row.ApplyPresence(AppPresence.NotRunning);
        Assert.True(model.Sessions[1].Apps[0].LaunchCommand.CanExecute(null));
        await model.StartSessionCommand.ExecuteAsync(null);
        Assert.False(model.Sessions[1].Apps[0].LaunchCommand.CanExecute(null));
        await model.EndSessionCommand.ExecuteAsync(null);
        await model.ConfirmEndSessionCommand.ExecuteAsync(null);
        Assert.True(model.Sessions[1].Apps[0].LaunchCommand.CanExecute(null));
        model.Dispose();
    }

    private static void Press(Window window, Button button)
    {
        Dispatcher.UIThread.RunJobs();
        button.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticEndOrStartupFailureWaitsForSaveWorkConfirmation(bool failStartup)
    {
        var support = new StartProcessAction(Guid.NewGuid(), "Notes", @"C:\Apps\Notes.exe");
        var main = new StartProcessAction(Guid.NewGuid(), "Main", @"C:\Apps\Main.exe");
        var definition = new SessionDefinition(Guid.NewGuid(), "Auto", "", [support, main], main.Id);
        var host = new AutomaticHost(failStartup);
        var runner = new SessionRunner(host);
        var model = new MainViewModel(new DefinitionStore(definition), runner: runner);
        await model.LoadCommand.ExecuteAsync(null);
        await model.StartSessionCommand.ExecuteAsync(null);
        if (!failStartup)
        {
            await model.ConfirmEndSessionCommand.ExecuteAsync(null); // No confirmation is pending.
            Assert.False(host.Support.Closed);
            model.NewSessionCommand.Execute(null); // Don't interrupt an unrelated draft with the automatic dialog.
            host.Main.Exit();
            await runner.RefreshAsync();
            Assert.False(model.IsEndConfirmation);
            model.CancelEditCommand.Execute(null);
        }
        Assert.Equal(SessionRunState.AwaitingEndConfirmation, runner.Snapshot!.State);
        Assert.True(model.IsEndConfirmation);
        Assert.False(host.Support.Closed);
        Assert.Contains("Notes", model.AppsToStop);
        model.CancelEndSessionCommand.Execute(null);
        Assert.False(model.IsEndConfirmation);
        await runner.RefreshAsync();
        Assert.False(model.IsEndConfirmation);
        Assert.False(host.Support.Closed);
        await model.EndSessionCommand.ExecuteAsync(null);
        Assert.True(model.IsEndConfirmation);
        await model.ConfirmEndSessionCommand.ExecuteAsync(null);
        Assert.False(model.HasActiveRun);
        Assert.True(host.Support.Closed);
        model.Dispose();
    }

    private sealed class DefinitionStore(SessionDefinition definition) : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionDefinition>>([definition]);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> definitions, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Execution must not change saved definitions.");
    }

    private sealed class AutomaticHost(bool failStartup) : ISessionProcessHost
    {
        public Process Support { get; } = new();
        public Process Main { get; } = new();
        private int _calls;
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken token)
        {
            _calls++;
            if (_calls == 2 && failStartup) throw new IOException("Missing main app");
            return Task.FromResult(new ProcessAcquisition([_calls == 1 ? Support : Main], true, "Owned"));
        }
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
    private sealed class Store : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionDefinition>>([
                new(Guid.NewGuid(), "Work", "Everything ready together.", [new(Guid.NewGuid(), "Notes", @"C:\Apps\Notes.exe")]),
                new(Guid.NewGuid(), "Development", "", [new(Guid.NewGuid(), "Editor", @"C:\Apps\Editor.exe")]),
                new(Guid.NewGuid(), "Empty", "", [])]);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Runtime actions must not save configuration.");
    }
    private sealed class Host : ISessionProcessHost
    {
        public Process Process { get; } = new();
        public bool RefuseClose { get; init; }
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken)
        {
            Process.Refuse = RefuseClose;
            return Task.FromResult(new ProcessAcquisition([Process], true, "Opened by this Session · closes when it ends"));
        }
    }
    private sealed class Process : ITrackedProcess
    {
        public bool Closed { get; private set; }
        public bool Refuse { get; set; }
        public TaskCompletionSource? CloseGate { get; set; }
        public bool HasExited => Closed;
        public void Exit() => Closed = true;
        public async Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false)
        {
            if (CloseGate is not null) await CloseGate.Task;
            Closed = !Refuse || allowForceQuit;
            return Closed;
        }
        public void Dispose() { }
    }
    private sealed class NoLaunch : IIndividualAppLauncher
    {
        public Task<AppLaunchResult> LaunchAsync(StartProcessAction app, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Manual launching must be disabled during a Session.");
    }
}
