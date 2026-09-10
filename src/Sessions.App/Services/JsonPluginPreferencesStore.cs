using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Sessions.Plugins;

namespace Sessions.App.Services;

public interface IPluginPreferencesStore
{
    Task<IReadOnlyDictionary<string, PluginConfiguration>> LoadAsync();
    Task SaveAsync(IReadOnlyDictionary<string, PluginConfiguration> configurations, bool preserveUnreadableFile);
}

public sealed class JsonPluginPreferencesStore(string path) : IPluginPreferencesStore
{
    private readonly string _path = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<IReadOnlyDictionary<string, PluginConfiguration>> LoadAsync()
    {
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var file = await JsonSerializer.DeserializeAsync<PluginFile>(stream, Options);
            if (file is not { Version: 1, Plugins: not null }) throw new InvalidDataException("The plugin preferences format is unsupported.");
            Validate(file.Plugins);
            return file.Plugins;
        }
        catch (FileNotFoundException) { return new Dictionary<string, PluginConfiguration>(); }
        catch (DirectoryNotFoundException) { return new Dictionary<string, PluginConfiguration>(); }
        catch (JsonException exception) { throw new InvalidDataException("Plugin preferences could not be read.", exception); }
    }

    private static void Validate(IReadOnlyDictionary<string, PluginConfiguration> configurations)
    {
        if (configurations.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null || pair.Value.Version < 1 ||
            pair.Value.Settings?.Any(setting => string.IsNullOrWhiteSpace(setting.Key) || setting.Value is null) == true))
            throw new InvalidDataException("Plugin preferences contain invalid values.");
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, PluginConfiguration> configurations, bool preserveUnreadableFile)
    {
        Validate(configurations);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, new PluginFile(1, configurations), Options);
                await stream.FlushAsync();
            }
            if (preserveUnreadableFile && File.Exists(_path)) File.Copy(_path, _path + ".recovery-" + Guid.NewGuid().ToString("N") + ".bak");
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record PluginFile(int Version, IReadOnlyDictionary<string, PluginConfiguration>? Plugins);
}
