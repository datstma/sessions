using Sessions.App.Services;
using Sessions.App.ViewModels;

namespace Sessions.App.Tests;

public sealed class PreferencesTests
{
    [Fact]
    public async Task PreferencesRoundTripWithoutTouchingSessionLibraryAndResetPersists()
    {
        using var directory = new TestDirectory();
        var library = Path.Combine(directory.Path, "sessions.json");
        await File.WriteAllTextAsync(library, "Session library sentinel", TestContext.Current.CancellationToken);
        var path = Path.Combine(directory.Path, "preferences.json");
        var store = new JsonPreferencesStore(path);
        Assert.Equal(new AppPreferences(), await store.LoadAsync());
        Assert.False(File.Exists(path));
        var chosen = new AppPreferences(AppTheme.Dark, 150, 125);
        await store.SaveAsync(chosen, false);
        Assert.Equal(chosen, await new JsonPreferencesStore(path).LoadAsync());
        var service = new PreferencesService(store);
        await service.LoadAsync();
        using var model = new SettingsViewModel(service);
        await model.ResetCommand.ExecuteAsync(null);
        Assert.Equal(new AppPreferences(), await store.LoadAsync());
        Assert.Equal("Session library sentinel", await File.ReadAllTextAsync(library, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("null")]
    [InlineData("{\"version\":2,\"preferences\":{}}")]
    [InlineData("{\"version\":1,\"preferences\":{\"interfacePercent\":900}}")]
    [InlineData("{\"version\":1,\"preferences\":{\"theme\":\"Unknown\"}}")]
    public async Task InvalidPreferencesUseDefaultsAndRequireExplicitRecoveryWithOriginalBackup(string original)
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
        var service = new PreferencesService(new JsonPreferencesStore(path));
        await service.LoadAsync();
        using var model = new SettingsViewModel(service);
        Assert.True(service.HasLoadError);
        Assert.Equal(new AppPreferences(), service.Current);
        Assert.False(model.ApplyCommand.CanExecute(null));
        Assert.False(await service.SaveAsync(new(AppTheme.Dark)));
        Assert.Equal(original, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        await model.ResetCommand.ExecuteAsync(null);
        Assert.False(service.HasLoadError);
        Assert.Equal(original, await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory.Path, "*.bak")), TestContext.Current.CancellationToken));
        Assert.Equal(new AppPreferences(), await new JsonPreferencesStore(path).LoadAsync());
    }

    [Fact]
    public async Task ReadFailureCanRetryAndSaveFailureRetainsChoicesAndAppliedAppearance()
    {
        var store = new Store { FailLoad = true };
        var service = new PreferencesService(store);
        await service.LoadAsync();
        using var model = new SettingsViewModel(service);
        Assert.True(service.HasLoadError);
        store.FailLoad = false;
        store.Saved = new(AppTheme.Light, 110, 110);
        await model.RetryLoadCommand.ExecuteAsync(null);
        Assert.Equal(store.Saved, model.Preview);
        store.FailSave = true;
        model.Theme = AppTheme.Dark;
        await model.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(AppTheme.Light, service.Current.Theme);
        Assert.Equal(AppTheme.Dark, model.Theme);
        Assert.NotNull(service.ErrorMessage);
        store.FailSave = false;
        await model.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(AppTheme.Dark, service.Current.Theme);
        Assert.Null(service.ErrorMessage);
    }

    [Fact]
    public async Task PendingSaveSerializesApplyResetAndReload()
    {
        var store = new Store { Gate = new TaskCompletionSource() };
        var service = new PreferencesService(store);
        using var model = new SettingsViewModel(service) { Theme = AppTheme.Dark };
        var saving = model.ApplyCommand.ExecuteAsync(null);
        Assert.True(service.IsBusy);
        Assert.False(model.ResetCommand.CanExecute(null));
        Assert.False(model.RetryLoadCommand.CanExecute(null));
        Assert.False(await service.SaveAsync(new(), true));
        Assert.Equal(new AppPreferences(), service.Current);
        store.Gate.SetResult();
        await saving;
        Assert.Equal(1, store.Writes);
        Assert.Equal(AppTheme.Dark, service.Current.Theme);
    }

    [Fact]
    public async Task FailedAtomicReplacementKeepsExistingBytes()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        var store = new JsonPreferencesStore(path);
        await store.SaveAsync(new(AppTheme.Light), false);
        var original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => store.SaveAsync(new(AppTheme.Dark), false));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Sessions-preferences-" + Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    internal sealed class Store : IPreferencesStore
    {
        public AppPreferences Saved { get; set; } = new();
        public bool FailLoad { get; set; }
        public bool FailSave { get; set; }
        public TaskCompletionSource? Gate { get; init; }
        public int Writes { get; private set; }
        public Task<AppPreferences> LoadAsync() => FailLoad
            ? throw new UnauthorizedAccessException("Test read denied") : Task.FromResult(Saved);
        public async Task SaveAsync(AppPreferences preferences, bool preserveUnreadableFile)
        {
            Writes++;
            if (Gate is not null) await Gate.Task;
            if (FailSave) throw new IOException("Test write failed");
            Saved = preferences;
        }
    }
}
