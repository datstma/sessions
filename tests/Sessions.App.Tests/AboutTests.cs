using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;

namespace Sessions.App.Tests;

public sealed class AboutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SessionsTests", Guid.NewGuid().ToString("N"));
    private string DataFolder => Path.Combine(_root, "data");
    private string InstallFolder => Path.Combine(_root, "install");

    [Fact]
    public void BuildMetadataSuppliesTheVersionStageAndRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sessions.slnx"))) directory = directory.Parent;
        var properties = XDocument.Load(Path.Combine(directory!.FullName, "Directory.Build.props")).Descendants("PropertyGroup").Elements().ToArray();
        string Property(string name) => properties.Single(element => element.Name.LocalName == name).Value;

        var info = AppInfo.Current;
        Assert.Equal(Property("Version"), info.Version);
        Assert.Equal(Property("ReleaseStage"), info.Stage);
        Assert.Equal(Property("RepositoryUrl"), info.RepositoryUrl);
        Assert.True(File.Exists(Path.Combine(info.InstallFolder, "LICENSE")));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sessions"), info.DataFolder);
    }

    [AvaloniaTheory]
    [InlineData(false, 700, 800)]
    [InlineData(true, 640, 480)]
    public async Task KeyboardReachesAboutAndEachActionOpensTheRightPlace(bool dark, int width, int height)
    {
        Directory.CreateDirectory(DataFolder);
        var launcher = new Launcher();
        var preferences = new PreferencesService();
        await preferences.SaveAsync(new(dark ? AppTheme.Dark : AppTheme.Light));
        var settings = new SettingsWindow(preferences, launcher: launcher, info: Info()) { Width = width, Height = height };
        settings.Show();
        try
        {
            Layout(settings);
            Assert.Equal("Version 1.2.3 Beta", settings.FindControl<TextBlock>("AboutVersion")!.Text);
            Assert.Equal(DataFolder, settings.FindControl<SelectableTextBlock>("DataFolderPath")!.Text);

            var openFolder = settings.FindControl<Button>("OpenDataFolderButton")!;
            settings.FindControl<ComboBox>("ThemeChoice")!.Focus();
            for (var i = 0; i < 60 && !openFolder.IsFocused; i++)
            {
                settings.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                settings.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            }
            Assert.True(openFolder.IsFocused, "Tab should reach About from the top of Settings.");
            Layout(settings);
            Capture(settings, $"about-{dark}-{width}");

            foreach (var name in new[] { "ReleaseNotesButton", "ReportIssueButton", "SourceCodeButton", "OpenDataFolderButton" })
                await PressAsync(settings, settings.FindControl<Button>(name)!);

            Assert.Equal([
                "https://example.test/sessions/releases",
                "https://example.test/sessions/issues",
                "https://example.test/sessions",
                DataFolder], launcher.Opened);
            Assert.False(settings.FindControl<TextBlock>("AboutStatus")!.IsEffectivelyVisible);
        }
        finally { settings.Close(); }
    }

    [AvaloniaFact]
    public async Task FailuresExplainWhereToFindThingsAndClearAfterASuccessfulOpen()
    {
        var launcher = new Launcher { Result = false };
        var settings = new SettingsWindow(new PreferencesService(), launcher: launcher, info: Info()) { Width = 700, Height = 800 };
        settings.Show();
        try
        {
            var about = ((SettingsViewModel)settings.DataContext!).About!;
            var status = settings.FindControl<TextBlock>("AboutStatus")!;

            await about.OpenDataFolderCommand.ExecuteAsync(null);
            Assert.Empty(launcher.Opened);
            Assert.Equal($"Sessions hasn't saved anything on this computer yet. Your data folder will be {DataFolder}", about.Status);
            Assert.False(about.IsStatusError);

            await about.OpenReleaseNotesCommand.ExecuteAsync(null);
            Layout(settings);
            Assert.Equal("Couldn't open your web browser. The address is https://example.test/sessions/releases", about.Status);
            Assert.True(about.IsStatusError);
            Assert.True(status.IsEffectivelyVisible);
            Assert.Equal(((ISolidColorBrush)settings.FindResource(settings.ActualThemeVariant, "SessionError")!).Color, ((ISolidColorBrush)status.Foreground!).Color);

            Directory.CreateDirectory(DataFolder);
            launcher.Error = new InvalidOperationException("Shell unavailable");
            await about.OpenDataFolderCommand.ExecuteAsync(null);
            Assert.Equal($"Couldn't open your data folder. You can find it at {DataFolder}", about.Status);

            launcher.Error = null;
            launcher.Result = true;
            await about.ReportIssueCommand.ExecuteAsync(null);
            Layout(settings);
            Assert.Null(about.Status);
            Assert.False(status.IsEffectivelyVisible);

            var offline = new AboutViewModel(Info() with { RepositoryUrl = "" }, launcher);
            Assert.False(offline.OpenReleaseNotesCommand.CanExecute(null));
            Assert.True(offline.OpenDataFolderCommand.CanExecute(null));
        }
        finally { settings.Close(); }
    }

    private AppInfo Info() => new("1.2.3", "Beta", "https://example.test/sessions", DataFolder, InstallFolder);

    private static async Task PressAsync(Window window, Button button)
    {
        button.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        if (button.Command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand { ExecutionTask: { } task }) await task;
        Layout(window);
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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class Launcher : ILauncher
    {
        public List<string> Opened { get; } = [];
        public bool Result { get; set; } = true;
        public Exception? Error { get; set; }

        public Task<bool> LaunchUriAsync(Uri uri) => Record(uri.ToString().TrimEnd('/'));
        public Task<bool> LaunchFileAsync(IStorageItem storageItem) => Record(storageItem.Path.LocalPath.TrimEnd(Path.DirectorySeparatorChar));

        private Task<bool> Record(string target)
        {
            if (Error is not null) throw Error;
            if (Result) Opened.Add(target);
            return Task.FromResult(Result);
        }
    }
}
