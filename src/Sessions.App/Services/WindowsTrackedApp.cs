using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sessions.Core;

namespace Sessions.App.Services;

/// <summary>Tracks an exact launch and verified same-executable self-restarts, including elevation.</summary>
internal sealed class WindowsTrackedApp(WindowsProcessIdentity initial, bool owned) : ITrackedProcess
{
    // Keep ancestor handles until the run ends; a recyclable numeric parent ID is never enough.
    private readonly List<WindowsProcessIdentity> _lineage = [initial];
    private WindowsProcessIdentity Current => _lineage[^1];
    private bool _exitConfirmed;
    public string? TrackingMessage => _lineage.Count > 1
        ? "Tracking restarted app · stops when you confirm End Session"
        : null;

    public bool HasExited
    {
        get
        {
            if (_exitConfirmed) return true;
            while (Current.HasExited)
            {
                if (!owned) return _exitConfirmed = true;
                var parent = Current;
                var candidates = new List<WindowsProcessIdentity>();
                try
                {
                    foreach (var entry in WindowsProcessIdentity.Snapshot().Where(e => e.Parent == parent.Id &&
                                 string.Equals(e.Name, Path.GetFileName(parent.Path), StringComparison.OrdinalIgnoreCase)))
                    {
                        WindowsProcessIdentity child;
                        try { child = WindowsProcessIdentity.Open(entry.Id); }
                        catch (Win32Exception exception) when (exception.NativeErrorCode == 87) { continue; } // Already gone.
                        if (child.SessionId == parent.SessionId && child.Created >= parent.Created && child.Created <= parent.ExitedAt &&
                            string.Equals(child.Path, parent.Path, StringComparison.OrdinalIgnoreCase)) candidates.Add(child);
                        else child.Dispose();
                    }
                    // Multiple same-executable children (e.g. browser workers) aren't a verified single restart.
                    if (candidates.Count > 1)
                        throw new InvalidOperationException("This app handed off to several processes. Close it manually; Sessions won't guess which one to manage.");
                    if (candidates.Count == 0) return _exitConfirmed = true;
                    if (_lineage.Count >= 16) throw new InvalidOperationException("This app restarted too many times. Close it manually.");
                    _lineage.Add(candidates[0]);
                    candidates.Clear(); // Ownership transferred to retained lineage.
                }
                finally { foreach (var candidate in candidates) candidate.Dispose(); }
            }
            return false;
        }
    }

    public async Task<bool> RequestCloseAsync(TimeSpan timeout)
    {
        // A restart may complete while a close is in flight. Resolve it before declaring success.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (HasExited) return true;
            if (!await WindowsProcessCleanup.CloseAsync(Current, timeout, force: true).ConfigureAwait(false)) return false;
        }
        return HasExited;
    }

    public void Dispose() { foreach (var process in _lineage) process.Dispose(); }
}
