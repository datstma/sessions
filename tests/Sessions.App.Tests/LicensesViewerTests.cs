using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;

namespace Sessions.App.Tests;

public sealed class LicensesViewerTests : IDisposable
{
    private readonly string _install = Path.Combine(Path.GetTempPath(), "SessionsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PublishedLayoutListsSessionsLicenseThenPackagesSummaryAndNoticeTexts()
    {
        CreatePublishedLayout();

        var documents = LicenseCatalog.Discover(_install);

        Assert.Equal([
            "Sessions: GNU General Public License v3",
            "Included packages",
            "About these notices",
            "Avalonia-12.1.2 · LICENSE.md",
            "Microsoft.NETCore.App-10.0.0 · THIRD-PARTY-NOTICES.TXT",
            "supplemental · Avalonia-NOTICE.md",
            "Tiny.Package-1.0.0 · LICENSE"], documents.Select(document => document.Title));
        Assert.DoesNotContain(documents, document => document.Path.EndsWith(".nuspec"));

        var packages = await LicenseCatalog.ReadAsync(documents[1], Cancel);
        Assert.Null(packages.Problem);
        Assert.Contains("Avalonia 12.1.2\nLicense: MIT\nProject: https://avaloniaui.net/\nNotice files: LICENSE.md", packages.Text);
        Assert.Contains("CommunityToolkit.Mvvm 8.4.2\nLicense: MIT\nNotice files: none in the package", packages.Text);
        Assert.Equal("Plain notice", (await LicenseCatalog.ReadAsync(documents[6], Cancel)).Text);

        // PowerShell writes a single-package inventory as an object.
        File.WriteAllText(documents[1].Path, """{ "package": "Solo/1.0.0", "license": "", "projectUrl": "", "packageNotices": [] }""");
        Assert.Contains("Solo 1.0.0\nLicense: not declared; see the project page\nNotice files: none in the package", (await LicenseCatalog.ReadAsync(documents[1], Cancel)).Text);
    }

    [Fact]
    public async Task SourceBuildOutputShowsTheRealGplAndBundledFontLicense()
    {
        var documents = LicenseCatalog.Discover(AppContext.BaseDirectory);

        Assert.Equal("Sessions: GNU General Public License v3", documents[0].Title);
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", (await LicenseCatalog.ReadAsync(documents[0], Cancel)).Text);
        var font = Assert.Single(documents, document => document.Title == "Manrope-OFL.txt");
        Assert.Contains("SIL OPEN FONT LICENSE", (await LicenseCatalog.ReadAsync(font, Cancel)).Text, StringComparison.OrdinalIgnoreCase);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MissingOrOversizedFilesExplainWhereTheyAre()
    {
        Directory.CreateDirectory(Path.Combine(_install, "licenses"));
        var large = Path.Combine(_install, "licenses", "LARGE.txt");
        using (var stream = File.Create(large)) stream.SetLength(LicenseCatalog.MaximumBytes + 1);

        var model = new LicensesViewModel(_install);
        await model.Loading;
        Assert.Equal($"Couldn't read this file. It's at {Path.Combine(_install, "LICENSE")}", model.Problem);
        Assert.Equal("", model.Text);

        model.SelectedDocument = model.Documents.Single(document => document.Path == large);
        await model.Loading;
        Assert.Equal($"This file is too large to show here. It's at {large}", model.Problem);
    }

    [AvaloniaTheory]
    [InlineData(false, 820, 700)]
    [InlineData(true, 520, 460)]
    public async Task SettingsOpensTheViewerByKeyboardAndReturnsFocusOnEscape(bool dark, int width, int height)
    {
        CreatePublishedLayout();
        var preferences = new PreferencesService();
        await preferences.SaveAsync(new(dark ? AppTheme.Dark : AppTheme.Light, TextPercent: 125));
        var info = new AppInfo("1.2.3", "Beta", "https://example.test/sessions", _install, _install);
        var settings = new SettingsWindow(preferences, info: info) { Width = 700, Height = 800 };
        settings.Show();
        try
        {
            var button = settings.FindControl<Button>("LicensesButton")!;
            Press(settings, button, Key.Enter);
            var viewer = Assert.IsType<LicensesWindow>(Assert.Single(settings.OwnedWindows));
            viewer.Width = width;
            viewer.Height = height;
            var model = (LicensesViewModel)viewer.DataContext!;
            await model.Loading;
            Layout(viewer);

            Assert.True(viewer.FindControl<ComboBox>("DocumentChoice")!.IsFocused);
            Assert.Equal("GPL text", viewer.FindControl<SelectableTextBlock>("DocumentText")!.Text);
            Assert.Equal(Path.Combine(_install, "LICENSE"), viewer.FindControl<SelectableTextBlock>("DocumentPath")!.Text);
            Assert.False(viewer.FindControl<TextBlock>("DocumentProblem")!.IsEffectivelyVisible);

            Press(viewer, viewer.FindControl<ComboBox>("DocumentChoice")!, Key.Down);
            await model.Loading;
            Layout(viewer);
            Assert.Equal("Included packages", model.SelectedDocument!.Title);
            Assert.StartsWith("Packages included in this build", viewer.FindControl<SelectableTextBlock>("DocumentText")!.Text);
            Assert.True(viewer.FindControl<Button>("CloseLicensesButton")!.IsEffectivelyVisible);
            Capture(viewer, $"licenses-{dark}-{width}");

            Press(settings, button, Key.Enter);
            Assert.Single(settings.OwnedWindows);

            Press(viewer, viewer.FindControl<ComboBox>("DocumentChoice")!, Key.Escape);
            Layout(settings);
            Assert.False(viewer.IsVisible);
            Assert.Empty(settings.OwnedWindows);
            Assert.True(button.IsFocused);
        }
        finally { settings.Close(); }
    }

    [Fact]
    public async Task ASlowReadNeverReplacesALaterChoice()
    {
        CreatePublishedLayout();
        var model = new LicensesViewModel(_install);
        var first = model.Loading;
        model.SelectedDocument = model.Documents[2];
        await Task.WhenAll(first, model.Loading);
        Assert.StartsWith("Sessions is licensed", model.Text);
    }

    private void CreatePublishedLayout()
    {
        var notices = Path.Combine(_install, "licenses");
        Directory.CreateDirectory(Path.Combine(notices, "Avalonia-12.1.2"));
        Directory.CreateDirectory(Path.Combine(notices, "Tiny.Package-1.0.0"));
        Directory.CreateDirectory(Path.Combine(notices, "Microsoft.NETCore.App-10.0.0"));
        Directory.CreateDirectory(Path.Combine(notices, "supplemental"));
        File.WriteAllText(Path.Combine(_install, "LICENSE"), "GPL text");
        File.WriteAllText(Path.Combine(_install, "THIRD-PARTY-NOTICES.txt"), "Sessions is licensed under GPL-3.0-only.");
        File.WriteAllText(Path.Combine(notices, "Avalonia-12.1.2", "LICENSE.md"), "MIT");
        File.WriteAllText(Path.Combine(notices, "Avalonia-12.1.2", "avalonia.nuspec"), "<package />");
        File.WriteAllText(Path.Combine(notices, "Tiny.Package-1.0.0", "LICENSE"), "Plain notice");
        File.WriteAllText(Path.Combine(notices, "Microsoft.NETCore.App-10.0.0", "THIRD-PARTY-NOTICES.TXT"), "Runtime notices");
        File.WriteAllText(Path.Combine(notices, "supplemental", "Avalonia-NOTICE.md"), "Supplemental");
        File.WriteAllText(Path.Combine(notices, "dependencies.json"), """
            [
              { "package": "Avalonia/12.1.2", "license": "MIT", "projectUrl": "https://avaloniaui.net/", "packageNotices": ["LICENSE.md"] },
              { "package": "CommunityToolkit.Mvvm/8.4.2", "license": "MIT", "projectUrl": "", "packageNotices": [] }
            ]
            """);
    }

    private static void Press(Window window, Control control, Key key)
    {
        Layout(window);
        control.Focus();
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, key == Key.Enter ? "\r" : null);
        // Escape can close the window before the key is released.
        if (!window.IsVisible) return;
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, key == Key.Enter ? "\r" : null);
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
        if (Directory.Exists(_install)) Directory.Delete(_install, recursive: true);
    }
}
