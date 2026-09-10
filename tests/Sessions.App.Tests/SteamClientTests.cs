using System.Diagnostics;
using Sessions.App.Services;
using Sessions.Plugins.Steam;
using Sessions.Core;

namespace Sessions.App.Tests;

[Collection("Native desktop")]
public sealed class SteamClientTests
{
    public static bool ReadNativeSteam => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("SESSIONS_READ_STEAM") == "1";
    public static bool RunNativeTracking => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("SESSIONS_RUN_RUNTIME_SMOKE") == "1";

    [Fact(Skip = "Opt-in Steam tracking/cleanup with isolated hidden helpers and a synthetic log; never launches Steam or games.", SkipUnless = nameof(RunNativeTracking))]
    public async Task NativeSteamCaptureClosesOnlyNewVerifiedHelperAndPreservesPreExistingProcess()
    {
        var root = Path.Combine(Path.GetTempPath(), "Sessions Steam tracking " + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "steamapps", "common", "Fixture");
        var log = Path.Combine(root, "logs", "gameprocess_log.txt");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        await File.WriteAllTextAsync(log, "previous log entry\n", TestContext.Current.CancellationToken);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "launch-probe")))
            File.Copy(file, Path.Combine(game, Path.GetFileName(file)));
        var processes = new List<Process>();
        ProcessAcquisition? acquired = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        async Task<Process> StartHelper(string name)
        {
            var directory = Path.Combine(game, name);
            Directory.CreateDirectory(directory);
            var info = new ProcessStartInfo(Path.Combine(game, "Sessions.LaunchProbe.exe"))
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            info.ArgumentList.Add("window"); info.ArgumentList.Add(directory); info.ArgumentList.Add("hidden");
            var process = Process.Start(info)!;
            processes.Add(process);
            _ = process.SafeHandle;
            while (!File.Exists(Path.Combine(directory, "window-ready"))) await Task.Delay(25, deadline.Token);
            return process;
        }
        try
        {
            var existing = await StartHelper("existing");
            // Baseline protection is checked separately; the cursor test exercises mixed PID entries.
            using var baseline = new WindowsSteamLaunchObservation(root, game);
            Assert.True(baseline.HasExistingAppProcesses);
            using var observation = new WindowsSteamLaunchObservation(root, Path.Combine(game, "new"));
            Process? launched = null;
            acquired = await new SteamLaunchTracker(TimeSpan.FromSeconds(1), TimeSpan.Zero).LaunchAsync(10, game, observation, async () =>
            {
                launched = await StartHelper("new");
                var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                await File.AppendAllTextAsync(log, $"[{stamp}] AppID 10 adding PID {existing.Id} as a tracked process\n[{stamp}] AppID 10 adding PID {launched.Id} as a tracked process\n");
            }, deadline.Token);
            Assert.True(acquired.Owned);
            var tracked = Assert.Single(acquired.Processes);
            Assert.True(await tracked.RequestCloseAsync(TimeSpan.FromSeconds(3)));
            Assert.True(launched!.HasExited);
            Assert.False(existing.HasExited);
        }
        finally
        {
            if (acquired is not null) foreach (var process in acquired.Processes) process.Dispose();
            foreach (var process in processes)
            {
                if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); }
                process.Dispose();
            }
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SteamLaunchUsesDesktopActivationAndOnlyANumericGameUri()
    {
        var info = WindowsSteamClient.CreateStartInfo(@"C:\Fixture\Steam Client", 10);
        Assert.Equal(@"C:\Fixture\Steam Client\steam.exe", info.FileName);
        Assert.Equal("steam://run/10", info.Arguments);
        Assert.Equal(@"C:\Fixture\Steam Client", info.WorkingDirectory);
        Assert.True(info.UseShellExecute);
        Assert.Equal("open", info.Verb);
        Assert.False(info.RedirectStandardOutput);
        Assert.False(info.RedirectStandardError);
        Assert.Throws<ArgumentException>(() => WindowsSteamClient.CreateStartInfo("relative", 10));
        Assert.Throws<ArgumentException>(() => WindowsSteamClient.CreateStartInfo(@"C:\Fixture", 0));
    }

    [Fact(Skip = "Opt-in read-only local Steam library discovery; no games or client are launched.", SkipUnless = nameof(ReadNativeSteam))]
    public async Task ReadsInstalledSteamLibrariesWithoutLaunchingOrWriting()
    {
        var client = new ReadOnlyClient();
        var root = client.FindInstallation();
        Assert.False(string.IsNullOrWhiteSpace(root));
        var plugin = new SteamPlugin(client);
        var result = await plugin.DiscoverAsync(new Dictionary<string, string>(), TestContext.Current.CancellationToken);
        Assert.NotEmpty(result.Apps);
        var available = result.Apps.First(app => app.UnavailableReason is null);
        await plugin.ValidateAsync(available.Reference, new Dictionary<string, string>(), TestContext.Current.CancellationToken);
        var version = FileVersionInfo.GetVersionInfo(Path.Combine(root!, "steam.exe")).FileVersion;
        var presence = await new WindowsSteamClient().GetPresenceAsync(223850, TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper!.WriteLine($"3DMark local Steam running state: {presence}. Read only.");
        TestContext.Current.TestOutputHelper!.WriteLine($"Steam {version}: {result.Apps.Count} discovered apps; {result.Apps.Count(app => app.UnavailableReason is null)} available. Discovery message: {result.Message ?? "none"}. No launch calls.");
    }

    private sealed class ReadOnlyClient : ISteamClient
    {
        public string? FindInstallation() => new WindowsSteamClient().FindInstallation();
        public Task LaunchAsync(string installation, uint appId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Read-only fixture must never issue a Steam launch.");
    }
}
