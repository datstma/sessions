using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sessions.App.Services;
using Sessions.Core;

namespace Sessions.App.ViewModels;

public partial class SessionAppRow(int order, string name, string executablePath, string role,
    IAppPresenceService? presenceService = null, IIndividualAppLauncher? appLauncher = null,
    StartProcessAction? definition = null) : ViewModelBase
{
    private DateTimeOffset? _pendingUntil;
    private bool _launchInProgress;
    public bool IsOpening => _launchInProgress || (IsStarting && _pendingUntil > DateTimeOffset.UtcNow);
    public int Order { get; } = order;
    public string Name { get; } = name;
    public string ExecutablePath { get; } = executablePath;
    public string Role { get; } = role;
    [ObservableProperty] private AppPresence _presence = AppPresence.Checking;
    [ObservableProperty] private string? _focusMessage;
    [ObservableProperty] private bool _isStarting;
    [ObservableProperty] private bool _allowLaunch = true;
    [ObservableProperty] private string? _runMessage;
    public bool HasRunMessage => RunMessage is not null;
    public bool IsRunning => Presence is AppPresence.Window or AppPresence.Background;
    public bool HasWindow => Presence == AppPresence.Window;
    public bool HasLaunchAction => !HasWindow && appLauncher is not null && (Presence == AppPresence.NotRunning || IsStarting);
    public bool HasPassiveStatus => !HasWindow && !HasLaunchAction;
    public bool HasFocusMessage => FocusMessage is not null;
    public string StatusLabel => IsStarting ? "Starting…" : Presence switch
    {
        AppPresence.Window => "Running",
        AppPresence.Background => "Running · no window",
        AppPresence.NotRunning => "Not running",
        AppPresence.Checking => "Checking…",
        _ => "Status unavailable"
    };
    public string StatusHint => IsStarting ? $"Waiting for {Name} to open." : Presence switch
    {
        AppPresence.Window => $"Bring {Name} to the front",
        AppPresence.Background => $"{Name} is running in the background with no window to show.",
        AppPresence.NotRunning => $"Start {Name}",
        AppPresence.Checking => $"Checking whether {Name} is running.",
        _ => $"The running status of {Name} could not be checked. Sessions will retry automatically."
    };

    private bool CanFocus() => !IsStarting && Presence == AppPresence.Window && presenceService is not null;
    private bool CanLaunch() => AllowLaunch && !IsStarting && Presence == AppPresence.NotRunning && appLauncher is not null && definition is not null;

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (!CanLaunch()) return;
        _launchInProgress = true;
        IsStarting = true;
        FocusMessage = null;
        try
        {
            var result = await appLauncher!.LaunchAsync(definition!);
            _pendingUntil = result.PendingUntil;
            Presence = result.Presence;
            IsStarting = _pendingUntil is not null;
            if (result.Presence == AppPresence.Window) await FocusAsync();
            else if (result.Presence == AppPresence.Background)
                FocusMessage = $"{Name} is already running in the background with no window to show.";
        }
        catch (Exception exception) when (IsObservationError(exception))
        {
            _pendingUntil = null;
            IsStarting = false;
            FocusMessage = $"Couldn't start {Name}. {exception.Message}";
        }
        finally { _launchInProgress = false; }
        await RefreshPresenceAsync();
    }

    public void ApplyPresence(AppPresence presence)
    {
        Presence = presence;
        if (!IsStarting || _launchInProgress) return;
        if (presence is AppPresence.Window or AppPresence.Background || _pendingUntil <= DateTimeOffset.UtcNow)
        {
            IsStarting = false;
            _pendingUntil = null;
            if (presence == AppPresence.NotRunning)
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
        if (presenceService is null) return;
        // A failed follow-up check must not misreport a successful launch or focus.
        try
        {
            var snapshot = await presenceService.GetPresenceAsync([ExecutablePath]);
            ApplyPresence(snapshot.TryGetValue(ExecutablePath, out var presence) ? presence : AppPresence.Unknown);
        }
        catch (Exception exception) when (IsObservationError(exception)) { ApplyPresence(AppPresence.Unknown); }
    }

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
}
