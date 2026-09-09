using System.Threading;
using System.Threading.Tasks;

namespace Sessions.App.Services;

public interface IStartupFocusService
{
    // A null path targets Sessions; a path targets a current app window, without taking ownership.
    Task<AppFocusResult> FocusAsync(string? executablePath, CancellationToken cancellationToken);
}
