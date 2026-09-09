namespace Sessions.Core;

public enum AudioFlow { Output, Input }
public enum AudioRole { Console, Multimedia, Communications }
public sealed record AudioDeviceChoice(string Id, string Name);
public sealed record AudioDevice(string Id, string Name, AudioFlow Flow);

public interface IAudioDeviceService
{
    Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<string?> GetDefaultAsync(AudioFlow flow, AudioRole role);
    Task SetDefaultAsync(AudioFlow flow, AudioRole role, string deviceId);
}

// A run owns changes to individual default-device roles, never the user's entire audio configuration.
internal sealed class SessionAudio(IAudioDeviceService? devices)
{
    private readonly List<Change> _changes = [];
    private readonly List<Change> _captured = [];
    public bool HasPendingChanges => _changes.Count > 0;
    public string? Message { get; private set; }

    public async Task ApplyAsync(SessionDefinition definition, CancellationToken cancellationToken)
    {
        var choices = new[] { (AudioFlow.Output, definition.OutputAudioDevice), (AudioFlow.Input, definition.InputAudioDevice) }
            .Where(pair => pair.Item2 is not null).ToArray();
        if (choices.Length == 0) return;
        if (devices is null) throw new InvalidOperationException("Audio device switching is unavailable.");
        var available = await devices.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        var planned = new List<Change>();
        // Validate all targets and capture all previous roles before changing any default.
        foreach (var (flow, choice) in choices)
        {
            if (!available.Any(device => device.Flow == flow && device.Id == choice!.Id))
                throw new InvalidOperationException($"Audio device '{choice!.Name}' is unavailable. Connect it or edit this Session's audio choices.");
            foreach (var role in Enum.GetValues<AudioRole>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var previous = await devices.GetDefaultAsync(flow, role).ConfigureAwait(false);
                if (previous is not null) _captured.Add(new(flow, role, previous, choice!.Id));
                if (previous == choice!.Id) continue;
                if (previous is null || !available.Any(device => device.Flow == flow && device.Id == previous))
                    throw new InvalidOperationException($"The previous {flow.ToString().ToLowerInvariant()} device cannot be restored. Choose a default in Windows before starting.");
                planned.Add(new(flow, role, previous, choice.Id));
            }
        }
        // Windows can change Console and Multimedia together. Retain all captured roles before
        // the first setter so its side effects are owned and recoverable even if a later call fails.
        _changes.AddRange(planned);
        foreach (var change in planned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await devices.GetDefaultAsync(change.Flow, change.Role).ConfigureAwait(false);
            if (current == change.Applied) continue; // A preceding role switch already applied this one.
            if (current != change.Previous)
                throw new InvalidOperationException("Audio defaults changed while this Session was starting. Your later choice was kept; retry when ready.");
            await devices.SetDefaultAsync(change.Flow, change.Role, change.Applied).ConfigureAwait(false);
            if (await devices.GetDefaultAsync(change.Flow, change.Role).ConfigureAwait(false) != change.Applied)
                throw new InvalidOperationException("Windows did not keep the selected audio device. Check your audio settings and retry.");
        }
        Message = "Session audio devices applied. Previous defaults will be restored on End Session.";
    }

    public async Task<bool> RestoreAsync()
    {
        if (_changes.Count == 0) return true;
        var errors = new List<string>();
        var keptExternalChoice = false;
        var unverifiableFlows = new HashSet<AudioFlow>();
        // Never restore one ordinary role over a later choice in its linked role.
        foreach (var group in _changes.Where(change => change.Role != AudioRole.Communications).GroupBy(change => change.Flow).ToArray())
        {
            try
            {
                var changedExternally = false;
                foreach (var change in _captured.Where(change => change.Flow == group.Key && change.Role != AudioRole.Communications))
                {
                    var current = await devices!.GetDefaultAsync(change.Flow, change.Role).ConfigureAwait(false);
                    changedExternally |= current != change.Applied && current != change.Previous;
                }
                if (!changedExternally) continue;
                foreach (var change in group) _changes.Remove(change);
                keptExternalChoice = true;
            }
            catch (Exception exception)
            {
                // Leave both roles pending when their current state cannot be verified.
                errors.Add($"{group.Key}: {exception.Message}");
                unverifiableFlows.Add(group.Key);
            }
        }
        foreach (var change in _changes.ToArray().Reverse())
        {
            if (change.Role != AudioRole.Communications && unverifiableFlows.Contains(change.Flow)) continue;
            try
            {
                var current = await devices!.GetDefaultAsync(change.Flow, change.Role).ConfigureAwait(false);
                if (current == change.Applied)
                {
                    await devices.SetDefaultAsync(change.Flow, change.Role, change.Previous).ConfigureAwait(false);
                    if (await devices.GetDefaultAsync(change.Flow, change.Role).ConfigureAwait(false) != change.Previous)
                        throw new InvalidOperationException("Windows did not restore the previous device.");
                }
                else keptExternalChoice = true; // A later user/OS/app selection takes precedence.
                _changes.Remove(change);
            }
            catch (Exception exception) { errors.Add($"{change.Flow} ({change.Role}): {exception.Message}"); }
        }
        Message = errors.Count > 0
            ? "Audio restoration needs attention. Retry End Session, or choose your defaults in Windows and retry. " + string.Join(" ", errors)
            : keptExternalChoice ? "Audio cleanup complete. Later device changes were kept." : "Previous audio devices restored.";
        return errors.Count == 0;
    }

    private sealed record Change(AudioFlow Flow, AudioRole Role, string Previous, string Applied);
}
