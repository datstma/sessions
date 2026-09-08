using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using Sessions.App.Services;

namespace Sessions.App.Tests;

[Collection("Native desktop")]
public sealed class StartMenuDiscoveryTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ReadsUserAndSharedShortcutsWithoutChangingOrLaunchingThem()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "Sessions shortcut test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var user = Path.Combine(root, "User", "Tools");
            var shared = Path.Combine(root, "Shared");
            Directory.CreateDirectory(user);
            Directory.CreateDirectory(shared);
            var executable = Path.Combine(root, "Fixture app.exe");
            File.WriteAllText(executable, "Not an executable. Discovery must never try to launch it.");
            var link = Path.Combine(user, "My app.lnk");
            CreateShortcut(link, executable, "--profile \"Work profile\"", root, true);
            File.Copy(link, Path.Combine(shared, "My app.lnk"));
            CreateShortcut(Path.Combine(shared, "Missing.lnk"), Path.Combine(root, "missing.exe"), "", "", false);
            CreateShortcut(Path.Combine(shared, "Document.lnk"), Path.Combine(root, "readme.txt"), "", "", false);
            File.WriteAllText(Path.Combine(shared, "Broken.lnk"), "not a shortcut");
            File.WriteAllText(Path.Combine(shared, "Website.url"), "[InternetShortcut]\nURL=https://example.com");
            var before = File.ReadAllBytes(link);
            var apps = await new WindowsStartMenuAppSource(Path.Combine(root, "User"), shared).GetAppsAsync(TestContext.Current.CancellationToken);
            var app = Assert.Single(apps, a => a.UnavailableReason is null);
            Assert.Equal("My app", app.Name);
            Assert.Equal(executable, app.ExecutablePath);
            Assert.Equal("--profile \"Work profile\"", app.Arguments);
            Assert.Equal(root, app.WorkingDirectory);
            Assert.True(app.RunAsAdministrator);
            Assert.Equal(AppDiscoveryOrigin.StartMenu, app.Origin);
            Assert.Equal(5, apps.Count);
            Assert.All(apps.Where(a => a.Name != "My app"), a => Assert.NotNull(a.UnavailableReason));
            Assert.Equal(before, File.ReadAllBytes(link));
            Assert.StartsWith("Not an executable", File.ReadAllText(executable));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WindowsStartMenuAppSource(root).GetAppsAsync(cancellation.Token));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    public static bool RunNativeDiscovery => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("SESSIONS_RUN_DISCOVERY_SMOKE") == "1";

    [Fact(Skip = "Opt-in read-only scan of the real Start menu.", SkipUnless = nameof(RunNativeDiscovery))]
    public async Task ReadsActualStartMenuWithoutLaunchingAppsOrChangingShortcuts()
    {
        var apps = await new WindowsStartMenuAppSource().GetAppsAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(apps);
        Assert.Contains(apps, a => a.UnavailableReason is null && a.ExecutablePath is not null);
        Assert.All(apps, a => Assert.Equal(AppDiscoveryOrigin.StartMenu, a.Origin));
        output.WriteLine($"Found {apps.Count} Start menu entries; {apps.Count(a => a.UnavailableReason is null)} supported desktop shortcuts; {apps.Count(a => a.IconPng is { Length: > 0 })} icons.");
    }

    [SupportedOSPlatform("windows")]
    private static void CreateShortcut(string file, string path, string arguments, string directory, bool elevated)
    {
        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
        try
        {
            var link = (WindowsShellShortcut.IShellLinkW)instance;
            link.SetPath(path);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(directory);
            var data = (WindowsShellShortcut.IShellLinkDataList)instance;
            data.GetFlags(out var flags);
            if (elevated) data.SetFlags(flags | 0x2000);
            ((IPersistFile)instance).Save(file, true);
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }
}
