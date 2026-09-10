using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;
using System;
using System.IO;
using Sessions.App.Services;
using Avalonia.Threading;
using Avalonia.Controls;

namespace Sessions.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var presence = new WindowsAppPresenceService();
            var audio = new WindowsAudioDeviceService();
            var launcher = new IndividualAppLauncher(presence, new WindowsProcessStarter());
            var preferences = new PreferencesService(new JsonPreferencesStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sessions", "preferences.json")));
            var window = new MainWindow { Preferences = preferences };
            window.Opened += async (_, _) => await preferences.LoadAsync();
            window.DataContext = new MainViewModel(new JsonSessionStore(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Sessions", "sessions.json")), presence, launcher, new SessionRunner(new WindowsSessionProcessHost(), audioDevices: audio),
                    new WindowStartupFocusService(window, presence), audio);
            desktop.MainWindow = window;
            Program.Instance?.Listen(() => Dispatcher.UIThread.Post(() =>
            {
                if (desktop.MainWindow is not { } mainWindow) return;
                if (mainWindow.WindowState == WindowState.Minimized) mainWindow.WindowState = WindowState.Normal;
                mainWindow.Show();
                mainWindow.Activate();
            }));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
