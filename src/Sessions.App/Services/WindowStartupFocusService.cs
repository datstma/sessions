using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Sessions.App.Services;

public sealed class WindowStartupFocusService(Window window, IAppPresenceService presence) : IStartupFocusService
{
    public async Task<AppFocusResult> FocusAsync(string? executablePath, CancellationToken cancellationToken)
    {
        if (executablePath is not null) return await presence.FocusAsync(executablePath, cancellationToken);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        });
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return window.IsActive ? AppFocusResult.Focused : AppFocusResult.Denied;
        });
    }
}
