using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class SettingsInteractionTests
{
    [AvaloniaFact]
    public async Task KeyboardChoicesPreviewWithoutSavingAndSystemTracksThemeChanges()
    {
        var service = new PreferencesService();
        await service.SaveAsync(new(AppTheme.Dark));
        var settings = new SettingsWindow(service);
        var originalAppTheme = Application.Current!.RequestedThemeVariant;
        settings.Show();
        try
        {
            Layout(settings);
            var model = (SettingsViewModel)settings.DataContext!;
            var choice = settings.FindControl<ComboBox>("TextChoice")!;
            choice.Focus();
            settings.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            settings.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal(110, model.TextPercent);
            Assert.Equal(100, service.Current.TextPercent);
            model.Theme = AppTheme.System;
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            Layout(settings);
            Assert.Equal(ThemeVariant.Dark, settings.ActualThemeVariant);
            Assert.Equal(ThemeVariant.Light, settings.FindControl<ThemeVariantScope>("PreviewTheme")!.ActualThemeVariant);
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            Layout(settings);
            Assert.Equal(ThemeVariant.Dark, settings.FindControl<ThemeVariantScope>("PreviewTheme")!.ActualThemeVariant);
            // Tab reaches fixed actions even when selectors/preview are scrolled.
            var reset = settings.FindControl<Button>("ResetButton")!;
            for (var i = 0; i < 20 && !reset.IsFocused; i++)
            {
                settings.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
                settings.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
            }
            Assert.True(reset.IsFocused);
            settings.Close();
            Assert.Equal(new AppPreferences(AppTheme.Dark), service.Current);
            var reopened = new SettingsWindow(service);
            reopened.Show();
            Assert.Equal(service.Current, ((SettingsViewModel)reopened.DataContext!).Preview);
            reopened.Close();
        }
        finally { if (settings.IsVisible) settings.Close(); Application.Current.RequestedThemeVariant = originalAppTheme; }
    }

    [AvaloniaFact]
    public async Task LoadWarningOpensRecoveryAndResetClearsItWithoutChangingLibrary()
    {
        var store = new PreferencesTests.Store { FailLoad = true };
        var preferences = new PreferencesService(store);
        await preferences.LoadAsync();
        var model = new MainViewModel(new Library());
        var main = new MainWindow { DataContext = model, Preferences = preferences, Width = 640, Height = 480 };
        main.Show();
        try
        {
            Assert.True(main.FindControl<Border>("PreferencesWarning")!.IsVisible);
            Press(main, main.FindControl<Button>("SettingsButton")!);
            var settings = Assert.IsType<SettingsWindow>(Assert.Single(main.OwnedWindows));
            var vm = (SettingsViewModel)settings.DataContext!;
            Assert.False(settings.FindControl<Button>("ApplyButton")!.IsEffectivelyEnabled);
            Assert.True(settings.FindControl<Button>("ResetButton")!.IsEffectivelyEnabled);
            await vm.ResetCommand.ExecuteAsync(null);
            Assert.False(main.FindControl<Border>("PreferencesWarning")!.IsVisible);
            Assert.Equal(2, model.Sessions.Count);
            settings.Close();
        }
        finally { main.Close(); }
    }

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
    public async Task AppearancePreviewApplyResetAndCompactControls(bool dark, int width, int height, double dpi)
    {
        var preferences = new PreferencesService(new PreferencesTests.Store());
        var model = new MainViewModel(new Library());
        var main = new MainWindow { DataContext = model, Preferences = preferences, Width = width, Height = height };
        main.Show();
        var settings = new SettingsWindow(preferences) { Width = Math.Min(width, 700), Height = height };
        settings.Show(main);
        try
        {
            main.SetRenderScaling(dpi);
            settings.SetRenderScaling(dpi);
            var vm = (SettingsViewModel)settings.DataContext!;
            Assert.True(settings.FindControl<ComboBox>("ThemeChoice")!.IsFocused);
            var originalTheme = main.RequestedThemeVariant;
            settings.FindControl<ComboBox>("ThemeChoice")!.SelectedItem = dark ? AppTheme.Dark : AppTheme.Light;
            settings.FindControl<ComboBox>("InterfaceChoice")!.SelectedItem = 150;
            settings.FindControl<ComboBox>("TextChoice")!.SelectedItem = 125;
            Layout(settings);
            Assert.Equal(new AppPreferences(), preferences.Current);
            Assert.Equal(originalTheme, main.RequestedThemeVariant);
            Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, settings.FindControl<ThemeVariantScope>("PreviewTheme")!.ActualThemeVariant);
            await vm.ApplyCommand.ExecuteAsync(null);
            Layout(main); Layout(settings);
            Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, main.ActualThemeVariant);
            Assert.Equal(17.5, main.FontSize);
            var primaryLabel = main.FindControl<Button>("NewSessionButton")!.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "+  New Session");
            Assert.Equal(((ISolidColorBrush)main.FindResource(main.ActualThemeVariant, "OnBrandBrush")!).Color, ((ISolidColorBrush)primaryLabel.Foreground!).Color);
            var transform = (ScaleTransform)main.FindControl<LayoutTransformControl>("AppearanceRoot")!.LayoutTransform!;
            Assert.Equal(width == 640 ? 1 : 1.5, transform.ScaleX);
            AssertVisible(settings, settings.FindControl<Button>("ResetButton")!);
            AssertVisible(settings, settings.FindControl<Button>("ApplyButton")!);
            Capture(settings, $"settings-{dark}-{width}-{dpi}");
            var scroller = settings.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroller.ScrollToEnd();
            Layout(settings);
            Capture(settings, $"settings-preview-{dark}-{width}-{dpi}");

            model.EditSessionCommand.Execute(null);
            model.Editor!.Name = "A draft you keep";
            Layout(main);
            AssertVisible(main, main.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Save changes")));
            Capture(main, $"settings-editor-{dark}-{width}-{dpi}");
            await vm.ResetCommand.ExecuteAsync(null);
            Assert.Equal(new AppPreferences(), preferences.Current);
            Assert.Equal("A draft you keep", model.Editor.Name);
            Layout(main);
            Assert.Equal(ThemeVariant.Default, main.RequestedThemeVariant);
            Assert.Equal(14, main.FontSize);
        }
        finally { settings.Close(); model.CancelEditCommand.Execute(null); main.Close(); }
    }

    [AvaloniaFact]
    public async Task SettingsIsReachableFromEmptyLibraryAndPreservesDraftAndActiveRun()
    {
        var preferences = new PreferencesService();
        var emptyModel = new MainViewModel(new Library(empty: true));
        var empty = new MainWindow { DataContext = emptyModel, Preferences = preferences };
        empty.Show();
        Press(empty, empty.FindControl<Button>("EmptySettingsButton")!);
        var emptySettings = Assert.IsType<SettingsWindow>(Assert.Single(empty.OwnedWindows));
        Press(empty, empty.FindControl<Button>("EmptySettingsButton")!);
        Assert.Single(empty.OwnedWindows);
        emptySettings.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Empty(empty.OwnedWindows);
        Assert.True(empty.FindControl<Button>("EmptySettingsButton")!.IsFocused);
        empty.Close();

        var runner = new SessionRunner(new Host());
        var model = new MainViewModel(new Library(), runner: runner);
        var main = new MainWindow { DataContext = model, Preferences = preferences, Width = 640, Height = 480 };
        main.Show();
        try
        {
            await model.StartSessionCommand.ExecuteAsync(null);
            model.SelectedSession = model.Sessions[1];
            model.EditSessionCommand.Execute(null);
            model.Editor!.Name = "Unsaved workspace";
            var editor = model.Editor;
            Press(main, main.FindControl<Button>("SettingsButton")!);
            var settings = Assert.IsType<SettingsWindow>(Assert.Single(main.OwnedWindows));
            var settingsModel = (SettingsViewModel)settings.DataContext!;
            settingsModel.Theme = AppTheme.Dark;
            settingsModel.InterfacePercent = 150;
            settingsModel.TextPercent = 125;
            await settingsModel.ApplyCommand.ExecuteAsync(null);
            Layout(main);
            Assert.Same(editor, model.Editor);
            Assert.True(model.HasActiveRun);
            AssertVisible(main, main.FindControl<Button>("EndSessionButton")!);
            Assert.False(main.FindControl<Button>("EndSessionButton")!.IsEffectivelyEnabled); // Existing editor guard.
            settings.Close();
            Assert.Equal("Unsaved workspace", model.Editor.Name);
            main.Close();
            Assert.True(model.IsDraftCloseConfirmation);
            model.KeepEditingCommand.Execute(null);
            model.CancelEditCommand.Execute(null);
            Press(main, main.FindControl<Button>("SettingsButton")!);
            settings = Assert.IsType<SettingsWindow>(Assert.Single(main.OwnedWindows));
            Layout(main);
            Assert.True(main.FindControl<Button>("EndSessionButton")!.IsEffectivelyEnabled);
            var newLabel = main.FindControl<Button>("NewSessionButton")!.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "+  New Session");
            Assert.Equal(TextWrapping.Wrap, newLabel.TextWrapping);
            AssertVisible(main, newLabel);
            Capture(main, "settings-active-compact");
            settings.Close();
            main.Close();
            Assert.True(model.IsCloseConfirmation);
        }
        finally { await runner.LeaveAppsOpenAsync(); model.CancelEditCommand.Execute(null); main.Close(); }
    }

    [AvaloniaFact]
    public async Task PendingPreferenceWritePreventsWindowCloseAndFailureAllowsRetry()
    {
        var store = new PreferencesTests.Store { Gate = new TaskCompletionSource(), FailSave = true };
        var preferences = new PreferencesService(store);
        var main = new MainWindow { DataContext = new MainViewModel(new Library()), Preferences = preferences };
        main.Show();
        Press(main, main.FindControl<Button>("SettingsButton")!);
        var settings = Assert.IsType<SettingsWindow>(Assert.Single(main.OwnedWindows));
        var vm = (SettingsViewModel)settings.DataContext!;
        vm.Theme = AppTheme.Dark;
        var pending = vm.ApplyCommand.ExecuteAsync(null);
        settings.Close(); main.Close();
        Assert.True(settings.IsVisible);
        Assert.True(main.IsVisible);
        store.Gate.SetResult();
        await pending;
        Assert.Equal(ThemeVariant.Default, main.RequestedThemeVariant);
        Assert.True(vm.ApplyCommand.CanExecute(null));
        store.FailSave = false;
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(ThemeVariant.Dark, main.RequestedThemeVariant);
        settings.Close(); main.Close();
    }

    [AvaloniaFact]
    public async Task PickerSharesPreferencesAndSystemRestoresThemeInheritance()
    {
        var service = new PreferencesService();
        await service.SaveAsync(new(AppTheme.Dark, 150, 125));
        var picker = new AppPickerWindow { Preferences = service, Width = 820, Height = 700 };
        picker.Show();
        try
        {
            Layout(picker);
            Assert.Equal(ThemeVariant.Dark, picker.ActualThemeVariant);
            Assert.Equal(17.5, picker.FontSize);
            Assert.Equal(1.5, ((ScaleTransform)picker.FindControl<LayoutTransformControl>("AppearanceRoot")!.LayoutTransform!).ScaleX);
            await service.SaveAsync(new());
            Assert.Equal(ThemeVariant.Default, picker.RequestedThemeVariant);
            Assert.Equal(Application.Current!.ActualThemeVariant, picker.ActualThemeVariant);
        }
        finally { picker.Close(); }
    }

    private static void Press(Window window, Button button)
    {
        Layout(window);
        button.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        Layout(window);
    }
    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void AssertVisible(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        var point = control.TranslatePoint(default, window)!.Value;
        var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.True(point.X >= 0 && point.Y >= 0 && end.X <= window.Bounds.Width + 1 &&
                    end.Y <= window.Bounds.Height + 1, $"{control.Name ?? control.ToString()} outside {window.Bounds}: {point} {end}");
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
    private sealed class Library(bool empty = false) : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionDefinition>>(empty ? [] : [new(Guid.NewGuid(), "Work", "Your workspace", [new(Guid.NewGuid(), "Editor", @"C:\Test\editor.exe")]), new(Guid.NewGuid(), "Other Session", "", [])]);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Host : ISessionProcessHost
    {
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken) => Task.FromResult(new ProcessAcquisition([], false, "Isolated test"));
    }
}
