using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

[assembly: AvaloniaTestApplication(typeof(Sessions.App.Tests.TestAppBuilder))]

namespace Sessions.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::Sessions.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class SessionInteractionTests
{
    [AvaloniaFact]
    public async Task FirstRunCanCreateSaveAndReloadAUserBoundSession()
    {
        var store = new MemoryStore();
        var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            Assert.True(model.IsEmpty);
            Capture(window, "first-run");
            Press(window, window.FindControl<Button>("CreateFirstButton")!);
            Assert.NotNull(model.Editor);
            var name = window.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "SessionName");
            name.Text = "Work";
            Assert.Equal("Work", model.Editor.Name);
            Assert.True(model.SaveSessionCommand.CanExecute(null));
            Capture(window, "creator");
            var create = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Create Session"));
            Press(window, create);
            Assert.Null(model.Editor);
            Assert.Equal("Work", model.SelectedSession!.Name);
            Assert.Null(Assert.Single(store.Saved).MainAppId);
            Assert.True(model.ShowDetails);
            var reloaded = new MainViewModel(store);
            await reloaded.LoadCommand.ExecuteAsync(null);
            Assert.Equal("Work", reloaded.SelectedSession!.Name);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CancellingEditsPreservesSavedAppsAndSessionSelection()
    {
        var store = new MemoryStore { Saved = Examples() };
        var model = new MainViewModel(store);
        await model.LoadCommand.ExecuteAsync(null);
        var original = model.SelectedSession!;
        model.EditSessionCommand.Execute(null);
        model.Editor!.Name = "Unsaved name";
        model.Editor.SelectedApp = model.Editor.Apps[0];
        model.Editor.RemoveAppCommand.Execute(null);
        model.CancelEditCommand.Execute(null);
        Assert.Same(original, model.SelectedSession);
        Assert.Equal(2, original.Definition.Apps.Count);
        Assert.Equal("Work", model.SelectedSession!.Name);
        Assert.Equal(0, store.SaveCount);
    }

    [AvaloniaFact]
    public void ReorderingPreservesTheMainAppAndRemovingItRequiresAChoice()
    {
        var editor = new SessionEditorViewModel(Examples()[1]);
        var main = editor.MainApp;
        editor.SelectedApp = main;
        editor.MoveUpCommand.Execute(null);
        Assert.Same(main, editor.Apps[0]);
        Assert.Equal(main!.Id, editor.BuildDefinition().MainAppId);
        editor.RemoveAppCommand.Execute(null);
        Assert.True(editor.EndWithApp);
        Assert.Null(editor.MainApp);
        Assert.False(editor.CanSave);
        editor.EndWithApp = false;
        Assert.True(editor.CanSave);
        Assert.Null(editor.BuildDefinition().MainAppId);
    }

    [AvaloniaFact]
    public async Task SaveFailureKeepsDraftAndOriginalThenAllowsRetry()
    {
        var store = new MemoryStore { Saved = Examples(), FailSave = true };
        var model = new MainViewModel(store);
        await model.LoadCommand.ExecuteAsync(null);
        model.EditSessionCommand.Execute(null);
        model.Editor!.Name = "Updated work";
        await model.SaveSessionCommand.ExecuteAsync(null);
        Assert.True(model.HasError);
        Assert.Equal("Updated work", model.Editor!.Name);
        Assert.Equal("Work", model.SelectedSession!.Name);
        store.FailSave = false;
        await model.SaveSessionCommand.ExecuteAsync(null);
        Assert.False(model.HasError);
        Assert.Null(model.Editor);
        Assert.Equal("Updated work", model.SelectedSession!.Name);
        Assert.Equal(3, model.Sessions.Count);
    }

    [AvaloniaFact]
    public async Task FailedLoadBlocksCreationUntilTheLibraryCanBeRead()
    {
        var store = new MemoryStore { FailLoad = true };
        var model = new MainViewModel(store);
        await model.LoadCommand.ExecuteAsync(null);
        Assert.True(model.HasError);
        Assert.False(model.IsEmpty);
        Assert.False(model.NewSessionCommand.CanExecute(null));
        store.FailLoad = false;
        await model.LoadCommand.ExecuteAsync(null);
        Assert.True(model.IsEmpty);
        Assert.True(model.NewSessionCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData(false, 1120, 800)]
    [InlineData(true, 1120, 800)]
    [InlineData(false, 860, 620)]
    [InlineData(true, 860, 620)]
    public void SidebarSelectionAndEditorRenderAtSupportedSizes(bool dark, int width, int height)
    {
        var model = new MainViewModel(new MemoryStore { Saved = Examples() });
        var window = new MainWindow { DataContext = model, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            var list = window.FindControl<ListBox>("SessionList")!;
            list.SelectedIndex = 1;
            Assert.Equal("Flight sim", model.SelectedSession!.Name);
            Assert.True(model.ShowDetails);
            Assert.Null(model.Editor);
            Capture(window, $"sidebar-{(dark ? "dark" : "light")}-{width}");
            var edit = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Edit Session"));
            Press(window, edit);
            Assert.True(model.IsEditing);
            Capture(window, $"editor-{(dark ? "dark" : "light")}-{width}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteConfirmationDefaultsToCancelAndSupportsEscape(bool dark)
    {
        var examples = Examples();
        examples[0] = examples[0] with { Name = new string('W', 120) };
        var store = new MemoryStore { Saved = examples };
        var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model, Width = 860, Height = 620,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            var request = window.FindControl<Button>("DeleteSessionButton")!;
            var cancel = window.FindControl<Button>("CancelDeleteButton")!;
            var confirm = window.FindControl<Button>("ConfirmDeleteButton")!;
            Press(window, request);
            Assert.True(model.IsConfirmingDelete);
            Assert.Contains(examples[0].Name, model.DeleteTitle);
            Assert.True(cancel.IsFocused);
            Assert.False(window.FindControl<ListBox>("SessionList")!.IsEffectivelyEnabled);
            Assert.False(model.NewSessionCommand.CanExecute(null));
            Assert.False(model.EditSessionCommand.CanExecute(null));
            Assert.Equal(0, store.SaveCount);
            Capture(window, $"delete-{(dark ? "dark" : "light")}-860");
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.True(confirm.IsFocused);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.True(cancel.IsFocused);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.IsConfirmingDelete);
            Assert.True(request.IsFocused);
            Assert.Equal(3, model.Sessions.Count);
            Press(window, request);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.IsConfirmingDelete);
            Assert.Equal(0, store.SaveCount);
            store.FailSave = true;
            Press(window, request);
            Press(window, confirm);
            Assert.True(model.HasDeleteError);
            Capture(window, $"delete-error-{(dark ? "dark" : "light")}-860");
            var confirmPosition = confirm.TranslatePoint(default, window)!.Value;
            Assert.True(confirmPosition.Y >= 0 && confirmPosition.Y + confirm.Bounds.Height <= window.Bounds.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public async Task ConfirmedDeletionPersistsAndSelectsTheNextOrPreviousSession(int deletedIndex, int selectedIndex)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SessionsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var appPath = Path.Combine(directory, "example.exe");
            await File.WriteAllTextAsync(appPath, "Do not remove this app file.");
            var store = new JsonSessionStore(Path.Combine(directory, "sessions.json"));
            var examples = Examples();
            examples[deletedIndex] = examples[deletedIndex] with
            {
                Apps = [new StartProcessAction(Guid.NewGuid(), "Example", appPath)], MainAppId = null
            };
            await store.SaveAsync(examples);
            var model = new MainViewModel(store);
            await model.LoadCommand.ExecuteAsync(null);
            model.SelectedSession = model.Sessions[deletedIndex];
            model.RequestDeleteSessionCommand.Execute(null);
            await model.ConfirmDeleteSessionCommand.ExecuteAsync(null);
            Assert.False(model.IsConfirmingDelete);
            Assert.Equal(examples[selectedIndex].Id, model.SelectedSession!.Definition.Id);
            var reloaded = new MainViewModel(store);
            await reloaded.LoadCommand.ExecuteAsync(null);
            Assert.Equal(examples.Where((_, index) => index != deletedIndex).Select(item => item.Id),
                reloaded.Sessions.Select(item => item.Definition.Id));
            Assert.Equal("Do not remove this app file.", await File.ReadAllTextAsync(appPath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaFact]
    public void DeletingTheLastSessionReturnsToFirstRunAndFocusesCreate()
    {
        var store = new MemoryStore { Saved = [Examples()[0]] };
        var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            Press(window, window.FindControl<Button>("DeleteSessionButton")!);
            Press(window, window.FindControl<Button>("ConfirmDeleteButton")!);
            Assert.Empty(store.Saved);
            Assert.Empty(model.Sessions);
            Assert.Null(model.SelectedSession);
            Assert.True(model.IsEmpty);
            Assert.True(window.FindControl<Button>("CreateFirstButton")!.IsFocused);
            Assert.False(model.RequestDeleteSessionCommand.CanExecute(null));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FailedDeletionKeepsTheOriginalAndAllowsRetry()
    {
        var store = new MemoryStore { Saved = Examples(), FailSave = true };
        var model = new MainViewModel(store);
        await model.LoadCommand.ExecuteAsync(null);
        var original = model.SelectedSession;
        model.RequestDeleteSessionCommand.Execute(null);
        await model.ConfirmDeleteSessionCommand.ExecuteAsync(null);
        Assert.True(model.IsConfirmingDelete);
        Assert.True(model.HasDeleteError);
        Assert.Same(original, model.SelectedSession);
        Assert.Equal(3, model.Sessions.Count);
        Assert.Equal(3, store.Saved.Count);
        Assert.True(model.CancelDeleteSessionCommand.CanExecute(null));
        store.FailSave = false;
        await model.ConfirmDeleteSessionCommand.ExecuteAsync(null);
        Assert.False(model.IsConfirmingDelete);
        Assert.False(model.HasDeleteError);
        Assert.Equal(2, store.Saved.Count);
    }

    [AvaloniaFact]
    public async Task DeletionUsesConfirmedIdentityAndBlocksRepeatedRequestsDuringSave()
    {
        var examples = Examples();
        examples[1] = examples[1] with { Name = examples[0].Name };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new MemoryStore { Saved = examples, SaveGate = gate.Task };
        var model = new MainViewModel(store);
        await model.LoadCommand.ExecuteAsync(null);
        model.SelectedSession = model.Sessions[1];
        model.RequestDeleteSessionCommand.Execute(null);
        model.SelectedSession = model.Sessions[0];
        var deletion = model.ConfirmDeleteSessionCommand.ExecuteAsync(null);
        Assert.True(model.IsBusy);
        Assert.Equal(3, model.Sessions.Count);
        Assert.False(model.ConfirmDeleteSessionCommand.CanExecute(null));
        Assert.False(model.CancelDeleteSessionCommand.CanExecute(null));
        await model.ConfirmDeleteSessionCommand.ExecuteAsync(null);
        model.CancelDeleteSessionCommand.Execute(null);
        Assert.True(model.IsConfirmingDelete);
        gate.SetResult();
        await deletion;
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(new[] { examples[0].Id, examples[2].Id }, store.Saved.Select(item => item.Id));
    }

    private static void Press(Window window, Button button)
    {
        Dispatcher.UIThread.RunJobs();
        button.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
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

    private static SessionDefinition[] Examples()
    {
        var outlook = new StartProcessAction(Guid.NewGuid(), "Outlook", @"C:\Apps\Outlook.exe");
        var teams = new StartProcessAction(Guid.NewGuid(), "Teams", @"C:\Apps\Teams.exe");
        var support = new StartProcessAction(Guid.NewGuid(), "MOZA Cockpit", @"C:\Apps\MOZA.exe");
        var dcs = new StartProcessAction(Guid.NewGuid(), "DCS World", @"D:\DCS World\bin\DCS.exe");
        return [new(Guid.NewGuid(), "Work", "A little space to do your best work.", [outlook, teams]),
            new(Guid.NewGuid(), "Flight sim", "Everything ready for your next flight.", [support, dcs], dcs.Id),
            new(Guid.NewGuid(), "Development", "Pick up where you left off.", [])];
    }

    private sealed class MemoryStore : ISessionStore
    {
        public IReadOnlyList<SessionDefinition> Saved { get; set; } = [];
        public bool FailSave { get; set; }
        public bool FailLoad { get; set; }
        public int SaveCount { get; private set; }
        public Task? SaveGate { get; init; }
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) =>
            FailLoad ? Task.FromException<IReadOnlyList<SessionDefinition>>(new InvalidDataException("Invalid library")) : Task.FromResult(Saved);
        public async Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default)
        {
            if (SaveGate is not null) await SaveGate;
            if (FailSave) throw new IOException("Unavailable");
            Saved = sessions.ToArray();
            SaveCount++;
        }
    }
}
