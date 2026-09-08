namespace Sessions.Core;

public interface ISessionStore
{
    Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default);
}
