namespace Sessions.Core.Tests;

/// <summary>
/// Fixtures follow the envelopes written by each release: v1 (0.1.0), v2 (0.2.0), v3 (0.2.2),
/// v4 (0.3.0–0.4.0) and v5 (0.5.0). Stray newer fields in older files must stay ignored.
/// </summary>
public sealed class LibraryFormatUpgradeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 14, 30, 12, TimeSpan.FromHours(2));
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SessionsTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "sessions.json");

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task EveryReleasedFormatLoadsWithItsOwnSettingsAndIsLeftUnchanged(int version)
    {
        var original = CopyFixture(version);
        var sessions = await new JsonSessionStore(FilePath).LoadAsync();

        Assert.Equal(["Flight sim", "Work"], sessions.Select(session => session.Name));
        var flight = sessions[0];
        var radio = flight.Apps[0];
        Assert.Equal("SR-ClientRadio", radio.Name);
        Assert.True(radio.RunAsAdministrator);
        Assert.Equal("--force_enable_VR", flight.Apps[1].Arguments);
        Assert.Equal(flight.Apps[1].Id, flight.MainAppId);
        Assert.Empty(sessions[1].Apps);
        Assert.Null(sessions[1].MainAppId);

        Assert.Equal(version >= 2 ? SessionLaunchMode.Together : SessionLaunchMode.InOrder, flight.LaunchMode);
        Assert.Equal(version >= 2 ? 5 : 0, flight.PauseBetweenAppsSeconds);
        Assert.Equal(version >= 2 ? radio.Id : null, flight.FocusAppId);
        Assert.Equal(version >= 2 ? AppReadiness.WindowAppeared : AppReadiness.LaunchCompleted, radio.Readiness);
        Assert.Equal(version >= 2 ? 45 : 30, radio.ReadinessTimeoutSeconds);
        // Force quit is honoured only from v3; v1's legacy forceClose and v2's stray flag stay off.
        Assert.Equal(version >= 3, radio.AllowForceQuit);
        // v3 ignores a stray audio choice; v4 introduced audio.
        Assert.Equal(version >= 4 ? new AudioDeviceChoice("{0.0.0.00000000}.{headset}", "Headset") : null, flight.OutputAudioDevice);
        Assert.Equal(version >= 4 ? "Microphone" : null, flight.InputAudioDevice?.Name);
        Assert.Equal(version >= 5 ? new PluginAppReference("steam", "223850", CloseOnEnd: true) : null, flight.Apps.ElementAtOrDefault(2)?.Plugin);

        Assert.Equal(original, await File.ReadAllBytesAsync(FilePath));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task FirstSaveOverAnOlderFormatKeepsAnExactCopyAndLaterSavesDoNot(int version)
    {
        var original = CopyFixture(version);
        var store = new JsonSessionStore(FilePath, new FixedTime(Now));
        var sessions = await store.LoadAsync();

        await store.SaveAsync(sessions);

        var backup = Path.Combine(_directory, $"sessions.v{version}-backup-20260915-143012.json");
        Assert.Equal(backup, store.LastUpgradeBackupPath);
        Assert.Equal(original, await File.ReadAllBytesAsync(backup));
        Assert.Contains("\"version\": 5", await File.ReadAllTextAsync(FilePath));
        Assert.Equal(sessions.Select(session => session.Name), (await store.LoadAsync()).Select(session => session.Name));

        await store.SaveAsync(sessions);
        Assert.Null(store.LastUpgradeBackupPath);
        Assert.Equal([backup], Directory.GetFiles(_directory, "*backup*"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task NewAndCurrentFormatLibrariesAreSavedWithoutBackups()
    {
        var store = new JsonSessionStore(FilePath, new FixedTime(Now));
        await store.SaveAsync([new SessionDefinition(Guid.NewGuid(), "Work", "", [])]);
        Assert.Null(store.LastUpgradeBackupPath);

        CopyFixture(5);
        await store.SaveAsync(await store.LoadAsync());
        Assert.Null(store.LastUpgradeBackupPath);
        Assert.Empty(Directory.GetFiles(_directory, "*backup*"));
    }

    [Fact]
    public async Task ReplacingAnUnreadableFileOrAnEarlierBackupNameKeepsSeparateCopies()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "{ damaged");
        await File.WriteAllTextAsync(Path.Combine(_directory, "sessions.unreadable-backup-20260915-143012.json"), "earlier");
        var store = new JsonSessionStore(FilePath, new FixedTime(Now));

        await store.SaveAsync([new SessionDefinition(Guid.NewGuid(), "Work", "", [])]);

        Assert.Equal(Path.Combine(_directory, "sessions.unreadable-backup-20260915-143012-2.json"), store.LastUpgradeBackupPath);
        Assert.Equal("{ damaged", await File.ReadAllTextAsync(store.LastUpgradeBackupPath!));
        Assert.Equal("earlier", await File.ReadAllTextAsync(Path.Combine(_directory, "sessions.unreadable-backup-20260915-143012.json")));
    }

    [Fact]
    public async Task FailedBackupBlocksTheUpgradeAndLeavesTheOlderLibraryUntouched()
    {
        var original = CopyFixture(4);
        var store = new JsonSessionStore(FilePath, new FixedTime(Now));
        var sessions = await store.LoadAsync();
        // A folder with the backup's name makes the copy fail.
        Directory.CreateDirectory(Path.Combine(_directory, "sessions.v4-backup-20260915-143012.json"));

        var error = await Assert.ThrowsAnyAsync<Exception>(() => store.SaveAsync(sessions));

        // The editor and delete flows report these as storage errors and keep the user's change for retry.
        Assert.True(error is IOException or UnauthorizedAccessException, error.GetType().Name);
        Assert.Null(store.LastUpgradeBackupPath);
        Assert.Equal(original, await File.ReadAllBytesAsync(FilePath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    private byte[] CopyFixture(int version)
    {
        Directory.CreateDirectory(_directory);
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Libraries", $"v{version}.json"));
        File.WriteAllBytes(FilePath, bytes);
        return bytes;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Fixture", now.Offset, "Fixture", "Fixture");
    }
}
