using System.Collections.Generic;
using System.Linq;
using Sessions.App.Services;
using Sessions.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sessions.App.ViewModels;

public sealed partial class SessionViewModel(SessionDefinition definition, IAppPresenceService? presenceService = null,
    IIndividualAppLauncher? appLauncher = null) : ViewModelBase
{
    [ObservableProperty] private bool _isActive;
    public SessionDefinition Definition { get; } = definition;
    public string Name => Definition.Name;
    public string Initial => System.Globalization.StringInfo.GetNextTextElement(Name).ToUpperInvariant();
    public string Description => string.IsNullOrWhiteSpace(Definition.Description)
        ? "Your apps, ready together." : Definition.Description;
    public string AppCount => Definition.Apps.Count == 1 ? "1 app" : $"{Definition.Apps.Count} apps";
    public bool HasApps => Definition.Apps.Count > 0;
    public IReadOnlyList<SessionAppRow> Apps { get; } = definition.Apps
        .Select((app, index) => new SessionAppRow(index + 1, app.Name, app.ExecutablePath,
            app.Id == definition.MainAppId ? "Ends with this app" : "Open app", presenceService, appLauncher, app)).ToArray();
    public string EndSummary => Definition.MainAppId is { } id
        ? $"Ask to end when {Definition.Apps.First(app => app.Id == id).Name} closes"
        : "When you confirm End Session";
}
