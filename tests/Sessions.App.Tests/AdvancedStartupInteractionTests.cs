using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class AdvancedStartupInteractionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionsFocusRestoresMinimizedWindowUnlessCancelled(bool cancel)
    {
        var window = new Window { Width = 640, Height = 480 };
        window.Show();
        using var cancellation = new CancellationTokenSource();
        try
        {
            window.WindowState = WindowState.Minimized;
            if (cancel) cancellation.Cancel();
            var service = new WindowStartupFocusService(window, new UnusedPresence());
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FocusAsync(null, cancellation.Token));
                Assert.Equal(WindowState.Minimized, window.WindowState);
            }
            else
            {
                await service.FocusAsync(null, cancellation.Token);
                Assert.Equal(WindowState.Normal, window.WindowState);
            }
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupControlsAreKeyboardOperableAndPersistWithTheDraft(bool dark)
    {
        var original = Definition();
        var store = new Store(original);
        var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model, Width = 640, Height = 480,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            model.EditSessionCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Expand(window, $"{original.Name} advanced startup options");
            var mode = Named<ComboBox>(window, "Session launch mode");
            mode.Focus();
            Press(window, Key.Down);
            Assert.Equal(1, model.Editor!.LaunchModeIndex);
            Assert.False(model.Editor.IsOrdered);
            Capture(window, $"startup-together-{dark}");
            mode.Focus();
            Press(window, Key.Up);
            Named<NumericUpDown>(window, "Pause between apps in seconds").Value = 2;
            var focus = Named<ComboBox>(window, "Focus after startup");
            focus.Focus();
            Press(window, Key.Down);
            Press(window, Key.Down);
            Assert.False(model.Editor.CanSave);
            var target = Named<ComboBox>(window, "App to focus after startup");
            target.Focus();
            Press(window, Key.Down);
            Assert.NotNull(model.Editor.FocusApp);
            Capture(window, $"startup-focus-{dark}");
            Expand(window, $"{original.Apps[0].Name} options");
            var readiness = Named<ComboBox>(window, "App startup condition");
            readiness.Focus();
            Press(window, Key.Down);
            Press(window, Key.Down);
            Named<NumericUpDown>(window, "Maximum readiness wait in seconds").Value = 45;
            var check = window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Override the pause after this app"));
            check.Focus();
            Press(window, Key.Space);
            Named<NumericUpDown>(window, "Pause after this app in seconds").Value = 0;
            check.BringIntoView();
            Capture(window, $"startup-app-options-{dark}");
            var save = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Save changes"));
            save.Focus();
            Press(window, Key.Space);
            Assert.False(model.IsEditing);
            var saved = Assert.Single(store.Saved);
            Assert.Equal(2, saved.PauseBetweenAppsSeconds);
            Assert.Equal(StartupFocus.App, saved.FocusAfterStartup);
            Assert.Equal(saved.Apps[0].Id, saved.FocusAppId);
            Assert.Equal(AppReadiness.WindowAppeared, saved.Apps[0].Readiness);
            Assert.Equal(45, saved.Apps[0].ReadinessTimeoutSeconds);
            Assert.Equal(0, saved.Apps[0].PauseAfterSeconds);
            Assert.Equal(AppReadiness.LaunchCompleted, original.Apps[0].Readiness);
            var reopened = new SessionEditorViewModel(saved).BuildDefinition();
            Assert.Equal(saved with { Apps = reopened.Apps }, reopened);
            Assert.Equal(saved.Apps, reopened.Apps);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RemovedFocusTargetAndInvalidTimingBlockSaveWithoutLosingChoices()
    {
        var definition = Definition();
        var editor = new SessionEditorViewModel(definition with { FocusAfterStartup = StartupFocus.App, FocusAppId = definition.Apps[0].Id });
        editor.RemoveAppCommand.Execute(null);
        Assert.False(editor.CanSave);
        Assert.Contains("focus", editor.ValidationMessage!);
        editor.StartupFocusIndex = 0;
        Assert.True(editor.CanSave);
        editor.PauseBetweenAppsSeconds = null;
        Assert.False(editor.CanSave);
        editor.PauseBetweenAppsSeconds = 1.5m;
        Assert.False(editor.CanSave);
        editor.PauseBetweenAppsSeconds = 3;
        editor.LaunchModeIndex = 1;
        Assert.True(editor.CanSave);
        Assert.Equal(3, editor.BuildDefinition().PauseBetweenAppsSeconds); // Switching modes retains the ordered-mode preference.
    }

    [AvaloniaTheory]
    [InlineData(StartupFocus.Sessions)]
    [InlineData(StartupFocus.App)]
    [InlineData(StartupFocus.Unchanged)]
    public async Task CompletionFocusUsesCapturedDefinitionOnceAfterReadiness(StartupFocus choice)
    {
        var definition = Definition();
        definition = definition with { FocusAfterStartup = choice, FocusAppId = definition.Apps[0].Id,
            Apps = [definition.Apps[0] with { Readiness = AppReadiness.WindowAppeared }] };
        var process = new Process { HasWindow = false };
        var runner = new SessionRunner(new Host((_, _) => Task.FromResult(new ProcessAcquisition([process], true, "owned"))));
        var focus = new Focus();
        using var model = new MainViewModel(new Store(definition, Definition()), runner: runner, startupFocus: focus);
        await model.LoadCommand.ExecuteAsync(null);
        var start = model.StartSessionCommand.ExecuteAsync(null);
        await Until(() => runner.Snapshot?.Apps[0].Message.StartsWith("Waiting for") == true);
        model.SelectedSession = model.Sessions[1];
        Assert.Empty(focus.Paths);
        process.HasWindow = true;
        await start;
        Assert.Equal(choice == StartupFocus.Unchanged ? 0 : 1, focus.Paths.Count);
        if (choice != StartupFocus.Unchanged)
            Assert.Equal(choice == StartupFocus.App ? definition.Apps[0].ExecutablePath : null, focus.Paths[0]);
        await runner.RefreshAsync();
        Assert.Equal(choice == StartupFocus.Unchanged ? 0 : 1, focus.Paths.Count);
        await runner.EndAsync();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledStartupNeverRequestsFocus(bool cancel)
    {
        var definition = Definition() with { FocusAfterStartup = StartupFocus.Sessions };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new SessionRunner(new Host(async (_, _) =>
        {
            await gate.Task;
            if (!cancel) throw new IOException("Launch failed");
            return new([new Process()], true, "owned");
        }));
        var focus = new Focus();
        using var model = new MainViewModel(new Store(definition), runner: runner, startupFocus: focus);
        await model.LoadCommand.ExecuteAsync(null);
        var start = model.StartSessionCommand.ExecuteAsync(null);
        var end = cancel ? runner.EndAsync() : Task.CompletedTask;
        gate.SetResult();
        await Task.WhenAll(start, end);
        Assert.Empty(focus.Paths);
    }

    [AvaloniaFact]
    public async Task EditingDuringStartupSkipsCompletionFocus()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new SessionRunner(new Host(async (_, _) => { await gate.Task; return new([], false, "untracked"); }));
        var focus = new Focus();
        using var model = new MainViewModel(new Store(Definition() with { FocusAfterStartup = StartupFocus.Sessions }), runner: runner, startupFocus: focus);
        await model.LoadCommand.ExecuteAsync(null);
        var start = model.StartSessionCommand.ExecuteAsync(null);
        model.NewSessionCommand.Execute(null);
        gate.SetResult();
        await start;
        Assert.Empty(focus.Paths);
        Assert.Contains("editing", model.StartupFocusMessage!);
        model.CancelEditCommand.Execute(null);
        Assert.Empty(focus.Paths);
        runner.LeaveAppsOpen();
    }

    [AvaloniaFact]
    public async Task DeniedFocusReportsFeedbackWithoutFailingTheRunAndEndCancelsPendingFocus()
    {
        var runner = new SessionRunner(new Host((_, _) => Task.FromResult(new ProcessAcquisition([], false, "untracked"))));
        var focus = new Focus { Result = AppFocusResult.Denied };
        using var model = new MainViewModel(new Store(Definition() with { FocusAfterStartup = StartupFocus.Sessions }), runner: runner, startupFocus: focus);
        await model.LoadCommand.ExecuteAsync(null);
        await model.StartSessionCommand.ExecuteAsync(null);
        Assert.Equal(SessionRunState.Running, model.Runtime!.State);
        Assert.Contains("Windows", model.StartupFocusMessage!);
        await runner.EndAsync();
        Dispatcher.UIThread.RunJobs();
        focus.Block = true;
        var start = model.StartSessionCommand.ExecuteAsync(null);
        await focus.Entered.Task;
        await runner.EndAsync();
        await start.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(focus.Cancelled);
        Assert.False(model.HasActiveRun);
        Assert.Null(model.StartupFocusMessage);
    }

    private static SessionDefinition Definition() => new(Guid.NewGuid(), "Work", "", [new(Guid.NewGuid(), "Editor", @"C:\Apps\Editor.exe")]);
    private static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>()
        .Single(c => AutomationProperties.GetName(c) == name);
    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }
    private static void Expand(Window window, string header)
    {
        var expander = window.GetVisualDescendants().OfType<Expander>().Single(e => Equals(e.Header, header));
        expander.GetVisualDescendants().OfType<ToggleButton>().First().Focus();
        Press(window, Key.Space);
        Assert.True(expander.IsExpanded);
    }
    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SESSIONS_SCREENSHOT_DIR") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(path);
            frame.Save(Path.Combine(path, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
    private sealed class Store(params SessionDefinition[] definitions) : ISessionStore
    {
        public IReadOnlyList<SessionDefinition> Saved { get; private set; } = definitions;
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) { Saved = sessions; return Task.CompletedTask; }
    }
    private sealed class Process : ITrackedProcess
    {
        public bool HasExited { get; private set; }
        public bool HasWindow { get; set; } = true;
        public Task<bool> RequestCloseAsync(TimeSpan timeout) { HasExited = true; return Task.FromResult(true); }
        public void Dispose() { }
    }
    private sealed class Host(Func<StartProcessAction, CancellationToken, Task<ProcessAcquisition>> open) : ISessionProcessHost
    {
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken) => open(app, cancellationToken);
    }
    private sealed class Focus : IStartupFocusService
    {
        public List<string?> Paths { get; } = [];
        public AppFocusResult Result { get; init; } = AppFocusResult.Focused;
        public bool Block { get; set; }
        public bool Cancelled { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AppFocusResult> FocusAsync(string? executablePath, CancellationToken cancellationToken)
        {
            Paths.Add(executablePath);
            if (!Block) return Result;
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return Result;
        }
    }
    private sealed class UnusedPresence : IAppPresenceService
    {
        public Task<IReadOnlyDictionary<string, AppPresence>> GetPresenceAsync(IReadOnlyList<string> executablePaths, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Sessions focus must not scan user apps.");
        public Task<AppFocusResult> FocusAsync(string executablePath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Sessions focus must not focus another app.");
    }
}
