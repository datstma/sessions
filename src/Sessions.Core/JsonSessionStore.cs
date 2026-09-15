using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sessions.Core;

/// <summary>Readable local storage; a failed load is never treated as an empty library.</summary>
public sealed class JsonSessionStore(string filePath, TimeProvider? timeProvider = null) : ISessionStore
{
    private const int CurrentVersion = 6;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _filePath = Path.GetFullPath(filePath);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public string? LastUpgradeBackupPath { get; private set; }

    public async Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            var library = await JsonSerializer.DeserializeAsync<Library>(stream, Options, cancellationToken);
            if (library is null || library.Version is < 1 or > CurrentVersion || library.Sessions is null)
                throw new InvalidDataException("This Session library has an unsupported format.");
            Validate(library.Sessions);
            if (library.Version < 5 && library.Sessions.Any(session => session.Apps.Any(app => app.Plugin is not null)))
                throw new InvalidDataException("Plugin apps require library format 5.");
            // Old forceClose flags and unknown fields in older formats must not opt apps into force quit or optional startup.
            return library.Sessions.Select(session => session with
            {
                Apps = session.Apps.Select(app => app with
                {
                    AllowForceQuit = library.Version >= 3 && app.AllowForceQuit,
                    Optional = library.Version >= 6 && app.Optional
                }).ToArray(),
                OutputAudioDevice = library.Version < 4 ? null : session.OutputAudioDevice,
                InputAudioDevice = library.Version < 4 ? null : session.InputAudioDevice
            }).ToArray();
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
        LastUpgradeBackupPath = null;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous))
            {
                // Older builds must reject newer libraries rather than silently dropping plugin apps (v5) or optional apps (v6).
                await JsonSerializer.SerializeAsync(stream, new Library(CurrentVersion, sessions), Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // A failed backup throws before the replaced library is touched.
            var backup = await BackUpReplacedFormatAsync(cancellationToken);
            File.Move(temporaryPath, _filePath, overwrite: true);
            LastUpgradeBackupPath = backup;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Keeps a byte-for-byte copy when a save would replace a library in another (usually older) format,
    /// because older Sessions versions cannot read the file afterwards.
    /// </summary>
    private async Task<string?> BackUpReplacedFormatAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return null;
        int? version = null;
        try
        {
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var property in document.RootElement.EnumerateObject())
                    if (string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase) && property.Value.TryGetInt32(out var number))
                        version = number;
        }
        catch (JsonException) { }
        if (version == CurrentVersion) return null;

        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var prefix = Path.Combine(Path.GetDirectoryName(_filePath)!, Path.GetFileNameWithoutExtension(_filePath)) +
            (version is { } found ? $".v{found}" : ".unreadable") + "-backup-" + stamp;
        for (var attempt = 1; ; attempt++)
        {
            var backup = prefix + (attempt == 1 ? "" : "-" + attempt) + Path.GetExtension(_filePath);
            try
            {
                File.Copy(_filePath, backup, overwrite: false);
                return backup;
            }
            catch (IOException) when (File.Exists(backup)) { }
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
