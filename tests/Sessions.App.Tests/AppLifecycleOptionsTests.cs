using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class AppLifecycleOptionsTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppOptionsCanBeChosenWithKeyboardAndSurviveEditing(bool dark)
    {
        var original = new SessionDefinition(Guid.NewGuid(), "Flight sim", "",
            [new StartProcessAction(Guid.NewGuid(), "Game hub", @"C:\Games\Hub.exe")]);
        var editor = new SessionEditorViewModel(original);
        var view = new SessionEditorView { DataContext = editor };
        var window = new Window { Content = new ScrollViewer { Content = view }, Width = 620, Height = 620,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            view.GetVisualDescendants().OfType<Expander>().Single(e => e.Header?.ToString() == "App options").IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var options = view.GetVisualDescendants().OfType<CheckBox>().Where(c =>
                c.Content?.ToString() is "Run as administrator").ToArray();
            Assert.Single(options);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<CheckBox>(), c => c.Content?.ToString()?.StartsWith("Force quit") == true);
            foreach (var option in options)
            {
                option.BringIntoView();
                option.Focus();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Assert.True(option.IsChecked);
            }
            var saved = editor.BuildDefinition();
            Assert.False(original.Apps[0].RunAsAdministrator);
            var reopened = new SessionEditorViewModel(saved).BuildDefinition().Apps[0];
            Assert.True(reopened.RunAsAdministrator);
            var output = Environment.GetEnvironmentVariable("SESSIONS_SCREENSHOT_DIR");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                Directory.CreateDirectory(output);
                frame?.Save(Path.Combine(output, $"app-lifecycle-options-{(dark ? "dark" : "light")}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ExplicitAdministratorLaunchUsesShellElevationAndPreservesArguments()
    {
        var app = new StartProcessAction(Guid.NewGuid(), "Probe", Environment.ProcessPath!, "--value \"with spaces\"", AppContext.BaseDirectory);
        var ordinary = WindowsProcessStarter.CreateStartInfo(app);
        var elevated = WindowsProcessStarter.CreateStartInfo(app with { RunAsAdministrator = true });
        Assert.Equal("open", ordinary.Verb);
        Assert.Equal("runas", elevated.Verb);
        Assert.True(elevated.UseShellExecute);
        Assert.Equal(ordinary.FileName, elevated.FileName);
        Assert.Equal(ordinary.Arguments, elevated.Arguments);
        Assert.Equal(ordinary.WorkingDirectory, elevated.WorkingDirectory);
    }
}
