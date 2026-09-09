using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
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

public sealed class EditorValidationTests
{
    [AvaloniaTheory]
    [InlineData(false, 1440, 900, 1)]
    [InlineData(true, 1440, 900, 1)]
    [InlineData(false, 640, 480, 1)]
    [InlineData(true, 640, 480, 1)]
    [InlineData(false, 640, 480, 1.25)]
    [InlineData(true, 640, 480, 1.25)]
    [InlineData(false, 640, 480, 1.5)]
    [InlineData(true, 640, 480, 1.5)]
    [InlineData(false, 640, 480, 2)]
    [InlineData(true, 640, 480, 2)]
    public void ReviewFindsUnselectedCollapsedAppAndCorrectionsEnableSaving(bool dark, int width, int height, double scale)
    {
        var store = new Store(Definition());
        using var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.SetRenderScaling(scale);
            model.EditSessionCommand.Execute(null);
            var editor = model.Editor!;
            var invalid = editor.Apps[1];
            invalid.Name = "  ";
            invalid.ExecutablePath = " \t ";
            editor.SelectedApp = editor.Apps[0];
            var view = window.FindControl<SessionEditorView>("EditorView")!;
            Assert.False(view.FindControl<Expander>("AppOptions")!.IsExpanded);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.SaveSessionCommand.CanExecute(null));
            Assert.Contains("App 2 (Unnamed app)", editor.ValidationMessage);
            var list = view.FindControl<ListBox>("AppOrderList")!;
            list.BringIntoView();
            list.ScrollIntoView(invalid);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Contains("Enter an app display name", ControlAutomationPeer.CreatePeerForElement((Control)list.ContainerFromIndex(1)!)!.GetName());
            Capture(window, $"validation-list-{dark}-{width}-{scale}");

            Review(window, "App display name");
            Assert.Same(invalid, editor.SelectedApp);
            Assert.True(view.FindControl<Expander>("AppOptions")!.IsExpanded);
            var name = Named<TextBox>(window, "App display name");
            Assert.Equal(invalid.NameError, AutomationProperties.GetHelpText(name));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("fieldError") && t.Text == invalid.NameError && t.IsEffectivelyVisible);
            Capture(window, $"validation-name-{dark}-{width}-{scale}");
            name.Text = "Portable editor";
            Assert.False(model.SaveSessionCommand.CanExecute(null));
            Review(window, "App executable path");
            Capture(window, $"validation-path-{dark}-{width}-{scale}");
            Named<TextBox>(window, "App executable path").Text = @"Z:\Disconnected drive\Editor.exe";
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.SaveSessionCommand.CanExecute(null));
            Assert.Null(editor.ValidationMessage);
            Assert.False(window.FindControl<Button>("ReviewFieldsButton")!.IsEffectivelyVisible);
            model.SaveSessionCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.IsEditing);
            Assert.Equal(@"Z:\Disconnected drive\Editor.exe", Assert.Single(store.Saved).Apps[1].ExecutablePath);
            Assert.Equal("Second app", store.Original.Apps[1].Name);
        }
        finally { model.CancelEditCommand.Execute(null); window.Close(); }
    }

    [AvaloniaFact]
    public void WhitespaceNameAndRemovedMainAndFocusTargetsHaveReachableCorrections()
    {
        using var model = new MainViewModel(new Store(Definition()));
        var window = new MainWindow { DataContext = model, Width = 640, Height = 480 };
        window.Show();
        try
        {
            model.NewSessionCommand.Execute(null);
            var editor = model.Editor!;
            Assert.False(editor.CanSave); // Untouched new drafts also explain the disabled button.
            editor.Name = " \t ";
            Review(window, "Session name");
            Named<TextBox>(window, "Session name").Text = "Work";
            Assert.True(editor.CanSave); // Empty Sessions remain valid.
            editor.AddApp(@"Z:\Offline\Editor.exe");
            editor.EndWithApp = true;
            editor.StartupFocusIndex = 2;
            editor.FocusApp = editor.SelectedApp;
            editor.RemoveAppCommand.Execute(null);
            Review(window, "App to focus after startup");
            Assert.Contains("focus", editor.ValidationMessage);
            editor.StartupFocusIndex = 0;
            Review(window, "App that ends the Session");
            Assert.Contains("turn off", editor.MainAppError);
            editor.EndWithApp = false;
            Assert.True(model.SaveSessionCommand.CanExecute(null));
        }
        finally { model.CancelEditCommand.Execute(null); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(EditorField.SessionPause, "Pause between apps in seconds")]
    [InlineData(EditorField.ReadinessTimeout, "Maximum readiness wait in seconds")]
    [InlineData(EditorField.AppPause, "Pause after this app in seconds")]
    public void InvalidTimingRemainsCorrectableAfterItsOptionIsSwitchedOff(EditorField field, string controlName)
    {
        using var model = new MainViewModel(new Store(Definition()));
        var window = new MainWindow { DataContext = model, Width = 640, Height = 480 };
        window.Show();
        try
        {
            model.EditSessionCommand.Execute(null);
            var editor = model.Editor!;
            var app = editor.Apps[1];
            if (field == EditorField.SessionPause) { editor.PauseBetweenAppsSeconds = null; editor.LaunchModeIndex = 1; }
            if (field == EditorField.ReadinessTimeout) { app.ReadinessTimeoutSeconds = null; app.ReadinessIndex = 0; }
            if (field == EditorField.AppPause) { app.PauseAfterSeconds = null; app.OverridePause = false; }
            Assert.False(editor.CanSave);
            Assert.Equal(field, editor.FirstValidationIssue!.Field);
            Review(window, controlName);
            var input = Named<NumericUpDown>(window, controlName);
            Assert.NotNull(AutomationProperties.GetHelpText(input));
            input.Value = 1.5m;
            Assert.False(editor.CanSave);
            Assert.Contains("whole number", editor.ValidationMessage);
            input.Value = 3;
            Assert.True(model.SaveSessionCommand.CanExecute(null));
            var saved = editor.BuildDefinition();
            if (field == EditorField.SessionPause) Assert.Equal(SessionLaunchMode.Together, saved.LaunchMode);
            Assert.Null(saved.Apps[1].PauseAfterSeconds); // Review does not enable an override.
            Assert.Equal(AppReadiness.LaunchCompleted, saved.Apps[1].Readiness);
        }
        finally { model.CancelEditCommand.Execute(null); window.Close(); }
    }

    [AvaloniaFact]
    public void ClearingANumericFieldWithTheKeyboardBlocksSaveAndRetainsTheDraft()
    {
        using var model = new MainViewModel(new Store(Definition()));
        var window = new MainWindow { DataContext = model, Width = 640, Height = 480 };
        window.Show();
        try
        {
            model.EditSessionCommand.Execute(null);
            var editor = model.Editor!;
            var view = window.FindControl<SessionEditorView>("EditorView")!;
            view.FindControl<Expander>("AdvancedStartup")!.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var input = Named<NumericUpDown>(window, "Pause between apps in seconds");
            input.BringIntoView();
            input.Focus();
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
            window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(editor.PauseBetweenAppsSeconds);
            Assert.False(model.SaveSessionCommand.CanExecute(null));
            Assert.True(editor.HasChanges);
            Review(window, "Pause between apps in seconds");
            window.KeyTextInput("3");
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.SaveSessionCommand.CanExecute(null));
            Assert.Equal(3, editor.BuildDefinition().PauseBetweenAppsSeconds);
        }
        finally { model.CancelEditCommand.Execute(null); window.Close(); }
    }

    [AvaloniaFact]
    public void ReorderAndRemovalUpdateTheReportedAppWithoutChangingOtherDraftValues()
    {
        var editor = new SessionEditorViewModel(Definition());
        var invalid = editor.Apps[1];
        invalid.ExecutablePath = " ";
        editor.SelectedApp = invalid;
        editor.MoveUpCommand.Execute(null);
        Assert.Contains("App 1 (Second app)", editor.ValidationMessage);
        Assert.Same(invalid, editor.FirstValidationIssue!.App);
        editor.RemoveAppCommand.Execute(null);
        Assert.True(editor.CanSave);
        Assert.Null(editor.ValidationMessage);
        Assert.Equal(@"C:\Apps\First.exe", editor.BuildDefinition().Apps[0].ExecutablePath);
    }

    private static void Review(MainWindow window, string targetName)
    {
        Dispatcher.UIThread.RunJobs();
        var button = window.FindControl<Button>("ReviewFieldsButton")!;
        Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled);
        AssertInWindow(button, window);
        button.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        var target = Named<Control>(window, targetName);
        Assert.True(target.IsEffectivelyVisible);
        Assert.True(target.IsFocused || target.IsKeyboardFocusWithin, $"Focus did not reach {targetName}.");
        AssertInWindow(target, window);
        var help = AutomationProperties.GetHelpText(target);
        var explanation = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("fieldError") &&
            t.IsEffectivelyVisible && ReferenceEquals(t.DataContext, target.DataContext) && t.Text == help);
        var explanationBottom = explanation.TranslatePoint(default, window)!.Value.Y + explanation.Bounds.Height;
        Assert.True(explanationBottom <= button.TranslatePoint(default, window)!.Value.Y,
            $"The explanation for {targetName} is hidden below the editor footer.");
    }
    private static void AssertInWindow(Control control, Window window)
    {
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.True(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= window.Bounds.Width + 1 &&
            point.Y + control.Bounds.Height <= window.Bounds.Height + 1, $"{control} is outside the window: {point}, {control.Bounds}.");
    }
    private static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>()
        .Single(c => AutomationProperties.GetName(c) == name);
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
    private static SessionDefinition Definition() => new(Guid.NewGuid(), new string('W', 120), "",
        [new(Guid.NewGuid(), "First app", @"C:\Apps\First.exe"), new(Guid.NewGuid(), "Second app", @"C:\Apps\Second.exe")]);
    private sealed class Store(SessionDefinition definition) : ISessionStore
    {
        public SessionDefinition Original { get; } = definition;
        public IReadOnlyList<SessionDefinition> Saved { get; private set; } = [definition];
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) { Saved = sessions; return Task.CompletedTask; }
    }
}
