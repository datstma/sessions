using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sessions.Core;
using Sessions.App.Services;

namespace Sessions.App.ViewModels;

public partial class SessionEditorViewModel : ViewModelBase
{
    private readonly Guid _id;
    public bool IsNew { get; }
    public string Title => IsNew ? "Create a Session" : "Edit Session";
    public string SaveLabel => IsNew ? "Create Session" : "Save changes";
    public ObservableCollection<AppEditorViewModel> Apps { get; } = [];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private AppEditorViewModel? _selectedApp;
    [ObservableProperty] private bool _endWithApp;
    [ObservableProperty] private AppEditorViewModel? _mainApp;
    [ObservableProperty] private string? _validationMessage;
    [ObservableProperty] private int _launchModeIndex;
    [ObservableProperty] private decimal? _pauseBetweenAppsSeconds = 0;
    [ObservableProperty] private int _startupFocusIndex;
    [ObservableProperty] private AppEditorViewModel? _focusApp;
    public string[] LaunchModes { get; } = ["In order", "All at once"];
    public string[] StartupFocusChoices { get; } = ["Leave focus unchanged", "Bring Sessions forward", "Focus a chosen app"];
    public bool IsOrdered => LaunchModeIndex == 0;
    public bool NeedsFocusApp => StartupFocusIndex == 2;
    public string OrderingHint => IsOrdered ? "They open in the order shown." : "They open together. Startup waits still apply.";
    private bool ValidStartup => LaunchModeIndex is 0 or 1 && StartupFocusIndex is >= 0 and <= 2 &&
        ValidSeconds(PauseBetweenAppsSeconds, 0, 300) && (!NeedsFocusApp || FocusApp is not null && Apps.Contains(FocusApp)) &&
        Apps.All(app => app.ValidStartup);
    internal static bool ValidSeconds(decimal? value, int minimum, int maximum) => value is { } seconds &&
        seconds >= minimum && seconds <= maximum && decimal.Truncate(seconds) == seconds;

    public bool HasApps => Apps.Count > 0;
    public bool HasSelectedApp => SelectedApp is not null;
    public string AppOptionsHeading => string.IsNullOrWhiteSpace(SelectedApp?.Name)
        ? "App options" : $"{SelectedApp.Name.Trim()} options";
    public string AdvancedStartupHeading => string.IsNullOrWhiteSpace(Name)
        ? "Advanced startup options" : $"{Name.Trim()} advanced startup options";
    public string SelectedAppSummary => SelectedApp is { } app ? $"Selected: {app.Name}, app {app.Order} of {Apps.Count}." : "";
    public bool HasValidationMessage => ValidationMessage is not null;
    public bool CanSave => !string.IsNullOrWhiteSpace(Name) &&
                           Apps.All(app => !string.IsNullOrWhiteSpace(app.Name) && !string.IsNullOrWhiteSpace(app.ExecutablePath)) &&
                           (!EndWithApp || MainApp is not null && Apps.Contains(MainApp)) && ValidStartup;

    public SessionEditorViewModel(SessionDefinition? definition = null)
    {
        IsNew = definition is null;
        _id = definition?.Id ?? Guid.NewGuid();
        Name = definition?.Name ?? "";
        Description = definition?.Description ?? "";
        LaunchModeIndex = (int)(definition?.LaunchMode ?? SessionLaunchMode.InOrder);
        PauseBetweenAppsSeconds = definition?.PauseBetweenAppsSeconds ?? 0;
        StartupFocusIndex = (int)(definition?.FocusAfterStartup ?? StartupFocus.Unchanged);
        Apps.CollectionChanged += AppsChanged;
        foreach (var app in definition?.Apps ?? [])
            Apps.Add(new AppEditorViewModel(app));
        MainApp = Apps.FirstOrDefault(app => app.Id == definition?.MainAppId);
        EndWithApp = MainApp is not null;
        SelectedApp = Apps.FirstOrDefault();
        FocusApp = Apps.FirstOrDefault(app => app.Id == definition?.FocusAppId);
    }

    public void AddApp(string path, string? displayName = null)
    {
        var app = new AppEditorViewModel(new StartProcessAction(Guid.NewGuid(),
            displayName ?? System.IO.Path.GetFileNameWithoutExtension(path), path));
        Apps.Add(app);
        SelectedApp = app;
        MainApp ??= app;
    }

    public void AddPickedApps(System.Collections.Generic.IEnumerable<DiscoveredApp> apps)
    {
        var paths = Apps.Select(app => AppPickerViewModel.NormalizePath(app.ExecutablePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            if (app.UnavailableReason is null && app.ExecutablePath is { } path &&
                paths.Add(AppPickerViewModel.NormalizePath(path)))
            {
                var added = new AppEditorViewModel(new StartProcessAction(Guid.NewGuid(), app.Name, path,
                    app.Arguments, app.WorkingDirectory, app.RunAsAdministrator));
                Apps.Add(added);
                SelectedApp = added;
                MainApp ??= added;
            }
        }
    }

    public SessionDefinition BuildDefinition()
    {
        if (!CanSave) throw new ArgumentException("Complete the required Session and startup settings before saving.");
        var definition = new SessionDefinition(_id, Name.Trim(), Description.Trim(),
            Apps.Select(app => app.BuildAction()).ToArray(), EndWithApp ? MainApp?.Id : null,
            (SessionLaunchMode)LaunchModeIndex, (int)PauseBetweenAppsSeconds!.Value,
            (StartupFocus)StartupFocusIndex, FocusApp?.Id);
        definition.Validate();
        return definition;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedApp))]
    private void RemoveApp()
    {
        if (SelectedApp is not { } app) return;
        var index = Apps.IndexOf(app);
        Apps.Remove(app);
        if (ReferenceEquals(MainApp, app)) MainApp = null;
        if (ReferenceEquals(FocusApp, app)) FocusApp = null;
        SelectedApp = Apps.Count == 0 ? null : Apps[Math.Min(index, Apps.Count - 1)];
        NotifyValidation();
    }

    private bool CanMoveUp() => SelectedApp is not null && Apps.IndexOf(SelectedApp) > 0;
    private bool CanMoveDown() => SelectedApp is not null && Apps.IndexOf(SelectedApp) < Apps.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(1);

    private void Move(int offset)
    {
        var app = SelectedApp!;
        var index = Apps.IndexOf(app);
        Apps.Move(index, index + offset);
        SelectedApp = app;
        NotifyAppCommands();
    }

    private void AppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (AppEditorViewModel app in e.OldItems) app.PropertyChanged -= AppChanged;
        if (e.NewItems is not null)
            foreach (AppEditorViewModel app in e.NewItems) app.PropertyChanged += AppChanged;
        for (var index = 0; index < Apps.Count; index++) Apps[index].Order = index + 1;
        OnPropertyChanged(nameof(HasApps));
        NotifyValidation();
        NotifyAppCommands();
    }

    private void AppChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyValidation();
        if (ReferenceEquals(sender, SelectedApp) && e.PropertyName == nameof(AppEditorViewModel.Name))
            OnPropertyChanged(nameof(AppOptionsHeading));
        if (e.PropertyName is nameof(AppEditorViewModel.Name) or nameof(AppEditorViewModel.Order))
            OnPropertyChanged(nameof(SelectedAppSummary));
    }
    private void NotifyValidation()
    {
        OnPropertyChanged(nameof(CanSave));
        ValidationMessage = EndWithApp && MainApp is null ? "Choose which app should end this Session." :
            NeedsFocusApp && FocusApp is null ? "Choose an app to focus in Advanced startup." :
            !ValidStartup ? "Check Advanced startup and app startup options: pauses must be whole seconds from 0 to 300; timeouts from 1 to 600." : null;
    }
    private void NotifyAppCommands()
    {
        RemoveAppCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(AdvancedStartupHeading));
        NotifyValidation();
    }
    partial void OnEndWithAppChanged(bool value) => NotifyValidation();
    partial void OnMainAppChanged(AppEditorViewModel? value) => NotifyValidation();
    partial void OnLaunchModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsOrdered));
        OnPropertyChanged(nameof(OrderingHint));
        NotifyValidation();
    }
    partial void OnPauseBetweenAppsSecondsChanged(decimal? value) => NotifyValidation();
    partial void OnStartupFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(NeedsFocusApp));
        NotifyValidation();
    }
    partial void OnFocusAppChanged(AppEditorViewModel? value) => NotifyValidation();
    partial void OnValidationMessageChanged(string? value) => OnPropertyChanged(nameof(HasValidationMessage));
    partial void OnSelectedAppChanged(AppEditorViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedApp));
        OnPropertyChanged(nameof(AppOptionsHeading));
        OnPropertyChanged(nameof(SelectedAppSummary));
        NotifyAppCommands();
    }
}

public partial class AppEditorViewModel : ViewModelBase
{
    public Guid Id { get; }
    [ObservableProperty] private int _order;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _executablePath;
    [ObservableProperty] private string _arguments;
    [ObservableProperty] private string _workingDirectory;
    [ObservableProperty] private bool _runAsAdministrator;
    [ObservableProperty] private bool _allowForceQuit;
    [ObservableProperty] private int _readinessIndex;
    [ObservableProperty] private decimal? _readinessTimeoutSeconds = 30;
    [ObservableProperty] private bool _overridePause;
    [ObservableProperty] private decimal? _pauseAfterSeconds = 0;
    public string[] ReadinessChoices { get; } = ["Launch request completed", "Process is running", "A window appears"];
    public bool NeedsReadinessTimeout => ReadinessIndex != 0;
    public bool ValidStartup => ReadinessIndex is >= 0 and <= 2 &&
        SessionEditorViewModel.ValidSeconds(ReadinessTimeoutSeconds, 1, 600) &&
        SessionEditorViewModel.ValidSeconds(PauseAfterSeconds, 0, 300);
    partial void OnReadinessIndexChanged(int value) => OnPropertyChanged(nameof(NeedsReadinessTimeout));

    public string AccessibleName => $"{Order}. {Name}";
    partial void OnOrderChanged(int value) => OnPropertyChanged(nameof(AccessibleName));
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(AccessibleName));

    public AppEditorViewModel(StartProcessAction app)
    {
        Id = app.Id;
        _name = app.Name;
        _executablePath = app.ExecutablePath;
        _arguments = app.Arguments;
        _workingDirectory = app.WorkingDirectory;
        _runAsAdministrator = app.RunAsAdministrator;
        _allowForceQuit = app.AllowForceQuit;
        _readinessIndex = (int)app.Readiness;
        _readinessTimeoutSeconds = app.ReadinessTimeoutSeconds;
        _overridePause = app.PauseAfterSeconds.HasValue;
        _pauseAfterSeconds = app.PauseAfterSeconds ?? 0;
    }

    public StartProcessAction BuildAction() => new(Id, Name.Trim(), ExecutablePath.Trim(), Arguments, WorkingDirectory.Trim(), RunAsAdministrator,
        (AppReadiness)ReadinessIndex, (int)ReadinessTimeoutSeconds!.Value, OverridePause ? (int)PauseAfterSeconds!.Value : null, AllowForceQuit);
}
