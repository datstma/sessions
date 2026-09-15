using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class SessionDuplicationInteractionTests
{
    [AvaloniaTheory]
    [InlineData(false, 1440, 900)]
    [InlineData(true, 640, 480)]
    public void DuplicateOpensAnUnsavedCopyAndSavesItRightAfterTheOriginal(bool dark, int width, int height)
    {
        var library = Library();
        var store = new MemoryStore { Saved = library };
        var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model, Width = width, Height = height, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Layout(window);
            model.SelectedSession = model.Sessions[1];
            Layout(window);
            var duplicate = window.FindControl<Button>("DuplicateSessionButton")!;
            var delete = window.FindControl<Button>("DeleteSessionButton")!;
            duplicate.BringIntoView();
            Layout(window);
            Assert.Equal(duplicate.TranslatePoint(default, window)!.Value.Y, delete.TranslatePoint(default, window)!.Value.Y, precision: 1);
            Capture(window, $"duplicate-detail-{dark}-{width}");

            Press(window, duplicate);
            var editor = model.Editor!;
            Assert.Equal("Duplicate Session", editor.Title);
            Assert.Equal("Create Session", editor.SaveLabel);
            Assert.Equal("Flight sim copy", editor.Name);
            Assert.True(editor.HasChanges);
            Assert.Equal(["MOZA Cockpit", "DCS World"], editor.Apps.Select(app => app.Name));
            Assert.Empty(editor.Apps.Select(app => app.Id).Intersect(library[1].Apps.Select(app => app.Id)));
            Assert.True(editor.EndWithApp);
            Assert.Same(editor.Apps[1], editor.MainApp);
            var nameField = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "SessionName");
            Assert.True(nameField.IsFocused);
            Assert.Equal("Flight sim copy", nameField.SelectedText);
            Assert.Equal(0, store.SaveCount);
            Capture(window, $"duplicate-editor-{dark}-{width}");

            editor.Name = "Helicopter sim";
            Press(window, window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Create Session")));
            Assert.Null(model.Editor);
            Assert.Equal(["Work", "Flight sim", "Helicopter sim", "Development"], store.Saved.Select(session => session.Name));
            Assert.Equal(["Work", "Flight sim", "Helicopter sim", "Development"], model.Sessions.Select(session => session.Name));
            Assert.Same(library[1], store.Saved[1]);
            Assert.Same(model.Sessions[2], model.SelectedSession);
            Assert.Equal(store.Saved[2].Apps[1].Id, store.Saved[2].MainAppId);
            Assert.True(window.FindControl<Button>("DuplicateSessionButton")!.IsFocused);

            model.SelectedSession = model.Sessions[2];
            model.DuplicateSessionCommand.Execute(null);
            Assert.Equal("Helicopter sim copy", model.Editor!.Name);
        }
        finally
        {
            model.Editor = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CancellingOrClosingDuringAnUntouchedDuplicateNeverWritesTheCopy()
    {
        var store = new MemoryStore { Saved = Library() };
        var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model, Width = 1120, Height = 800 };
        window.Show();
        Layout(window);
        var original = model.SelectedSession;

        model.DuplicateSessionCommand.Execute(null);
        model.CancelEditCommand.Execute(null);
        Assert.Null(model.Editor);
        Assert.Same(original, model.SelectedSession);

        model.DuplicateSessionCommand.Execute(null);
        window.Close();
        Layout(window);
        Assert.True(window.IsVisible);
        Assert.True(model.IsDraftCloseConfirmation);
        model.DiscardDraftAndCloseCommand.Execute(null);
        Layout(window);

        Assert.False(window.IsVisible);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(3, store.Saved.Count);
    }

    [AvaloniaFact]
    public async Task DuplicatingTheActiveSessionLeavesItsRunUntouched()
    {
        var library = Library();
        var runner = new SessionRunner(new Host());
        using var model = new MainViewModel(new MemoryStore { Saved = library }, runner: runner);
        try
        {
            await model.LoadCommand.ExecuteAsync(null);
            await model.StartSessionCommand.ExecuteAsync(null);
            Assert.True(model.SelectedIsActive);
            Assert.False(model.EditSessionCommand.CanExecute(null));
            Assert.True(model.DuplicateSessionCommand.CanExecute(null));

            model.DuplicateSessionCommand.Execute(null);
            await model.SaveSessionCommand.ExecuteAsync(null);

            Assert.Equal("Work copy", model.SelectedSession!.Name);
            Assert.Equal(library[0].Id, runner.Snapshot!.SessionId);
            Assert.True(model.HasActiveRun);
            Assert.False(model.SelectedIsActive);
            Assert.False(model.StartSessionCommand.CanExecute(null));
            Assert.Equal(["Work", "Work copy", "Flight sim", "Development"], model.Sessions.Select(session => session.Name));
        }
        finally { await runner.LeaveAppsOpenAsync(); }
    }

    [AvaloniaFact]
    public void CopyNamesAreNumberedAndStayWithinTheNameLimit()
    {
        var sessions = new[] { "Gaming", "Gaming copy", "GAMING COPY 2" }
            .Select(name => new SessionDefinition(Guid.NewGuid(), name, "", [])).ToArray();
        using var model = new MainViewModel(new MemoryStore { Saved = sessions });
        model.LoadCommand.Execute(null);
        Assert.Equal("Gaming copy 3", model.CopyName("Gaming"));
        Assert.Equal("Gaming copy copy", model.CopyName("Gaming copy"));

        var longName = model.CopyName(new string('W', 118) + " x");
        Assert.Equal(120, longName.Length);
        Assert.EndsWith("W copy", longName);
    }

    [AvaloniaFact]
    public async Task SavedCopyReloadsWithPluginAndAudioChoices()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SessionsTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonSessionStore(Path.Combine(directory, "sessions.json"));
            var tool = new StartProcessAction(Guid.NewGuid(), "Radio", @"C:\Apps\radio.exe", "--minimized");
            var game = new StartProcessAction(Guid.NewGuid(), "Game", "", Plugin: new("steam", "223750", CloseOnEnd: true));
            await store.SaveAsync([new SessionDefinition(Guid.NewGuid(), "Sim", "Weekend flying", [tool, game],
                FocusAfterStartup: StartupFocus.App, FocusAppId: tool.Id, OutputAudioDevice: new("headset-id", "Headset"))]);

            using var model = new MainViewModel(store);
            await model.LoadCommand.ExecuteAsync(null);
            model.DuplicateSessionCommand.Execute(null);
            await model.SaveSessionCommand.ExecuteAsync(null);

            using var reloaded = new MainViewModel(store);
            await reloaded.LoadCommand.ExecuteAsync(null);
            var copy = reloaded.Sessions[1].Definition;
            Assert.Equal("Sim copy", copy.Name);
            Assert.Equal("Weekend flying", copy.Description);
            Assert.Equal("--minimized", copy.Apps[0].Arguments);
            Assert.Equal(new PluginAppReference("steam", "223750", CloseOnEnd: true), copy.Apps[1].Plugin);
            Assert.Equal(copy.Apps[0].Id, copy.FocusAppId);
            Assert.Equal(new AudioDeviceChoice("headset-id", "Headset"), copy.OutputAudioDevice);
            Assert.NotEqual(reloaded.Sessions[0].Definition.Apps[0].Id, copy.Apps[0].Id);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static SessionDefinition[] Library()
    {
        var outlook = new StartProcessAction(Guid.NewGuid(), "Outlook", @"C:\Apps\Outlook.exe");
        var moza = new StartProcessAction(Guid.NewGuid(), "MOZA Cockpit", @"C:\Apps\MOZA.exe");
        var dcs = new StartProcessAction(Guid.NewGuid(), "DCS World", @"D:\DCS World\bin\DCS.exe");
        return [new(Guid.NewGuid(), "Work", "A little space to do your best work.", [outlook]),
            new(Guid.NewGuid(), "Flight sim", "Everything ready for your next flight.", [moza, dcs], dcs.Id),
            new(Guid.NewGuid(), "Development", "Pick up where you left off.", [])];
    }

    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    private static void Press(Window window, Button button)
    {
        Layout(window);
        button.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Layout(window);
    }

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

    private sealed class MemoryStore : ISessionStore
    {
        public IReadOnlyList<SessionDefinition> Saved { get; set; } = [];
        public int SaveCount { get; private set; }
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default)
        {
            Saved = sessions.ToArray();
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class Host : ISessionProcessHost
    {
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessAcquisition([new Process()], true, "Opened by this Session"));
    }

    private sealed class Process : ITrackedProcess
    {
        public bool HasExited => false;
        public Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false) => Task.FromResult(true);
        public void Dispose() { }
    }
}
