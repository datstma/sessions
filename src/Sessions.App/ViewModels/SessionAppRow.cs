using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sessions.App.Services;
using Sessions.Core;

namespace Sessions.App.ViewModels;

public partial class SessionAppRow(int order, string name, string executablePath, string role,
    IAppPresenceService? presenceService = null, IIndividualAppLauncher? appLauncher = null,
    StartProcessAction? definition = null, ISessionPluginHost? plugins = null) : ViewModelBase
{
    private DateTimeOffset? _pendingUntil;
    private bool _launchInProgress;
    public bool IsOpening => _launchInProgress || (IsStarting && _pendingUntil > DateTimeOffset.UtcNow);
    public bool IsPlugin => definition?.Plugin is not null;
    internal StartProcessAction? Definition => definition;
    public string CloseHint => $"Close {Name}";
    public int Order { get; } = order;
    public string Name { get; } = name;
    public string Initial => System.Globalization.StringInfo.GetNextTextElement(Name).ToUpperInvariant();
    public string PathSummary => IsPlugin ? $"{Order} · Plugin app · " + (definition!.Plugin!.CloseOnEnd ? "close with Session when tracked" : "close manually") : $"{Order} · {ExecutablePath}";
    public string ExecutablePath { get; } = executablePath;
    public string Role { get; } = role;
    public bool HasRole => Role.Length > 0;
    [ObservableProperty] private AppPresence _presence = AppPresence.Checking;
    [ObservableProperty] private string? _focusMessage;
    [ObservableProperty] private string? _launchMessage;
    [ObservableProperty] private string? _closeMessage;
    [ObservableProperty] private bool _isCloseError;
    [ObservableProperty] private bool _isStarting;
    [ObservableProperty] private bool _allowLaunch = true;
    [ObservableProperty] private string? _runMessage;
    public bool HasRunMessage => RunMessage is not null;
    public bool IsRunning => Presence is AppPresence.Window or AppPresence.Background;
    public bool HasWindow => !IsPlugin && Presence == AppPresence.Window;
    public bool HasLaunchAction => !HasWindow && !IsRunning && appLauncher is not null && (IsPlugin || Presence == AppPresence.NotRunning || IsStarting);
    public bool HasPassiveStatus => !HasWindow && !HasLaunchAction;
    public bool HasFocusMessage => FocusMessage is not null;
    public bool HasLaunchMessage => LaunchMessage is not null;
    public bool HasCloseMessage => CloseMessage is not null;
    public string StatusLabel => IsPlugin ? (IsRunning ? "Running" : IsStarting ? "Launch requested" : "Launch via plugin") : IsStarting ? "Starting…" : Presence switch
    {
        AppPresence.Window => "Running",
        AppPresence.Background => "Running · no window",
        AppPresence.NotRunning => "Not running",
        AppPresence.Checking => "Checking…",
        _ => "Status unavailable"
    };
    public string StatusHint => IsPlugin ? (IsRunning ? $"Steam reports {Name} is running. Running status does not grant cleanup ownership." :
        $"Request {Name} through its plugin. Sessions checks the provider's running status.") : IsStarting ? $"Waiting for {Name} to open." : Presence switch
    {
        AppPresence.Window => $"Bring {Name} to the front",
        AppPresence.Background => $"{Name} is running in the background with no window to show.",
        AppPresence.NotRunning => $"Start {Name}",
        AppPresence.Checking => $"Checking whether {Name} is running.",
        _ => $"The running status of {Name} could not be checked. Sessions will retry automatically."
    };

    private bool CanFocus() => !IsPlugin && !IsStarting && Presence == AppPresence.Window && presenceService is not null;
    private bool CanLaunch() => AllowLaunch && !IsStarting && !IsRunning && (IsPlugin || Presence == AppPresence.NotRunning) && appLauncher is not null && definition is not null;

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (!CanLaunch()) return;
        _launchInProgress = true;
        IsStarting = true;
        FocusMessage = null;
        LaunchMessage = null;
        SetCloseFeedback(null);
        try
        {
            var result = await appLauncher!.LaunchAsync(definition!);
            LaunchMessage = result.Message;
            _pendingUntil = result.PendingUntil;
            Presence = result.Presence;
            IsStarting = _pendingUntil is not null;
            if (result.Presence == AppPresence.Window) await FocusAsync();
            else if (!IsPlugin && result.Presence == AppPresence.Background)
                LaunchMessage = $"{Name} is already running in the background with no window to show.";
        }
        catch (Exception exception) when (IsObservationError(exception))
        {
            _pendingUntil = null;
            IsStarting = false;
            LaunchMessage = null;
            FocusMessage = $"Couldn't start {Name}. {exception.Message}";
        }
        finally { _launchInProgress = false; }
        await RefreshPresenceAsync();
    }

    public void ApplyPresence(AppPresence presence)
    {
        ApplyPresenceCore(IsPlugin ? AppPresence.Unknown : presence);
    }

    private void ApplyPresenceCore(AppPresence presence)
    {
        Presence = presence;
        // A provider can report running after the repeated-click grace period has expired.
        // Clear only informational launch feedback, independently of the pending state.
        if (IsRunning) LaunchMessage = null;
        // Only an observed stop resolves close feedback; unknown status is not an exit.
        if (presence == AppPresence.NotRunning) SetCloseFeedback(null);
        if (!IsStarting || _launchInProgress) return;
        if (presence is AppPresence.Window or AppPresence.Background || _pendingUntil <= DateTimeOffset.UtcNow)
        {
            IsStarting = false;
            _pendingUntil = null;
            if (!IsPlugin && presence == AppPresence.NotRunning)
                FocusMessage = $"{Name} hasn't appeared as running. You can try again or check its settings in Edit Session.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanFocus))]
    private async Task FocusAsync()
    {
        if (!CanFocus()) return;
        FocusMessage = null;
        try
        {
            var result = await presenceService!.FocusAsync(ExecutablePath);
            FocusMessage = result switch
            {
                AppFocusResult.Focused => null,
                AppFocusResult.NoWindow => $"{Name} no longer has a window to show.",
                _ => $"Couldn't bring {Name} forward. Select it from the taskbar or try again."
            };
        }
        catch (Exception exception) when (IsObservationError(exception))
        {
            Presence = AppPresence.Unknown;
            FocusMessage = $"Couldn't reach {Name}. Try selecting it from the taskbar.";
            return;
        }
        await RefreshPresenceAsync();
    }

    private async Task RefreshPresenceAsync()
    {
        if (IsPlugin) { await RefreshPluginPresenceAsync(); return; }
        if (presenceService is null) return;
        // A failed follow-up check must not misreport a successful launch or focus.
        try
        {
            var snapshot = await presenceService.GetPresenceAsync([ExecutablePath]);
            ApplyPresence(snapshot.TryGetValue(ExecutablePath, out var presence) ? presence : AppPresence.Unknown);
        }
        catch (Exception exception) when (IsObservationError(exception)) { ApplyPresence(AppPresence.Unknown); }
    }

    public async Task RefreshPluginPresenceAsync(CancellationToken cancellationToken = default)
    {
        if (definition?.Plugin is not { } reference) return;
        try
        {
            var presence = plugins is null ? PluginAppPresence.Unknown :
                await plugins.Capture().GetPresenceAsync(reference, cancellationToken);
            if (!cancellationToken.IsCancellationRequested) ApplyPresenceCore(presence switch
            {
                PluginAppPresence.Running => AppPresence.Background,
                PluginAppPresence.NotRunning => AppPresence.NotRunning,
                _ => AppPresence.Unknown
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (IsObservationError(exception)) { ApplyPresenceCore(AppPresence.Unknown); }
    }

    public void SetCloseFeedback(string? message, bool isError = false)
    {
        IsCloseError = message is not null && isError;
        CloseMessage = message;
    }

    partial void OnCloseMessageChanged(string? value) => OnPropertyChanged(nameof(HasCloseMessage));

    internal static bool IsObservationError(Exception exception) => exception is Win32Exception or
        InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException;

    partial void OnPresenceChanged(AppPresence value) => RefreshStatus();
    partial void OnIsStartingChanged(bool value) => RefreshStatus();
    partial void OnAllowLaunchChanged(bool value) => LaunchCommand.NotifyCanExecuteChanged();
    partial void OnRunMessageChanged(string? value) => OnPropertyChanged(nameof(HasRunMessage));

    private void RefreshStatus()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(HasWindow));
        OnPropertyChanged(nameof(HasLaunchAction));
        OnPropertyChanged(nameof(HasPassiveStatus));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusHint));
        FocusCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
    }
    partial void OnFocusMessageChanged(string? value) => OnPropertyChanged(nameof(HasFocusMessage));
    partial void OnLaunchMessageChanged(string? value) => OnPropertyChanged(nameof(HasLaunchMessage));
}
