namespace Sessions.Core;

public interface ISessionStore
{
    Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default);

    /// <summary>
    /// The copy of the replaced library kept by the most recent successful save because that save changed
    /// the file's format; null when the last save kept the format or no save has completed.
    /// </summary>
    string? LastUpgradeBackupPath => null;
}
