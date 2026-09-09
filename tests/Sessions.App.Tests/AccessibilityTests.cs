using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.ViewModels;
using Sessions.App.Services;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class AccessibilityTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongRuntimeAndErrorsLeaveEditorReachableAndCloseDialogRestoresFocus(bool dark)
    {
        var first = new SessionDefinition(Guid.NewGuid(), new string('W', 120), "",
            [new(Guid.NewGuid(), "Test app", @"C:\Apps\Test.exe")]);
        var second = new SessionDefinition(Guid.NewGuid(), "Other workspace", "", []);
        var runner = new SessionRunner(new UntrackedHost());
        var model = new MainViewModel(new Store([first, second]), runner: runner);
        var window = new MainWindow { DataContext = model, Width = 640, Height = 480,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await model.StartSessionCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            TabTo(window, () => window.FindControl<Button>("EndSessionButton")!);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<Button>("KeepSessionsOpenButton")!.IsFocused);
            Capture(window, $"accessibility-close-{dark}");
            PressKey(window, Key.Escape);
            Assert.True(window.FindControl<Button>("EndSessionButton")!.IsFocused);
            PressKey(window, Key.Space);
            Assert.True(window.FindControl<Button>("CancelEndButton")!.IsFocused);
            Capture(window, $"accessibility-end-{dark}");
            PressKey(window, Key.Escape);
            model.SelectedSession = model.Sessions[1];
            TabTo(window, () => Button(window, "Edit Session"));
            PressKey(window, Key.Space);
            model.ErrorMessage = string.Concat(Enumerable.Repeat("A long recoverable save error. ", 30));
            Dispatcher.UIThread.RunJobs();
            var name = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SessionName");
            Assert.True(name.IsFocused);
            Assert.True(name.GetVisualAncestors().OfType<ScrollViewer>().First().Bounds.Height >= 100);
            TabTo(window, () => Button(window, "Save changes"));
            var save = Button(window, "Save changes");
            Assert.True(save.TranslatePoint(default, window)!.Value.Y + save.Bounds.Height <= 480);
            Capture(window, $"accessibility-runtime-error-{dark}");
        }
        finally { runner.LeaveAppsOpen(); model.CancelEditCommand.Execute(null); window.Close(); }
    }
    [AvaloniaFact]
    public void FirstRunAndLastAppRemovalKeepKeyboardFocusUsable()
    {
        var model = new MainViewModel(new Store([]));
        var window = new MainWindow { DataContext = model, Width = 640, Height = 480 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<Button>("CreateFirstButton")!.IsFocused);
            PressKey(window, Key.Space);
            window.KeyTextInput("New workspace");
            model.Editor!.AddApp(@"C:\Apps\Editor.exe", "Editor");
            model.Editor.EndWithApp = true;
            TabTo(window, () => Button(window, "Remove"));
            PressKey(window, Key.Space);
            Assert.Empty(model.Editor.Apps);
            Assert.True(Button(window, "+  Add app").IsFocused);
            Assert.False(model.Editor.CanSave);
            var validation = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == model.Editor.ValidationMessage);
            Assert.Equal(AutomationLiveSetting.Polite, ControlAutomationPeer.CreatePeerForElement(validation)!.GetLiveSetting());
            TabTo(window, () => window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Ask to end when an app closes")));
            PressKey(window, Key.Space);
            Assert.True(model.Editor.CanSave);
            TabTo(window, () => Button(window, "Cancel"));
            PressKey(window, Key.Space);
            Assert.True(window.FindControl<Button>("CreateFirstButton")!.IsFocused);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PickerKeyboardScrollsToLastChoiceAndExposesUnavailableReason(bool dark)
    {
        using var model = new AppPickerViewModel(new Source(), []);
        var window = new AppPickerWindow { DataContext = model, Width = 520, Height = 460,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var choices = window.GetVisualDescendants().OfType<CheckBox>().ToArray();
            var unavailable = choices.Single(c => !c.IsEffectivelyEnabled);
            Assert.Equal("Unsupported launch method", ControlAutomationPeer.CreatePeerForElement(unavailable)!.GetHelpText());
            var last = choices.Last(c => c.IsEffectivelyEnabled);
            TabTo(window, () => last);
            PressKey(window, Key.Space);
            Assert.True(last.IsChecked);
            var scroll = last.GetVisualAncestors().OfType<ScrollViewer>().First();
            var point = last.TranslatePoint(default, scroll)!.Value;
            Assert.True(point.Y >= -1 && point.Y + last.Bounds.Height <= scroll.Bounds.Height + 1);
            Capture(window, $"accessibility-picker-{dark}");
            TabTo(window, () => window.FindControl<Button>("AddSelectedButton")!);
            PressKey(window, Key.Space);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongDeleteConfirmationKeepsButtonsVisibleAndFocusContained(bool dark)
    {
        var model = new MainViewModel(new Store([new SessionDefinition(Guid.NewGuid(), new string('W', 120), "", [])]));
        var window = new MainWindow { DataContext = model, Width = 640, Height = 480,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            TabTo(window, () => window.FindControl<Button>("DeleteSessionButton")!);
            PressKey(window, Key.Space);
            var cancel = window.FindControl<Button>("CancelDeleteButton")!;
            var confirm = window.FindControl<Button>("ConfirmDeleteButton")!;
            Assert.True(cancel.IsFocused);
            PressKey(window, Key.Tab, RawInputModifiers.Shift);
            Assert.True(confirm.IsFocused);
            var point = confirm.TranslatePoint(default, window)!.Value;
            Assert.True(point.Y >= 0 && point.Y + confirm.Bounds.Height <= 480);
            Capture(window, $"accessibility-delete-{dark}");
            PressKey(window, Key.Escape);
            Assert.False(model.IsConfirmingDelete);
            Assert.True(window.FindControl<Button>("DeleteSessionButton")!.IsFocused);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SaveAndCancelReturnFocusToInitiatingAction(bool save, bool newSession)
    {
        var window = Open();
        try
        {
            var model = (MainViewModel)window.DataContext!;
            var edit = Button(window, newSession ? "+  New Session" : "Edit Session");
            edit.Focus();
            PressKey(window, Key.Space);
            Assert.True(window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SessionName").IsFocused);
            if (newSession) window.KeyTextInput("New workspace");
            TabTo(window, () => Button(window, save ? newSession ? "Create Session" : "Save changes" : "Cancel"));
            PressKey(window, Key.Space);
            Assert.False(model.IsEditing);
            Assert.True(edit.IsFocused);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CustomListItemsExposeNamesAndOrderInsteadOfViewModelTypes()
    {
        var window = Open();
        try
        {
            var sessions = window.FindControl<ListBox>("SessionList")!;
            var item = (Control)sessions.ContainerFromIndex(0)!;
            Assert.Contains("Work", ControlAutomationPeer.CreatePeerForElement(item)!.GetName());
            ((MainViewModel)window.DataContext!).Sessions[0].IsActive = true;
            Assert.Contains("active", ControlAutomationPeer.CreatePeerForElement(item)!.GetName());
            Button(window, "Edit Session").Focus();
            PressKey(window, Key.Space);
            var apps = window.GetVisualDescendants().OfType<ListBox>().Single(l => l != sessions);
            var app = (Control)apps.ContainerFromIndex(0)!;
            var name = ControlAutomationPeer.CreatePeerForElement(app)!.GetName();
            Assert.Contains("Editor", name);
            Assert.Contains("1", name);
            var editor = ((MainViewModel)window.DataContext!).Editor!;
            editor.MoveDownCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var moved = (Control)apps.ContainerFromIndex(1)!;
            Assert.Equal("2. Editor", ControlAutomationPeer.CreatePeerForElement(moved)!.GetName());
            Assert.Contains("app 2 of 2", editor.SelectedAppSummary);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 640, 480, 1)]
    [InlineData(true, 640, 480, 1)]
    [InlineData(false, 960, 640, 1.25)]
    [InlineData(true, 960, 640, 1.25)]
    [InlineData(false, 640, 480, 1.5)]
    [InlineData(true, 640, 480, 1.5)]
    [InlineData(false, 640, 480, 2)]
    [InlineData(true, 640, 480, 2)]
    public void KeyboardEditorRemainsReachableInSmallLogicalViewports(bool dark, int width, int height, double scaling)
    {
        var window = Open(width, height, dark, many: true);
        try
        {
            window.SetRenderScaling(scaling);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(scaling, window.RenderScaling);
            Assert.Equal(width, window.Bounds.Width);
            Assert.Equal(height, window.Bounds.Height);
            Button(window, "Edit Session").Focus();
            PressKey(window, Key.Space);
            var model = (MainViewModel)window.DataContext!;
            Capture(window, $"accessibility-editor-top-{dark}-{width}-{scaling}");
            TabTo(window, () => Button(window, "Move down"));
            PressKey(window, Key.Space);
            Assert.Equal("Editor", model.Editor!.Apps[1].Name);
            TabTo(window, () => Button(window, "Move up"), reverse: true);
            PressKey(window, Key.Space);
            Assert.Equal("Editor", model.Editor.Apps[0].Name);
            var options = window.GetVisualDescendants().OfType<Expander>().Single(e => Equals(e.Header, "Editor options"));
            TabTo(window, () => options.GetVisualDescendants().OfType<ToggleButton>().First());
            PressKey(window, Key.Space);
            Assert.True(options.IsExpanded);
            TabTo(window, () => window.GetVisualDescendants().OfType<TextBox>()
                .Single(t => AutomationProperties.GetName(t) == "App arguments"));
            window.KeyTextInput("--workspace test");
            TabTo(window, () => window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => Equals(c.Content, "Ask to end when an app closes")));
            PressKey(window, Key.Space);
            Assert.True(model.Editor.EndWithApp);
            TabTo(window, () => window.GetVisualDescendants().OfType<ComboBox>()
                .Single(c => AutomationProperties.GetName(c) == "App that ends the Session"));
            PressKey(window, Key.Down);
            Assert.NotNull(model.Editor.MainApp);
            TabTo(window, () => Button(window, "Save changes"));
            var save = Button(window, "Save changes");
            var point = save.TranslatePoint(default, window)!.Value;
            Assert.True(point.Y >= 0 && point.Y + save.Bounds.Height <= height);
            Capture(window, $"accessibility-editor-{dark}-{width}-{scaling}");
            PressKey(window, Key.Space);
            Assert.False(model.IsEditing);
            Assert.Equal("--workspace test", model.SelectedSession!.Definition.Apps[0].Arguments);
            Capture(window, $"accessibility-details-{dark}-{width}-{scaling}");
        }
        finally { window.Close(); }
    }

    private static MainWindow Open(int width = 860, int height = 620, bool dark = false, bool many = false)
    {
        var apps = Enumerable.Range(0, many ? 30 : 2).Select(i => new StartProcessAction(Guid.NewGuid(),
            i == 0 ? "Editor" : $"Browser {i} with a long application name", $@"C:\Apps\App{i}.exe")).ToArray();
        var definitions = Enumerable.Range(0, many ? 50 : 1).Select(i => new SessionDefinition(Guid.NewGuid(),
            i == 0 ? "Work" : $"Session {i} with a long descriptive name", new string('W', many ? 500 : 10), apps)).ToArray();
        var window = new MainWindow { DataContext = new MainViewModel(new Store(definitions)), Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Button Button(Window window, string label) => window.GetVisualDescendants().OfType<Button>()
        .Single(b => b.IsEffectivelyVisible && Equals(b.Content, label));

    private static void TabTo(Window window, Func<Control> target, bool reverse = false)
    {
        for (var i = 0; i < 100; i++)
        {
            if (target()?.IsFocused == true) return;
            PressKey(window, Key.Tab, reverse ? RawInputModifiers.Shift : RawInputModifiers.None);
        }
        Assert.Fail($"Keyboard could not reach {target()}");
    }

    private static void PressKey(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
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

    private sealed class Store(IReadOnlyList<SessionDefinition> definitions) : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(definitions);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Source : IAppSource
    {
        public Task<IReadOnlyList<DiscoveredApp>> GetAppsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DiscoveredApp>>(Enumerable.Range(0, 80).Select(i => new DiscoveredApp(
                $"Application {i:D2} with a longer descriptive name", $@"C:\Apps\App{i}.exe",
                UnavailableReason: i == 0 ? "Unsupported launch method" : null)).ToArray());
    }

    private sealed class UntrackedHost : ISessionProcessHost
    {
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessAcquisition([], false, "Simulated untracked launch"));
    }
}
