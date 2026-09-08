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

public sealed class AppPresenceTests
{
    private const string EditorPath = @"C:\Apps\Editor.exe";
    private const string MusicPath = @"C:\Apps\Music.exe";
    private const string NotesPath = @"C:\Apps\Notes.exe";

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunningIndicatorSupportsKeyboardFocusAndBothThemes(bool dark)
    {
        var source = new FakePresence();
        var model = new MainViewModel(new NoWriteStore(), source);
        var window = new MainWindow
        {
            DataContext = model, Width = 860, Height = 620,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            await model.RefreshPresenceAsync();
            Dispatcher.UIThread.RunJobs();
            var rows = model.SelectedSession!.Apps;
            Assert.Equal(AppPresence.Window, rows[0].Presence);
            Assert.True(rows[1].IsRunning);
            Assert.False(rows[1].FocusCommand.CanExecute(null));
            Assert.False(rows[2].IsRunning);
            Assert.False(rows[2].FocusCommand.CanExecute(null));
            var button = window.GetVisualDescendants().OfType<Button>()
                .Single(control => ReferenceEquals(control.Command, rows[0].FocusCommand));
            Assert.True(button.IsVisible);
            Assert.True(button.IsEnabled);
            Assert.True(button.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            if (rows[0].FocusCommand.ExecutionTask is { } task) await task;
            Assert.Equal(new[] { EditorPath }, source.FocusedPaths);
            Assert.Null(rows[0].FocusMessage);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (Environment.GetEnvironmentVariable("SESSIONS_SCREENSHOT_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, $"presence-{(dark ? "dark" : "light")}.png"),
                    Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ObservationsUpdateWithoutSavingOrChangingDefinitionsAndRecoverFromFailure()
    {
        var source = new FakePresence();
        var model = new MainViewModel(new NoWriteStore(), source);
        await model.LoadCommand.ExecuteAsync(null);
        var definition = model.SelectedSession!.Definition;
        await model.RefreshPresenceAsync();
        var row = model.SelectedSession.Apps[0];
        Assert.True(row.IsRunning);
        source.Error = new Win32Exception("Desktop unavailable");
        await model.RefreshPresenceAsync();
        Assert.Equal(AppPresence.Unknown, row.Presence);
        Assert.False(row.FocusCommand.CanExecute(null));
        source.Error = null;
        source.Snapshot[EditorPath] = AppPresence.NotRunning;
        await model.RefreshPresenceAsync();
        Assert.Equal(AppPresence.NotRunning, row.Presence);
        source.Snapshot[EditorPath] = AppPresence.Window;
        await model.RefreshPresenceAsync();
        Assert.True(row.FocusCommand.CanExecute(null));
        Assert.Same(definition, model.SelectedSession.Definition);
        Assert.Empty(source.FocusedPaths); // Observation never activates anything.
    }

    [AvaloniaFact]
    public async Task LateScanDoesNotApplyAfterSwitchingAwayAndBackAndScansDoNotOverlap()
    {
        var source = new FakePresence();
        var model = new MainViewModel(new NoWriteStore(), source);
        await model.LoadCommand.ExecuteAsync(null);
        var original = model.SelectedSession!;
        source.Gate = new();
        var scan = model.RefreshPresenceAsync();
        await model.RefreshPresenceAsync();
        Assert.Equal(1, source.Scans);
        model.SelectedSession = model.Sessions[1];
        model.SelectedSession = original;
        source.Gate.SetResult(source.Snapshot);
        await scan;
        Assert.All(original.Apps, app => Assert.Equal(AppPresence.Checking, app.Presence));
        source.Gate = null;
        await model.RefreshPresenceAsync();
        Assert.True(original.Apps[0].IsRunning);
        model.EditSessionCommand.Execute(null);
        await model.RefreshPresenceAsync();
        Assert.Equal(2, source.Scans);
        model.CancelEditCommand.Execute(null);
        await model.RefreshPresenceAsync();
        Assert.Equal(3, source.Scans);
    }

    [AvaloniaFact]
    public async Task ClosingWindowCancelsDiscoveryAndIgnoresLateResults()
    {
        var source = new FakePresence { Gate = new() };
        var model = new MainViewModel(new NoWriteStore(), source);
        var window = new MainWindow { DataContext = model };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(source.Scans > 0);
        window.Close();
        Assert.True(source.LastToken.IsCancellationRequested);
        source.Gate.SetResult(source.Snapshot);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.All(model.SelectedSession!.Apps, app => Assert.Equal(AppPresence.Checking, app.Presence));
    }

    [AvaloniaFact]
    public async Task AppClosingBeforeClickRemovesRunningStateAndExplainsMissingWindow()
    {
        var source = new FakePresence { FocusResult = AppFocusResult.NoWindow };
        var model = new MainViewModel(new NoWriteStore(), source);
        await model.LoadCommand.ExecuteAsync(null);
        await model.RefreshPresenceAsync();
        source.Snapshot[EditorPath] = AppPresence.NotRunning;
        var row = model.SelectedSession!.Apps[0];
        await row.FocusCommand.ExecuteAsync(null);
        Assert.False(row.IsRunning);
        Assert.False(row.FocusCommand.CanExecute(null));
        Assert.Contains("no longer has a window", row.FocusMessage);
        Assert.Equal(new[] { EditorPath }, source.FocusedPaths);
    }

    [AvaloniaFact]
    public async Task DeniedFocusStaysRetryableAndSuccessfulRetryClearsMessage()
    {
        var source = new FakePresence { FocusResult = AppFocusResult.Denied };
        var model = new MainViewModel(new NoWriteStore(), source);
        await model.LoadCommand.ExecuteAsync(null);
        await model.RefreshPresenceAsync();
        var row = model.SelectedSession!.Apps[0];
        await row.FocusCommand.ExecuteAsync(null);
        Assert.Contains("taskbar", row.FocusMessage);
        Assert.True(row.FocusCommand.CanExecute(null));
        source.FocusResult = AppFocusResult.Focused;
        await row.FocusCommand.ExecuteAsync(null);
        Assert.Null(row.FocusMessage);
        Assert.Equal(2, source.FocusedPaths.Count);
    }

    private sealed class FakePresence : IAppPresenceService
    {
        public Dictionary<string, AppPresence> Snapshot { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            [EditorPath] = AppPresence.Window, [MusicPath] = AppPresence.Background, [NotesPath] = AppPresence.NotRunning
        };
        public List<string> FocusedPaths { get; } = [];
        public int Scans { get; private set; }
        public Exception? Error { get; set; }
        public AppFocusResult FocusResult { get; set; } = AppFocusResult.Focused;
        public TaskCompletionSource<IReadOnlyDictionary<string, AppPresence>>? Gate { get; set; }
        public CancellationToken LastToken { get; private set; }
        public Task<IReadOnlyDictionary<string, AppPresence>> GetPresenceAsync(IReadOnlyList<string> paths,
            CancellationToken cancellationToken = default)
        {
            Scans++;
            LastToken = cancellationToken;
            return Gate?.Task ?? (Error is null ? Task.FromResult<IReadOnlyDictionary<string, AppPresence>>(Snapshot)
                : Task.FromException<IReadOnlyDictionary<string, AppPresence>>(Error));
        }
        public Task<AppFocusResult> FocusAsync(string path, CancellationToken cancellationToken = default)
        {
            FocusedPaths.Add(path);
            return Task.FromResult(FocusResult);
        }
    }

    private sealed class NoWriteStore : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionDefinition>>([
                new(Guid.NewGuid(), "Work", "Your everyday apps, ready together.", [
                    new(Guid.NewGuid(), "Editor", EditorPath),
                    new(Guid.NewGuid(), "Music with a longer application name", MusicPath),
                    new(Guid.NewGuid(), "Notes", NotesPath)]),
                new(Guid.NewGuid(), "Development", "", [])]);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Presence and focus must never write the library.");
    }
}
