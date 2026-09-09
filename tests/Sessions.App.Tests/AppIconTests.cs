using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class AppIconTests
{
    [AvaloniaFact]
    public async Task CacheSharesCaseInsensitiveRequestsAndReadsOffTheUiThread()
    {
        var calls = 0;
        var readOnUiThread = true;
        var source = new WindowsAppIconSource(_ =>
        {
            Interlocked.Increment(ref calls);
            readOnUiThread = Dispatcher.UIThread.CheckAccess();
            return [1, 2, 3];
        });
        var path = Path.Combine(Path.GetTempPath(), "Icon-test.exe");
        var first = source.GetIconAsync(path);
        var second = source.GetIconAsync(path.ToUpperInvariant());
        Assert.Same(first, second);
        Assert.Equal(new byte[] { 1, 2, 3 }, await first);
        Assert.False(readOnUiThread);
        Assert.Equal(1, calls);
        Assert.Null(await source.GetIconAsync(""));
        Assert.Null(await source.GetIconAsync("not-an-executable.txt"));
        Assert.Equal(1, calls);
    }

    [AvaloniaFact]
    public async Task LateResultsCannotReplaceNewPathOrRepopulateDetachedControl()
    {
        var source = new Source();
        var icon = new AppIcon { IconSource = source, AppName = "Notes", ExecutablePath = "old.exe" };
        var window = new Window { Content = icon, Width = 80, Height = 80 };
        window.Show();
        try
        {
            var fallback = icon.FindControl<TextBlock>("Fallback")!;
            var image = icon.FindControl<Image>("IconImage")!;
            Assert.True(fallback.IsVisible);
            Assert.Equal("N", fallback.Text);
            icon.ExecutablePath = "new.exe";
            source.Complete("new.exe", Png());
            await WaitFor(() => image.Source is not null);
            var current = image.Source;
            source.Complete("old.exe", Png());
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Same(current, image.Source);
            Assert.False(fallback.IsVisible);
            icon.AppName = "Editor";
            icon.ExecutablePath = "pending.exe";
            Assert.Null(image.Source);
            Assert.True(fallback.IsVisible);
            Assert.Equal("E", fallback.Text);
            window.Content = null;
            source.Complete("pending.exe", Png());
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Null(image.Source);
            window.Content = icon;
            await WaitFor(() => image.Source is not null);
            window.Content = null;
            Assert.Null(image.Source);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("denied")]
    public async Task UnavailableArtworkKeepsTheInitial(string failure)
    {
        var source = new Source();
        var icon = new AppIcon { IconSource = source, AppName = "Word", ExecutablePath = "word.exe" };
        var window = new Window { Content = icon };
        window.Show();
        try
        {
            if (failure == "denied") source.Requests["word.exe"].SetException(new UnauthorizedAccessException());
            else source.Complete("word.exe", failure == "missing" ? null : [1, 2, 3]);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Null(icon.FindControl<Image>("IconImage")!.Source);
            Assert.True(icon.FindControl<TextBlock>("Fallback")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 1440, 900)]
    [InlineData(true, 1440, 900)]
    [InlineData(false, 640, 480)]
    [InlineData(true, 640, 480)]
    public async Task SavedAppListLoadsExecutableIconAndKeepsMissingAppFallback(bool dark, int width, int height)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Sessions.App.exe");
        var missing = Path.Combine(AppContext.BaseDirectory, "missing-icon-app.exe");
        using var model = new MainViewModel(new Store(new(Guid.NewGuid(), "Work", "",
            [new(Guid.NewGuid(), "Sessions", path), new(Guid.NewGuid(), "Missing app", missing)])));
        var window = new MainWindow { DataContext = model, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var icons = window.GetVisualDescendants().OfType<AppIcon>().ToArray();
            Assert.Equal(2, icons.Length);
            if (OperatingSystem.IsWindows())
                await WaitFor(() => icons[0].FindControl<Image>("IconImage")!.Source is not null);
            Assert.True(icons[1].FindControl<TextBlock>("Fallback")!.IsVisible);
            Assert.Equal("M", icons[1].FindControl<TextBlock>("Fallback")!.Text);
            Assert.Equal(path, model.SelectedSession!.Definition.Apps[0].ExecutablePath);
            foreach (var scaling in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                window.SetRenderScaling(scaling);
                icons[0].BringIntoView();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(scaling, window.RenderScaling);
                // Fractional scaling rounds layout to physical pixels (30 DIP becomes 38px at 125%).
                if (OperatingSystem.IsWindows())
                    Assert.InRange(Math.Abs(icons[0].FindControl<Image>("IconImage")!.Bounds.Width - 30) * scaling, 0, 1);
                if (Environment.GetEnvironmentVariable("SESSIONS_SCREENSHOT_DIR") is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, $"app-icons-{width}-{(dark ? "dark" : "light")}-{scaling * 100}.png"), PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally { window.Close(); }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private static byte[] Png()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(4, 4), new Vector(96, 96));
        var border = new Border { Width = 4, Height = 4, Background = Brushes.Indigo };
        border.Measure(new Size(4, 4));
        border.Arrange(new Rect(0, 0, 4, 4));
        bitmap.Render(border);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private sealed class Source : IAppIconSource
    {
        public Dictionary<string, TaskCompletionSource<byte[]?>> Requests { get; } = [];
        public Task<byte[]?> GetIconAsync(string path)
        {
            if (!Requests.TryGetValue(path, out var request)) Requests[path] = request = new();
            return request.Task;
        }
        public void Complete(string path, byte[]? bytes) => Requests[path].SetResult(bytes);
    }

    private sealed class Store(SessionDefinition definition) : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionDefinition>>([definition]);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> definitions, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Loading artwork must not save configuration.");
    }
}
