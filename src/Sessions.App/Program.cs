using Avalonia;
using System;
using System.Security.Cryptography;
using System.Text;
using Sessions.App.Services;

namespace Sessions.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    internal static SingleInstanceGuard? Instance { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == WindowsProcessCleanup.HelperArgument)
        {
            Environment.ExitCode = WindowsProcessCleanup.RunHelperAsync(args).GetAwaiter().GetResult();
            return;
        }
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))));
        using var instance = new SingleInstanceGuard(@"Global\Sessions." + identity);
        if (!instance.IsOwner) return;
        Instance = instance;
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { Instance = null; }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
