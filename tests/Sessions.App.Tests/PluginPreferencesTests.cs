using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.Core;
using Sessions.Plugins;

namespace Sessions.App.Tests;

public sealed class PluginPreferencesTests
{
    [Fact]
    public async Task MissingFileUsesDefaultsAndUnknownPluginSettingsSurviveEditingKnownPlugin()
    {
        using var directory = new DirectoryFixture();
        var path = Path.Combine(directory.Path, "plugins.json");
        var store = new JsonPluginPreferencesStore(path);
        Assert.Empty(await store.LoadAsync());
        Assert.False(File.Exists(path));
        var unknown = new PluginConfiguration(false, 9, new Dictionary<string, string> { ["opaque"] = "keep", ["new"] = "future" });
        await store.SaveAsync(new Dictionary<string, PluginConfiguration> { ["unknown"] = unknown,
            ["test"] = new(true, Settings: new Dictionary<string, string> { ["path"] = "before", ["future"] = "preserved" }) }, false);
        var service = new PluginService(new([new TestPlugin()]), store);
        await service.LoadAsync();
        using var model = new PluginSettingsViewModel(service);
        var row = model.Plugins.Single(plugin => plugin.Id == "test");
        row.Enabled = false;
        row.Fields[0].Value = "after";
        await model.ApplyCommand.ExecuteAsync(null);
        var loaded = await new JsonPluginPreferencesStore(path).LoadAsync();
        Assert.False(loaded["test"].Enabled);
        Assert.Equal("after", loaded["test"].Settings!["path"]);
        Assert.Equal("preserved", loaded["test"].Settings!["future"]);
        Assert.Equal(9, loaded["unknown"].Version);
        Assert.Equal("keep", loaded["unknown"].Settings!["opaque"]);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("{\"version\":2,\"plugins\":{}}")]
    [InlineData("{\"version\":1,\"plugins\":{\"test\":null}}")]
    [InlineData("{\"version\":1,\"plugins\":{\"test\":{\"enabled\":true,\"version\":0}}}")]
    public async Task FailedLoadBlocksPluginLaunchAndApplyUntilExplicitBackupRecovery(string original)
    {
        using var directory = new DirectoryFixture();
        var path = Path.Combine(directory.Path, "plugins.json");
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
        var service = new PluginService(new([new TestPlugin()]), new JsonPluginPreferencesStore(path));
        Assert.False(await service.SaveAsync(new Dictionary<string, PluginConfiguration>(), reset: true));
        Assert.Equal(original, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        await service.LoadAsync();
        using var model = new PluginSettingsViewModel(service);
        Assert.True(service.HasLoadError);
        Assert.False(model.CanApply);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Capture().ValidateAsync(new("test", "1"), TestContext.Current.CancellationToken));
        Assert.False(await service.SaveAsync(new Dictionary<string, PluginConfiguration>()));
        Assert.Equal(original, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        await model.ResetCommand.ExecuteAsync(null);
        Assert.False(service.HasLoadError);
        Assert.Equal(original, await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory.Path, "*.bak")), TestContext.Current.CancellationToken));
        await service.Capture().ValidateAsync(new("test", "1"), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FailedSaveRetainsDraftAndAppliedSettingsAndRetrySucceeds()
    {
        var store = new Store { FailSave = true };
        var service = new PluginService(new([new TestPlugin()]), store);
        await service.LoadAsync();
        using var model = new PluginSettingsViewModel(service);
        model.Plugins[0].Enabled = false;
        model.Plugins[0].Fields[0].Value = "chosen";
        await model.ApplyCommand.ExecuteAsync(null);
        Assert.False(model.Plugins[0].Enabled);
        Assert.Equal("chosen", model.Plugins[0].Fields[0].Value);
        Assert.Empty(service.Current);
        Assert.NotNull(service.ErrorMessage);
        store.FailSave = false;
        await model.ApplyCommand.ExecuteAsync(null);
        Assert.False(service.Current["test"].Enabled);
        Assert.Null(service.ErrorMessage);
    }

    [Fact]
    public async Task AtomicReplacementFailureKeepsExistingBytesAndCleansTemporaryFile()
    {
        using var directory = new DirectoryFixture();
        var path = Path.Combine(directory.Path, "plugins.json");
        var store = new JsonPluginPreferencesStore(path);
        await store.SaveAsync(new Dictionary<string, PluginConfiguration> { ["test"] = new(true) }, false);
        var original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => store.SaveAsync(new Dictionary<string, PluginConfiguration>(), false));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task SettingsWritesSerializeAndAppearanceResetNeverResetsPlugins()
    {
        var store = new Store { Gate = new() };
        var service = new PluginService(new([new TestPlugin()]), store);
        await service.LoadAsync();
        var preferences = new PreferencesService();
        using var settings = new SettingsViewModel(preferences, service);
        settings.Plugins!.Plugins[0].Enabled = false;
        var saving = settings.Plugins.ApplyCommand.ExecuteAsync(null);
        Assert.True(service.IsBusy);
        Assert.False(settings.Plugins.ResetCommand.CanExecute(null));
        Assert.False(await service.SaveAsync(new Dictionary<string, PluginConfiguration>(), true));
        await service.LoadAsync();
        Assert.Equal(1, store.Reads);
        store.Gate.SetResult();
        await saving;
        await settings.ResetCommand.ExecuteAsync(null);
        Assert.False(service.Current["test"].Enabled);
        await preferences.SaveAsync(new(AppTheme.Dark, 125, 125));
        await settings.Plugins.ResetCommand.ExecuteAsync(null);
        Assert.Equal(new AppPreferences(AppTheme.Dark, 125, 125), preferences.Current);
        Assert.Empty(service.Current);
    }

    internal sealed class TestPlugin : IApplicationPlugin
    {
        public int Launches;
        public bool FailDiscovery;
        public PluginAppPresence Presence { get; set; } = PluginAppPresence.Unknown;
        public Task<PluginAppPresence> GetPresenceAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) => Task.FromResult(Presence);
        public PluginDescriptor Descriptor => new("test", "Test plugin", "1.0.0", EnabledByDefault: true);
        public IReadOnlyList<PluginSetting> Settings => [new("path", "Installation folder", "Leave blank to find the application automatically.")];
        public Task<PluginDiscovery> DiscoverAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) =>
            FailDiscovery ? throw new IOException("discovery failed") : Task.FromResult(new PluginDiscovery([new("A plugin game", new("test", "10")), new("Another plugin game", new("test", "20"))]));
        public Task ValidateAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> LaunchAsync(PluginAppReference app, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
        { Launches++; return Task.FromResult("Launch requested · running status is unverified · close manually"); }
    }
    internal sealed class Store : IPluginPreferencesStore
    {
        public bool FailSave;
        public TaskCompletionSource? Gate;
        public int Reads;
        public Task<IReadOnlyDictionary<string, PluginConfiguration>> LoadAsync()
        { Reads++; return Task.FromResult<IReadOnlyDictionary<string, PluginConfiguration>>(new Dictionary<string, PluginConfiguration>()); }
        public async Task SaveAsync(IReadOnlyDictionary<string, PluginConfiguration> configurations, bool preserveUnreadableFile)
        { if (Gate is not null) await Gate.Task; if (FailSave) throw new IOException("fixture save failure"); }
    }
    private sealed class DirectoryFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SessionsPluginPreferences", Guid.NewGuid().ToString("N"));
        public DirectoryFixture() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
