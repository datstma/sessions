using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Linq;
using Sessions.App.Services;
using Sessions.App.ViewModels;

namespace Sessions.App.Views;

public partial class SessionEditorView : UserControl
{
    public SessionEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SessionEditorViewModel)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => SessionName.Focus());
        };
    }

    // File dialogs are a presentation/platform concern. Definitions and ordering remain in the ViewModel/Core.
    private async void AddAppClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SessionEditorViewModel editor || TopLevel.GetTopLevel(this) is not MainWindow owner)
            return;
        try
        {
            using var model = new AppPickerViewModel(owner.RunningAppSource, editor.Apps.Select(app => app.ExecutablePath), owner.StartMenuAppSource);
            var picker = new AppPickerWindow { DataContext = model };
            var apps = await picker.ShowDialog<IReadOnlyList<DiscoveredApp>?>(owner);
            if (apps is not null && ReferenceEquals(DataContext, editor)) editor.AddPickedApps(apps);
        }
        catch (Exception exception)
        {
            editor.ValidationMessage = "The app picker could not be opened. " + exception.Message;
        }
        finally { AddAppsButton.Focus(); }
    }
}
