using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Sessions.App.Services;

public interface IPreferencesStore
{
    Task<AppPreferences> LoadAsync();
    Task SaveAsync(AppPreferences preferences, bool preserveUnreadableFile);
}

public sealed class JsonPreferencesStore(string filePath) : IPreferencesStore
{
    private readonly string _filePath = Path.GetFullPath(filePath);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<AppPreferences> LoadAsync()
    {
        try
        {
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            var file = await JsonSerializer.DeserializeAsync<PreferencesFile>(stream, Options);
            if (file is not { Version: 1, Preferences: not null })
                throw new InvalidDataException("The preferences file has an unsupported format.");
            file.Preferences.Validate();
            return file.Preferences;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (JsonException exception) { throw new InvalidDataException("The preferences file could not be read.", exception); }
        catch (ArgumentException exception) { throw new InvalidDataException("The preferences file contains unsupported values.", exception); }
    }

    public async Task SaveAsync(AppPreferences preferences, bool preserveUnreadableFile)
    {
        preferences.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporary = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, new PreferencesFile(1, preferences), Options);
                await stream.FlushAsync();
            }
            // Recovery is explicit and keeps the original bytes for inspection or a newer app version.
            if (preserveUnreadableFile && File.Exists(_filePath))
                File.Copy(_filePath, _filePath + ".recovery-" + Guid.NewGuid().ToString("N") + ".bak");
            File.Move(temporary, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record PreferencesFile(int Version, AppPreferences? Preferences);
}
