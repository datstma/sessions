using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sessions.App.Services;

public enum AppPresence { Checking, Unknown, NotRunning, Background, Window }
public enum AppFocusResult { Focused, NoWindow, Denied }

// Observation is deliberately separate from Session execution and process ownership.
public interface IAppPresenceService
{
    Task<IReadOnlyDictionary<string, AppPresence>> GetPresenceAsync(
        IReadOnlyList<string> executablePaths, CancellationToken cancellationToken = default);
    Task<AppFocusResult> FocusAsync(string executablePath, CancellationToken cancellationToken = default);
}
