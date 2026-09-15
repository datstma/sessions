using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class WindowStateTests : IDisposable
{
    private static readonly Size Minimum = new(640, 480);
    private static readonly Size Allowance = new(32, 64);
    private static readonly ScreenArea Primary = new(new PixelRect(0, 0, 2560, 1392), 1.5, true);
    private static readonly ScreenArea Secondary = new(new PixelRect(2560, 0, 1920, 1040), 1, false);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sessions-window-state-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void KeepsBoundsThatFitTheirScreen()
    {
        var saved = new WindowPlacement(2700, 100, 1000, 700, false);
        Assert.Equal(saved, WindowPlacementPolicy.Fit(saved, [Primary, Secondary], Minimum, Allowance));
    }

    [Fact]
    public void UsesTheMostOverlappedScreenAndMovesThePartlyHiddenWindowInside()
    {
        // Most of the window is on the secondary screen, but its right edge runs past it.
        var fitted = WindowPlacementPolicy.Fit(new WindowPlacement(3700, -40, 1000, 700, true), [Primary, Secondary], Minimum, Allowance)!;
        Assert.Equal(new WindowPlacement(3480, 0, 1000, 700, true), fitted);
    }

    [Fact]
    public void ShrinksToTheWorkingAreaAtThatScreensScalingButNeverBelowTheMinimum()
    {
        var fitted = WindowPlacementPolicy.Fit(new WindowPlacement(10, 10, 1800, 1000, false), [Primary], Minimum, Allowance)!;
        Assert.Equal(2560 / 1.5 - 32, fitted.Width, precision: 3);
        Assert.Equal(1392 / 1.5 - 64, fitted.Height, precision: 3);
        Assert.True(fitted.X + fitted.Width * 1.5 <= 2560 && fitted.Y + fitted.Height * 1.5 <= 1392);

        var tiny = new ScreenArea(new PixelRect(0, 0, 600, 400), 1, true);
        Assert.Equal(new WindowPlacement(0, 0, 640, 480, false),
            WindowPlacementPolicy.Fit(new WindowPlacement(100, 50, 900, 700, false), [tiny], Minimum, Allowance));
    }

    [Fact]
    public void CentersOnThePrimaryScreenWhenTheSavedDisplayIsGone()
    {
        // The saved window was on a display to the right; only a left-hand display and the primary remain.
        var left = new ScreenArea(new PixelRect(-1920, 0, 1920, 1040), 1, false);
        var fitted = WindowPlacementPolicy.Fit(new WindowPlacement(3000, 200, 900, 600, true), [left, Primary], Minimum, Allowance)!;
        Assert.Equal(900, fitted.Width);
        Assert.Equal(600, fitted.Height);
        Assert.Equal((2560 - 1350) / 2, fitted.X);
        Assert.Equal((1392 - 900) / 2, fitted.Y);
        Assert.True(fitted.IsMaximized);
    }

    [Theory]
    [InlineData(double.NaN, 700)]
    [InlineData(900, double.PositiveInfinity)]
    [InlineData(0, 700)]
    [InlineData(900, -1)]
    public void RejectsUnusableSavedSizes(double width, double height) =>
        Assert.Null(WindowPlacementPolicy.Fit(new WindowPlacement(0, 0, width, height, false), [Primary], Minimum, Allowance));

    [Fact]
    public void RequiresAKnownScreen() =>
        Assert.Null(WindowPlacementPolicy.Fit(new WindowPlacement(0, 0, 900, 700, false), [], Minimum, Allowance));

    [Fact]
    public void StoreRoundTripsAndTreatsMissingOrUnreadableFilesAsNoSavedState()
    {
        var path = Path.Combine(_directory, "nested", "window.json");
        var store = new JsonMainWindowStateStore(path);
        Assert.Null(store.Load());

        var state = new MainWindowState(new WindowPlacement(-1200, 40, 1024.5, 768, true), Guid.NewGuid());
        Assert.True(store.Save(state));
        Assert.Equal(state, store.Load());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));

        foreach (var content in new[] { "{ not json", """{ "version": 2, "state": { "selectedSessionId": null } }""", """{ "version": 1 }""" })
        {
            File.WriteAllText(path, content);
            Assert.Null(store.Load());
            Assert.Equal(content, File.ReadAllText(path));
        }
    }

    [Fact]
    public void StoreSaveFailureReturnsFalseWithoutThrowing()
    {
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "not-a-folder");
        File.WriteAllText(blocker, "");
        Assert.False(new JsonMainWindowStateStore(Path.Combine(blocker, "window.json")).Save(new MainWindowState(null, null)));
    }

    [AvaloniaFact]
    public void MainWindowReopensWithItsSizePositionAndSelectedSession()
    {
        var sessions = new[] { Session("Work"), Session("Gaming"), Session("Streaming") };
        var store = new JsonMainWindowStateStore(Path.Combine(_directory, "window.json"));

        var first = OpenWindow(sessions, store);
        var model = (MainViewModel)first.DataContext!;
        Assert.Equal("Work", model.SelectedSession!.Name);
        first.Width = 900;
        first.Height = 700;
        first.Position = new PixelPoint(50, 60);
        model.SelectedSession = model.Sessions[1];
        Dispatcher.UIThread.RunJobs();
        first.Close();

        Assert.Equal(new MainWindowState(new WindowPlacement(50, 60, 900, 700, false), sessions[1].Id), store.Load());

        var second = OpenWindow(sessions, store);
        try
        {
            Assert.Equal(WindowStartupLocation.Manual, second.WindowStartupLocation);
            Assert.Equal(new PixelPoint(50, 60), second.Position);
            Assert.Equal(900, second.Width);
            Assert.Equal(700, second.Height);
            Assert.Equal("Gaming", ((MainViewModel)second.DataContext!).SelectedSession!.Name);
        }
        finally { second.Close(); }
    }

    [AvaloniaFact]
    public void MaximizedWindowKeepsItsNormalBoundsAndFallsBackWhenTheSavedSessionIsGone()
    {
        var store = new JsonMainWindowStateStore(Path.Combine(_directory, "window.json"));
        Assert.True(store.Save(new MainWindowState(new WindowPlacement(40, 30, 1000, 720, true), Guid.NewGuid())));
        var sessions = new[] { Session("Work"), Session("Gaming") };

        var window = OpenWindow(sessions, store);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.Equal("Work", ((MainViewModel)window.DataContext!).SelectedSession!.Name);
        window.Close();

        Assert.Equal(new MainWindowState(new WindowPlacement(40, 30, 1000, 720, true), sessions[0].Id), store.Load());
    }

    [AvaloniaFact]
    public void UnreadableLibraryKeepsTheEarlierSelectedSession()
    {
        var store = new JsonMainWindowStateStore(Path.Combine(_directory, "window.json"));
        var remembered = Guid.NewGuid();
        Assert.True(store.Save(new MainWindowState(null, remembered)));

        var window = new MainWindow { DataContext = new MainViewModel(new FailingStore()), StateStore = store, Width = 900, Height = 700 };
        window.RestoreSavedState();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(((MainViewModel)window.DataContext!).HasError);
        window.Close();

        Assert.Equal(remembered, store.Load()!.SelectedSessionId);
    }

    private static MainWindow OpenWindow(SessionDefinition[] sessions, JsonMainWindowStateStore store)
    {
        var window = new MainWindow { DataContext = new MainViewModel(new Store(sessions)), StateStore = store };
        window.RestoreSavedState();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(((MainViewModel)window.DataContext!).HasSessions);
        return window;
    }

    private static SessionDefinition Session(string name) =>
        new(Guid.NewGuid(), name, "", [new StartProcessAction(Guid.NewGuid(), name + " app", $@"C:\Apps\{name}.exe")]);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class Store(SessionDefinition[] sessions) : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SessionDefinition>>(sessions);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> definitions, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FailingStore : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<SessionDefinition>>(new IOException("locked"));
        public Task SaveAsync(IReadOnlyList<SessionDefinition> definitions, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
