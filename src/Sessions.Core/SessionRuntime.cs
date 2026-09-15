namespace Sessions.Core;

public enum SessionRunState { Starting, Running, AwaitingEndConfirmation, Stopping, NeedsAttention, Completed, Failed }
public enum SessionAppState { Waiting, Starting, AlreadyRunning, Running, Exited, Untracked, Failed, Closing, Closed, LeftOpen, Skipped }

public sealed record SessionAppOutcome(Guid AppId, string Name, SessionAppState State, bool Owned, string Message,
    string ExecutablePath = "", bool AllowForceQuit = false);
public sealed record SessionRunSnapshot(Guid SessionId, string Name, SessionRunState State,
    IReadOnlyList<SessionAppOutcome> Apps, string Message, Guid RunId = default, bool StartupSucceeded = false)
{
    public bool IsActive => State is SessionRunState.Starting or SessionRunState.Running or SessionRunState.AwaitingEndConfirmation or
        SessionRunState.Stopping or SessionRunState.NeedsAttention;
}

// References identify particular process lifetimes, never just an executable name or reusable PID.
public interface ITrackedProcess : IDisposable
{
    bool HasExited { get; }
    bool HasWindow => false;
    string? TrackingMessage => null;
    Task<bool> RequestCloseAsync(TimeSpan timeout, bool allowForceQuit = false);
}

public sealed record ProcessAcquisition(IReadOnlyList<ITrackedProcess> Processes, bool Owned, string Message);
public interface ISessionProcessHost
{
    Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken);
}
