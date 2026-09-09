using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class DraftCloseTests
{
    [Fact]
    public void DraftComparisonIncludesInvalidAndNestedEditsButIgnoresNavigationAndRevertedChanges()
    {
        var editor = new SessionEditorViewModel(Definition());
        Assert.False(editor.HasChanges);
        editor.SelectedApp = editor.Apps[1];
        Assert.False(editor.HasChanges);
        editor.MoveUpCommand.Execute(null);
        Assert.True(editor.HasChanges);
        editor.MoveDownCommand.Execute(null);
        Assert.False(editor.HasChanges);
        editor.Description = "Changed description";
        Assert.True(editor.HasChanges);
        editor.Description = "";
        var app = editor.Apps[0];
        app.ReadinessTimeoutSeconds = null;
        Assert.False(editor.CanSave);
        Assert.True(editor.HasChanges);
        app.ReadinessTimeoutSeconds = 30;
        Assert.False(editor.HasChanges);
        app.AllowForceQuit = true;
        Assert.True(editor.HasChanges);
        app.AllowForceQuit = false;
        app.Arguments = "--unsaved";
        Assert.True(editor.HasChanges);
        app.Arguments = "";
        app.ExecutablePath = " ";
        Assert.False(editor.CanSave);
        Assert.True(editor.HasChanges);
        app.ExecutablePath = @"C:\Notes.exe";
        editor.StartupFocusIndex = 2;
        Assert.True(editor.HasChanges);
        editor.StartupFocusIndex = 0;
        Assert.False(editor.HasChanges);
        editor.Apps.RemoveAt(1);
        Assert.True(editor.HasChanges);
        var fresh = new SessionEditorViewModel();
        Assert.False(fresh.HasChanges);
        fresh.Name = "Temporary";
        fresh.Name = "";
        Assert.False(fresh.HasChanges);
    }

    [AvaloniaTheory]
    [InlineData(false, 1440, 900)]
    [InlineData(true, 1440, 900)]
    [InlineData(false, 640, 480)]
    [InlineData(true, 640, 480)]
    public void ChangedDraftUsesSafeKeyboardDefaultsAndExplicitDiscard(bool dark, int width, int height)
    {
        var store = new Store();
        var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            model.EditSessionCommand.Execute(null);
            var editor = model.Editor!;
            editor.Name = new string('W', 120);
            Dispatcher.UIThread.RunJobs();
            var field = window.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "SessionName");
            field.Focus();
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);
            Assert.True(model.IsDraftCloseConfirmation);
            Assert.False(model.IsMainContentEnabled);
            Assert.True(window.FindControl<Button>("KeepEditingButton")!.IsFocused);
            window.Close(); // Repeated X must not replace the original editor focus target.
            SendKey(window, Key.Enter, PhysicalKey.Enter, "\r");
            Assert.False(model.IsDraftCloseConfirmation);
            Assert.Same(editor, model.Editor);
            Assert.True(field.IsFocused);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            SendKey(window, Key.Escape, PhysicalKey.Escape, "");
            Assert.False(model.IsDraftCloseConfirmation);
            Assert.True(field.IsFocused);
            window.Close();
            foreach (var scaling in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                window.SetRenderScaling(scaling);
                Dispatcher.UIThread.RunJobs();
                foreach (var name in new[] { "KeepEditingButton", "DiscardDraftButton", "SaveDraftButton" })
                {
                    var button = window.FindControl<Button>(name)!;
                    Assert.True(button.IsEffectivelyVisible);
                    var point = button.TranslatePoint(default, window)!.Value;
                    Assert.InRange(point.Y + button.Bounds.Height, 0, height + 1);
                    Assert.InRange(point.X + button.Bounds.Width, 0, width + 1);
                }
                Capture(window, $"draft-close-{dark}-{width}-{scaling * 100}");
            }
            model.DiscardDraftAndCloseCommand.Execute(null);
            Assert.False(window.IsVisible);
            Assert.Equal("Work", store.Saved[0].Name);
            Assert.Equal(0, store.SaveCount);
        }
        finally { CleanUp(model, window); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnchangedDraftClosesWithoutPromptOrWrite(bool isNew)
    {
        var store = new Store();
        var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model };
        window.Show();
        if (isNew) model.NewSessionCommand.Execute(null);
        else model.EditSessionCommand.Execute(null);
        Assert.False(model.Editor!.HasChanges);
        window.Close();
        Assert.False(window.IsVisible);
        Assert.False(model.IsDraftCloseConfirmation);
        Assert.Equal(0, store.SaveCount);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveFromClosePersistsBeforeWindowCloses(bool isNew)
    {
        var store = new Store { Gate = new() };
        var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            if (isNew) model.NewSessionCommand.Execute(null);
            else model.EditSessionCommand.Execute(null);
            model.Editor!.Name = "Updated";
            window.Close();
            var save = model.SaveDraftAndCloseCommand.ExecuteAsync(null);
            Assert.True(window.IsVisible);
            Assert.True(model.IsBusy);
            Assert.False(model.DiscardDraftAndCloseCommand.CanExecute(null));
            Assert.False(model.KeepEditingCommand.CanExecute(null));
            Assert.False(model.SaveSessionCommand.CanExecute(null));
            window.Close();
            store.Gate.SetResult();
            await save;
            Assert.Equal(1, store.SaveCount);
            Assert.False(window.IsVisible);
            using var reloaded = new MainViewModel(store);
            await reloaded.LoadCommand.ExecuteAsync(null);
            Assert.Contains(reloaded.Sessions, s => s.Name == "Updated");
            Assert.Equal(isNew ? 2 : 1, reloaded.Sessions.Count);
        }
        finally { CleanUp(model, window); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseDuringOrdinarySaveWaitsAndOnlyClosesOnSuccess(bool fail)
    {
        var store = new Store { Gate = new(), Fail = fail };
        var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            model.EditSessionCommand.Execute(null);
            model.Editor!.Description = "Unsaved description";
            var save = model.SaveSessionCommand.ExecuteAsync(null);
            window.Close();
            window.Close();
            Assert.True(window.IsVisible);
            Assert.False(model.IsDraftCloseConfirmation);
            store.Gate.SetResult();
            await save;
            Assert.Equal(fail, window.IsVisible);
            Assert.Equal(1, store.SaveCount);
            if (fail)
            {
                Assert.True(model.Editor!.HasChanges);
                Assert.Equal("", store.Saved[0].Description);
                Assert.True(model.HasError);
                store.Fail = false;
                await model.SaveSessionCommand.ExecuteAsync(null);
                Assert.True(window.IsVisible); // Failed close intent must not leak into a later ordinary save.
            }
        }
        finally { CleanUp(model, window); }
    }

    [AvaloniaFact]
    public async Task FailedModalSavePreservesDraftAndOffersRetryWhileInvalidDraftExplainsDisabledSave()
    {
        var store = new Store { Fail = true };
        var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model, Width = 640, Height = 480 };
        window.Show();
        try
        {
            model.NewSessionCommand.Execute(null);
            model.Editor!.Description = "Don't lose this draft";
            var editor = model.Editor;
            window.Close();
            Assert.True(model.DraftNeedsCorrection);
            Assert.False(model.SaveDraftAndCloseCommand.CanExecute(null));
            Assert.True(model.DiscardDraftAndCloseCommand.CanExecute(null));
            model.KeepEditingCommand.Execute(null);
            editor.Name = "New Session";
            window.Close();
            await model.SaveDraftAndCloseCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);
            Assert.True(model.IsDraftCloseConfirmation);
            Assert.Same(editor, model.Editor);
            Assert.True(model.HasError);
            Assert.True(window.FindControl<Button>("KeepEditingButton")!.IsFocused);
            Assert.True(model.SaveDraftAndCloseCommand.CanExecute(null));
            Capture(window, "draft-save-failed");
            store.Fail = false;
            await model.SaveDraftAndCloseCommand.ExecuteAsync(null);
            Assert.False(window.IsVisible);
            Assert.Contains(store.Saved, s => s.Description == "Don't lose this draft");
        }
        finally { CleanUp(model, window); }
    }

    [AvaloniaTheory]
    [InlineData("save")]
    [InlineData("discard")]
    [InlineData("unchanged")]
    public async Task DraftResolutionPrecedesActiveRunConfirmationWithoutStoppingApps(string choice)
    {
        var active = Definition();
        active = active with { MainAppId = active.Apps[0].Id };
        var store = new Store { Saved = [active, new(Guid.NewGuid(), "Other", "", [])] };
        var host = new Host();
        var runner = new SessionRunner(host);
        var model = new MainViewModel(store, runner: runner);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await model.StartSessionCommand.ExecuteAsync(null);
            model.SelectedSession = model.Sessions[1];
            model.EditSessionCommand.Execute(null);
            if (choice != "unchanged") model.Editor!.Name = "Updated other";
            host.Main.Exited = true;
            await runner.RefreshAsync();
            Assert.False(model.IsEndConfirmation);
            window.Close();
            if (choice == "save") await model.SaveDraftAndCloseCommand.ExecuteAsync(null);
            if (choice == "discard") model.DiscardDraftAndCloseCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);
            Assert.Null(model.Editor);
            Assert.False(model.IsDraftCloseConfirmation);
            Assert.False(model.IsEndConfirmation);
            Assert.True(model.IsCloseConfirmation);
            Assert.True(window.FindControl<Button>("KeepSessionsOpenButton")!.IsFocused);
            Assert.False(host.Support.Exited);
            Assert.Equal(choice == "save" ? "Updated other" : "Other", store.Saved[1].Name);
            await model.EndAndCloseCommand.ExecuteAsync(null);
            Assert.True(host.Support.Exited);
            Assert.False(window.IsVisible);
        }
        finally { if (runner.Snapshot?.IsActive == true) await runner.LeaveAppsOpenAsync(); CleanUp(model, window); }
    }

    private static SessionDefinition Definition() => new(Guid.NewGuid(), "Work", "",
        [new(Guid.NewGuid(), "Notes", @"C:\Notes.exe"), new(Guid.NewGuid(), "Browser", @"C:\Browser.exe")]);
    private static void SendKey(Window window, Key key, PhysicalKey physical, string text)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, text);
        window.KeyRelease(key, RawInputModifiers.None, physical, text);
        Dispatcher.UIThread.RunJobs();
    }
    private static void CleanUp(MainViewModel model, Window window)
    {
        model.KeepEditingCommand.Execute(null);
        model.CancelEditCommand.Execute(null);
        window.Close();
    }
    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        if (Environment.GetEnvironmentVariable("SESSIONS_SCREENSHOT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    private sealed class Store : ISessionStore
    {
        public IReadOnlyList<SessionDefinition> Saved { get; set; } = [Definition()];
        public TaskCompletionSource? Gate { get; init; }
        public bool Fail { get; set; }
        public int SaveCount { get; private set; }
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);
        public async Task SaveAsync(IReadOnlyList<SessionDefinition> definitions, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (Gate is not null) await Gate.Task;
            if (Fail) throw new IOException("Test disk is unavailable");
            Saved = definitions.ToArray();
        }
    }
    private sealed class Host : ISessionProcessHost
    {
        public Process Main { get; } = new();
        public Process Support { get; } = new();
        private int _calls;
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessAcquisition([_calls++ == 0 ? Main : Support], true, "Owned"));
    }
    private sealed class Process : ITrackedProcess
    {
        public bool Exited { get; set; }
        public bool HasExited => Exited;
        public Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false)
        { Exited = true; return Task.FromResult(true); }
        public void Dispose() { }
    }
}
