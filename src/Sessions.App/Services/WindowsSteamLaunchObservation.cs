using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Sessions.Plugins.Steam;

namespace Sessions.App.Services;

/// <summary>Retains the pre-launch log file across rotation and reads only newly appended complete lines.</summary>
internal sealed class WindowsSteamLaunchObservation : ISteamLaunchObservation
{
    private readonly FileStream _log;
    private readonly StreamReader _reader;
    private string _partial = "";
    private int _characters;
    public uint SessionId { get; }
    public bool HasExistingAppProcesses { get; }
    public IReadOnlySet<int> ExistingProcessIds { get; }

    public WindowsSteamLaunchObservation(string installation, string appDirectory)
    {
        using var self = Process.GetCurrentProcess();
        SessionId = (uint)self.SessionId;
        ExistingProcessIds = WindowsProcessIdentity.Snapshot().Select(process => process.Id).ToHashSet();
        var prefix = Path.GetFullPath(appDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var id in ExistingProcessIds)
        {
            try
            {
                using var identity = WindowsProcessIdentity.Open(id);
                if (identity.SessionId == SessionId && !identity.HasExited && identity.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                { HasExistingAppProcesses = true; break; }
            }
            catch (Win32Exception) { } // Inaccessible system processes grant no ownership; Steam state must also be known clear.
        }
        _log = new FileStream(Path.Combine(installation, "logs", "gameprocess_log.txt"), FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        _log.Position = _log.Length;
        _reader = new StreamReader(_log, Encoding.UTF8, false, 4096, leaveOpen: true);
    }

    public IReadOnlyList<SteamProcessEvent> ReadAddedProcesses(uint appId)
    {
        if (_log.Length < _log.Position || _log.Length - _log.Position > 1024 * 1024)
            throw new IOException("Steam's process log changed unexpectedly.");
        var buffer = new char[8192];
        var added = new StringBuilder(_partial);
        int count;
        while ((count = _reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            _characters += count;
            if (_characters > 1024 * 1024) throw new IOException("Steam's process log exceeded the launch tracking limit.");
            added.Append(buffer, 0, count);
        }
        var text = added.ToString();
        var last = text.LastIndexOf('\n');
        _partial = text[(last + 1)..];
        return last < 0 ? [] : SteamProcessLog.ReadAdditions(text[..(last + 1)], appId);
    }

    public SteamProcessCapture? Capture(int processId)
    {
        try
        {
            var identity = WindowsProcessIdentity.Open(processId);
            // owned:false disables later self-restart adoption; cleanup still acts on this exact identity.
            return new(identity.Path, identity.SessionId, identity.Created, new WindowsTrackedApp(identity, owned: false));
        }
        catch (Win32Exception) { return null; }
    }

    public void Dispose() { _reader.Dispose(); _log.Dispose(); }
}
