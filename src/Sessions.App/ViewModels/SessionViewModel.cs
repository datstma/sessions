using System.Collections.Generic;
using System.Linq;
using Sessions.App.Services;
using Sessions.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sessions.App.ViewModels;

public sealed partial class SessionViewModel(SessionDefinition definition, IAppPresenceService? presenceService = null,
    IIndividualAppLauncher? appLauncher = null, ISessionPluginHost? plugins = null) : ViewModelBase
{
    [ObservableProperty] private bool _isActive;
    public SessionDefinition Definition { get; } = definition;
    public string Name => Definition.Name;
    public string Initial => System.Globalization.StringInfo.GetNextTextElement(Name).ToUpperInvariant();
    public string Description => string.IsNullOrWhiteSpace(Definition.Description)
        ? "Your apps, ready together." : Definition.Description;
    public string AppCount => Definition.Apps.Count == 1 ? "1 app" : $"{Definition.Apps.Count} apps";
    public string AccessibleName => $"{Name}, {AppCount}{(IsActive ? ", active" : "")}";
    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(AccessibleName));
    public bool HasAudioChoices => Definition.OutputAudioDevice is not null || Definition.InputAudioDevice is not null;
    public string AudioSummary => $"Output: {Definition.OutputAudioDevice?.Name ?? "Leave unchanged"}. Input: {Definition.InputAudioDevice?.Name ?? "Leave unchanged"}. Previous devices are restored on End Session.";
    public bool HasApps => Definition.Apps.Count > 0;
    public string StartupSummary => (Definition.LaunchMode == SessionLaunchMode.Together ? "Apps open together; pauses are ignored." :
        $"Apps open in order; {Definition.PauseBetweenAppsSeconds}s between apps unless overridden.") +
        (Definition.FocusAfterStartup switch
        {
            StartupFocus.Sessions => " Bring Sessions forward after startup.",
            StartupFocus.App => $" Focus {Definition.Apps.First(app => app.Id == Definition.FocusAppId).Name} after startup.",
            _ => ""
        });
    public IReadOnlyList<SessionAppRow> Apps { get; } = definition.Apps
        .Select((app, index) => new SessionAppRow(index + 1, app.Name, app.ExecutablePath,
            string.Join(" · ", new[] { app.Id == definition.MainAppId ? "Ends with this app" : "", app.Optional ? "Optional" : "" }.Where(part => part.Length > 0)),
            presenceService, appLauncher, app, plugins)).ToArray();
    public string EndSummary => Definition.MainAppId is { } id
        ? $"Sessions asks to end when {Definition.Apps.First(app => app.Id == id).Name} closes."
        : "It ends when you confirm End Session.";
}
