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
using Sessions.Plugins;
using Sessions.Plugins.Steam;

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
            var plugins = new PluginService(new PluginCatalog([new SteamPlugin(new WindowsSteamClient())]),
                new JsonPluginPreferencesStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sessions", "plugins.json")));
            var launcher = new IndividualAppLauncher(presence, new WindowsProcessStarter(), plugins: plugins);
            var preferences = new PreferencesService(new JsonPreferencesStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sessions", "preferences.json")));
            var window = new MainWindow { Preferences = preferences, Plugins = plugins };
            window.Opened += async (_, _) => { await preferences.LoadAsync(); await plugins.LoadAsync(); };
            window.DataContext = new MainViewModel(new JsonSessionStore(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Sessions", "sessions.json")), presence, launcher, new SessionRunner(new WindowsSessionProcessHost(), audioDevices: audio, plugins: plugins),
                    new WindowStartupFocusService(window, presence), audio, plugins, new WindowsIndividualAppCloser(plugins));
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
