using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sessions.App.Services;
using Sessions.App.ViewModels;
using Sessions.App.Views;
using Sessions.Core;

namespace Sessions.App.Tests;

public sealed class SessionAudioInteractionTests
{
    [AvaloniaTheory]
    [InlineData(false, 1440, 900, 1)]
    [InlineData(true, 1440, 900, 1)]
    [InlineData(false, 640, 480, 1)]
    [InlineData(true, 640, 480, 1)]
    [InlineData(false, 640, 480, 1.25)]
    [InlineData(true, 640, 480, 1.25)]
    [InlineData(false, 640, 480, 1.5)]
    [InlineData(true, 640, 480, 1.5)]
    [InlineData(false, 640, 480, 2)]
    [InlineData(true, 640, 480, 2)]
    public async Task AudioChoicesSaveWithoutSwitchingDevicesAndDraftCloseProtectsThem(bool dark, int width, int height, double scale)
    {
        var devices = new Devices();
        var store = new Store();
        using var model = new MainViewModel(store, audioDevices: devices);
        var window = new MainWindow { DataContext = model, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.SetRenderScaling(scale);
            model.EditSessionCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var editor = model.Editor!;
            var expander = window.GetVisualDescendants().OfType<Expander>().Single(e => Equals(e.Header, "Session audio"));
            expander.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            if (editor.RefreshAudioDevicesCommand.ExecutionTask is { } loading) await loading;
            Assert.False(editor.HasChanges);
            var output = Named<ComboBox>(window, "Session sound output");
            var input = Named<ComboBox>(window, "Session microphone input");
            output.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            input.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            input.Focus();
            input.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("headset", editor.OutputAudio?.Choice?.Id);
            Assert.Equal("mic", editor.InputAudio?.Choice?.Id);
            Assert.True(editor.HasChanges);
            Assert.Equal(0, devices.Writes);
            var point = input.TranslatePoint(default, window)!.Value;
            Assert.True(point.Y >= 0 && point.Y + input.Bounds.Height <= height);
            Capture(window, $"audio-editor-{dark}-{width}-{scale}");
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsDraftCloseConfirmation);
            model.KeepEditingCommand.Execute(null);
            await model.SaveSessionCommand.ExecuteAsync(null);
            Assert.False(model.IsEditing);
            Assert.Equal("headset", store.Saved.OutputAudioDevice?.Id);
            Assert.Equal("mic", store.Saved.InputAudioDevice?.Id);
            model.EditSessionCommand.Execute(null);
            Assert.False(model.Editor!.HasChanges);
            Assert.Equal("headset", model.Editor.OutputAudio?.Choice?.Id);
            Assert.Equal(0, devices.Writes);
        }
        finally { model.CancelEditCommand.Execute(null); window.Close(); }
    }

    [AvaloniaFact]
    public async Task RefreshRetainsUnavailableAndRenamedChoicesWithoutDirtyingDraftAndErrorsPreserveLists()
    {
        var devices = new Devices();
        var definition = new Store().Saved with { OutputAudioDevice = new("headset", "Old name"), InputAudioDevice = new("missing", "Disconnected mic") };
        var editor = new SessionEditorViewModel(definition, devices);
        await editor.RefreshAudioDevicesCommand.ExecuteAsync(null);
        Assert.Equal("Old name", editor.OutputAudio?.Choice?.Name);
        Assert.Contains("Headset", editor.OutputAudio!.Label);
        Assert.Contains("unavailable", editor.InputAudio!.Label);
        Assert.False(editor.HasChanges);
        var before = editor.OutputAudioOptions;
        devices.FailReads = true;
        await editor.RefreshAudioDevicesCommand.ExecuteAsync(null);
        Assert.Same(before, editor.OutputAudioOptions);
        Assert.False(editor.HasChanges);
        Assert.Contains("Couldn't list", editor.AudioDevicesMessage);
        Assert.Equal(definition.InputAudioDevice, editor.BuildDefinition().InputAudioDevice);
    }

    [AvaloniaFact]
    public async Task FailedRestorePreventsLeaveAndCloseUntilRetrySucceeds()
    {
        var devices = new Devices();
        var store = new Store { Saved = new Store().Saved with { OutputAudioDevice = new("headset", "Headset") } };
        var runner = new SessionRunner(new Host(), audioDevices: devices);
        using var model = new MainViewModel(store, runner: runner, audioDevices: devices);
        await model.LoadCommand.ExecuteAsync(null);
        await model.StartSessionCommand.ExecuteAsync(null);
        var closeRequests = 0;
        model.CloseRequested += (_, _) => closeRequests++;
        Assert.False(model.RequestWindowClose());
        devices.FailRestore = true;
        await model.LeaveAppsAndCloseCommand.ExecuteAsync(null);
        Assert.True(model.HasActiveRun);
        Assert.Equal(0, closeRequests);
        Assert.Contains("Audio restoration", model.Runtime!.Message);
        devices.FailRestore = false;
        Assert.False(model.RequestWindowClose());
        await model.LeaveAppsAndCloseCommand.ExecuteAsync(null);
        Assert.False(model.HasActiveRun);
        Assert.Equal(1, closeRequests);
    }

    private static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>()
        .Single(control => AutomationProperties.GetName(control) == name);
    private static void Capture(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SESSIONS_SCREENSHOT_DIR") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(path);
            frame.Save(Path.Combine(path, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
    private sealed class Store : ISessionStore
    {
        public SessionDefinition Saved = new(Guid.NewGuid(), "Flight", "", [new(Guid.NewGuid(), "Game", @"C:\Apps\game.exe")]);
        public Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SessionDefinition>>([Saved]);
        public Task SaveAsync(IReadOnlyList<SessionDefinition> sessions, CancellationToken cancellationToken = default) { Saved = sessions.Single(); return Task.CompletedTask; }
    }
    private sealed class Host : ISessionProcessHost
    {
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken) => Task.FromResult(new ProcessAcquisition([], false, "untracked"));
    }
    private sealed class Devices : IAudioDeviceService
    {
        public int Writes;
        public bool FailReads, FailRestore;
        private readonly Dictionary<(AudioFlow, AudioRole), string> _defaults = [];
        public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) => FailReads ? throw new IOException("Unavailable") :
            Task.FromResult<IReadOnlyList<AudioDevice>>([new("headset", "Headset with a long friendly device name", AudioFlow.Output), new("mic", "Microphone", AudioFlow.Input),
                new("original", "Original", AudioFlow.Output)]);
        public Task<string?> GetDefaultAsync(AudioFlow flow, AudioRole role) => Task.FromResult<string?>(_defaults.GetValueOrDefault((flow, role), "original"));
        public Task SetDefaultAsync(AudioFlow flow, AudioRole role, string deviceId)
        {
            if (FailRestore && deviceId == "original") throw new IOException("Previous device unavailable");
            Writes++; _defaults[(flow, role)] = deviceId; return Task.CompletedTask;
        }
    }
}

public sealed class NativeAudioDeviceTests
{
    public static bool Enabled => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("SESSIONS_NATIVE_AUDIO") == "1";
    public static bool SwitchEnabled => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("SESSIONS_NATIVE_AUDIO_SWITCH") == "1";

    [Fact(Skip = "Opt-in brief output-only switch with restoration; no apps launched.", SkipUnless = nameof(SwitchEnabled))]
    public async Task SwitchesAnAlternateOutputAndRestoresOriginalRoles()
    {
        var service = new WindowsAudioDeviceService();
        var devices = await service.GetDevicesAsync(TestContext.Current.CancellationToken);
        var originals = new Dictionary<AudioRole, string>();
        foreach (var role in Enum.GetValues<AudioRole>())
        {
            var id = await service.GetDefaultAsync(AudioFlow.Output, role);
            if (id is null) Assert.Skip("An output role has no restorable default.");
            originals.Add(role, id!);
        }
        var target = devices.FirstOrDefault(device => device.Flow == AudioFlow.Output && !originals.Values.Contains(device.Id));
        if (target is null) Assert.Skip("No alternate active output endpoint is available.");
        var runner = new SessionRunner(new NoLaunchHost(), audioDevices: service);
        try
        {
            await runner.StartAsync(new(Guid.NewGuid(), "Isolated audio check", "", [], OutputAudioDevice: new(target!.Id, target.Name)));
            Assert.True(runner.Snapshot!.StartupSucceeded, runner.Snapshot.Message);
            foreach (var role in originals.Keys) Assert.Equal(target.Id, await service.GetDefaultAsync(AudioFlow.Output, role));
            await runner.EndAsync();
            Assert.False(runner.Snapshot.IsActive, runner.Snapshot.Message);
            foreach (var (role, id) in originals) Assert.Equal(id, await service.GetDefaultAsync(AudioFlow.Output, role));
        }
        finally
        {
            if (runner.Snapshot?.IsActive == true) await runner.EndAsync();
            // Restore only this fixture's remaining selection; don't overwrite an unrelated later choice.
            foreach (var (role, id) in originals)
                if (await service.GetDefaultAsync(AudioFlow.Output, role) == target!.Id)
                    await service.SetDefaultAsync(AudioFlow.Output, role, id);
        }
    }

    private sealed class NoLaunchHost : ISessionProcessHost
    {
        public Task<ProcessAcquisition> OpenAsync(StartProcessAction app, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The native audio fixture must not launch apps.");
    }

    [Fact(Skip = "Opt-in enumeration and reapplication of the current defaults; no device change.", SkipUnless = nameof(Enabled))]
    public async Task EnumeratesDevicesAndCanReapplyCurrentDefaults()
    {
        var service = new WindowsAudioDeviceService();
        var devices = await service.GetDevicesAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(devices);
        var verified = 0;
        foreach (var flow in Enum.GetValues<AudioFlow>())
            foreach (var role in Enum.GetValues<AudioRole>())
            {
                var current = await service.GetDefaultAsync(flow, role);
                if (current is null) continue;
                Assert.Contains(devices, device => device.Flow == flow && device.Id == current && !string.IsNullOrWhiteSpace(device.Name));
                await service.SetDefaultAsync(flow, role, current);
                Assert.Equal(current, await service.GetDefaultAsync(flow, role));
                verified++;
            }
        Assert.True(verified > 0);
    }
}
