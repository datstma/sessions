namespace Sessions.Core;

/// <summary>One active run, configurable startup, lifetime observation, and ownership-safe cleanup.</summary>
public sealed class SessionRunner(ISessionProcessHost host, TimeSpan? closeTimeout = null)
{
    private readonly object _sync = new();
    private Run? _run;
    private Run? _latestRun;
    private SessionRunSnapshot? _snapshot;
    public event EventHandler? Changed;
    public SessionRunSnapshot? Snapshot { get { lock (_sync) return _snapshot; } }

    public Task StartAsync(SessionDefinition definition)
    {
        definition.Validate();
        Run run;
        lock (_sync)
        {
            if (_run is not null) throw new InvalidOperationException("End the active Session before starting another.");
            run = new Run(definition with { Apps = definition.Apps.ToArray() });
            _run = run;
            _latestRun = run;
        }
        Publish(run);
        return StartCoreAsync(run);
    }

    private async Task StartCoreAsync(Run run)
    {
        try
        {
            if (run.Definition.LaunchMode == SessionLaunchMode.Together)
            {
                // Same-executable rows cannot race the host's existing-process check.
                // Each group keeps definition order; independent executables overlap.
                var groups = run.Entries.GroupBy(e => StartupPathKey(e.App.ExecutablePath),
                    StringComparer.OrdinalIgnoreCase).ToArray();
                await Task.WhenAll(groups.Select(async group =>
                {
                    Entry? previous = null;
                    foreach (var entry in group)
                    {
                        await StartEntryAsync(run, entry, previous).ConfigureAwait(false);
                        previous = entry;
                    }
                })).ConfigureAwait(false);
            }
            else foreach (var entry in run.Entries) await StartEntryAsync(run, entry).ConfigureAwait(false);
        }
        finally { run.StartFinished.TrySetResult(); }

        bool end;
        lock (_sync)
        {
            end = run.StopRequested;
            if (!end && run.Failed)
            {
                if (run.Entries.Any(e => e.Owned))
                    RequestEndConfirmation(run, "Startup stopped. Save any work in the opened apps before confirming cleanup.");
                else Finish(run, SessionRunState.Failed, "Session couldn't start. No owned apps need cleanup.");
            }
            else if (!end)
            {
                run.State = SessionRunState.Running;
                run.StartupSucceeded = true;
                run.Message = run.Definition.MainAppId is { } main && run.Entries.First(e => e.App.Id == main).Processes.Count == 0
                    ? "The main app couldn't be tracked. Use End Session when you're finished."
                    : "Your Session is active.";
            }
        }
        Publish(run);
        if (end) await EndRunAsync(run).ConfigureAwait(false);
        else if (!run.Failed) _ = MonitorAsync(run);
    }

    private async Task StartEntryAsync(Run run, Entry entry, Entry? previousAtSamePath = null)
    {
        lock (_sync)
        {
            if (run.StopRequested || run.Failed) return;
            entry.State = SessionAppState.Starting;
            entry.Message = "Opening…";
        }
        Publish(run);
        string? acquiredMessage = null;
        try
        {
            if (previousAtSamePath is { Processes.Count: 0 })
                throw new InvalidOperationException("An earlier launch of this executable was untracked. Its repeated entry was not launched.");
            var acquisition = await host.OpenAsync(entry.App, run.StartCancellation.Token).ConfigureAwait(false);
            // Always retain a completed acquisition, even if cancellation/failure occurred while Windows was opening it.
            lock (_sync)
            {
                entry.Processes = acquisition.Processes;
                entry.Owned = acquisition.Owned;
                acquiredMessage = acquisition.Message;
                entry.Message = acquiredMessage;
            }
            run.StartCancellation.Token.ThrowIfCancellationRequested();
            await WaitForReadinessAsync(run, entry).ConfigureAwait(false);
            if (run.Definition.LaunchMode == SessionLaunchMode.InOrder)
            {
                var seconds = entry.App.PauseAfterSeconds ??
                    (ReferenceEquals(entry, run.Entries.LastOrDefault()) ? 0 : run.Definition.PauseBetweenAppsSeconds);
                for (var remaining = seconds; remaining > 0; remaining--)
                {
                    lock (_sync) entry.Message = $"Pausing after {entry.App.Name} · {remaining}s remaining";
                    Publish(run);
                    await Task.Delay(TimeSpan.FromSeconds(1), run.StartCancellation.Token).ConfigureAwait(false);
                }
            }
            lock (_sync)
            {
                entry.State = entry.Processes.Count == 0 ? SessionAppState.Untracked :
                    entry.Owned ? SessionAppState.Running : SessionAppState.AlreadyRunning;
                entry.Message = acquiredMessage;
            }
        }
        catch (OperationCanceledException) when (run.StopRequested || run.Failed)
        {
            lock (_sync)
            {
                entry.State = acquiredMessage is null ? SessionAppState.Waiting :
                    entry.Processes.Count == 0 ? SessionAppState.Untracked :
                    entry.Owned ? SessionAppState.Running : SessionAppState.AlreadyRunning;
                entry.Message = acquiredMessage is null ? "Startup cancelled before opening" : acquiredMessage + " · Startup wait cancelled";
            }
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                entry.State = SessionAppState.Failed;
                entry.Message = exception.Message;
                run.Failed = true;
                run.Message = $"Startup stopped at {entry.App.Name}. Waiting for in-flight launches to finish.";
            }
            run.StartCancellation.Cancel();
        }
        Publish(run);
    }

    private async Task WaitForReadinessAsync(Run run, Entry entry)
    {
        if (entry.App.Readiness == AppReadiness.LaunchCompleted) return;
        if (entry.Processes.Count == 0)
            throw new InvalidOperationException("Readiness cannot be verified for this untracked launch. Use 'Launch request completed' or manage it manually.");
        var token = run.StartCancellation.Token;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(entry.App.ReadinessTimeoutSeconds);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var ready = await Task.Run(() =>
            {
                var live = entry.Processes.Where(p => !p.HasExited).ToArray();
                if (live.Length == 0) throw new InvalidOperationException("The tracked app exited before its startup condition was met.");
                return entry.App.Readiness == AppReadiness.ProcessRunning || live.Any(p => p.HasWindow);
            }, token).ConfigureAwait(false);
            if (ready) return;
            if (timer.Elapsed >= timeout)
                throw new TimeoutException($"No window appeared for {entry.App.Name} within {entry.App.ReadinessTimeoutSeconds} seconds. Startup stopped.");
            var remaining = (int)Math.Ceiling((timeout - timer.Elapsed).TotalSeconds);
            lock (_sync) entry.Message = $"Waiting for {entry.App.Name}'s window · {remaining}s remaining";
            Publish(run);
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, Math.Min(250, (timeout - timer.Elapsed).TotalMilliseconds))), token).ConfigureAwait(false);
        }
    }

    private static string StartupPathKey(string path)
    {
        try { return Path.GetFullPath(path.Trim()).Replace('/', '\\'); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        { return path.Trim().Replace('/', '\\'); } // The host reports an invalid path as an ordinary launch failure.
    }

    /// <summary>Execute cleanup after the caller has obtained the user's save-work confirmation.</summary>
    public Task EndAsync() => EndRunAsync(null);

    public void DismissEndRequest()
    {
        Run? run;
        lock (_sync)
        {
            run = _run;
            if (run is null || run.State != SessionRunState.AwaitingEndConfirmation) return;
            run.MainEndDismissed = true;
            run.State = run.Failed ? SessionRunState.NeedsAttention : SessionRunState.Running;
            run.Message = run.Failed ? "Startup stopped. Opened apps were left running; use End Session when ready."
                : "Apps were left running. Use End Session when you're ready.";
        }
        Publish(run);
    }

    private static void RequestEndConfirmation(Run run, string message)
    {
        run.State = SessionRunState.AwaitingEndConfirmation;
        run.Message = message;
    }

    private Task EndRunAsync(Run? expected)
    {
        Run? run;
        Task task;
        lock (_sync)
        {
            run = _run;
            if (run is null) return Task.CompletedTask;
            if (expected is not null && !ReferenceEquals(run, expected)) return Task.CompletedTask;
            if (run.EndTask is { IsCompleted: false }) return run.EndTask;
            run.StopRequested = true;
            run.StartCancellation.Cancel();
            run.State = SessionRunState.Stopping;
            run.MonitorCancellation.Cancel();
            task = run.EndTask = Task.Run(() => EndCoreAsync(run));
        }
        Publish(run);
        return task;
    }

    private async Task EndCoreAsync(Run run)
    {
        await run.StartFinished.Task.ConfigureAwait(false);
        foreach (var entry in run.Entries.Reverse())
        {
            if (!entry.Owned) continue;
            lock (_sync) { entry.State = SessionAppState.Closing; entry.Message = "Asking the app to close…"; }
            Publish(run);
            var closed = true;
            var requestedClose = false;
            string? error = null;
            foreach (var process in entry.Processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        requestedClose = true;
                        if (!await process.RequestCloseAsync(closeTimeout ?? TimeSpan.FromSeconds(3)).ConfigureAwait(false)) closed = false;
                    }
                }
                catch (Exception exception) { closed = false; error = exception.Message; }
            }
            lock (_sync)
            {
                entry.State = closed ? SessionAppState.Closed : SessionAppState.LeftOpen;
                entry.Message = closed ? (requestedClose ? "Closed by this Session" : "Already closed") :
                    "Still open. Save your work and close the app, then retry End." + (error is null ? "" : " " + error);
            }
            Publish(run);
        }
        lock (_sync)
        {
            if (run.Entries.Any(e => e.Owned && e.State == SessionAppState.LeftOpen))
            {
                run.State = SessionRunState.NeedsAttention;
                run.Message = "Some apps stayed open. Close them and retry, or finish the Session and leave them open.";
            }
            else Finish(run, run.Failed ? SessionRunState.Failed : SessionRunState.Completed,
                run.Failed ? "Session couldn't start. Cleanup is complete; already-open and untracked apps were left alone." :
                    "Session ended. Already-open and untracked apps were left alone.");
        }
        Publish(run);
    }

    /// <summary>Explicitly release cleanup ownership without stopping any remaining apps.</summary>
    public void LeaveAppsOpen()
    {
        Run? run;
        lock (_sync)
        {
            run = _run;
            if (run is null) return;
            if (run.State is SessionRunState.Starting or SessionRunState.Stopping)
                throw new InvalidOperationException("Wait for the current Session operation to finish.");
            foreach (var entry in run.Entries.Where(e => e.Owned && e.State != SessionAppState.Closed))
            {
                entry.State = SessionAppState.LeftOpen;
                entry.Message = "Left open by your choice";
                entry.Owned = false;
            }
            Finish(run, SessionRunState.Completed, "Session ended. Remaining apps were left open by your choice.");
        }
        Publish(run);
    }

    // Public for an explicit refresh and deterministic tests; the background monitor calls the same operation.
    public Task RefreshAsync()
    {
        Run? run;
        var end = false;
        lock (_sync)
        {
            run = _run;
            if (run is null || run.State != SessionRunState.Running) return Task.CompletedTask;
            foreach (var entry in run.Entries.Where(e => e.Processes.Count > 0))
            {
                try
                {
                    if (!entry.Processes.All(p => p.HasExited))
                    {
                        var message = entry.Processes.Select(p => p.TrackingMessage).FirstOrDefault(m => m is not null);
                        if (message is not null) entry.Message = message;
                        continue;
                    }
                    entry.State = SessionAppState.Exited;
                    entry.Message = "Tracked app exited · any separately opened copy stays independent";
                    if (entry.App.Id == run.Definition.MainAppId && !run.MainEndDismissed) end = true;
                }
                catch (Exception exception) { entry.Message = "Couldn't check this app: " + exception.Message; }
            }
            if (end) RequestEndConfirmation(run, "The main app exited. Save your work before confirming that the other apps can stop.");
        }
        Publish(run);
        return Task.CompletedTask;
    }

    private async Task MonitorAsync(Run run)
    {
        try
        {
            while (!run.MonitorCancellation.IsCancellationRequested)
            {
                await Task.Delay(1000, run.MonitorCancellation.Token).ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (run.MonitorCancellation.IsCancellationRequested) { }
    }

    private void Finish(Run run, SessionRunState state, string message)
    {
        run.MonitorCancellation.Cancel();
        run.State = state;
        run.Message = message;
        foreach (var process in run.Entries.SelectMany(e => e.Processes)) process.Dispose();
        _run = null;
    }

    private void Publish(Run run)
    {
        lock (_sync)
        {
            // A final event from an old operation must never overwrite a newer run.
            if (!ReferenceEquals(_latestRun, run)) return;
            _snapshot = new SessionRunSnapshot(run.Definition.Id, run.Definition.Name, run.State,
                run.Entries.Select(e => new SessionAppOutcome(e.App.Id, e.App.Name, e.State, e.Owned, e.Message)).ToArray(), run.Message,
                run.Id, run.StartupSucceeded);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Run(SessionDefinition definition)
    {
        public SessionDefinition Definition { get; } = definition;
        public Guid Id { get; } = Guid.NewGuid();
        public Entry[] Entries { get; } = definition.Apps.Select(app => new Entry(app)).ToArray();
        public SessionRunState State = SessionRunState.Starting;
        public string Message = definition.LaunchMode == SessionLaunchMode.Together ? "Opening your apps together…" : "Opening your apps in order…";
        public bool StartupSucceeded;
        public bool StopRequested;
        public bool Failed;
        public bool MainEndDismissed;
        public Task? EndTask;
        public TaskCompletionSource StartFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource StartCancellation { get; } = new();
        public CancellationTokenSource MonitorCancellation { get; } = new();
    }

    private sealed class Entry(StartProcessAction app)
    {
        public StartProcessAction App { get; } = app;
        public IReadOnlyList<ITrackedProcess> Processes = [];
        public bool Owned;
        public SessionAppState State = SessionAppState.Waiting;
        public string Message = "Waiting";
    }
}
