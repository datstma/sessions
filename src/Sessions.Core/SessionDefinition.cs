namespace Sessions.Core;

public enum SessionLaunchMode { InOrder, Together }
public enum AppReadiness { LaunchCompleted, ProcessRunning, WindowAppeared }
public enum StartupFocus { Unchanged, Sessions, App }

/// <summary>A saved context. Runtime process state is deliberately kept separate.</summary>
public sealed record SessionDefinition(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyList<StartProcessAction> Apps,
    Guid? MainAppId = null,
    SessionLaunchMode LaunchMode = SessionLaunchMode.InOrder,
    int PauseBetweenAppsSeconds = 0,
    StartupFocus FocusAfterStartup = StartupFocus.Unchanged,
    Guid? FocusAppId = null,
    AudioDeviceChoice? OutputAudioDevice = null,
    AudioDeviceChoice? InputAudioDevice = null)
{
    public void Validate()
    {
        foreach (var device in new[] { OutputAudioDevice, InputAudioDevice })
            if (device is not null && (string.IsNullOrWhiteSpace(device.Id) || string.IsNullOrWhiteSpace(device.Name)))
                throw new ArgumentException("An audio selection needs a device identity and name.");
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Description is null || Apps is null)
            throw new ArgumentException("A Session needs an identity, a name, and an app list.");
        if (!Enum.IsDefined(LaunchMode) || !Enum.IsDefined(FocusAfterStartup) || PauseBetweenAppsSeconds is < 0 or > 300)
            throw new ArgumentException("Choose a valid startup mode and a pause between 0 and 300 seconds.");

        var appIds = new HashSet<Guid>();
        foreach (var app in Apps)
        {
            if (app is null || app.Id == Guid.Empty || !appIds.Add(app.Id) ||
                string.IsNullOrWhiteSpace(app.Name) || string.IsNullOrWhiteSpace(app.ExecutablePath) ||
                app.Arguments is null || app.WorkingDirectory is null)
                throw new ArgumentException("Every app needs a unique identity, a name, and an executable path.");
            if (!Enum.IsDefined(app.Readiness) || app.ReadinessTimeoutSeconds is < 1 or > 600 ||
                app.PauseAfterSeconds is < 0 or > 300)
                throw new ArgumentException("App startup waits need a timeout from 1 to 600 seconds and a pause from 0 to 300 seconds.");
        }

        if (MainAppId is { } mainAppId && !appIds.Contains(mainAppId))
            throw new ArgumentException("The app that ends the Session must be in its app list.");
        if (FocusAfterStartup == StartupFocus.App && (FocusAppId is not { } focusId || !appIds.Contains(focusId)))
            throw new ArgumentException("Choose an app to focus after startup.");
        if (FocusAppId is { } id && !appIds.Contains(id))
            throw new ArgumentException("The app to focus must be in this Session.");
    }
}

public sealed record StartProcessAction(
    Guid Id,
    string Name,
    string ExecutablePath,
    string Arguments = "",
    string WorkingDirectory = "",
    bool RunAsAdministrator = false,
    AppReadiness Readiness = AppReadiness.LaunchCompleted,
    int ReadinessTimeoutSeconds = 30,
    int? PauseAfterSeconds = null,
    bool AllowForceQuit = false);
