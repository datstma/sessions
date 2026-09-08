using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class AppPickerTests
{
    private static readonly DiscoveredApp Rider = new("JetBrains Rider", @"C:\Apps\Rider.exe");
    private static readonly DiscoveredApp Discord = new("Discord", @"C:\Apps\Discord.exe");
    private static readonly DiscoveredApp Browser = new("Browser", @"C:\Apps\Browser.exe");

    [AvaloniaFact]
    public async Task PickerGroupsWindowsAndDisablesExistingAndUnsupportedApps()
    {
        var source = new FakeSource { Apps = [Rider, Discord, Discord with { ExecutablePath = @"c:\apps\DISCORD.exe" },
            new("Packaged app", @"C:\Apps\packaged.exe", UnavailableReason: "Unsupported launch method"),
            new("Protected app", null, UnavailableReason: "Location unavailable")] };
        using var model = new AppPickerViewModel(source, [@"c:\APPS\rider.exe"]);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(4, model.VisibleApps.Count);
        var existing = model.VisibleApps.Single(app => app.App.Name == Rider.Name);
        Assert.False(existing.CanSelect);
        Assert.Equal("Already in this Session", existing.Status);
        // Even programmatically selecting a disabled row must not include it in the result.
        foreach (var app in model.VisibleApps) app.IsSelected = true;
        Assert.Equal(Discord, Assert.Single(model.GetSelection()));
    }

    [AvaloniaFact]
    public async Task FilteringPreservesSelectionAndRefreshingRemovesExitedApps()
    {
        var source = new FakeSource { Apps = [Rider, Discord] };
        using var model = new AppPickerViewModel(source, []);
        await model.RefreshCommand.ExecuteAsync(null);
        foreach (var app in model.VisibleApps) app.IsSelected = true;
        model.SearchText = "rider";
        Assert.Single(model.VisibleApps);
        Assert.Equal(2, model.SelectedCount);
        Assert.Equal("Add 2 apps", model.AddLabel);
        source.Apps = [Rider, Browser];
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(Rider, Assert.Single(model.GetSelection()));
        model.SearchText = "does not exist";
        Assert.True(model.IsEmpty);
        Assert.True(model.CanAdd); // The selected app is retained outside the filter.
    }

    [AvaloniaFact]
    public async Task BrowseMergesWithSelectionsWithoutDuplicatingExistingApps()
    {
        var source = new FakeSource { Apps = [Rider, Discord] };
        using var model = new AppPickerViewModel(source, [Rider.ExecutablePath!]);
        await model.RefreshCommand.ExecuteAsync(null);
        model.VisibleApps.Single(app => app.App.Name == Discord.Name).IsSelected = true;
        model.AddBrowsedApps([@"c:\apps\RIDER.exe", Discord.ExecutablePath!, Browser.ExecutablePath!]);
        Assert.Equal(2, model.GetSelection().Count);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, model.GetSelection().Count);
        Assert.Equal(3, model.VisibleApps.Count);
    }

    [AvaloniaFact]
    public async Task RefreshErrorAllowsRetryAndKeepsBrowseAvailable()
    {
        var source = new FakeSource { Error = new IOException("Cannot enumerate") };
        using var model = new AppPickerViewModel(source, []);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.HasError);
        Assert.False(model.IsLoading);
        model.AddBrowsedApps([Rider.ExecutablePath!]);
        Assert.True(model.CanAdd);
        source.Error = null;
        source.Apps = [Discord];
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.HasError);
        Assert.Single(model.GetSelection());
    }

    [AvaloniaFact]
    public async Task ClosingDuringDiscoveryCancelsAndIgnoresLateResults()
    {
        var result = new TaskCompletionSource<IReadOnlyList<DiscoveredApp>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeSource { PendingResult = result.Task };
        var model = new AppPickerViewModel(source, []);
        var refresh = model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.IsLoading);
        model.Dispose();
        Assert.True(source.LastToken.IsCancellationRequested);
        result.SetResult([Rider]);
        await refresh;
        Assert.Empty(model.VisibleApps);
        Assert.Empty(model.GetSelection());
    }

    [AvaloniaFact]
    public void PickedAppsOnlyChangeTheDraftAndPreserveExistingOptions()
    {
        var existing = new StartProcessAction(Guid.NewGuid(), "My Rider", Rider.ExecutablePath!, "--existing", @"C:\Work");
        var definition = new SessionDefinition(Guid.NewGuid(), "Work", "", [existing], existing.Id);
        var editor = new SessionEditorViewModel(definition);
        editor.AddPickedApps([Rider, Discord, Browser, Discord]);
        var draft = editor.BuildDefinition();
        Assert.Equal(3, draft.Apps.Count);
        Assert.Equal(existing, draft.Apps[0]);
        Assert.Equal(existing.Id, draft.MainAppId);
        Assert.Equal(Discord.Name, draft.Apps[1].Name);
        Assert.All(draft.Apps.Skip(1), app => { Assert.Empty(app.Arguments); Assert.Empty(app.WorkingDirectory); });
        Assert.Single(definition.Apps);
    }

    [AvaloniaTheory]
    [InlineData(false, 650, 620)]
    [InlineData(true, 520, 460)]
    public async Task EditorOpensPickerAndOnlyAddsAfterConfirmation(bool dark, int width, int height)
    {
        var source = new FakeSource { Apps = [Rider, Discord, Browser,
            new("An app with a very long display name to check wrapping and layout", @"C:\Apps\long.exe",
                UnavailableReason: "This app needs a launch method Sessions doesn't support yet.")] };
        var model = new MainViewModel(new NoWriteStore());
        var owner = new MainWindow { DataContext = model, RunningAppSource = source, StartMenuAppSource = source };
        owner.Show();
        try
        {
            model.NewSessionCommand.Execute(null);
            model.Editor!.Name = "Work";
            Dispatcher.UIThread.RunJobs();
            var add = owner.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AddAppsButton");
            Press(owner, add);
            var picker = Assert.Single(owner.OwnedWindows.OfType<AppPickerWindow>());
            picker.Width = width;
            picker.Height = height;
            picker.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var selection = Assert.IsType<AppPickerViewModel>(picker.DataContext);
            Assert.Empty(model.Editor.Apps);
            var search = picker.FindControl<TextBox>("AppSearch")!;
            search.Text = "Discord";
            Dispatcher.UIThread.RunJobs();
            var checkbox = picker.GetVisualDescendants().OfType<CheckBox>().Single();
            checkbox.IsChecked = true;
            search.Text = "";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, selection.SelectedCount);
            Capture(picker, $"app-picker-{(dark ? "dark" : "light")}-{width}");
            Press(picker, picker.FindControl<Button>("PickerCancelButton")!);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Empty(model.Editor.Apps);
            Press(owner, add);
            picker = Assert.Single(owner.OwnedWindows.OfType<AppPickerWindow>());
            selection = Assert.IsType<AppPickerViewModel>(picker.DataContext);
            foreach (var item in selection.VisibleApps.Where(app => app.CanSelect)) item.IsSelected = true;
            Press(picker, picker.FindControl<Button>("AddSelectedButton")!);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(3, model.Editor.Apps.Count);
            Assert.Empty(model.Sessions); // No persistence or launching occurs in this picker.
            Assert.True(add.IsFocused);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task StartMenuSearchAndSourceSwitchPreserveSelectionsWithoutDuplicateExecutables()
    {
        var shortcut = Discord with { Name = "Discord shortcut", Arguments = "--profile \"Work profile\"", WorkingDirectory = @"C:\Work", RunAsAdministrator = true, Location = "Chat" };
        var running = new FakeSource { Apps = [Discord, Browser] };
        using var model = new AppPickerViewModel(running, [], new FakeSource { Apps = [shortcut, Rider] });
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.ShowStartMenu);
        model.SearchText = "chat";
        var selectedShortcut = Assert.Single(model.VisibleApps);
        selectedShortcut.IsSelected = true;
        model.ShowRunningApps = true;
        model.SearchText = "";
        model.VisibleApps.Single(app => app.App.Name == Browser.Name).IsSelected = true;
        Assert.Equal(2, model.SelectedCount);
        model.VisibleApps.Single(app => app.App.Name == Discord.Name).IsSelected = true;
        Assert.False(selectedShortcut.IsSelected); // Switching launch choices replaces, never silently adds a duplicate.
        model.ShowStartMenu = true;
        selectedShortcut.IsSelected = true;
        Assert.Equal(2, model.SelectedCount);
        running.Apps = [];
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(shortcut.Arguments, Assert.Single(model.GetSelection()).Arguments);
        var editor = new SessionEditorViewModel();
        editor.Name = "Work";
        editor.AddPickedApps(model.GetSelection());
        var added = Assert.Single(editor.BuildDefinition().Apps);
        Assert.Equal(shortcut.ExecutablePath, added.ExecutablePath);
        Assert.Equal(shortcut.Arguments, added.Arguments);
        Assert.Equal(shortcut.WorkingDirectory, added.WorkingDirectory);
        Assert.True(added.RunAsAdministrator);
    }

    [AvaloniaFact]
    public async Task StartMenuFailureKeepsRunningAppsUsableAndRecoversOnRefresh()
    {
        var installed = new FakeSource { Error = new IOException("Folder unavailable") };
        using var model = new AppPickerViewModel(new FakeSource { Apps = [Discord] }, [], installed);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Contains("Start menu", model.ErrorMessage);
        model.ShowRunningApps = true;
        Assert.Single(model.VisibleApps).IsSelected = true;
        Assert.True(model.CanAdd);
        installed.Error = null;
        installed.Apps = [Rider];
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.HasError);
        Assert.Equal(Discord.Name, Assert.Single(model.GetSelection()).Name);
        model.ShowStartMenu = true;
        Assert.Equal(Rider.Name, Assert.Single(model.VisibleApps).App.Name);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstalledListCanScrollSearchAndSwitchWithKeyboard(bool dark)
    {
        var apps = Enumerable.Range(1, 80).Select(i => new DiscoveredApp($"Installed app {i:00}", $@"C:\Apps\App{i}.exe")).ToArray();
        using var model = new AppPickerViewModel(new FakeSource { Apps = [Discord] }, [], new FakeSource { Apps = apps });
        var window = new AppPickerWindow { DataContext = model, Width = 520, Height = 460, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Content is ItemsControl);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            scroll.Offset = new Avalonia.Vector(0, scroll.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            Assert.True(scroll.Offset.Y > 0);
            model.SearchText = "app 80";
            Assert.Single(model.VisibleApps).IsSelected = true;
            model.SearchText = "";
            var runningButton = window.FindControl<RadioButton>("RunningSourceButton")!;
            runningButton.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.True(model.ShowRunningApps);
            Assert.Equal(1, model.SelectedCount);
            Assert.Equal(Discord.Name, Assert.Single(model.VisibleApps).App.Name);
            model.ShowStartMenu = true;
            Capture(window, $"start-menu-picker-{(dark ? "dark" : "light")}");
        }
        finally { window.Close(); }
    }

    private static void Press(Window window, Button button)
    {
        Dispatcher.UIThread.RunJobs();
        button.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SESSIONS_SCREENSHOT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
        }
    }

    private sealed class FakeSource : IAppSource
    {
        public IReadOnlyList<DiscoveredApp> Apps { get; set; } = [];
        public Exception? Error { get; set; }
        public Task<IReadOnlyList<DiscoveredApp>>? PendingResult { get; init; }
        public CancellationToken LastToken { get; private set; }
        public Task<IReadOnlyList<DiscoveredApp>> GetAppsAsync(CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            return PendingResult ?? (Error is null ? Task.FromResult(Apps) : Task.FromException<IReadOnlyList<DiscoveredApp>>(Error));
        }
    }

    private sealed class NoWriteStore : ISessionStore
    {
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionDefinition>>([]);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The picker must not save the library.");
    }
}

[Collection("Native desktop")]
public sealed class NativeRunningAppTests(ITestOutputHelper output)
{
    public static bool RunNativeDiscovery => OperatingSystem.IsWindows() &&
        Environment.GetEnvironmentVariable("SESSIONS_RUN_DISCOVERY_SMOKE") == "1";

    [Fact(Skip = "Opt-in read-only check against an interactive Windows desktop.", SkipUnless = nameof(RunNativeDiscovery))]
    public async Task DiscoverActualDesktopAppsWithoutLaunchingOrChangingThem()
    {
        var apps = await new WindowsRunningAppSource().GetAppsAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(apps);
        Assert.Contains(apps, app => app.ExecutablePath is not null && app.UnavailableReason is null);
        Assert.Contains(apps, app => app.IconPng is { Length: > 0 });
        var paths = apps.Where(app => app.ExecutablePath is not null).Select(app => app.ExecutablePath!).ToArray();
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(paths, path => string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase));
        output.WriteLine($"Found {apps.Count} app choices; {apps.Count(app => app.IconPng is { Length: > 0 })} icons; " +
                         $"{apps.Count(app => app.UnavailableReason is null && app.ExecutablePath is not null)} selectable.");
        var ownPath = Environment.ProcessPath!;
        var differentLocation = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), Path.GetFileName(ownPath));
        var presence = await new WindowsAppPresenceService().GetPresenceAsync(
            [.. paths, ownPath, ownPath.ToUpperInvariant(), differentLocation], TestContext.Current.CancellationToken);
        Assert.Contains(paths, path => presence[path] == AppPresence.Window);
        Assert.Equal(AppPresence.Background, presence[ownPath]); // This headless test process has no desktop window.
        Assert.Equal(AppPresence.Background, presence[ownPath.ToUpperInvariant()]);
        Assert.Equal(AppPresence.NotRunning, presence[differentLocation]); // Same filename is insufficient.
        output.WriteLine($"Presence found {paths.Count(path => presence[path] == AppPresence.Window)} windowed apps; " +
                         "verified background presence, case-insensitive paths, and rejection of another executable location.");
    }
}
