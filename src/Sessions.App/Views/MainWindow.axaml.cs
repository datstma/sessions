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
            if (DataContext is MainViewModel model && !model.RequestWindowClose()) e.Cancel = true;
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
            if (DataContext is MainViewModel viewModel && viewModel.LoadCommand.CanExecute(null))
                await viewModel.LoadCommand.ExecuteAsync(null);
            if (!_presenceLifetime.IsCancellationRequested)
            {
                _presenceTimer.Start();
                await RefreshPresenceAsync();
            }
        };
    }

    private bool _deleteWasOpen;
    private bool _endWasOpen;
    private void OnCloseRequested(object? sender, System.EventArgs e) => Close();

    private Task RefreshPresenceAsync() => !_presenceLifetime.IsCancellationRequested && IsVisible &&
        WindowState != WindowState.Minimized && DataContext is MainViewModel model
            ? model.RefreshPresenceAsync(_presenceLifetime.Token) : Task.CompletedTask;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsEndConfirmation) && sender is MainViewModel endModel && _endWasOpen != endModel.IsEndConfirmation)
        {
            _endWasOpen = endModel.IsEndConfirmation;
            Dispatcher.UIThread.Post(() =>
            {
                if (endModel.IsEndConfirmation) CancelEndButton.Focus();
                else if (endModel.HasActiveRun) EndSessionButton.Focus();
            });
        }
        if (e.PropertyName == nameof(MainViewModel.IsCloseConfirmation) && sender is MainViewModel { IsCloseConfirmation: true })
            Dispatcher.UIThread.Post(() => KeepSessionsOpenButton.Focus());
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
