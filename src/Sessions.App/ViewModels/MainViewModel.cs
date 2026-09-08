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

    public MainViewModel(ISessionStore store, IAppPresenceService? presenceService = null,
        IIndividualAppLauncher? appLauncher = null, SessionRunner? runner = null)
    {
        this.store = store;
        this.presenceService = presenceService;
        this.appLauncher = appLauncher;
        this.runner = runner;
        if (runner is not null) runner.Changed += RuntimeChanged;
    }

    [ObservableProperty] private SessionRunSnapshot? _runtime;
    [ObservableProperty] private bool _isCloseConfirmation;
    [ObservableProperty] private Guid? _pendingEndSessionId;
    public bool IsEndConfirmation => PendingEndSessionId is not null;
    public string EndConfirmationTitle => $"End “{Runtime?.Name}”?";
    public string[] AppsToStop => Runtime?.Apps.Where(app => app.Owned && app.State is not (SessionAppState.Closed or SessionAppState.Exited))
        .Select(app => app.Name).ToArray() ?? [];
    private bool _closeAfterEnd;
    public event EventHandler? CloseRequested;
    public bool HasActiveRun => Runtime?.IsActive == true;
    public bool HasRuntime => Runtime is not null;
    public bool NeedsCleanup => Runtime?.State == SessionRunState.NeedsAttention;
    public bool SelectedIsActive => HasActiveRun && SelectedSession?.Definition.Id == Runtime?.SessionId;
    public bool ShowStart => !SelectedIsActive;
    public bool ShowActiveNavigation => HasActiveRun && !SelectedIsActive;
    public bool IsMainContentEnabled => !IsConfirmingDelete && !IsCloseConfirmation && !IsEndConfirmation;
    public string RuntimeTitle => Runtime is null ? "" : $"{Runtime.Name} · {Runtime.State switch
    {
        SessionRunState.Starting => "Starting…", SessionRunState.Running => "Active",
        SessionRunState.Stopping => "Ending…", SessionRunState.NeedsAttention => "Apps still open",
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
    private bool CanStartSession() => runner is not null && CanBrowse && SelectedSession is not null && !HasActiveRun && !HasOpeningApps;
    private bool CanEndSession() => runner is not null && HasActiveRun && Runtime?.State != SessionRunState.Stopping && !IsBusy && Editor is null && !IsConfirmingDelete && !IsEndConfirmation && !IsCloseConfirmation;
    private bool CanConfirmEndSession() => runner is not null && IsEndConfirmation && Runtime is { IsActive: true } runtime && runtime.SessionId == PendingEndSessionId && runtime.State != SessionRunState.Stopping && !IsBusy && Editor is null;
    private bool CanEndAndClose() => runner is not null && IsCloseConfirmation && HasActiveRun && Runtime?.State != SessionRunState.Stopping && !IsBusy && Editor is null;
    private bool CanLeaveApps() => runner is not null && Runtime?.State is SessionRunState.Running or SessionRunState.NeedsAttention or SessionRunState.AwaitingEndConfirmation;
    private bool CanFinishSession() => CanLeaveApps() && NeedsCleanup && CanBrowse;

    [RelayCommand(CanExecute = nameof(CanStartSession))]
    private async Task StartSessionAsync()
    {
        if (!CanStartSession()) return;
        ErrorMessage = null;
        try { await runner!.StartAsync(SelectedSession!.Definition); }
        catch (Exception exception) when (SessionAppRow.IsObservationError(exception)) { ErrorMessage = exception.Message; }
        finally { ApplyRuntime(); }
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
    private void FinishSession()
    {
        if (!CanFinishSession()) return;
        runner!.LeaveAppsOpen();
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
        if (!HasActiveRun) return true;
        if (IsEndConfirmation) return false;
        if (IsBusy || Editor is not null)
        {
            ErrorMessage = Editor is not null ? "Save or cancel your edits before closing Sessions." : "Wait for the current operation to finish before closing Sessions.";
            return false;
        }
        IsCloseConfirmation = true;
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
    private void LeaveAppsAndClose()
    {
        if (!CanLeaveApps()) return;
        runner!.LeaveAppsOpen();
        ApplyRuntime();
        IsCloseConfirmation = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RuntimeChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) ApplyRuntime();
        else Dispatcher.UIThread.Post(ApplyRuntime);
    }
    private void ApplyRuntime()
    {
        Runtime = runner?.Snapshot;
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
    public bool CanBrowse => _loaded && !IsBusy && Editor is null && !IsConfirmingDelete && !IsCloseConfirmation && !IsEndConfirmation;
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
        Editor = new SessionEditorViewModel();
    }

    private bool CanEdit() => CanBrowse && SelectedSession is not null && !SelectedIsActive && !HasOpeningApps;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void EditSession()
    {
        ErrorMessage = null;
        Editor = new SessionEditorViewModel(SelectedSession!.Definition);
    }

    private bool CanCancel() => IsEditing && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelEdit()
    {
        Editor = null;
        ErrorMessage = null;
    }

    private bool CanSave() => _loaded && !IsBusy && Editor?.CanSave == true;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveSessionAsync()
    {
        if (Editor is not { } editor) return;
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
            Editor = null;
        }
        catch (Exception exception) when (IsStorageError(exception) || exception is ArgumentException)
        {
            ErrorMessage = "Your changes could not be saved. They are still here so you can try again. " + exception.Message;
        }
        finally { IsBusy = false; ApplyRuntime(); }
    }

    private static bool IsStorageError(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException;
    private void EditorChanged(object? sender, PropertyChangedEventArgs e) => SaveSessionCommand.NotifyCanExecuteChanged();
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
        if (Runtime?.State == SessionRunState.AwaitingEndConfirmation && CanBrowse)
            PendingEndSessionId = Runtime.SessionId;
        foreach (var property in new[] { nameof(HasSessions), nameof(IsEmpty), nameof(IsEditing), nameof(ShowDetails),
                     nameof(CanBrowse), nameof(CanRetryLoad), nameof(LibraryCount), nameof(ShowLoading),
                     nameof(IsConfirmingDelete), nameof(DeleteTitle), nameof(DeleteButtonLabel),
                     nameof(HasActiveRun), nameof(HasRuntime), nameof(NeedsCleanup), nameof(SelectedIsActive), nameof(ShowStart),
                     nameof(ShowActiveNavigation), nameof(RuntimeTitle), nameof(EndLabel), nameof(StartHint), nameof(AppInteractionHint), nameof(IsMainContentEnabled),
                     nameof(IsEndConfirmation), nameof(EndConfirmationTitle), nameof(AppsToStop) })
            OnPropertyChanged(property);
        StartSessionCommand.NotifyCanExecuteChanged();
        ViewActiveSessionCommand.NotifyCanExecuteChanged();
        EndSessionCommand.NotifyCanExecuteChanged();
        ConfirmEndSessionCommand.NotifyCanExecuteChanged();
        FinishSessionCommand.NotifyCanExecuteChanged();
        EndAndCloseCommand.NotifyCanExecuteChanged();
        LeaveAppsAndCloseCommand.NotifyCanExecuteChanged();
        NewSessionCommand.NotifyCanExecuteChanged();
        EditSessionCommand.NotifyCanExecuteChanged();
        CancelEditCommand.NotifyCanExecuteChanged();
        SaveSessionCommand.NotifyCanExecuteChanged();
        LoadCommand.NotifyCanExecuteChanged();
        RequestDeleteSessionCommand.NotifyCanExecuteChanged();
        CancelDeleteSessionCommand.NotifyCanExecuteChanged();
        ConfirmDeleteSessionCommand.NotifyCanExecuteChanged();
    }
}
