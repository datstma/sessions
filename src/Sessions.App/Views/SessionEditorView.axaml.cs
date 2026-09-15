using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.VisualTree;
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
            if (DataContext is SessionEditorViewModel editor)
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    SessionName.Focus();
                    // A copy's suggested name is usually replaced, so typing should overwrite it.
                    if (editor.DuplicateOf is not null) SessionName.SelectAll();
                });
        };
    }

    private void AudioExpanding(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SessionEditorViewModel editor && !editor.IsLoadingAudioDevices)
            _ = editor.RefreshAudioDevicesCommand.ExecuteAsync(null);
    }

    public void ReviewFirstInvalidField()
    {
        if (DataContext is not SessionEditorViewModel { FirstValidationIssue: { } issue } editor) return;
        if (issue.App is { } app)
        {
            editor.SelectedApp = app;
            AppOptions.IsExpanded = true;
        }
        if (issue.Field is EditorField.LaunchMode or EditorField.SessionPause or EditorField.StartupFocus or EditorField.FocusApp)
            AdvancedStartup.IsExpanded = true;
        var name = issue.Field switch
        {
            EditorField.SessionName => "Session name",
            EditorField.AppName => "App display name",
            EditorField.ExecutablePath => "App executable path",
            EditorField.Readiness => "App startup condition",
            EditorField.ReadinessTimeout => "Maximum readiness wait in seconds",
            EditorField.AppPause => "Pause after this app in seconds",
            EditorField.LaunchMode => "Session launch mode",
            EditorField.SessionPause => "Pause between apps in seconds",
            EditorField.StartupFocus => "Focus after startup",
            EditorField.FocusApp => "App to focus after startup",
            EditorField.MainApp => "App that ends the Session",
            _ => throw new ArgumentOutOfRangeException()
        };
        // Let selection bindings and expander templates settle before moving keyboard focus.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(DataContext, editor) || !IsEffectivelyVisible) return;
            UpdateLayout();
            var field = this.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(control => AutomationProperties.GetName(control) == name);
            field?.Focus();
            if (field is null) return;
            var help = AutomationProperties.GetHelpText(field);
            var explanation = this.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(text =>
                text.Classes.Contains("fieldError") && text.IsEffectivelyVisible &&
                ReferenceEquals(text.DataContext, field.DataContext) && text.Text == help);
            // Include the nearby explanation, so compact layouts don't hide it under the footer.
            if (explanation?.TranslatePoint(default, field) is { } point && point.Y >= 0)
                field.BringIntoView(new Rect(0, 0, field.Bounds.Width, point.Y + explanation.Bounds.Height));
            else field.BringIntoView();
        });
    }

    private void AppListActionClicked(object? sender, RoutedEventArgs e)
    {
        var hadKeyboardFocus = sender is Button { IsFocused: true };
        // Commands can hide Remove or disable a move button at the end of the list.
        // Wait for the command/bindings, then keep keyboard focus on a usable control.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!hadKeyboardFocus || sender is not Button button ||
                button.IsEffectivelyVisible && button.IsEffectivelyEnabled) return;
            if (DataContext is SessionEditorViewModel { SelectedApp: { } selected })
            {
                AppOrderList.ScrollIntoView(selected);
                AppOrderList.UpdateLayout();
                if (AppOrderList.ContainerFromItem(selected) is Control item) item.Focus();
                else AppOrderList.Focus();
            }
            else AddAppsButton.Focus();
        });
    }

    // File dialogs are a presentation/platform concern. Definitions and ordering remain in the ViewModel/Core.
    private async void AddAppClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SessionEditorViewModel editor || TopLevel.GetTopLevel(this) is not MainWindow owner)
            return;
        try
        {
            using var model = new AppPickerViewModel(owner.RunningAppSource, editor.Apps.Where(app => app.Plugin is null).Select(app => app.ExecutablePath),
                owner.StartMenuAppSource, owner.Plugins, editor.Apps.Where(app => app.Plugin is not null).Select(app => app.Plugin!));
            var picker = new AppPickerWindow { DataContext = model, Preferences = (owner as MainWindow)?.Preferences };
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
