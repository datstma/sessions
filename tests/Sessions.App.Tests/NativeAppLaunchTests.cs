using Sessions.App.Services;
using Sessions.Core;
using System.Diagnostics;
using System.Text.Json;

namespace Sessions.App.Tests;

[Collection("Native desktop")]
public sealed class NativeAppLaunchTests
{
    public static bool RunNativeLaunch => OperatingSystem.IsWindows() &&
        Environment.GetEnvironmentVariable("SESSIONS_RUN_LAUNCH_SMOKE") == "1";

    [Theory(Skip = "Opt-in windowless parent/child process lifetime regression.", SkipUnless = nameof(RunNativeLaunch))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildHasNoLauncherPipesAndSurvivesParentExit(bool terminateParent)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        var directory = Path.Combine(Path.GetTempPath(), "Sessions lifetime test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Process? parent = null;
        Process? child = null;
        try
        {
            var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "launch-probe", "Sessions.LaunchProbe.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
            };
            info.ArgumentList.Add("parent");
            info.ArgumentList.Add(directory);
            parent = Process.Start(info)!;
            var readyPath = Path.Combine(directory, "child-ready");
            while (!File.Exists(readyPath)) await Task.Delay(25, token);
            // The helper wrote this identity itself. Keep the handle so cleanup cannot target a reused PID.
            child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(readyPath, token)));
            _ = child.SafeHandle;
            if (terminateParent) parent.Kill(); // Only our owned helper; never Kill(entireProcessTree: true).
            else await File.WriteAllTextAsync(Path.Combine(directory, "exit-parent"), "exit", token);
            await parent.WaitForExitAsync(token);
            parent.StandardOutput.Dispose();
            parent.StandardError.Dispose();
            parent.StandardInput.Dispose();
            await File.WriteAllTextAsync(Path.Combine(directory, "write-after-exit"), "write", token);
            var reportPath = Path.Combine(directory, "report.json");
            while (!File.Exists(reportPath)) await Task.Delay(25, token);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, token));
            await child.WaitForExitAsync(token);
            Assert.Equal(0, child.ExitCode);
            Assert.Equal(0, report.RootElement.GetProperty("OutputWriteError").GetInt32());
            Assert.Equal(0, report.RootElement.GetProperty("ErrorWriteError").GetInt32());
            Assert.False(report.RootElement.GetProperty("OutputIsPipe").GetBoolean());
            Assert.False(report.RootElement.GetProperty("ErrorIsPipe").GetBoolean());
            Assert.Equal(directory, report.RootElement.GetProperty("WorkingDirectory").GetString());
            Assert.Equal("argument with spaces", report.RootElement.GetProperty("Argument").GetString());
        }
        finally
        {
            // Only handles for these test-owned processes are eligible for failure cleanup.
            foreach (var process in new[] { child, parent })
            {
                if (process is null) continue;
                try { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(TestContext.Current.CancellationToken); } }
                finally { process.Dispose(); }
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(Skip = "Opt-in background Windows Script Host launch; writes a temporary test receipt and exits.",
        SkipUnless = nameof(RunNativeLaunch))]
    public async Task NativeLaunchPassesArgumentsAndWorkingDirectory()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "Sessions launch test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var script = Path.Combine(directory, "launch receipt.js");
            var receipt = Path.Combine(directory, "receipt.txt");
            await File.WriteAllTextAsync(script, """
                var fs = new ActiveXObject("Scripting.FileSystemObject");
                var shell = new ActiveXObject("WScript.Shell");
                var file = fs.CreateTextFile("receipt.txt", true);
                file.WriteLine(shell.CurrentDirectory);
                file.WriteLine(WScript.Arguments.Item(0));
                file.WriteLine("complete");
                file.Close();
                WScript.Quit(0);
                """, token);
            var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wscript.exe");
            await new WindowsProcessStarter().StartAsync(new StartProcessAction(Guid.NewGuid(), "Launch test",
                executable, $"//B //Nologo \"{script}\" \"argument with spaces\"", directory), token);
            string[] lines = [];
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                try { if (File.Exists(receipt)) lines = await File.ReadAllLinesAsync(receipt, token); }
                catch (IOException) { } // Writer may still have the receipt open.
                if (lines.Length == 3 && lines[2] == "complete") break;
                await Task.Delay(50, token);
            }
            Assert.Equal(new[] { directory, "argument with spaces", "complete" }, lines);
        }
        finally { Directory.Delete(directory, recursive: true); } // Only this test's uniquely created directory.
    }
}
