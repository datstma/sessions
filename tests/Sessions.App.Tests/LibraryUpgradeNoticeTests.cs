using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class LibraryUpgradeNoticeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 9, 5, 0, TimeSpan.Zero);
    private const string Backup = "sessions.v4-backup-20260915-090500.json";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SessionsTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "sessions.json");

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task UpgradingChangeShowsWhereThePreviousLibraryWasKept(bool dark, bool delete)
    {
        var original = WriteVersionFourLibrary();
        var store = new JsonSessionStore(FilePath, new FixedTime(Now));
        var model = new MainViewModel(store);
        var window = new MainWindow { DataContext = model, Width = 1120, Height = 800, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await model.LoadCommand.ExecuteAsync(null);
            Layout(window);
            var panel = window.FindControl<Border>("LibraryNoticePanel")!;
            Assert.False(panel.IsEffectivelyVisible);

            await ChangeLibraryAsync(model, delete);
            Layout(window);

            var backup = Path.Combine(_directory, Backup);
            Assert.Equal(original, await File.ReadAllTextAsync(backup));
            Assert.True(panel.IsEffectivelyVisible);
            Assert.Contains(backup, window.FindControl<SelectableTextBlock>("LibraryNotice")!.Text);
            Assert.Contains("earlier versions of Sessions can't open them", model.LibraryNotice);
            Capture(window, $"library-upgrade-notice-{dark}");

            var dismiss = window.FindControl<Button>("DismissLibraryNoticeButton")!;
            dismiss.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Layout(window);
            Assert.False(model.HasLibraryNotice);
            Assert.False(panel.IsEffectivelyVisible);

            // The library is current now, so later changes neither back up nor notify again.
            await ChangeLibraryAsync(model, delete: false);
            Assert.False(model.HasLibraryNotice);
            Assert.Single(Directory.GetFiles(_directory, "*backup*"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FailedBackupKeepsTheDraftAndTheOlderLibrary()
    {
        var original = WriteVersionFourLibrary();
        Directory.CreateDirectory(Path.Combine(_directory, Backup));
        using var model = new MainViewModel(new JsonSessionStore(FilePath, new FixedTime(Now)));
        await model.LoadCommand.ExecuteAsync(null);

        model.EditSessionCommand.Execute(null);
        model.Editor!.Name = "Renamed";
        await model.SaveSessionCommand.ExecuteAsync(null);

        Assert.True(model.IsEditing);
        Assert.Equal("Renamed", model.Editor.Name);
        Assert.Contains("could not be saved", model.ErrorMessage);
        Assert.False(model.HasLibraryNotice);
        Assert.Equal(original, await File.ReadAllTextAsync(FilePath));
    }

    private static async Task ChangeLibraryAsync(MainViewModel model, bool delete)
    {
        if (delete)
        {
            model.RequestDeleteSessionCommand.Execute(null);
            await model.ConfirmDeleteSessionCommand.ExecuteAsync(null);
            Assert.False(model.IsConfirmingDelete);
            return;
        }
        model.EditSessionCommand.Execute(null);
        model.Editor!.Name += " again";
        await model.SaveSessionCommand.ExecuteAsync(null);
        Assert.False(model.IsEditing);
    }

    private string WriteVersionFourLibrary()
    {
        Directory.CreateDirectory(_directory);
        var content = $$"""
            {
              "version": 4,
              "sessions": [
                { "id": "{{Guid.NewGuid()}}", "name": "Work", "description": "", "apps": [], "outputAudioDevice": { "id": "headset-id", "name": "Headset" } },
                { "id": "{{Guid.NewGuid()}}", "name": "Gaming", "description": "", "apps": [] }
              ]
            }
            """;
        File.WriteAllText(FilePath, content);
        return content;
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
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
