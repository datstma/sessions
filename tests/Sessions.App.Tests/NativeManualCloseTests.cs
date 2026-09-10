using System.Diagnostics;
using System.Globalization;
using Sessions.App.Services;
using Sessions.Core;

namespace Sessions.App.Tests;

[Collection("Native desktop")]
public sealed class NativeManualCloseTests
{
    public static bool RunNative => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("SESSIONS_RUN_RUNTIME_SMOKE") == "1";

    [Theory(Skip = "Opt-in one-off closing with hidden isolated helpers; never closes user apps or Steam.", SkipUnless = nameof(RunNative))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualCloseCapturesExistingAppButPreservesLaterCopiesAndOtherPaths(bool steam)
    {
        var root = Path.Combine(Path.GetTempPath(), "Sessions manual close " + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        var other = Path.Combine(root, "other");
        var log = Path.Combine(root, "logs", "gameprocess_log.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        foreach (var folder in new[] { game, other })
        {
            Directory.CreateDirectory(folder);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "launch-probe")))
                File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        }
        var processes = new List<Process>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        async Task<Process> Start(string folder, string name)
        {
            var directory = Path.Combine(folder, name); Directory.CreateDirectory(directory);
            var info = new ProcessStartInfo(Path.Combine(folder, "Sessions.LaunchProbe.exe"))
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            info.ArgumentList.Add("window"); info.ArgumentList.Add(directory); info.ArgumentList.Add("hidden");
            var process = Process.Start(info)!; processes.Add(process); _ = process.SafeHandle;
            while (!File.Exists(Path.Combine(directory, "window-ready"))) await Task.Delay(25, deadline.Token);
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            await File.AppendAllTextAsync(log, $"[{stamp}] AppID 10 adding PID {process.Id} as a tracked process\n", deadline.Token);
            return process;
        }
        async Task<IPreparedAppClose> Prepare() => steam
            ? WindowsSteamManualClose.Prepare(root, game, 10, deadline.Token)
            : await new WindowsIndividualAppCloser().PrepareAsync(new(Guid.NewGuid(), "Fixture", Path.Combine(game, "Sessions.LaunchProbe.exe"), AllowForceQuit: true), deadline.Token);
        try
        {
            var existing = await Start(game, "existing");
            var unrelated = await Start(other, "unrelated");
            using (var cancelled = await Prepare()) Assert.Equal(1, cancelled.TargetCount);
            Assert.False(existing.HasExited); // Cancel disposes handles without sending WM_CLOSE.
            using var confirmed = await Prepare();
            Assert.Equal(1, confirmed.TargetCount);
            var later = await Start(game, "later");
            Assert.True(await confirmed.CloseAsync());
            Assert.True(existing.HasExited);
            Assert.False(later.HasExited);
            Assert.False(unrelated.HasExited);
        }
        finally
        {
            foreach (var process in processes)
            {
                if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); }
                process.Dispose();
            }
            Directory.Delete(root, true);
        }
    }
}
