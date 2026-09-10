using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sessions.App.Services;
using Sessions.Core;

namespace Sessions.App.ViewModels;

public partial class MainViewModel
{
    private IIndividualAppCloser? _appCloser;
    private IPreparedAppClose? _preparedAppClose;
    [ObservableProperty] private SessionAppRow? _pendingAppClose;
    [ObservableProperty] private bool _isAppCloseBusy;
    public bool IsAppCloseConfirmation => PendingAppClose is not null;
    public string AppCloseTitle => $"Close “{PendingAppClose?.Name}”?";
    public string AppCloseDescription => "Save your work first. This asks the currently identified copies of this app to close, including copies opened outside Sessions. Other apps stay open.";
    public string AppCloseButtonLabel => IsAppCloseBusy ? "Closing…" : "Close app";
    public string AppCloseSessionHint => "This doesn't end your Session. Closing its main app may ask you whether to end it.";

    private bool CanRequestCloseApp(SessionAppRow? row) => _appCloser is not null && CanBrowse && !HasOpeningApps &&
        Runtime?.State is not (SessionRunState.Starting or SessionRunState.Stopping) &&
        row is { IsRunning: true, Definition: not null } && SelectedSession?.Apps.Contains(row) == true;
    private bool CanConfirmCloseApp() => !_disposed && !IsAppCloseBusy && IsAppCloseConfirmation && _preparedAppClose is not null;
    private bool CanCancelCloseApp() => !IsAppCloseBusy && IsAppCloseConfirmation;

    [RelayCommand(CanExecute = nameof(CanRequestCloseApp))]
    private async Task RequestCloseAppAsync(SessionAppRow? row)
    {
        if (!CanRequestCloseApp(row)) return;
        IsAppCloseBusy = true;
        row!.FocusMessage = null;
        row.LaunchMessage = null;
        row.SetCloseFeedback(null);
        try
        {
            var prepared = await _appCloser!.PrepareAsync(row.Definition!);
            if (_disposed || prepared.TargetCount == 0)
            {
                prepared.Dispose();
                if (!_disposed) row.SetCloseFeedback("This app is no longer running.");
                return;
            }
            _preparedAppClose = prepared;
            PendingAppClose = row;
        }
        catch (Exception exception) when (SessionAppRow.IsObservationError(exception))
        { row.SetCloseFeedback("Couldn't prepare to close this app. " + exception.Message, isError: true); }
        finally { IsAppCloseBusy = false; if (!_disposed) await RefreshPresenceAsync(); }
    }

    [RelayCommand(CanExecute = nameof(CanConfirmCloseApp))]
    private async Task ConfirmCloseAppAsync()
    {
        if (!CanConfirmCloseApp()) return;
        var request = _preparedAppClose!;
        var row = PendingAppClose!;
        IsAppCloseBusy = true;
        try
        {
            if (await Task.Run(request.CloseAsync)) row.SetCloseFeedback("Close requested. Waiting for the app to stop.");
            else row.SetCloseFeedback("This app is still running. Check for a save prompt, or close it from its own window.", isError: true);
        }
        catch (Exception exception) when (SessionAppRow.IsObservationError(exception))
        { row.SetCloseFeedback("Couldn't close this app. " + exception.Message, isError: true); }
        finally
        {
            request.Dispose();
            _preparedAppClose = null;
            PendingAppClose = null;
            IsAppCloseBusy = false;
            if (!_disposed)
            {
                if (runner is not null) { await Task.Run(runner.RefreshAsync); ApplyRuntime(); }
                await RefreshPresenceAsync();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelCloseApp))]
    private void CancelCloseApp()
    {
        if (!CanCancelCloseApp()) return;
        _preparedAppClose?.Dispose();
        _preparedAppClose = null;
        PendingAppClose = null;
    }

    partial void OnPendingAppCloseChanged(SessionAppRow? value) => Refresh();
    partial void OnIsAppCloseBusyChanged(bool value) => Refresh();
}
