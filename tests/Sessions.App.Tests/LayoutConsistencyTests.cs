using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class LayoutConsistencyTests
{
    private const string WindowPath = @"C:\Apps\Slack.exe";
    private const string BackgroundPath = @"C:\Apps\Broadcast.exe";
    private const string NotRunningPath = @"C:\Apps\Notes.exe";
    private const string UnknownPath = @"C:\Apps\Radio.exe";

    [AvaloniaTheory]
    [InlineData(false, 1440, 900, 100)]
    [InlineData(true, 640, 480, 125)]
    public async Task FieldAndButtonTextStaysVerticallyCentered(bool dark, int width, int height, int textPercent)
    {
        // Controls are taller than their text; stretched content used to pin text to the top.
        var preferences = new PreferencesService();
        await preferences.SaveAsync(new(dark ? AppTheme.Dark : AppTheme.Light, 100, textPercent));
        var apps = new[] { App("Slack", WindowPath), App("Broadcast", BackgroundPath), App("Notes", NotRunningPath) };
        using var model = new MainViewModel(new Store(new SessionDefinition(Guid.NewGuid(), "Work", "", apps)), new Presence(), new Launcher(), appCloser: new Closer());
        var window = new MainWindow { DataContext = model, Preferences = preferences, Width = width, Height = height };
        window.Show();
        try
        {
            await model.LoadCommand.ExecuteAsync(null);
            await model.RefreshPresenceAsync();
            Layout(window);
            AssertTextCentered(window, "detail");

            model.EditSessionCommand.Execute(null);
            Layout(window);
            model.Editor!.SelectedApp = model.Editor.Apps[0];
            Layout(window);
            window.GetVisualDescendants().OfType<Expander>().Single(expander => expander.Name == "AppOptions").IsExpanded = true;
            window.GetVisualDescendants().OfType<Expander>().Single(expander => expander.Name == "AdvancedStartup").IsExpanded = true;
            Layout(window);
            Assert.Contains(window.GetVisualDescendants().OfType<NumericUpDown>(), control => control.IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<ComboBox>(), control => control.IsEffectivelyVisible);
            AssertTextCentered(window, "editor");
            model.CancelEditCommand.Execute(null);
        }
        finally { window.Close(); }

        using var pickerModel = new AppPickerViewModel(new Source(apps), [], new Source(apps));
        var picker = new AppPickerWindow { DataContext = pickerModel, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Width = width == 640 ? 520 : 820, Height = width == 640 ? 460 : 700 };
        picker.Show();
        try
        {
            await pickerModel.RefreshCommand.ExecuteAsync(null);
            pickerModel.SearchText = "no";
            Layout(picker);
            AssertTextCentered(picker, "picker");
        }
        finally { picker.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunningStatusesShareOneChipAppearance(bool dark)
    {
        var apps = new[] { App("Slack", WindowPath), App("Broadcast", BackgroundPath), App("Radio", UnknownPath) };
        using var model = new MainViewModel(new Store(new SessionDefinition(Guid.NewGuid(), "Work", "", apps)), new Presence(), new Launcher(), appCloser: new Closer());
        var window = new MainWindow { DataContext = model, Width = 1440, Height = 900, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await model.LoadCommand.ExecuteAsync(null);
            await model.RefreshPresenceAsync();
            Layout(window);
            var rows = model.SelectedSession!.Apps;
            var running = window.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("runningStatus") && button.DataContext == rows[0]);
            var background = window.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PassiveStatus" && border.DataContext == rows[1]);
            var unknown = window.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PassiveStatus" && border.DataContext == rows[2]);
            Assert.True(running.IsEffectivelyVisible && background.IsEffectivelyVisible && unknown.IsEffectivelyVisible);
            Assert.Equal("Running · no window", rows[1].StatusLabel);

            foreach (var chip in new[] { background, unknown })
            {
                Assert.Equal(running.Bounds.Height, chip.Bounds.Height, precision: 2);
                Assert.Equal(running.CornerRadius, chip.CornerRadius);
                Assert.Equal(running.Padding, chip.Padding);
                Assert.Equal(running.BorderThickness, chip.BorderThickness);
                Assert.Equal(Label(running).FontWeight, Label(chip).FontWeight);
                Assert.Equal(Label(running).FontSize, Label(chip).FontSize);
            }
            // Background-only apps are running, so they take the same green chip as the clickable status.
            Assert.Equal(Color(running.Background), Color(background.Background));
            Assert.Equal(Color(running.BorderBrush), Color(background.BorderBrush));
            Assert.Equal(Color(Label(running).Foreground), Color(Label(background).Foreground));
            Assert.Equal(Resource(window, "RunningBrush"), Color(Label(background).Foreground));
            Assert.Equal(Resource(window, "PanelAltBrush"), Color(unknown.Background));
            Assert.Equal(Resource(window, "TextMutedBrush"), Color(Label(unknown).Foreground));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypicalSessionAndNewDraftFitTheDefaultWindowWithoutScrolling(bool dark)
    {
        var apps = new[] { App("Slack", WindowPath), App("Broadcast", BackgroundPath), App("Notes", NotRunningPath) };
        var definition = new SessionDefinition(Guid.NewGuid(), "Work", "Chat, notes and a clean microphone for the workday.", apps);
        using var model = new MainViewModel(new Store(definition), new Presence(), new Launcher(), appCloser: new Closer());
        // The main window's XAML default size.
        var window = new MainWindow { DataContext = model, Width = 1120, Height = 800, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await model.LoadCommand.ExecuteAsync(null);
            await model.RefreshPresenceAsync();
            Layout(window);
            Assert.DoesNotContain("compact", window.Classes);
            var details = window.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(viewer => viewer.IsEffectivelyVisible && viewer.Content is StackPanel panel && panel.Classes.Contains("detailContent"));
            Assert.True(details.Extent.Height <= details.Viewport.Height + 0.5,
                $"A three-app Session needs {details.Extent.Height - details.Viewport.Height:0}px of scrolling at the default window size.");
            Assert.True(window.FindControl<Button>("DeleteSessionButton")!.IsEffectivelyVisible);

            model.NewSessionCommand.Execute(null);
            Layout(window);
            window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "SessionName").Text = "Streaming";
            Layout(window);
            var editor = window.GetVisualDescendants().OfType<ScrollViewer>().Single(viewer => viewer.IsEffectivelyVisible && viewer.Content is SessionEditorView);
            var lastChoice = editor.GetVisualDescendants().OfType<CheckBox>().Last(box => box.IsEffectivelyVisible);
            var bottom = lastChoice.TranslatePoint(new Point(0, lastChoice.Bounds.Height), editor)!.Value.Y;
            Assert.True(bottom <= editor.Viewport.Height,
                $"The last choice in a new Session draft is {bottom - editor.Viewport.Height:0}px below the default window's editor viewport.");
            model.CancelEditCommand.Execute(null);
        }
        finally { window.Close(); }
    }

    private static void AssertTextCentered(Visual root, string state)
    {
        var checkedControls = 0;
        foreach (var control in root.GetVisualDescendants().OfType<Control>()
                     .Where(control => control.IsEffectivelyVisible && control is Button or TextBox { AcceptsReturn: false } or ComboBox or NumericUpDown))
        {
            if (TextCenter(control) is not { } center) continue;
            var offset = center - control.Bounds.Height / 2;
            Assert.True(Math.Abs(offset) <= 1, $"{state}: {control.GetType().Name} {Describe(control)} text sits {offset:0.0}px from its vertical center.");
            checkedControls++;
        }
        Assert.True(checkedControls > 3, $"{state}: expected text controls to check.");
    }

    private static double? TextCenter(Control control)
    {
        // TextLayout height is the rendered line box; a stretched TextBlock draws its text at the top.
        if (control.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is { } presenter)
            return presenter.TranslatePoint(new Point(0, presenter.TextLayout.Height / 2), control)?.Y;
        var text = control.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(block => block.IsEffectivelyVisible && !string.IsNullOrEmpty(block.Text));
        return text?.TranslatePoint(new Point(0, text.TextLayout.Height / 2), control)?.Y;
    }

    private static string Describe(Control control) => control switch
    {
        ContentControl { Content: string content } => content,
        TextBox box => box.Name ?? box.PlaceholderText ?? "",
        _ => control.Name ?? ""
    };

    private static TextBlock Label(Visual chip) => chip.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible);
    private static Color Color(IBrush? brush) => ((ISolidColorBrush)brush!).Color;
    private static Color Resource(Window window, string key) => ((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, key)!).Color;
    private static StartProcessAction App(string name, string path) => new(Guid.NewGuid(), name, path);
    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    private sealed class Store(SessionDefinition definition) : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SessionDefinition>>([definition]);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Presence : IAppPresenceService
    {
        public Task<IReadOnlyDictionary<string, AppPresence>> GetPresenceAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, AppPresence>>(paths.ToDictionary(path => path, path => path switch
            {
                WindowPath => AppPresence.Window,
                BackgroundPath => AppPresence.Background,
                UnknownPath => AppPresence.Unknown,
                _ => AppPresence.NotRunning
            }));
        public Task<AppFocusResult> FocusAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(AppFocusResult.Focused);
    }
    private sealed class Launcher : IIndividualAppLauncher
    {
        public Task<AppLaunchResult> LaunchAsync(StartProcessAction app, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Layout checks must not launch apps.");
    }
    private sealed class Closer : IIndividualAppCloser
    {
        public Task<IPreparedAppClose> PrepareAsync(StartProcessAction app, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Layout checks must not close apps.");
    }
    private sealed class Source(StartProcessAction[] apps) : IAppSource
    {
        public Task<IReadOnlyList<DiscoveredApp>> GetAppsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DiscoveredApp>>(apps.Select(app => new DiscoveredApp(app.Name, app.ExecutablePath)).ToArray());
    }
}
