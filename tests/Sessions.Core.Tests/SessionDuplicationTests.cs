using System.Text.Json;

namespace Sessions.Core.Tests;

public sealed class SessionDuplicationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SessionsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DuplicateUsesNewIdentitiesAndRemapsAppChoicesToTheCopies()
    {
        var radio = new StartProcessAction(Guid.NewGuid(), "SRS", @"C:\SRS\srs.exe", "--quiet", @"C:\SRS", true,
            AppReadiness.WindowAppeared, 45, 5, AllowForceQuit: true);
        var tracker = new StartProcessAction(Guid.NewGuid(), "TrackIR", @"C:\TrackIR\trackir.exe");
        var original = new SessionDefinition(Guid.NewGuid(), "Flight", "Evening sorties", [radio, tracker], tracker.Id,
            SessionLaunchMode.Together, 3, StartupFocus.App, radio.Id, new("out", "Headset"), new("in", "Microphone"));

        var copy = original.Duplicate("Flight copy");

        copy.Validate();
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Empty(copy.Apps.Select(app => app.Id).Intersect(original.Apps.Select(app => app.Id)));
        Assert.Equal(copy.Apps.Count, copy.Apps.Select(app => app.Id).Distinct().Count());
        for (var index = 0; index < original.Apps.Count; index++)
            Assert.Equal(original.Apps[index] with { Id = copy.Apps[index].Id }, copy.Apps[index]);
        Assert.Equal(copy.Apps[1].Id, copy.MainAppId);
        Assert.Equal(copy.Apps[0].Id, copy.FocusAppId);
        // Everything else, including audio and startup choices, is the saved setup.
        Assert.Equal(original with { Id = copy.Id, Name = "Flight copy", Apps = copy.Apps, MainAppId = copy.MainAppId, FocusAppId = copy.FocusAppId }, copy);
        Assert.Equal([radio, tracker], original.Apps);
        Assert.NotEqual(copy.Id, original.Duplicate("Flight copy").Id);
    }

    [Fact]
    public void DuplicateKeepsPluginSettingsIndependentOfTheOriginalDocument()
    {
        SessionDefinition copy;
        using (var document = JsonDocument.Parse("""{"profile":"night","volume":7}"""))
        {
            var game = new StartProcessAction(Guid.NewGuid(), "DCS", "", Plugin: new("steam", "223750", 2, document.RootElement, CloseOnEnd: true));
            copy = new SessionDefinition(Guid.NewGuid(), "Sim", "", [game]).Duplicate("Sim copy");
        }

        var plugin = Assert.Single(copy.Apps).Plugin!;
        Assert.Equal(("steam", "223750", 2, true), (plugin.PluginId, plugin.TargetId, plugin.Version, plugin.CloseOnEnd));
        Assert.Equal("""{"profile":"night","volume":7}""", plugin.Settings!.Value.GetRawText());
        Assert.Null(copy.MainAppId);
        Assert.Null(copy.FocusAppId);
    }

    [Fact]
    public async Task OriginalAndCopyRoundTripTogether()
    {
        var main = new StartProcessAction(Guid.NewGuid(), "Main", @"C:\Apps\main.exe");
        var original = new SessionDefinition(Guid.NewGuid(), "Work", "", [main], main.Id);
        var store = new JsonSessionStore(Path.Combine(_directory, "sessions.json"));

        await store.SaveAsync([original, original.Duplicate("Work copy")]);
        var loaded = await store.LoadAsync();

        Assert.Equal(["Work", "Work copy"], loaded.Select(session => session.Name));
        Assert.Equal(original.MainAppId, loaded[0].MainAppId);
        Assert.Equal(loaded[1].Apps[0].Id, loaded[1].MainAppId);
        Assert.NotEqual(loaded[0].Apps[0].Id, loaded[1].Apps[0].Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
