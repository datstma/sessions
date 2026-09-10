namespace Sessions.Core;

/// <summary>Fixed process lifetimes prepared before a user confirms a one-off normal close.</summary>
public interface IPreparedAppClose : IDisposable
{
    int TargetCount { get; }
    Task<bool> CloseAsync();
}

public sealed class PreparedAppClose(IReadOnlyList<ITrackedProcess> processes, TimeSpan? timeout = null) : IPreparedAppClose
{
    private readonly ITrackedProcess[] _processes = processes.ToArray();
    private int _attempted;
    public int TargetCount => _processes.Length;
    public async Task<bool> CloseAsync()
    {
        if (Interlocked.Exchange(ref _attempted, 1) != 0) throw new InvalidOperationException("This close request has already been used or cancelled.");
        var closed = true;
        List<Exception> errors = [];
        foreach (var process in _processes)
        {
            try
            {
                if (!process.HasExited && !await process.RequestCloseAsync(timeout ?? TimeSpan.FromSeconds(3), allowForceQuit: false).ConfigureAwait(false))
                    closed = false;
            }
            catch (Exception exception) { errors.Add(exception); }
        }
        if (errors.Count > 0) throw new InvalidOperationException("Some app processes could not be closed. " + errors[0].Message, errors[0]);
        return closed;
    }
    public void Dispose()
    {
        Interlocked.Exchange(ref _attempted, 1);
        foreach (var process in _processes) process.Dispose();
    }
}
