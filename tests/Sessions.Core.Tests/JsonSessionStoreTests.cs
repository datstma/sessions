namespace Sessions.Core.Tests;

public sealed class JsonSessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SessionsTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "sessions.json");
    private JsonSessionStore Store => new(FilePath);

    [Fact]
    public async Task MissingLibraryStartsEmpty()
    {
        Assert.Empty(await Store.LoadAsync());
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task RoundTripPreservesSessionOrderAppOrderAndBothLifetimes()
    {
        var support = new StartProcessAction(Guid.NewGuid(), "Support", @"C:\Apps\support.exe", "--quiet", @"C:\Apps", RunAsAdministrator: true);
        var main = new StartProcessAction(Guid.NewGuid(), "Main", @"C:\Apps\main.exe");
        var work = new SessionDefinition(Guid.NewGuid(), "Work", "Time to focus", []);
        var flight = new SessionDefinition(Guid.NewGuid(), "Flight sim", "", [support, main], main.Id);

        await Store.SaveAsync([work, flight]);
        var loaded = await Store.LoadAsync();

        Assert.Equal(new[] { work.Id, flight.Id }, loaded.Select(session => session.Id));
        Assert.Null(loaded[0].MainAppId);
        Assert.Equal(main.Id, loaded[1].MainAppId);
        Assert.Equal(new[] { support, main }, loaded[1].Apps);
        Assert.Equal(work.Description, loaded[0].Description);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"version\":99,\"sessions\":[]}")]
    [InlineData("{\"version\":1,\"sessions\":[null]}")]
    public async Task InvalidLibraryIsReportedWithoutChangingTheFile(string contents)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, contents);
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.LoadAsync());
        Assert.Equal(contents, await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task OlderLibraryLoadsWithoutRewritingTheFile()
    {
        Directory.CreateDirectory(_directory);
        var content = $$"""
            {"version":1,"sessions":[{"id":"{{Guid.NewGuid()}}","name":"Old","description":"","apps":[
              {"id":"{{Guid.NewGuid()}}","name":"App","executablePath":"C:\\App.exe","arguments":"","workingDirectory":""}]}]}
            """;
        await File.WriteAllTextAsync(FilePath, content);
        var session = Assert.Single(await Store.LoadAsync());
        var app = Assert.Single(session.Apps);
        Assert.False(app.RunAsAdministrator);
        Assert.Equal(SessionLaunchMode.InOrder, session.LaunchMode);
        Assert.Equal(0, session.PauseBetweenAppsSeconds);
        Assert.Equal(StartupFocus.Unchanged, session.FocusAfterStartup);
        Assert.Equal(AppReadiness.LaunchCompleted, app.Readiness);
        Assert.Equal(30, app.ReadinessTimeoutSeconds);
        Assert.Null(app.PauseAfterSeconds);
        Assert.Equal(content, await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task AdvancedStartupRoundTripsInVersionTwoWithReadableEnums()
    {
        var app = new StartProcessAction(Guid.NewGuid(), "Editor", @"C:\Editor.exe", Readiness: AppReadiness.WindowAppeared,
            ReadinessTimeoutSeconds: 90, PauseAfterSeconds: 0);
        var definition = new SessionDefinition(Guid.NewGuid(), "Work", "", [app], LaunchMode: SessionLaunchMode.Together,
            PauseBetweenAppsSeconds: 3, FocusAfterStartup: StartupFocus.App, FocusAppId: app.Id);
        await Store.SaveAsync([definition]);
        var loaded = Assert.Single(await Store.LoadAsync());
        Assert.Equal(definition with { Apps = loaded.Apps }, loaded);
        Assert.Equal(app, Assert.Single(loaded.Apps));
        var contents = await File.ReadAllTextAsync(FilePath);
        Assert.Contains("\"version\": 2", contents);
        Assert.Contains("\"launchMode\": \"Together\"", contents);
        Assert.Contains("\"readiness\": \"WindowAppeared\"", contents);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("pause")]
    [InlineData("condition")]
    [InlineData("timeout")]
    [InlineData("override")]
    [InlineData("focus")]
    public async Task InvalidStartupSettingsCannotReplaceTheLibrary(string setting)
    {
        var app = new StartProcessAction(Guid.NewGuid(), "Editor", @"C:\Editor.exe");
        var original = new SessionDefinition(Guid.NewGuid(), "Work", "", [app]);
        await Store.SaveAsync([original]);
        var contents = await File.ReadAllTextAsync(FilePath);
        var invalid = setting switch
        {
            "mode" => original with { LaunchMode = (SessionLaunchMode)99 },
            "pause" => original with { PauseBetweenAppsSeconds = -1 },
            "condition" => original with { Apps = [app with { Readiness = (AppReadiness)99 }] },
            "timeout" => original with { Apps = [app with { ReadinessTimeoutSeconds = 0 }] },
            "override" => original with { Apps = [app with { PauseAfterSeconds = 301 }] },
            _ => original with { FocusAfterStartup = StartupFocus.App, FocusAppId = Guid.NewGuid() }
        };
        await Assert.ThrowsAsync<ArgumentException>(() => Store.SaveAsync([invalid]));
        Assert.Equal(contents, await File.ReadAllTextAsync(FilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyForceFlagIsIgnoredAndRemovedOnNextSave(bool legacyForce)
    {
        var definition = new SessionDefinition(Guid.NewGuid(), "Legacy", "", [new(Guid.NewGuid(), "App", @"C:\App.exe", RunAsAdministrator: true)]);
        await Store.SaveAsync([definition]);
        var content = await File.ReadAllTextAsync(FilePath);
        content = content.Replace("\"runAsAdministrator\": true", $"\"forceClose\": {legacyForce.ToString().ToLowerInvariant()}, \"runAsAdministrator\": true");
        await File.WriteAllTextAsync(FilePath, content);
        var loaded = await Store.LoadAsync();
        Assert.True(Assert.Single(Assert.Single(loaded).Apps).RunAsAdministrator);
        Assert.Equal(content, await File.ReadAllTextAsync(FilePath));
        await Store.SaveAsync(loaded);
        Assert.DoesNotContain("forceClose", await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task InvalidMainAppCannotReplaceASavedLibrary()
    {
        var original = new SessionDefinition(Guid.NewGuid(), "Work", "", []);
        await Store.SaveAsync([original]);
        var contents = await File.ReadAllTextAsync(FilePath);
        await Assert.ThrowsAsync<ArgumentException>(() => Store.SaveAsync([original with { MainAppId = Guid.NewGuid() }]));
        Assert.Equal(contents, await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task CancelledSaveKeepsTheOriginalLibrary()
    {
        var original = new SessionDefinition(Guid.NewGuid(), "Work", "", []);
        await Store.SaveAsync([original]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.SaveAsync([], cancellation.Token));
        Assert.Equal(original.Id, Assert.Single(await Store.LoadAsync()).Id);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task EditingReplacesExistingFileWithoutLeavingTemporaryFiles()
    {
        var original = new SessionDefinition(Guid.NewGuid(), "Work", "", []);
        await Store.SaveAsync([original]);
        await Store.SaveAsync([original with { Name = "Deep work" }]);
        Assert.Equal("Deep work", Assert.Single(await Store.LoadAsync()).Name);
        Assert.Single(Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
