using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Sessions.App.Services;
using Sessions.App.ViewModels;

namespace Sessions.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _model;
    private readonly WindowAppearance _appearance;
    private readonly PreferencesService _preferences;
    private LicensesWindow? _licensesWindow;

    public SettingsWindow() : this(new PreferencesService()) { }

    public SettingsWindow(PreferencesService preferences, PluginService? plugins = null, ILauncher? launcher = null, AppInfo? info = null)
    {
        InitializeComponent();
        _preferences = preferences;
        _model = new SettingsViewModel(preferences, plugins, new AboutViewModel(info ?? AppInfo.Current, launcher ?? Launcher));
        DataContext = _model;
        _appearance = new WindowAppearance(this, null, preferences);
        _model.PropertyChanged += ModelChanged;
        Application.Current!.PropertyChanged += SystemAppearanceChanged;
        UpdatePreview();
        Opened += (_, _) =>
        {
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                Width = Math.Min(Width, Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling - (double)this.FindResource("WindowHorizontalAllowance")!));
                Height = Math.Min(Height, Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - (double)this.FindResource("WindowVerticalAllowance")!));
            }
            ThemeChoice.Focus();
        };
        Closing += (_, e) => { if (preferences.IsBusy || plugins?.IsBusy == true) e.Cancel = true; };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !ThemeChoice.IsDropDownOpen && !InterfaceChoice.IsDropDownOpen && !TextChoice.IsDropDownOpen)
            {
                Close();
                e.Handled = true;
            }
        };
        Closed += (_, _) =>
        {
            _licensesWindow?.Close();
            _model.PropertyChanged -= ModelChanged;
            Application.Current!.PropertyChanged -= SystemAppearanceChanged;
            _model.Dispose();
            _appearance.Dispose();
        };
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Preview)) UpdatePreview();
    }

    private void SystemAppearanceChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(Application.ActualThemeVariant) && _model.Theme == AppTheme.System) UpdatePreview();
    }

    private void UpdatePreview()
    {
        PreviewTheme.RequestedThemeVariant = WindowAppearance.ThemeVariantFor(_model.Theme);
        // System previews follow the OS even if the currently applied app theme is overridden.
        if (_model.Theme == AppTheme.System) PreviewTheme.RequestedThemeVariant = Avalonia.Application.Current!.ActualThemeVariant;
        WindowAppearance.ApplyTypography(PreviewTheme, _model.TextPercent);
        var scale = _model.InterfacePercent / 100d;
        PreviewScale.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void LicensesClicked(object? sender, RoutedEventArgs e)
    {
        if (_licensesWindow is not null)
        {
            if (_licensesWindow.WindowState == WindowState.Minimized) _licensesWindow.WindowState = WindowState.Normal;
            _licensesWindow.Activate();
            return;
        }
        _licensesWindow = new LicensesWindow(new LicensesViewModel(_model.About!.InstallFolder), _preferences);
        _licensesWindow.Closed += (_, _) =>
        {
            _licensesWindow = null;
            if (IsVisible) LicensesButton.Focus();
        };
        _licensesWindow.Show(this);
    }
}
