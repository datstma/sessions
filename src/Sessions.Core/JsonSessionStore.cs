using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sessions.Core;

/// <summary>Readable local storage; a failed load is never treated as an empty library.</summary>
public sealed class JsonSessionStore(string filePath) : ISessionStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _filePath = Path.GetFullPath(filePath);

    public async Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            var library = await JsonSerializer.DeserializeAsync<Library>(stream, Options, cancellationToken);
            if (library is null || library.Version is not (1 or 2) || library.Sessions is null)
                throw new InvalidDataException("This Session library has an unsupported format.");
            Validate(library.Sessions);
            return library.Sessions;
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Session library could not be read.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The Session library contains invalid settings.", exception);
        }
    }

    public async Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default)
    {
        Validate(sessions);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous))
            {
                // Older builds must reject startup policies they cannot honor instead of silently ignoring them.
                await JsonSerializer.SerializeAsync(stream, new Library(2, sessions), Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void Validate(IReadOnlyList<SessionDefinition> sessions)
    {
        var ids = new HashSet<Guid>();
        foreach (var session in sessions)
        {
            if (session is null || !ids.Add(session.Id))
                throw new ArgumentException("Each Session must have a unique identity.");
            session.Validate();
        }
    }

    private sealed record Library(int Version, IReadOnlyList<SessionDefinition> Sessions);
}
