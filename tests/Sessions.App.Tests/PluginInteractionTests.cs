using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class PluginInteractionTests
{
    [AvaloniaTheory]
    [InlineData(false, 1440, 900, false)]
    [InlineData(true, 1440, 900, true)]
    [InlineData(false, 640, 480, true)]
    [InlineData(true, 640, 480, false)]
    public async Task LaunchFeedbackIsNeutralAndClearsWhenRunningWhileErrorsRemainDistinct(bool dark, int width, int height, bool expired)
    {
        var plugin = new PluginPreferencesTests.TestPlugin { Presence = PluginAppPresence.NotRunning };
        var service = new PluginService(new([plugin]));
        await service.LoadAsync();
        var launcher = new FeedbackLauncher(expired);
        var model = new MainViewModel(new Library(), appLauncher: launcher, plugins: service);
        var window = new MainWindow { DataContext = model, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await model.LoadCommand.ExecuteAsync(null);
            var row = model.SelectedSession!.Apps[0];
            await row.LaunchCommand.ExecuteAsync(null);
            Layout(window);
            Assert.True(row.HasLaunchMessage);
            Assert.False(row.HasFocusMessage);
            Assert.Equal(!expired, row.IsStarting);
            var feedback = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "LaunchFeedback" && block.DataContext == row);
            feedback.BringIntoView(); Layout(window);
            AssertVisible(window, feedback);
            Assert.Equal(((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, "TextMutedBrush")!).Color,
                ((ISolidColorBrush)feedback.Foreground!).Color);
            Capture(window, $"plugin-launch-feedback-{dark}-{width}");

            plugin.Presence = PluginAppPresence.Running;
            await model.RefreshPresenceAsync(); Layout(window);
            Assert.True(row.IsRunning);
            Assert.False(row.IsStarting);
            Assert.Null(row.LaunchMessage);
            Assert.False(feedback.IsEffectivelyVisible);
            Assert.False(row.HasFocusMessage);
            Capture(window, $"plugin-launch-running-{dark}-{width}");

            plugin.Presence = PluginAppPresence.NotRunning;
            await row.RefreshPluginPresenceAsync();
            launcher.Fail = true;
            await row.LaunchCommand.ExecuteAsync(null); Layout(window);
            Assert.False(row.HasLaunchMessage);
            Assert.Contains("Couldn't start", row.FocusMessage);
            var error = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "AppErrorFeedback" && block.DataContext == row);
            Assert.Equal(((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, "SessionError")!).Color,
                ((ISolidColorBrush)error.Foreground!).Color);
            plugin.Presence = PluginAppPresence.Running;
            await row.RefreshPluginPresenceAsync();
            Assert.True(row.HasFocusMessage); // An actual failed request isn't a stale waiting message.
        }
        finally { window.Close(); }
    }

    private sealed class FeedbackLauncher(bool expired) : IIndividualAppLauncher
    {
        public bool Fail;
        public Task<AppLaunchResult> LaunchAsync(StartProcessAction app, CancellationToken cancellationToken = default) =>
            Fail ? throw new IOException("fixture launch failure") : Task.FromResult(new AppLaunchResult(AppPresence.Unknown,
                DateTimeOffset.UtcNow.AddSeconds(expired ? -1 : 10), "Launch requested through Steam · waiting for Steam's running status"));
    }

    [AvaloniaFact]
    public async Task ProviderRunningStateUpdatesGreenStatusAndBlocksDuplicateLaunchWithoutGrantingFocus()
    {
        var plugin = new PluginPreferencesTests.TestPlugin { Presence = PluginAppPresence.Running };
        var service = new PluginService(new([plugin]));
        await service.LoadAsync();
        var launcher = new IndividualAppLauncher(new Presence(), new Starter(), plugins: service);
        var app = Game();
        var row = new SessionAppRow(1, app.Name, "", "Plugin", appLauncher: launcher, definition: app, plugins: service);
        await row.RefreshPluginPresenceAsync();
        Assert.True(row.IsRunning);
        Assert.Equal("Running", row.StatusLabel);
        Assert.False(row.HasLaunchAction);
        Assert.False(row.FocusCommand.CanExecute(null));
        await launcher.LaunchAsync(app);
        Assert.Equal(0, plugin.Launches);
        plugin.Presence = PluginAppPresence.NotRunning;
        await row.RefreshPluginPresenceAsync();
        Assert.False(row.IsRunning);
        Assert.True(row.LaunchCommand.CanExecute(null));
        plugin.Presence = PluginAppPresence.Running;
        using var model = new MainViewModel(new Library(), appLauncher: launcher, plugins: service);
        await model.LoadCommand.ExecuteAsync(null);
        await model.RefreshPresenceAsync();
        Assert.True(model.SelectedSession!.Apps[0].IsRunning);
        await service.SaveAsync(new Dictionary<string, Sessions.Plugins.PluginConfiguration> { ["test"] = new(false) });
        await row.RefreshPluginPresenceAsync();
        Assert.Equal(AppPresence.Unknown, row.Presence);
        Assert.False(row.IsRunning);
    }

    [AvaloniaFact]
    public void PluginCloseOptionParticipatesInDraftDetectionAndSurvivesReopening()
    {
        var definition = new SessionDefinition(Guid.NewGuid(), "Steam", "", [Game()]);
        var editor = new SessionEditorViewModel(definition);
        Assert.False(editor.Apps[0].CloseOnEnd);
        editor.Apps[0].CloseOnEnd = true;
        Assert.True(editor.HasChanges);
        var reopened = new SessionEditorViewModel(editor.BuildDefinition());
        Assert.True(reopened.Apps[0].CloseOnEnd);
        Assert.False(reopened.HasChanges);
        editor.Apps[0].CloseOnEnd = false;
        Assert.False(editor.HasChanges);
    }

    [AvaloniaFact]
    public async Task PickerKeepsSelectionsAcrossSourcesAndDisablesExistingAndDisabledPluginTargets()
    {
        var plugin = new PluginPreferencesTests.TestPlugin();
        var service = new PluginService(new([plugin]));
        await service.LoadAsync();
        using var model = new AppPickerViewModel(new Source(), [], new Source(), service, [new("test", "10")]);
        await model.RefreshCommand.ExecuteAsync(null);
        model.ShowPlugins = true;
        Assert.False(model.ShowRunningApps);
        Assert.False(model.ShowStartMenu);
        Assert.False(model.VisibleApps.Single(app => app.App.Plugin!.TargetId == "10").CanSelect);
        var choice = model.VisibleApps.Single(app => app.CanSelect);
        choice.IsSelected = true;
        model.ShowStartMenu = true;
        Assert.Single(model.GetSelection());
        model.ShowPlugins = true;
        Assert.True(choice.IsSelected);
        await service.SaveAsync(new Dictionary<string, Sessions.Plugins.PluginConfiguration> { ["test"] = new(false) });
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Empty(model.GetSelection());
        Assert.All(model.VisibleApps, app => Assert.False(app.CanSelect));
        Assert.Contains("disabled", Assert.Single(model.VisibleApps).Status);
        await service.SaveAsync(new Dictionary<string, Sessions.Plugins.PluginConfiguration>());
        plugin.FailDiscovery = true;
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.All(model.VisibleApps, app => Assert.False(app.CanSelect));
        Assert.Contains("discovery failed", Assert.Single(model.VisibleApps).Status);
    }

    [AvaloniaFact]
    public async Task EditorPreservesPluginIdentityAndOpaqueSettingsWhileFilteringProcessOnlyChoices()
    {
        using var json = System.Text.Json.JsonDocument.Parse("{\"future\":{\"value\":123}}");
        var app = new StartProcessAction(Guid.NewGuid(), "Unavailable plugin app", "", Plugin: new("missing", "opaque", 9, json.RootElement.Clone()));
        var ordinary = new StartProcessAction(Guid.NewGuid(), "Utility", @"C:\Fixture\utility.exe");
        var definition = new SessionDefinition(Guid.NewGuid(), "Mixed Session", "", [app, ordinary]);
        var editor = new SessionEditorViewModel(definition);
        Assert.False(editor.HasChanges);
        Assert.Equal(ordinary.Id, Assert.Single(editor.ProcessApps).Id);
        editor.Name = "Renamed";
        var saved = editor.BuildDefinition();
        Assert.Equal(9, saved.Apps[0].Plugin!.Version);
        Assert.True(System.Text.Json.JsonElement.DeepEquals(app.Plugin!.Settings!.Value, saved.Apps[0].Plugin!.Settings!.Value));
        editor.MainApp = editor.Apps[0];
        editor.EndWithApp = true;
        Assert.False(editor.CanSave);
        editor.EndWithApp = false;
        editor.StartupFocusIndex = 2;
        editor.FocusApp = editor.Apps[0];
        Assert.False(editor.CanSave);
        editor.StartupFocusIndex = 0;
        editor.FocusApp = null;
        editor.AddPickedApps([new("Duplicate", null, Plugin: app.Plugin), new("New game", null, Plugin: new("test", "20"))]);
        Assert.Equal(3, editor.Apps.Count);
        Assert.True(editor.CanSave);
        Assert.Equal("20", editor.BuildDefinition().Apps.Last().Plugin!.TargetId);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task ManualPluginLaunchNeverUsesProcessPresenceOrClaimsRunningAndDuplicateClicksAreGuarded()
    {
        var plugin = new PluginPreferencesTests.TestPlugin();
        var service = new PluginService(new([plugin]));
        await service.LoadAsync();
        var presence = new Presence();
        var starter = new Starter();
        var launcher = new IndividualAppLauncher(presence, starter, plugins: service);
        var app = Game();
        var row = new SessionAppRow(1, app.Name, "", "Plugin app", presence, launcher, app);
        row.ApplyPresence(AppPresence.Window);
        Assert.False(row.IsRunning);
        Assert.False(row.FocusCommand.CanExecute(null));
        Assert.Equal("Launch via plugin", row.StatusLabel);
        await row.LaunchCommand.ExecuteAsync(null);
        await row.LaunchCommand.ExecuteAsync(null);
        await launcher.LaunchAsync(app, TestContext.Current.CancellationToken);
        Assert.Equal(1, plugin.Launches);
        Assert.Equal(0, starter.Calls);
        Assert.Equal(0, presence.Calls);
        Assert.False(row.IsRunning);
        Assert.Contains("unverified", row.LaunchMessage);
        Assert.Null(row.FocusMessage);
        Assert.False(row.LaunchCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task PendingPluginSavePreventsClosingEitherWindowAndSettingsKeepsSessionDraft()
    {
        var store = new PluginPreferencesTests.Store { Gate = new() };
        var service = new PluginService(new([new PluginPreferencesTests.TestPlugin()]), store);
        await service.LoadAsync();
        var model = new MainViewModel(new Library());
        var main = new MainWindow { DataContext = model, Preferences = new(), Plugins = service };
        main.Show();
        model.EditSessionCommand.Execute(null);
        model.Editor!.Name = "Unsaved plugin Session";
        var settings = new SettingsWindow(main.Preferences, service);
        settings.Show(main);
        var vm = (SettingsViewModel)settings.DataContext!;
        var saving = vm.Plugins!.ApplyCommand.ExecuteAsync(null);
        try
        {
            Assert.False(settings.FindControl<Button>("CloseButton")!.IsEffectivelyEnabled);
            settings.Close(); main.Close();
            Assert.True(settings.IsVisible);
            Assert.True(main.IsVisible);
            Assert.Equal("Unsaved plugin Session", model.Editor.Name);
            store.Gate.SetResult();
            await saving;
            settings.Close();
            Assert.Equal("Unsaved plugin Session", model.Editor.Name);
        }
        finally { store.Gate.TrySetResult(); await saving; settings.Close(); model.CancelEditCommand.Execute(null); main.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 1440, 900, 1)]
    [InlineData(true, 1440, 900, 1)]
    [InlineData(false, 640, 480, 1)]
    [InlineData(true, 640, 480, 1)]
    [InlineData(false, 640, 480, 1.25)]
    [InlineData(true, 640, 480, 1.5)]
    [InlineData(false, 640, 480, 2)]
    [InlineData(true, 640, 480, 2)]
    public async Task PluginSettingsPickerAndEditorRemainReachableInBothThemes(bool dark, int width, int height, double dpi)
    {
        var service = new PluginService(new([new PluginPreferencesTests.TestPlugin { Presence = PluginAppPresence.Running }]));
        await service.LoadAsync();
        var preferences = new PreferencesService();
        await preferences.SaveAsync(new(dark ? AppTheme.Dark : AppTheme.Light, 150, 125));
        var launcher = new IndividualAppLauncher(new Presence(), new Starter(), plugins: service);
        var model = new MainViewModel(new Library(), appLauncher: launcher, plugins: service);
        var main = new MainWindow { DataContext = model, Preferences = preferences, Plugins = service, Width = width, Height = height };
        main.Show();
        main.SetRenderScaling(dpi);
        var settings = new SettingsWindow(preferences, service) { Width = Math.Min(width, 700), Height = height };
        settings.Show(main);
        settings.SetRenderScaling(dpi);
        try
        {
            Layout(settings);
            var toggle = settings.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Enable Test plugin"));
            toggle.BringIntoView();
            Layout(settings);
            AssertVisible(settings, toggle);
            toggle.Focus();
            settings.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            settings.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            var pluginVm = ((SettingsViewModel)settings.DataContext!).Plugins!;
            Assert.False(pluginVm.Plugins[0].Enabled);
            Assert.Empty(service.Current);
            var apply = settings.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Apply plugins"));
            apply.BringIntoView(); Layout(settings);
            AssertVisible(settings, apply);
            AssertVisible(settings, settings.FindControl<Button>("CloseButton")!);
            Capture(settings, $"plugins-settings-{dark}-{width}-{dpi}");
            settings.Close(); // Unapplied plugin choices are discarded.
            Assert.Empty(service.Current);
            await model.RefreshPresenceAsync();
            Layout(main);
            Capture(main, $"plugins-detail-{dark}-{width}-{dpi}");
            model.EditSessionCommand.Execute(null);
            Layout(main);
            var options = main.GetVisualDescendants().OfType<Expander>().Single(expander => expander.Name == "AppOptions");
            options.IsExpanded = true;
            options.BringIntoView(); Layout(main);
            var closePlugin = main.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Name == "ClosePluginOnEnd");
            closePlugin.BringIntoView(); Layout(main);
            AssertVisible(main, closePlugin);
            closePlugin.Focus();
            main.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            main.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.True(model.Editor!.SelectedApp!.CloseOnEnd);
            Assert.DoesNotContain(main.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(box) == "App executable path");
            var save = main.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Save changes"));
            AssertVisible(main, save);
            Capture(main, $"plugins-editor-{dark}-{width}-{dpi}");

            using var pickerModel = new AppPickerViewModel(new Source(), [], new Source(), service);
            await pickerModel.RefreshCommand.ExecuteAsync(null);
            pickerModel.ShowPlugins = true;
            var picker = new AppPickerWindow { DataContext = pickerModel, Preferences = preferences, Width = width == 640 ? 520 : 820, Height = height == 480 ? 460 : 700 };
            picker.Show(main);
            try
            {
                picker.SetRenderScaling(dpi); Layout(picker);
                AssertVisible(picker, picker.FindControl<RadioButton>("PluginSourceButton")!);
                Assert.Equal(2, pickerModel.VisibleApps.Count);
                var item = picker.GetVisualDescendants().OfType<CheckBox>().First();
                var viewport = item.GetVisualAncestors().OfType<ScrollViewer>().First();
                Assert.True(viewport.Viewport.Height >= item.Bounds.Height,
                    $"A complete plugin row must fit: row {item.Bounds.Height}, viewport {viewport.Viewport.Height}.");
                item.Focus();
                picker.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                picker.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Assert.Single(pickerModel.GetSelection());
                Capture(picker, $"plugins-picker-{dark}-{width}-{dpi}");
            }
            finally { picker.Close(); }
        }
        finally { settings.Close(); model.CancelEditCommand.Execute(null); main.Close(); }
    }

    private static StartProcessAction Game() => new(Guid.NewGuid(), "A plugin game", "", Plugin: new("test", "10"));
    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void AssertVisible(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        var start = control.TranslatePoint(default, window)!.Value;
        var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.True(start.X >= 0 && start.Y >= 0 && end.X <= window.Bounds.Width + 1 && end.Y <= window.Bounds.Height + 1,
            $"{control} outside {window.Bounds}: {start} to {end}");
    }
    private static void Capture(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SESSIONS_SCREENSHOT_DIR") is { Length: > 0 } directory)
        { Directory.CreateDirectory(directory); frame.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
    }
    private sealed class Source : IAppSource
    { public Task<IReadOnlyList<DiscoveredApp>> GetAppsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DiscoveredApp>>([]); }
    private sealed class Library : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SessionDefinition>>(
            [new(Guid.NewGuid(), "Gaming Session", "Launch your game with its supporting apps.", [Game(), new(Guid.NewGuid(), "Utility", @"C:\Fixture\utility.exe")])]);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Starter : IProcessStarter
    { public int Calls; public Task StartAsync(StartProcessAction app, CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; } }
    private sealed class Presence : IAppPresenceService
    {
        public int Calls;
        public Task<IReadOnlyDictionary<string, AppPresence>> GetPresenceAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult<IReadOnlyDictionary<string, AppPresence>>(paths.ToDictionary(path => path, _ => AppPresence.NotRunning)); }
        public Task<AppFocusResult> FocusAsync(string executablePath, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(AppFocusResult.Focused); }
    }
}
