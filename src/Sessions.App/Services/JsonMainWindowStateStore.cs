using System;
using System.IO;
using System.Text.Json;

namespace Sessions.App.Services;

/// <summary>
/// Stores <see cref="MainWindowState"/> beside the library. Reads and writes are synchronous because the
/// file is tiny and must be read before the window is shown and written while it closes. Failures never
/// block starting or closing Sessions; the window then uses its default placement.
/// </summary>
public sealed class JsonMainWindowStateStore(string filePath)
{
    private readonly string _filePath = Path.GetFullPath(filePath);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public MainWindowState? Load()
    {
        try
        {
            return JsonSerializer.Deserialize<StateFile>(File.ReadAllBytes(_filePath), Options) is { Version: 1, State: { } state }
                ? state : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public bool Save(MainWindowState state)
    {
        var temporary = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new StateFile(1, state), Options));
            File.Move(temporary, _filePath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record StateFile(int Version, MainWindowState? State);
}
