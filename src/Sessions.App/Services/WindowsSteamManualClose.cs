using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Sessions.Core;
using Sessions.Plugins.Steam;

namespace Sessions.App.Services;

internal static class WindowsSteamManualClose
{
    public static IPreparedAppClose Prepare(string installation, string appDirectory, uint appId, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(Path.Combine(installation, "logs", "gameprocess_log.txt"), FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        const int limit = 1024 * 1024;
        var start = Math.Max(0, stream.Length - limit);
        stream.Position = start;
        var bytes = new byte[(int)(stream.Length - start)];
        var count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        var text = Encoding.UTF8.GetString(bytes, 0, count);
        if (start > 0) text = text[(text.IndexOf('\n') + 1)..];
        var last = text.LastIndexOf('\n');
        text = last < 0 ? "" : text[..(last + 1)];
        var root = Path.GetFullPath(appDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var self = Process.GetCurrentProcess();
        var retained = new List<ITrackedProcess>();
        try
        {
            foreach (var entry in SteamProcessLog.ReadCurrentProcesses(text, appId).Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.ProcessId == self.Id) continue;
                WindowsProcessIdentity? identity = null;
                try
                {
                    identity = WindowsProcessIdentity.Open(entry.ProcessId);
                    var created = DateTime.FromFileTimeUtc(identity.Created);
                    if (identity.SessionId != self.SessionId || identity.HasExited ||
                        !identity.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                        created > entry.AddedAtUtc.AddSeconds(1) || created < entry.AddedAtUtc.AddSeconds(-10)) continue;
                    retained.Add(new WindowsTrackedApp(identity, owned: false));
                    identity = null;
                }
                catch (Win32Exception) { } // Unknown or reused identities never become close targets.
                finally { identity?.Dispose(); }
            }
            if (retained.Count == 0) throw new InvalidOperationException("Steam's current app processes couldn't be verified. Close this app from its own window or Steam.");
            return new PreparedAppClose(retained);
        }
        catch { foreach (var process in retained) process.Dispose(); throw; }
    }
}
