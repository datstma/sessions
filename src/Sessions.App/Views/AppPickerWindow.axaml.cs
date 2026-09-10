using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Sessions.App.ViewModels;
using Sessions.App.Services;

namespace Sessions.App.Views;

public partial class AppPickerWindow : Window
{
    public PreferencesService? Preferences { get; init; }
    private WindowAppearance? _appearance;
    public AppPickerWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (Preferences is not null) _appearance = new WindowAppearance(this, AppearanceRoot, Preferences);
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                Width = Math.Min(Width, Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling - 32));
                Height = Math.Min(Height, Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - 64));
            }
            AppSearch.Focus();
            if (DataContext is AppPickerViewModel model) await model.RefreshCommand.ExecuteAsync(null);
        };
        Closed += (_, _) =>
        {
            _appearance?.Dispose();
            (DataContext as AppPickerViewModel)?.Dispose();
        };
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close();

    private void AddClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AppPickerViewModel { CanAdd: true } model) Close(model.GetSelection());
    }

    private async void BrowseClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppPickerViewModel model) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose apps for your Session", AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Windows applications") { Patterns = ["*.exe"] }]
            });
            var paths = new List<string>();
            foreach (var file in files)
            {
                using (file)
                {
                    if (file.TryGetLocalPath() is { } path) paths.Add(path);
                    else model.ErrorMessage = "Choose an application stored on this computer.";
                }
            }
            model.AddBrowsedApps(paths);
        }
        catch (Exception exception)
        {
            model.ErrorMessage = "The file picker couldn't be opened. " + exception.Message;
        }
    }
}
