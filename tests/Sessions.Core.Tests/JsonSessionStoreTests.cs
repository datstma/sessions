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
    [InlineData("{\"version\":2,\"sessions\":[]}")]
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
        var app = Assert.Single(Assert.Single(await Store.LoadAsync()).Apps);
        Assert.False(app.RunAsAdministrator);
        Assert.Equal(content, await File.ReadAllTextAsync(FilePath));
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
