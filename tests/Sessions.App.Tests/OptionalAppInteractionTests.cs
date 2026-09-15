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

public sealed class OptionalAppInteractionTests
{
    [AvaloniaTheory]
    [InlineData(false, 1440, 900)]
    [InlineData(true, 640, 480)]
    public void KeyboardMarksAnAppOptionalAndTheDetailViewLabelsIt(bool dark, int width, int height)
    {
        var tracker = new StartProcessAction(Guid.NewGuid(), "TrackIR", @"C:\Apps\TrackIR.exe");
        var sim = new StartProcessAction(Guid.NewGuid(), "DCS World", @"D:\DCS\bin\DCS.exe", Optional: true);
        var game = new StartProcessAction(Guid.NewGuid(), "3DMark", "", Plugin: new("steam", "223850"));
        var store = new Store(new SessionDefinition(Guid.NewGuid(), "Flight", "", [tracker, sim, game], MainAppId: sim.Id));
        using var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model, Width = width, Height = height, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Layout(window);
            Assert.Equal(["", "Ends with this app · Optional", ""], model.SelectedSession!.Apps.Select(app => app.Role));

            model.EditSessionCommand.Execute(null);
            Layout(window);
            Expand(window, "TrackIR options");
            var optional = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Name == "OptionalApp");
            Assert.False(optional.IsChecked);
            optional.BringIntoView();
            optional.Focus();
            Press(window, Key.Space);
            Assert.True(model.Editor!.Apps[0].Optional);
            Assert.True(model.Editor.HasChanges);
            Layout(window);
            Capture(window, $"optional-editor-{dark}-{width}");

            // Steam apps offer the same choice.
            model.Editor.SelectedApp = model.Editor.Apps[2];
            Layout(window);
            Assert.True(window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Name == "OptionalApp").IsEffectivelyVisible);
            model.Editor.Apps[2].Optional = true;

            model.SaveSessionCommand.Execute(null);
            Layout(window);
            Assert.Equal([true, true, true], store.Saved[0].Apps.Select(app => app.Optional));
            Assert.Equal(["Optional", "Ends with this app · Optional", "Optional"], model.SelectedSession!.Apps.Select(app => app.Role));
            Capture(window, $"optional-detail-{dark}-{width}");

            model.EditSessionCommand.Execute(null);
            Assert.True(model.Editor!.Apps[2].Optional);
            Assert.False(model.Editor.HasChanges);
            model.CancelEditCommand.Execute(null);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SkippedOptionalAppsAreExplainedAndASkippedFocusTargetLeavesFocusAlone()
    {
        var tracker = new StartProcessAction(Guid.NewGuid(), "TrackIR", @"C:\Apps\TrackIR.exe", Optional: true);
        var radio = new StartProcessAction(Guid.NewGuid(), "SRS", @"C:\Apps\SRS.exe");
        var definition = new SessionDefinition(Guid.NewGuid(), "Flight", "", [tracker, radio],
            FocusAfterStartup: StartupFocus.App, FocusAppId: tracker.Id);
        var runner = new SessionRunner(new Host());
        var focus = new Focus();
        using var model = new MainViewModel(new Store(definition), runner: runner, startupFocus: focus);
        var window = new MainWindow { DataContext = model, Width = 1120, Height = 800 };
        window.Show();
        try
        {
            await model.LoadCommand.ExecuteAsync(null);
            await model.StartSessionCommand.ExecuteAsync(null);
            Layout(window);

            Assert.True(model.HasActiveRun);
            Assert.Equal("Your Session is active. TrackIR didn't finish starting; its status explains why.", model.Runtime!.Message);
            Assert.Equal("Skipped: The executable was not found.", model.SelectedSession!.Apps[0].RunMessage);
            Assert.Empty(focus.Paths);
            Assert.Equal("Startup finished. TrackIR didn't start, so focus was left unchanged.", model.StartupFocusMessage);
            Capture(window, "optional-skipped-run");
        }
        finally
        {
            await runner.LeaveAppsOpenAsync();
            window.Close();
        }
    }

    private static void Expand(Window window, string header)
    {
        var expander = window.GetVisualDescendants().OfType<Expander>().Single(e => Equals(e.Header, header));
        expander.GetVisualDescendants().OfType<ToggleButton>().First().Focus();
        Press(window, Key.Space);
        Assert.True(expander.IsExpanded);
    }

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, key == Key.Space ? " " : null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, key == Key.Space ? " " : null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    private static void Capture(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SESSIONS_SCREENSHOT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    private sealed class Store(params SessionDefinition[] definitions) : ISessionStore
    {
        public IReadOnlyList<SessionDefinition> Saved { get; private set; } = definitions;
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) { Saved = sessions; return Task.CompletedTask; }
    }

    private sealed class Host : ISessionProcessHost
    {
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken) =>
            app.Name == "TrackIR" ? throw new IOException("The executable was not found.")
                : Task.FromResult(new ProcessAcquisition([new Process()], true, "Opened by this Session"));
    }

    private sealed class Process : ITrackedProcess
    {
        public bool HasExited { get; private set; }
        public Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false) { HasExited = true; return Task.FromResult(true); }
        public void Dispose() { }
    }

    private sealed class Focus : IStartupFocusService
    {
        public List<string?> Paths { get; } = [];
        public Task<AppFocusResult> FocusAsync(string? executablePath, CancellationToken cancellationToken)
        {
            Paths.Add(executablePath);
            return Task.FromResult(AppFocusResult.Focused);
        }
    }
}
