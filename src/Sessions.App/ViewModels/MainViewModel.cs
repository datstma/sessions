using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sessions.Core;
using Sessions.App.Services;
using Avalonia.Threading;

namespace Sessions.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly ISessionStore store;
    private readonly IAppPresenceService? presenceService;
    private readonly IIndividualAppLauncher? appLauncher;
    private readonly SessionRunner? runner;
    private readonly IStartupFocusService? startupFocus;
    private readonly IAudioDeviceService? audioDevices;
    private CancellationTokenSource _startupFocusCancellation = new();
    private Guid? _focusRunId;
    private bool _disposed;

    public MainViewModel(ISessionStore store, IAppPresenceService? presenceService = null,
        IIndividualAppLauncher? appLauncher = null, SessionRunner? runner = null, IStartupFocusService? startupFocus = null, IAudioDeviceService? audioDevices = null)
    {
        this.store = store;
        this.presenceService = presenceService;
        this.appLauncher = appLauncher;
        this.runner = runner;
        this.startupFocus = startupFocus;
        this.audioDevices = audioDevices;
        if (runner is not null) runner.Changed += RuntimeChanged;
    }

    [ObservableProperty] private SessionRunSnapshot? _runtime;
    [ObservableProperty] private string? _startupFocusMessage;
    public bool HasStartupFocusMessage => StartupFocusMessage is not null;
    partial void OnStartupFocusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStartupFocusMessage));
    [ObservableProperty] private bool _isCloseConfirmation;
    [ObservableProperty] private bool _isDraftCloseConfirmation;
    private bool _savingEditor;
    private bool _closeAfterSave;
    public string DraftCloseTitle => string.IsNullOrWhiteSpace(Editor?.Name)
        ? "Save this Session?" : $"Save changes to “{Editor.Name.Trim()}”?";
    public string DraftCloseMessage => IsBusy ? "Saving your changes before closing…" :
        "You have unsaved changes. Save them before closing, or discard them.";
    public bool DraftNeedsCorrection => Editor?.CanSave == false;
    private bool CanResolveDraftClose() => IsDraftCloseConfirmation && Editor is not null && !IsBusy;
    private bool CanSaveDraftAndClose() => CanResolveDraftClose() && CanPersistEditor();
    [RelayCommand(CanExecute = nameof(CanResolveDraftClose))]
    private void KeepEditing()
    {
        if (!CanResolveDraftClose()) return;
        _closeAfterSave = false;
        IsDraftCloseConfirmation = false;
    }
    [RelayCommand(CanExecute = nameof(CanResolveDraftClose))]
    private void DiscardDraftAndClose()
    {
        if (!CanResolveDraftClose()) return;
        Editor = null;
        ErrorMessage = null;
        ContinueWindowClose();
    }
    [RelayCommand(CanExecute = nameof(CanSaveDraftAndClose))]
    private async Task SaveDraftAndCloseAsync()
    {
        if (!CanSaveDraftAndClose()) return;
        _closeAfterSave = true;
        await SaveEditorAsync();
    }
    private void ContinueWindowClose()
    {
        // Establish the active-run dialog before removing the draft modal, so an automatic End
        // request cannot appear between resolving edits and choosing what to do with running apps.
        if (HasActiveRun) IsCloseConfirmation = true;
        IsDraftCloseConfirmation = false;
        if (!HasActiveRun) CloseRequested?.Invoke(this, EventArgs.Empty);
    }
    partial void OnIsDraftCloseConfirmationChanged(bool value) => Refresh();
    [ObservableProperty] private Guid? _pendingEndSessionId;
    [ObservableProperty] private CleanupAppViewModel? _pendingForceQuit;
    [ObservableProperty] private string? _cleanupFocusMessage;
    public ObservableCollection<CleanupAppViewModel> CleanupApps { get; } = [];
    public bool IsForceQuitConfirmation => PendingForceQuit is not null;
    public string ForceQuitTitle => $"Force quit “{PendingForceQuit?.Name}”?";
    public string ForceQuitDescription => $"This force quits {PendingForceQuit?.Name} in “{Runtime?.Name}”. Unsaved changes may be lost.";
    public string AutomaticForceQuitWarning => "Automatic force quit is enabled for: " + string.Join(", ", Runtime?.Apps
        .Where(a => a.AllowForceQuit && (a.Owned || a.State is SessionAppState.Waiting or SessionAppState.Starting))
        .Select(a => a.Name) ?? []) + ". Unsaved changes in these apps may be lost.";
    public bool HasAutomaticForceQuit => Runtime?.Apps.Any(a => a.AllowForceQuit &&
        (a.Owned || a.State is SessionAppState.Waiting or SessionAppState.Starting)) == true;
    private bool IsCurrentCleanupApp(CleanupAppViewModel? app) => app is not null && Runtime is { State: SessionRunState.NeedsAttention } run &&
        run.RunId == app.RunId && run.Apps.Any(a => a.AppId == app.AppId && a.Owned && a.State == SessionAppState.LeftOpen);
    private bool CanActOnCleanupApp(CleanupAppViewModel? app) => CanBrowse && IsCurrentCleanupApp(app);
    private bool CanFocusCleanupApp(CleanupAppViewModel? app) => presenceService is not null && CanActOnCleanupApp(app);
    [RelayCommand(CanExecute = nameof(CanActOnCleanupApp))]
    private void RequestForceQuit(CleanupAppViewModel? app)
    {
        if (CanActOnCleanupApp(app)) PendingForceQuit = app;
    }
    private bool CanConfirmForceQuit() => IsForceQuitConfirmation && IsCurrentCleanupApp(PendingForceQuit);
    [RelayCommand(CanExecute = nameof(CanConfirmForceQuit))]
    private async Task ConfirmForceQuitAsync()
    {
        if (!CanConfirmForceQuit()) return;
        var app = PendingForceQuit!;
        var stopping = runner!.ForceQuitAppAsync(app.RunId, app.AppId);
        PendingForceQuit = null;
        await stopping;
        ApplyRuntime();
    }
    [RelayCommand]
    private void CancelForceQuit() => PendingForceQuit = null;
    [RelayCommand(CanExecute = nameof(CanFocusCleanupApp))]
    private async Task FocusCleanupAppAsync(CleanupAppViewModel? app)
    {
        if (!CanFocusCleanupApp(app)) return;
        try
        {
            var result = await presenceService!.FocusAsync(app!.ExecutablePath);
            if (IsCurrentCleanupApp(app)) CleanupFocusMessage = result switch
            {
                AppFocusResult.NoWindow => $"{app.Name} has no window to bring forward. Check the taskbar or notification area.",
                AppFocusResult.Denied => $"Windows couldn't bring {app.Name} forward. Select it from the taskbar.",
                _ => null
            };
        }
        catch (Exception exception) when (SessionAppRow.IsObservationError(exception))
        { if (IsCurrentCleanupApp(app)) CleanupFocusMessage = "Couldn't bring the app forward. " + exception.Message; }
    }
    partial void OnPendingForceQuitChanged(CleanupAppViewModel? value) => Refresh();
    public bool IsEndConfirmation => PendingEndSessionId is not null;
    public string EndConfirmationTitle => $"End “{Runtime?.Name}”?";
    public string[] AppsToStop => Runtime?.Apps.Where(app => app.Owned && app.State is not (SessionAppState.Closed or SessionAppState.Exited))
        .Select(app => app.Name).ToArray() ?? [];
    public string[] AppsToKeep => Runtime?.Apps.Where(app => !app.Owned && app.State == SessionAppState.AlreadyRunning)
        .Select(app => app.Name).ToArray() ?? [];
    public bool HasAppsToKeep => AppsToKeep.Length > 0;
    public bool HasAppsToStop => AppsToStop.Length > 0;
    private bool _closeAfterEnd;
    public event EventHandler? CloseRequested;
    public bool HasActiveRun => Runtime?.IsActive == true;
    public bool HasRuntime => Runtime is not null;
    public bool NeedsCleanup => Runtime?.State == SessionRunState.NeedsAttention;
    public bool SelectedIsActive => HasActiveRun && SelectedSession?.Definition.Id == Runtime?.SessionId;
    public bool ShowStart => !SelectedIsActive;
    public bool ShowActiveNavigation => HasActiveRun && !SelectedIsActive;
    public bool IsMainContentEnabled => !IsConfirmingDelete && !IsCloseConfirmation && !IsEndConfirmation && !IsForceQuitConfirmation && !IsDraftCloseConfirmation;
    public string RuntimeTitle => Runtime is null ? "" : $"{Runtime.Name} · {Runtime.State switch
    {
        SessionRunState.Starting => "Starting…", SessionRunState.Running => "Active",
        SessionRunState.Stopping => "Ending…", SessionRunState.NeedsAttention => "Needs attention",
        SessionRunState.AwaitingEndConfirmation => "Ready to end",
        SessionRunState.Failed => "Couldn't start", _ => "Ended"
    }}";
    public string EndLabel => NeedsCleanup ? "Retry End Session" : Runtime?.State == SessionRunState.Stopping ? "Ending…" : "End Session";
    public string StartHint => HasActiveRun ? "End the active Session before starting another."
        : HasOpeningApps ? "Wait for your app to finish opening before starting a Session."
        : "Apps already open will stay open when this Session ends.";
    public string AppInteractionHint => HasActiveRun
        ? "Click Running to bring an app forward. Individual launches are available after the Session ends."
        : "Click Not running to open an app, or Running to bring it forward. Apps opened individually stay open independently of the Session.";
    private bool HasOpeningApps => Sessions.SelectMany(s => s.Apps).Any(app => app.IsOpening);
    private bool CanStartSession() => runner is not null && CanBrowse && SelectedSession is { HasApps: true } && !HasActiveRun && !HasOpeningApps;
    private bool CanEndSession() => runner is not null && HasActiveRun && Runtime?.State != SessionRunState.Stopping && CanBrowse;
    private bool CanConfirmEndSession() => runner is not null && IsEndConfirmation && Runtime is { IsActive: true } runtime && runtime.SessionId == PendingEndSessionId && runtime.State != SessionRunState.Stopping && !IsBusy && Editor is null;
    private bool CanEndAndClose() => runner is not null && IsCloseConfirmation && HasActiveRun && Runtime?.State != SessionRunState.Stopping && !IsBusy && Editor is null;
    private bool CanLeaveApps() => runner is not null && Runtime?.State is SessionRunState.Running or SessionRunState.NeedsAttention or SessionRunState.AwaitingEndConfirmation;
    private bool CanFinishSession() => CanLeaveApps() && NeedsCleanup && CanBrowse;

    [RelayCommand(CanExecute = nameof(CanStartSession))]
    private async Task StartSessionAsync()
    {
        if (!CanStartSession()) return;
        ErrorMessage = null;
        StartupFocusMessage = null;
        var definition = SelectedSession!.Definition;
        try
        {
            var start = runner!.StartAsync(definition);
            var runId = runner.Snapshot!.RunId;
            await start;
            ApplyRuntime();
            await FocusAfterStartupAsync(definition, runId);
        }
        catch (Exception exception) when (SessionAppRow.IsObservationError(exception)) { ErrorMessage = exception.Message; }
        finally { ApplyRuntime(); }
    }

    private async Task FocusAfterStartupAsync(SessionDefinition definition, Guid runId)
    {
        if (_disposed || definition.FocusAfterStartup == StartupFocus.Unchanged ||
            Runtime is not { State: SessionRunState.Running, StartupSucceeded: true } runtime || runtime.RunId != runId) return;
        if (!CanBrowse)
        {
            StartupFocusMessage = "Startup finished. Focus was left unchanged while editing or showing a confirmation.";
            return;
        }
        _startupFocusCancellation.Cancel();
        _startupFocusCancellation.Dispose();
        _startupFocusCancellation = new CancellationTokenSource();
        var token = _startupFocusCancellation.Token;
        _focusRunId = runId;
        try
        {
            var path = definition.FocusAfterStartup == StartupFocus.App
                ? definition.Apps.First(app => app.Id == definition.FocusAppId).ExecutablePath : null;
            var result = startupFocus is null ? AppFocusResult.Denied : await startupFocus.FocusAsync(path, token);
            token.ThrowIfCancellationRequested();
            StartupFocusMessage = result switch
            {
                AppFocusResult.NoWindow => "Startup finished, but the chosen app has no window to focus.",
                AppFocusResult.Denied => "Startup finished, but Windows did not bring the chosen window forward. Select it from the taskbar.",
                _ => null
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (SessionAppRow.IsObservationError(exception))
        { if (!token.IsCancellationRequested) StartupFocusMessage = "Startup finished, but focus could not be changed. " + exception.Message; }
        finally { if (_focusRunId == runId) _focusRunId = null; }
    }

    [RelayCommand(CanExecute = nameof(CanEndSession))]
    private Task EndSessionAsync()
    {
        if (CanEndSession()) PendingEndSessionId = Runtime!.SessionId;
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanConfirmEndSession))]
    private async Task ConfirmEndSessionAsync()
    {
        if (!CanConfirmEndSession()) return;
        // Start the confirmed operation before dismissing its dialog, so a pending automatic request
        // cannot reopen the dialog between approval and the runner's Stopping state.
        var ending = runner!.EndAsync();
        PendingEndSessionId = null;
        await ending;
        ApplyRuntime();
    }

    [RelayCommand]
    private void CancelEndSession()
    {
        if (!IsEndConfirmation) return;
        runner?.DismissEndRequest();
        PendingEndSessionId = null;
    }

    private async Task ExecuteConfirmedEndAsync()
    {
        await runner!.EndAsync();
        ApplyRuntime();
    }

    [RelayCommand(CanExecute = nameof(CanFinishSession))]
    private async Task FinishSessionAsync()
    {
        if (!CanFinishSession()) return;
        await runner!.LeaveAppsOpenAsync();
        ApplyRuntime();
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private void ViewActiveSession()
    {
        if (CanBrowse && Runtime is { } runtime)
            SelectedSession = Sessions.FirstOrDefault(session => session.Definition.Id == runtime.SessionId);
    }

    public bool RequestWindowClose()
    {
        if (IsEndConfirmation || IsForceQuitConfirmation) return false;
        if (IsBusy)
        {
            if (_savingEditor) _closeAfterSave = true;
            else ErrorMessage = "Wait for the current operation to finish before closing Sessions.";
            return false;
        }
        if (IsDraftCloseConfirmation) return false;
        if (Editor?.HasChanges == true)
        {
            IsDraftCloseConfirmation = true;
            return false;
        }
        if (!HasActiveRun) return true;
        IsCloseConfirmation = true;
        Editor = null; // Unchanged draft; active-run choices still require their own confirmation.
        return false;
    }

    [RelayCommand]
    private void CancelClose()
    {
        _closeAfterEnd = false;
        runner?.DismissEndRequest();
        IsCloseConfirmation = false;
    }
    [RelayCommand(CanExecute = nameof(CanEndAndClose))]
    private async Task EndAndCloseAsync()
    {
        if (!CanEndAndClose()) return;
        _closeAfterEnd = true;
        await ExecuteConfirmedEndAsync();
        var shouldClose = _closeAfterEnd;
        _closeAfterEnd = false;
        IsCloseConfirmation = false;
        if (shouldClose && !HasActiveRun) CloseRequested?.Invoke(this, EventArgs.Empty);
    }
    [RelayCommand(CanExecute = nameof(CanLeaveApps))]
    private async Task LeaveAppsAndCloseAsync()
    {
        if (!CanLeaveApps()) return;
        await runner!.LeaveAppsOpenAsync();
        ApplyRuntime();
        IsCloseConfirmation = false;
        if (!HasActiveRun) CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RuntimeChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) ApplyRuntime();
        else Dispatcher.UIThread.Post(ApplyRuntime);
    }
    private void ApplyRuntime()
    {
        Runtime = runner?.Snapshot;
        if (PendingForceQuit is { } pending && !IsCurrentCleanupApp(pending)) PendingForceQuit = null;
        var remaining = Runtime?.Apps.Where(a => a.Owned && a.State == SessionAppState.LeftOpen && NeedsCleanup).ToArray() ?? [];
        for (var i = CleanupApps.Count - 1; i >= 0; i--)
            if (CleanupApps[i].RunId != Runtime?.RunId || !remaining.Any(a => a.AppId == CleanupApps[i].AppId)) CleanupApps.RemoveAt(i);
        foreach (var app in remaining)
            if (!CleanupApps.Any(a => a.AppId == app.AppId)) CleanupApps.Add(new(Runtime!.RunId, app.AppId, app.Name, app.ExecutablePath));
        if (!NeedsCleanup) CleanupFocusMessage = null;
        if (IsEndConfirmation && (!HasActiveRun || Runtime?.SessionId != PendingEndSessionId)) PendingEndSessionId = null;
        foreach (var session in Sessions)
        {
            session.IsActive = HasActiveRun && session.Definition.Id == Runtime?.SessionId;
            for (var index = 0; index < session.Apps.Count; index++)
            {
                var row = session.Apps[index];
                var outcome = session.Definition.Id == Runtime?.SessionId
                    ? Runtime.Apps.FirstOrDefault(app => app.AppId == session.Definition.Apps[index].Id) : null;
                row.RunMessage = outcome?.Message;
                row.AllowLaunch = !HasActiveRun;
            }
        }
        Refresh();
    }
    private SessionViewModel CreateSessionViewModel(SessionDefinition definition)
    {
        var session = new SessionViewModel(definition, presenceService, appLauncher);
        foreach (var row in session.Apps) row.PropertyChanged += AppRowChanged;
        return session;
    }
    private void AppRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionAppRow.IsStarting)) Refresh();
    }
    partial void OnIsCloseConfirmationChanged(bool value) => Refresh();
    partial void OnPendingEndSessionIdChanged(Guid? value) => Refresh();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _focusRunId = null;
        _startupFocusCancellation.Cancel();
        _startupFocusCancellation.Dispose();
        if (runner is not null) runner.Changed -= RuntimeChanged;
        foreach (var row in Sessions.SelectMany(s => s.Apps)) row.PropertyChanged -= AppRowChanged;
    }

    private bool _loaded;
    private bool _refreshingPresence;
    private int _presenceVersion;

    public async Task RefreshPresenceAsync(CancellationToken cancellationToken = default)
    {
        // Re-evaluate elapsed manual-start grace periods even when an empty Session is selected.
        RefreshLaunchAvailability();
        if (presenceService is null || _refreshingPresence || !ShowDetails || IsConfirmingDelete ||
            SelectedSession is not { HasApps: true } session) return;
        _refreshingPresence = true;
        var version = _presenceVersion;
        try
        {
            var snapshot = await presenceService.GetPresenceAsync(
                session.Apps.Select(app => app.ExecutablePath).ToArray(), cancellationToken);
            if (cancellationToken.IsCancellationRequested || version != _presenceVersion) return;
            foreach (var app in session.Apps)
                app.ApplyPresence(snapshot.TryGetValue(app.ExecutablePath, out var presence) ? presence : AppPresence.Unknown);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (SessionAppRow.IsObservationError(exception))
        {
            if (!cancellationToken.IsCancellationRequested && version == _presenceVersion)
                foreach (var app in session.Apps) app.ApplyPresence(AppPresence.Unknown);
        }
        finally
        {
            _refreshingPresence = false;
            RefreshLaunchAvailability();
        }
    }

    private void RefreshLaunchAvailability()
    {
        StartSessionCommand.NotifyCanExecuteChanged();
        EditSessionCommand.NotifyCanExecuteChanged();
        RequestDeleteSessionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(StartHint));
    }
    public ObservableCollection<SessionViewModel> Sessions { get; } = [];
    [ObservableProperty] private SessionViewModel? _selectedSession;
    [ObservableProperty] private SessionEditorViewModel? _editor;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private SessionViewModel? _pendingDeletion;
    [ObservableProperty] private string? _deleteErrorMessage;

    public bool HasSessions => Sessions.Count > 0;
    public bool IsEmpty => _loaded && !HasSessions && Editor is null;
    public bool IsEditing => Editor is not null;
    public bool ShowLoading => IsBusy && !IsEditing && !IsConfirmingDelete;
    public bool ShowDetails => SelectedSession is not null && Editor is null;
    public bool CanBrowse => _loaded && !IsBusy && Editor is null && IsMainContentEnabled;
    public bool CanRetryLoad => !_loaded && !IsBusy;
    public bool HasError => ErrorMessage is not null;
    public string LibraryCount => Sessions.Count == 1 ? "1 Session" : $"{Sessions.Count} Sessions";
    public bool IsConfirmingDelete => PendingDeletion is not null;
    public string DeleteTitle => $"Delete “{PendingDeletion?.Name}”?";
    public bool HasDeleteError => DeleteErrorMessage is not null;
    public string DeleteButtonLabel => IsBusy ? "Deleting…" : "Delete Session";

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RequestDeleteSession()
    {
        if (!CanEdit()) return;
        ErrorMessage = null;
        DeleteErrorMessage = null;
        PendingDeletion = SelectedSession;
    }

    private bool CanCancelDelete() => IsConfirmingDelete && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancelDelete))]
    private void CancelDeleteSession()
    {
        if (!CanCancelDelete()) return;
        PendingDeletion = null;
        DeleteErrorMessage = null;
    }

    private bool CanConfirmDelete() => _loaded && !IsBusy && Editor is null &&
                                       PendingDeletion is { } session && Sessions.Contains(session) && !(HasActiveRun && Runtime?.SessionId == session.Definition.Id);

    [RelayCommand(CanExecute = nameof(CanConfirmDelete))]
    private async Task ConfirmDeleteSessionAsync()
    {
        if (!CanConfirmDelete() || PendingDeletion is not { } session) return;
        IsBusy = true;
        DeleteErrorMessage = null;
        try
        {
            // Capture the confirmed identity, not a possibly changed selection. Persist before changing the UI.
            var index = Sessions.IndexOf(session);
            var remaining = Sessions.Where(item => item.Definition.Id != session.Definition.Id)
                .Select(item => item.Definition).ToArray();
            await store.SaveAsync(remaining);
            Sessions.RemoveAt(index);
            SelectedSession = Sessions.Count == 0 ? null : Sessions[Math.Min(index, Sessions.Count - 1)];
            PendingDeletion = null;
        }
        catch (Exception exception) when (IsStorageError(exception) || exception is ArgumentException)
        {
            DeleteErrorMessage = "The Session could not be deleted. It is still in your library. Try again or cancel. " + exception.Message;
        }
        finally { IsBusy = false; ApplyRuntime(); }
    }

    [RelayCommand(CanExecute = nameof(CanRetryLoad))]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var definitions = await store.LoadAsync();
            Sessions.Clear();
            foreach (var definition in definitions) Sessions.Add(CreateSessionViewModel(definition));
            SelectedSession = Sessions.FirstOrDefault();
            _loaded = true;
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            ErrorMessage = "Your Sessions could not be loaded. Your saved file has been left unchanged. " + exception.Message;
        }
        finally { IsBusy = false; ApplyRuntime(); }
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private void NewSession()
    {
        ErrorMessage = null;
        Editor = new SessionEditorViewModel(audioDevices: audioDevices);
    }

    private bool CanEdit() => CanBrowse && SelectedSession is not null && !SelectedIsActive && !HasOpeningApps;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void EditSession()
    {
        ErrorMessage = null;
        Editor = new SessionEditorViewModel(SelectedSession!.Definition, audioDevices);
    }

    private bool CanCancel() => IsEditing && !IsBusy && !IsDraftCloseConfirmation;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelEdit()
    {
        if (!CanCancel()) return;
        Editor = null;
        ErrorMessage = null;
    }

    private bool CanPersistEditor() => _loaded && !IsBusy && Editor?.CanSave == true;
    private bool CanSave() => CanPersistEditor() && !IsDraftCloseConfirmation;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveSessionAsync()
    {
        if (!CanSave()) return;
        await SaveEditorAsync();
    }

    private async Task SaveEditorAsync()
    {
        if (!CanPersistEditor() || Editor is not { } editor) return;
        var savedSuccessfully = false;
        _savingEditor = true;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var definition = editor.BuildDefinition();
            var definitions = Sessions.Select(session => session.Definition).ToList();
            var index = definitions.FindIndex(session => session.Id == definition.Id);
            if (index < 0) definitions.Add(definition);
            else definitions[index] = definition;
            await store.SaveAsync(definitions);
            var saved = CreateSessionViewModel(definition);
            if (index < 0) Sessions.Add(saved);
            else Sessions[index] = saved;
            SelectedSession = saved;
            if (_closeAfterSave && HasActiveRun) IsCloseConfirmation = true;
            Editor = null;
            savedSuccessfully = true;
        }
        catch (Exception exception) when (IsStorageError(exception) || exception is ArgumentException)
        {
            ErrorMessage = "Your changes could not be saved. They are still here so you can try again. " + exception.Message;
        }
        finally
        {
            _savingEditor = false;
            IsBusy = false;
            ApplyRuntime();
            var close = savedSuccessfully && _closeAfterSave;
            _closeAfterSave = false;
            if (close) ContinueWindowClose();
        }
    }

    private static bool IsStorageError(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException;
    private void EditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveSessionCommand.NotifyCanExecuteChanged();
        SaveDraftAndCloseCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DraftNeedsCorrection));
        OnPropertyChanged(nameof(DraftCloseTitle));
    }
    partial void OnEditorChanged(SessionEditorViewModel? oldValue, SessionEditorViewModel? newValue)
    {
        ResetPresence();
        if (oldValue is not null) oldValue.PropertyChanged -= EditorChanged;
        if (newValue is not null) newValue.PropertyChanged += EditorChanged;
        Refresh();
    }
    partial void OnSelectedSessionChanged(SessionViewModel? value)
    {
        ResetPresence();
        Refresh();
    }

    private void ResetPresence()
    {
        _presenceVersion++;
        if (SelectedSession is not { } session) return;
        foreach (var app in session.Apps)
        {
            app.Presence = AppPresence.Checking;
            app.FocusMessage = null;
        }
    }
    partial void OnIsBusyChanged(bool value) => Refresh();
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
    partial void OnPendingDeletionChanged(SessionViewModel? value) => Refresh();
    partial void OnDeleteErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasDeleteError));

    private void Refresh()
    {
        if (_focusRunId is { } focusRun && (Runtime?.RunId != focusRun || Runtime.State != SessionRunState.Running || !CanBrowse))
            _startupFocusCancellation.Cancel();
        if (Runtime?.State == SessionRunState.AwaitingEndConfirmation && CanBrowse)
            PendingEndSessionId = Runtime.SessionId;
        foreach (var property in new[] { nameof(HasSessions), nameof(IsEmpty), nameof(IsEditing), nameof(ShowDetails),
                     nameof(CanBrowse), nameof(CanRetryLoad), nameof(LibraryCount), nameof(ShowLoading),
                     nameof(IsConfirmingDelete), nameof(DeleteTitle), nameof(DeleteButtonLabel),
                     nameof(HasActiveRun), nameof(HasRuntime), nameof(NeedsCleanup), nameof(SelectedIsActive), nameof(ShowStart),
                     nameof(ShowActiveNavigation), nameof(RuntimeTitle), nameof(EndLabel), nameof(StartHint), nameof(AppInteractionHint), nameof(IsMainContentEnabled),
                     nameof(IsEndConfirmation), nameof(EndConfirmationTitle), nameof(AppsToStop), nameof(AppsToKeep), nameof(HasAppsToKeep), nameof(HasAppsToStop),
                     nameof(IsForceQuitConfirmation), nameof(ForceQuitTitle), nameof(ForceQuitDescription), nameof(AutomaticForceQuitWarning), nameof(HasAutomaticForceQuit),
                     nameof(DraftCloseTitle), nameof(DraftCloseMessage), nameof(DraftNeedsCorrection) })
            OnPropertyChanged(property);
        StartSessionCommand.NotifyCanExecuteChanged();
        ViewActiveSessionCommand.NotifyCanExecuteChanged();
        EndSessionCommand.NotifyCanExecuteChanged();
        ConfirmEndSessionCommand.NotifyCanExecuteChanged();
        RequestForceQuitCommand.NotifyCanExecuteChanged();
        ConfirmForceQuitCommand.NotifyCanExecuteChanged();
        FocusCleanupAppCommand.NotifyCanExecuteChanged();
        FinishSessionCommand.NotifyCanExecuteChanged();
        EndAndCloseCommand.NotifyCanExecuteChanged();
        LeaveAppsAndCloseCommand.NotifyCanExecuteChanged();
        NewSessionCommand.NotifyCanExecuteChanged();
        EditSessionCommand.NotifyCanExecuteChanged();
        CancelEditCommand.NotifyCanExecuteChanged();
        SaveSessionCommand.NotifyCanExecuteChanged();
        SaveDraftAndCloseCommand.NotifyCanExecuteChanged();
        DiscardDraftAndCloseCommand.NotifyCanExecuteChanged();
        KeepEditingCommand.NotifyCanExecuteChanged();
        LoadCommand.NotifyCanExecuteChanged();
        RequestDeleteSessionCommand.NotifyCanExecuteChanged();
        CancelDeleteSessionCommand.NotifyCanExecuteChanged();
        ConfirmDeleteSessionCommand.NotifyCanExecuteChanged();
    }
}

public sealed record CleanupAppViewModel(Guid RunId, Guid AppId, string Name, string ExecutablePath);
