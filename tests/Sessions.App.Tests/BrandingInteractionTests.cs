using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class BrandingInteractionTests
{
    [AvaloniaTheory]
    [InlineData(false, 1440, 900)]
    [InlineData(true, 1440, 900)]
    [InlineData(false, 640, 480)]
    [InlineData(true, 640, 480)]
    public async Task BrandedScreensKeepReadableThemesAndOwnershipConfirmation(bool dark, int width, int height)
    {
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var empty = new MainWindow { DataContext = new MainViewModel(new Store()), Width = width, Height = height, RequestedThemeVariant = theme };
        empty.Show();
        Capture(empty, "empty", dark, width);
        empty.Close();

        var apps = new[] { "Discord", "Playnite", "SR-ClientRadio", "TobiiGameHub" }
            .Select(name => new StartProcessAction(Guid.NewGuid(), name, $@"C:\Apps\{name}.exe")).ToArray();
        var definition = new SessionDefinition(Guid.NewGuid(), "Gaming", "Your simulator, radio and cockpit utilities, ready together.", apps);
        var presence = new Presence();
        var runner = new SessionRunner(new Host());
        using var model = new MainViewModel(new Store(definition), presence, new ManualLauncher(), runner);
        var window = new MainWindow { DataContext = model, Width = width, Height = height, RequestedThemeVariant = theme };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            await model.RefreshPresenceAsync();
            Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
            Assert.Contains("Manrope", new Typeface(window.FontFamily, weight: FontWeight.Medium).GlyphTypeface.FamilyName);
            foreach (var pair in new[] { ("TextPrimaryBrush", "PanelBrush"), ("TextMutedBrush", "AppBackgroundBrush"),
                ("TextMutedBrush", "PanelAltBrush"), ("TextMutedBrush", "BrandSoftBrush"),
                ("BrandForegroundBrush", "BrandSoftBrush"), ("DangerBrush", "PanelAltBrush"),
                ("RunningBrush", "RunningSoftBrush"), ("OnBrandBrush", "BrandBrush"),
                ("OnBrandBrush", "BrandHoverBrush"), ("OnBrandBrush", "DangerFillBrush"), ("OnBrandBrush", "DangerHoverBrush") })
                Assert.True(Contrast(Brush(window, pair.Item1), Brush(window, pair.Item2)) >= 4.5, $"{pair} must support ordinary text");

            var originalBackground = Brush(window, "AppBackgroundBrush");
            window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(originalBackground, Brush(window, "AppBackgroundBrush"));
            window.RequestedThemeVariant = theme;
            Capture(window, "detail", dark, width);
            var hero = window.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("hero"));
            var title = hero.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Gaming");
            var start = hero.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Start Session"));
            Assert.True(title.Bounds.Height < 50, "The short Session title must not break into a column of letters.");
            if (width == 640)
                Assert.True(start.TranslatePoint(default, window)!.Value.Y >= title.TranslatePoint(default, window)!.Value.Y + title.Bounds.Height,
                    "Compact hero actions must move below the Session title.");

            model.EditSessionCommand.Execute(null);
            Capture(window, "editor", dark, width);
            var nameField = window.GetVisualDescendants().OfType<TextBox>().Single(field => field.Name == "SessionName");
            var fieldBorder = nameField.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_BorderElement");
            Assert.True(nameField.IsFocused);
            Assert.Equal(Brush(window, "PanelAltBrush"), ((ISolidColorBrush)fieldBorder.Background!).Color);
            var advanced = window.GetVisualDescendants().OfType<Expander>()
                .Single(expander => Equals(expander.Header, "Gaming advanced startup options"));
            advanced.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            advanced.GetVisualDescendants().OfType<ComboBox>().First().BringIntoView();
            Capture(window, "advanced", dark, width);
            model.CancelEditCommand.Execute(null);

            await model.StartSessionCommand.ExecuteAsync(null);
            presence.Started = true;
            await model.RefreshPresenceAsync();
            Capture(window, "running", dark, width);
            await model.EndSessionCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Discord" }, model.AppsToKeep);
            Assert.Equal(new[] { "Playnite", "SR-ClientRadio", "TobiiGameHub" }, model.AppsToStop);
            Assert.True(window.FindControl<Button>("CancelEndButton")!.IsFocused);
            Capture(window, "end", dark, width);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.HasActiveRun);
            Assert.False(model.IsEndConfirmation);
            Assert.True(window.FindControl<Button>("EndSessionButton")!.IsFocused);

            using var pickerModel = new AppPickerViewModel(new Source(apps), [], new Source(apps));
            var picker = new AppPickerWindow { DataContext = pickerModel, RequestedThemeVariant = theme,
                Width = width == 640 ? 520 : 820, Height = width == 640 ? 460 : 700 };
            picker.Show();
            try
            {
                await pickerModel.RefreshCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                picker.GetVisualDescendants().OfType<CheckBox>().First().IsChecked = true;
                Capture(picker, "picker", dark, width);
                Assert.True(pickerModel.CanAdd);
                var search = picker.FindControl<TextBox>("AppSearch")!;
                Assert.True(search.IsFocused);
                var placeholder = search.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_Placeholder");
                Assert.Equal(1, placeholder.Opacity);
                Assert.Equal(Brush(picker, "TextMutedBrush"), ((ISolidColorBrush)placeholder.Foreground!).Color);
            }
            finally { picker.Close(); }
        }
        finally { runner.LeaveAppsOpen(); window.Close(); }
    }

    private static Color Brush(Window window, string key) => ((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, key)!).Color;
    private static double Contrast(Color first, Color second)
    {
        static double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
        static double Luminance(Color value) => .2126 * Linear(value.R) + .7152 * Linear(value.G) + .0722 * Linear(value.B);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static void Capture(Window window, string state, bool dark, int width)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SESSIONS_SCREENSHOT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"brand-{state}-{dark}-{width}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
    private sealed class Store(params SessionDefinition[] definitions) : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SessionDefinition>>(definitions);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Presence : IAppPresenceService
    {
        public bool Started { get; set; }
        public Task<IReadOnlyDictionary<string, AppPresence>> GetPresenceAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, AppPresence>>(paths.ToDictionary(path => path, path => Started || path.Contains("Discord") ? AppPresence.Window : AppPresence.NotRunning));
        public Task<AppFocusResult> FocusAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(AppFocusResult.Focused);
    }
    private sealed class ManualLauncher : IIndividualAppLauncher
    {
        public Task<AppLaunchResult> LaunchAsync(StartProcessAction app, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Rendering must not launch apps.");
    }
    private sealed class Host : ISessionProcessHost
    {
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessAcquisition([new Process()], app.Name != "Discord", app.Name == "Discord" ? "Already open · stays open" : "Opened by this Session · closes when it ends"));
    }
    private sealed class Process : ITrackedProcess
    {
        public bool HasExited => false;
        public Task<bool> RequestCloseAsync(TimeSpan timeout) => Task.FromResult(true);
        public void Dispose() { }
    }
    private sealed class Source(StartProcessAction[] apps) : IAppSource
    {
        public Task<IReadOnlyList<DiscoveredApp>> GetAppsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DiscoveredApp>>(apps.Select(app => new DiscoveredApp(app.Name, app.ExecutablePath)).ToArray());
    }
}
