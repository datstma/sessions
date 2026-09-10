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

public sealed class ManualCloseInteractionTests
{
    [AvaloniaTheory]
    [InlineData(false, 1440, 900, 1)]
    [InlineData(true, 1440, 900, 1)]
    [InlineData(false, 640, 480, 1)]
    [InlineData(true, 640, 480, 1)]
    [InlineData(false, 640, 480, 1.25)]
    [InlineData(true, 640, 480, 1.5)]
    [InlineData(false, 640, 480, 2)]
    [InlineData(true, 640, 480, 2)]
    public async Task RunningAppsOfferNamedCloseWithKeyboardConfirmationAndFixedControls(bool dark, int width, int height, double dpi)
    {
        var closer = new Closer();
        var preferences = new PreferencesService();
        await preferences.SaveAsync(new(dark ? AppTheme.Dark : AppTheme.Light, 150, 125));
        var model = new MainViewModel(new Library(), new Presence(), plugins: new Plugins(), appCloser: closer);
        var window = new MainWindow { DataContext = model, Preferences = preferences, Width = width, Height = height };
        window.Show();
        window.SetRenderScaling(dpi);
        try
        {
            await model.LoadCommand.ExecuteAsync(null);
            await model.RefreshPresenceAsync(); Layout(window);
            foreach (var row in model.SelectedSession!.Apps)
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "CloseAppButton" && control.DataContext == row);
                Assert.True(button.IsEffectivelyVisible);
                button.BringIntoView(); Layout(window); InBounds(window, button);
                var statusPanel = button.GetVisualAncestors().OfType<WrapPanel>().First();
                var status = statusPanel.Children.Single(control => control != button && control.IsVisible);
                Assert.Equal(status.Bounds.Height, button.Bounds.Height, precision: 2);
                Assert.Equal(status.Bounds.Top, button.Bounds.Top, precision: 2);
                button.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                Layout(window);
                Assert.True(model.IsAppCloseConfirmation);
                Assert.Contains(row.Name, model.AppCloseTitle);
                Assert.False(model.IsMainContentEnabled);
                Assert.False(model.RequestWindowClose());
                Assert.Equal(0, closer.Last!.Closes);
                Assert.Same(window.FindControl<Button>("CancelCloseAppButton"), window.FocusManager!.GetFocusedElement());
                InBounds(window, window.FindControl<Button>("CancelCloseAppButton")!);
                InBounds(window, window.FindControl<Button>("ConfirmCloseAppButton")!);
                Capture(window, $"manual-close-confirm-{dark}-{width}-{row.IsPlugin}-{dpi}");
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
                Layout(window);
                Assert.False(model.IsAppCloseConfirmation);
                Assert.True(closer.Last.Disposed);
                Assert.Equal(0, closer.Last.Closes);
                Assert.Same(button, window.FocusManager.GetFocusedElement());
            }
            var app = model.SelectedSession.Apps[0];
            await model.RequestCloseAppCommand.ExecuteAsync(app);
            closer.Last!.Result = false;
            await model.ConfirmCloseAppCommand.ExecuteAsync(null);
            Assert.False(model.IsAppCloseConfirmation);
            Assert.Equal(1, closer.Last.Closes);
            Assert.Contains("still running", app.CloseMessage);
            Assert.True(app.IsRunning);
            var closeButton = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "CloseAppButton" && control.DataContext == app);
            closeButton.BringIntoView(); Layout(window);
            Capture(window, $"manual-close-controls-{dark}-{width}-{dpi}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task CloseFeedbackTracksDelayedExitWithoutClearingUnrelatedErrors(bool dark, bool plugin, bool refuses)
    {
        var closer = new Closer();
        var presence = new Presence();
        var plugins = new Plugins();
        using var model = new MainViewModel(new Library(), presence, plugins: plugins, appCloser: closer);
        var window = new MainWindow { DataContext = model, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Width = 640, Height = 480 };
        window.Show();
        try
        {
            await model.LoadCommand.ExecuteAsync(null); await model.RefreshPresenceAsync();
            var app = model.SelectedSession!.Apps.Single(row => row.IsPlugin == plugin);
            await model.RequestCloseAppCommand.ExecuteAsync(app);
            closer.Last!.Result = !refuses;
            await model.ConfirmCloseAppCommand.ExecuteAsync(null);
            Layout(window);
            var feedback = window.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "CloseFeedback" && control.DataContext == app);
            var close = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "CloseAppButton" && control.DataContext == app);
            Assert.True(app.IsRunning);
            Assert.True(feedback.IsVisible);
            Assert.Equal(refuses, app.IsCloseError);
            Assert.Contains(refuses ? "still running" : "Waiting for the app to stop", feedback.Text);
            Assert.Equal(((Avalonia.Media.ISolidColorBrush)window.FindResource(window.ActualThemeVariant, refuses ? "SessionError" : "TextMutedBrush")!).Color,
                ((Avalonia.Media.ISolidColorBrush)feedback.Foreground!).Color);
            feedback.BringIntoView(); Layout(window);
            Capture(window, $"close-feedback-pending-{dark}-{plugin}-{refuses}");
            var pending = app.CloseMessage;
            presence.Current = AppPresence.Unknown;
            plugins.Current = PluginAppPresence.Unknown;
            await model.RefreshPresenceAsync();
            Assert.Equal(pending, app.CloseMessage);
            app.FocusMessage = "An unrelated focus error";
            presence.Current = AppPresence.NotRunning;
            plugins.Current = PluginAppPresence.NotRunning;
            await model.RefreshPresenceAsync(); Layout(window);
            Assert.False(app.IsRunning);
            Assert.False(feedback.IsVisible);
            Assert.False(close.IsVisible);
            Assert.Null(app.CloseMessage);
            Assert.False(app.IsCloseError);
            Assert.Equal("An unrelated focus error", app.FocusMessage);
            Assert.Equal(plugin ? "Launch via plugin" : "Not running", app.StatusLabel);
            Capture(window, $"close-feedback-stopped-{dark}-{plugin}-{refuses}");
            presence.Current = AppPresence.Window;
            plugins.Current = PluginAppPresence.Running;
            await model.RefreshPresenceAsync(); Layout(window);
            Assert.True(close.IsVisible);
            Assert.False(feedback.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PreparingAndClosingBlockCompetingActionsAndWindowClose()
    {
        var closer = new Closer { PrepareGate = new() };
        using var model = new MainViewModel(new Library(), new Presence(), appCloser: closer);
        await model.LoadCommand.ExecuteAsync(null); await model.RefreshPresenceAsync();
        var app = model.SelectedSession!.Apps[0];
        var preparing = model.RequestCloseAppCommand.ExecuteAsync(app);
        Assert.True(model.IsAppCloseBusy);
        Assert.False(model.RequestWindowClose());
        Assert.False(model.RequestCloseAppCommand.CanExecute(app));
        Assert.False(model.EditSessionCommand.CanExecute(null));
        closer.PrepareGate.SetResult(); await preparing;
        closer.Last!.CloseGate = new();
        var closing = model.ConfirmCloseAppCommand.ExecuteAsync(null);
        Assert.False(model.CancelCloseAppCommand.CanExecute(null));
        Assert.False(model.ConfirmCloseAppCommand.CanExecute(null));
        Assert.False(model.RequestWindowClose());
        closer.Last.CloseGate.SetResult(); await closing;
        Assert.True(closer.Last.Disposed);
        Assert.False(model.IsAppCloseBusy);
    }

    [AvaloniaFact]
    public async Task DisappearedOrUnverifiableAppNeverOpensDestructiveConfirmation()
    {
        var closer = new Closer { Targets = 0 };
        using var model = new MainViewModel(new Library(), new Presence(), appCloser: closer);
        await model.LoadCommand.ExecuteAsync(null); await model.RefreshPresenceAsync();
        var app = model.SelectedSession!.Apps[0];
        await model.RequestCloseAppCommand.ExecuteAsync(app);
        Assert.False(model.IsAppCloseConfirmation);
        Assert.True(closer.Last!.Disposed);
        Assert.Equal(0, closer.Last.Closes);
        closer.Fail = true;
        await model.RequestCloseAppCommand.ExecuteAsync(app);
        Assert.False(model.IsAppCloseConfirmation);
        Assert.Contains("Couldn't prepare", app.CloseMessage);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingOneActiveAppPreservesSupportAppsAndExistingMainExitConfirmation(bool isMain)
    {
        var target = new StartProcessAction(Guid.NewGuid(), "Target", @"C:\Fixture\target.exe");
        var support = new StartProcessAction(Guid.NewGuid(), "Support", @"C:\Fixture\support.exe");
        var definition = new SessionDefinition(Guid.NewGuid(), "Active", "", [target, support], isMain ? target.Id : null);
        var runtime = new RuntimeHost();
        var runner = new SessionRunner(runtime);
        using var model = new MainViewModel(new Library(definition), new Presence(), runner: runner, appCloser: runtime);
        try
        {
            await model.LoadCommand.ExecuteAsync(null);
            var starting = model.StartSessionCommand.ExecuteAsync(null);
            await model.RefreshPresenceAsync();
            Assert.False(model.RequestCloseAppCommand.CanExecute(model.SelectedSession!.Apps[0]));
            runtime.StartGate.SetResult(); await starting;
            await model.RequestCloseAppCommand.ExecuteAsync(model.SelectedSession.Apps[0]);
            await model.ConfirmCloseAppCommand.ExecuteAsync(null);
            Assert.True(runtime.States[target.Id].Exited);
            Assert.False(runtime.States[support.Id].Exited);
            Assert.True(runner.Snapshot!.IsActive);
            Assert.Equal(isMain ? SessionRunState.AwaitingEndConfirmation : SessionRunState.Running, runner.Snapshot.State);
            Assert.Equal(isMain, model.IsEndConfirmation);
        }
        finally { runtime.StartGate.TrySetResult(); await runner.LeaveAppsOpenAsync(); }
    }

    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void InBounds(Window window, Control control)
    {
        var top = control.TranslatePoint(default, window)!.Value;
        var bottom = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.True(top.X >= 0 && top.Y >= 0 && bottom.X <= window.Bounds.Width + 1 && bottom.Y <= window.Bounds.Height + 1);
    }
    private static void Capture(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SESSIONS_SCREENSHOT_DIR") is { Length: > 0 } directory)
        { Directory.CreateDirectory(directory); frame.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
    }
    private sealed class Library(SessionDefinition? definition = null) : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SessionDefinition>>(
            [definition ?? new(Guid.NewGuid(), "Work Session", "", [new(Guid.NewGuid(), "Utility", @"C:\Fixture\utility.exe"), new(Guid.NewGuid(), "3DMark", "", Plugin: new("test", "223850"))])]);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Presence : IAppPresenceService
    {
        public AppPresence Current = AppPresence.Window;
        public Task<IReadOnlyDictionary<string, AppPresence>> GetPresenceAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, AppPresence>>(paths.ToDictionary(path => path, _ => Current));
        public Task<AppFocusResult> FocusAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(AppFocusResult.Focused);
    }
    private sealed class Plugins : ISessionPluginHost, ISessionPluginLaunches
    {
        public PluginAppPresence Current = PluginAppPresence.Running;
        public ISessionPluginLaunches Capture() => this;
        public Task ValidateAsync(PluginAppReference app, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> LaunchAsync(PluginAppReference app, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<PluginAppPresence> GetPresenceAsync(PluginAppReference app, CancellationToken cancellationToken) => Task.FromResult(Current);
    }
    private sealed class Closer : IIndividualAppCloser
    {
        public Request? Last;
        public TaskCompletionSource? PrepareGate;
        public int Targets = 1;
        public bool Fail;
        public async Task<IPreparedAppClose> PrepareAsync(StartProcessAction app, CancellationToken cancellationToken = default)
        {
            if (PrepareGate is not null) await PrepareGate.Task;
            if (Fail) throw new InvalidOperationException("fixture unavailable");
            return Last = new Request { TargetCount = Targets };
        }
    }
    private sealed class Request : IPreparedAppClose
    {
        public int TargetCount { get; init; } = 1;
        public int Closes;
        public bool Disposed, Result = true;
        public TaskCompletionSource? CloseGate;
        public async Task<bool> CloseAsync() { Closes++; if (CloseGate is not null) await CloseGate.Task; return Result; }
        public void Dispose() => Disposed = true;
    }
    private sealed class RuntimeState { public bool Exited; }
    private sealed class RuntimeProcess(RuntimeState state) : ITrackedProcess
    {
        public bool HasExited => state.Exited;
        public Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false) { state.Exited = true; return Task.FromResult(true); }
        public void Dispose() { }
    }
    private sealed class RuntimeHost : ISessionProcessHost, IIndividualAppCloser
    {
        public TaskCompletionSource StartGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Dictionary<Guid, RuntimeState> States = [];
        public async Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken)
        {
            await StartGate.Task;
            var state = new RuntimeState(); States[app.Id] = state;
            return new([new RuntimeProcess(state)], true, "owned");
        }
        public Task<IPreparedAppClose> PrepareAsync(StartProcessAction app, CancellationToken cancellationToken = default) =>
            Task.FromResult<IPreparedAppClose>(new PreparedAppClose([new RuntimeProcess(States[app.Id])]));
    }
}
