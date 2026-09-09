using Avalonia.Controls;
using Avalonia.Threading;
using System.ComponentModel;
using System;
using System.Threading;
using System.Threading.Tasks;
using Sessions.App.ViewModels;
using Sessions.App.Services;

namespace Sessions.App.Views;

public partial class MainWindow : Window
{
    public IAppSource RunningAppSource { get; init; } = new WindowsRunningAppSource();
    public IAppSource StartMenuAppSource { get; init; } = new WindowsStartMenuAppSource();
    private readonly CancellationTokenSource _presenceLifetime = new();
    private readonly DispatcherTimer _presenceTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Classes.Set("compact", Bounds.Width < (double)this.FindResource("CompactBreakpoint")!);
        _presenceTimer.Tick += async (_, _) => await RefreshPresenceAsync();
        Activated += async (_, _) => await RefreshPresenceAsync();
        MainViewModel? observedModel = null;
        DataContextChanged += (_, _) =>
        {
            if (observedModel is not null)
            {
                observedModel.PropertyChanged -= OnViewModelChanged;
                observedModel.CloseRequested -= OnCloseRequested;
            }
            observedModel = DataContext as MainViewModel;
            if (observedModel is not null)
            {
                observedModel.PropertyChanged += OnViewModelChanged;
                observedModel.CloseRequested += OnCloseRequested;
            }
        };
        Closing += (_, e) =>
        {
            if (DataContext is MainViewModel model)
            {
                if (!model.IsCloseConfirmation) _closeReturnFocus = FocusManager?.GetFocusedElement() as Control;
                if (!model.RequestWindowClose()) e.Cancel = true;
            }
        };
        Closed += (_, _) =>
        {
            _presenceTimer.Stop();
            _presenceLifetime.Cancel();
            _presenceLifetime.Dispose();
            if (observedModel is not null)
            {
                observedModel.PropertyChanged -= OnViewModelChanged;
                observedModel.CloseRequested -= OnCloseRequested;
                observedModel.Dispose();
            }
        };
        Opened += async (_, _) =>
        {
            // Size in logical pixels: leave space for the taskbar and window decorations at high DPI.
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                Width = Math.Min(Width, Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling - (double)this.FindResource("WindowHorizontalAllowance")!));
                Height = Math.Min(Height, Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - (double)this.FindResource("WindowVerticalAllowance")!));
            }
            if (DataContext is MainViewModel viewModel && viewModel.LoadCommand.CanExecute(null))
                await viewModel.LoadCommand.ExecuteAsync(null);
            if (!_presenceLifetime.IsCancellationRequested)
            {
                _presenceTimer.Start();
                await RefreshPresenceAsync();
                Dispatcher.UIThread.Post(() =>
                {
                    if (DataContext is not MainViewModel { IsMainContentEnabled: true, IsEditing: false } ready) return;
                    if (ready.IsEmpty) CreateFirstButton.Focus();
                    else SessionList.Focus();
                });
            }
        };
    }

    private bool _deleteWasOpen;
    private bool _endWasOpen;
    private bool _closeWasOpen;
    private bool _forceWasOpen;
    private Control? _forceReturnFocus;
    private void ForceQuitRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _forceReturnFocus = sender as Control;
    private Control? _editorReturnFocus;
    private Control? _closeReturnFocus;
    private void EditRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _editorReturnFocus = sender as Control;
    private void OnCloseRequested(object? sender, System.EventArgs e) => Close();

    private Task RefreshPresenceAsync() => !_presenceLifetime.IsCancellationRequested && IsVisible &&
        WindowState != WindowState.Minimized && DataContext is MainViewModel model
            ? model.RefreshPresenceAsync(_presenceLifetime.Token) : Task.CompletedTask;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsForceQuitConfirmation) && sender is MainViewModel forceModel && _forceWasOpen != forceModel.IsForceQuitConfirmation)
        {
            _forceWasOpen = forceModel.IsForceQuitConfirmation;
            Dispatcher.UIThread.Post(() =>
            {
                if (forceModel.IsForceQuitConfirmation) CancelForceQuitButton.Focus();
                else if (forceModel.IsMainContentEnabled)
                {
                    if (_forceReturnFocus is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } previous) previous.Focus();
                    else if (forceModel.HasActiveRun) EndSessionButton.Focus();
                    else NewSessionButton.Focus();
                    _forceReturnFocus = null;
                }
            });
        }
        if (e.PropertyName == nameof(MainViewModel.Editor) && sender is MainViewModel { IsEditing: false } editorModel)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (editorModel.IsEditing || !editorModel.IsMainContentEnabled) return;
                if (_editorReturnFocus is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } previous)
                    previous.Focus();
                else if (editorModel.ShowDetails && EditSessionButton.IsEffectivelyEnabled) EditSessionButton.Focus();
                else if (editorModel.HasSessions) NewSessionButton.Focus();
                else CreateFirstButton.Focus();
                _editorReturnFocus = null;
            });
        }
        if (e.PropertyName == nameof(MainViewModel.IsEndConfirmation) && sender is MainViewModel endModel && _endWasOpen != endModel.IsEndConfirmation)
        {
            _endWasOpen = endModel.IsEndConfirmation;
            Dispatcher.UIThread.Post(() =>
            {
                if (endModel.IsEndConfirmation) CancelEndButton.Focus();
                else if (endModel.HasActiveRun) EndSessionButton.Focus();
            });
        }
        if (e.PropertyName == nameof(MainViewModel.IsCloseConfirmation) && sender is MainViewModel closeModel &&
            _closeWasOpen != closeModel.IsCloseConfirmation)
        {
            _closeWasOpen = closeModel.IsCloseConfirmation;
            Dispatcher.UIThread.Post(() =>
            {
                if (closeModel.IsCloseConfirmation) KeepSessionsOpenButton.Focus();
                else if (closeModel.IsMainContentEnabled)
                {
                    if (_closeReturnFocus is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } previous) previous.Focus();
                    else if (closeModel.HasActiveRun) EndSessionButton.Focus();
                    _closeReturnFocus = null;
                }
            });
        }
        if (e.PropertyName == nameof(MainViewModel.ShowDetails))
            Dispatcher.UIThread.Post(async () => await RefreshPresenceAsync());
        if (e.PropertyName != nameof(MainViewModel.IsConfirmingDelete) || sender is not MainViewModel model ||
            _deleteWasOpen == model.IsConfirmingDelete) return;
        _deleteWasOpen = model.IsConfirmingDelete;
        Dispatcher.UIThread.Post(() =>
        {
            if (model.IsConfirmingDelete) CancelDeleteButton.Focus();
            else if (model.HasSessions) DeleteSessionButton.Focus();
            else CreateFirstButton.Focus();
        });
    }
}
