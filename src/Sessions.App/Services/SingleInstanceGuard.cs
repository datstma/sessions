using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sessions.App.Services;

// Acquire/release on the startup thread. The OS releases an abandoned mutex after a crash.
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private readonly CancellationTokenSource _closing = new();
    private Task? _listener;
    public bool IsOwner { get; }

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(false, name);
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        try { IsOwner = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; }
        if (!IsOwner) _activation.Set();
    }

    public void Listen(Action activate)
    {
        if (!IsOwner || _listener is not null) return;
        _listener = Task.Run(() =>
        {
            while (!_closing.IsCancellationRequested)
                if (_activation.WaitOne(250) && !_closing.IsCancellationRequested) activate();
        });
    }

    public void Dispose()
    {
        _closing.Cancel();
        _activation.Set();
        _listener?.GetAwaiter().GetResult();
        if (IsOwner) _mutex.ReleaseMutex();
        _activation.Dispose();
        _mutex.Dispose();
        _closing.Dispose();
    }
}
