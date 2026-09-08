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

    public bool HasApps => Apps.Count > 0;
    public bool HasSelectedApp => SelectedApp is not null;
    public bool HasValidationMessage => ValidationMessage is not null;
    public bool CanSave => !string.IsNullOrWhiteSpace(Name) &&
                           Apps.All(app => !string.IsNullOrWhiteSpace(app.Name) && !string.IsNullOrWhiteSpace(app.ExecutablePath)) &&
                           (!EndWithApp || MainApp is not null && Apps.Contains(MainApp));

    public SessionEditorViewModel(SessionDefinition? definition = null)
    {
        IsNew = definition is null;
        _id = definition?.Id ?? Guid.NewGuid();
        Name = definition?.Name ?? "";
        Description = definition?.Description ?? "";
        Apps.CollectionChanged += AppsChanged;
        foreach (var app in definition?.Apps ?? [])
            Apps.Add(new AppEditorViewModel(app));
        MainApp = Apps.FirstOrDefault(app => app.Id == definition?.MainAppId);
        EndWithApp = MainApp is not null;
        SelectedApp = Apps.FirstOrDefault();
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
        var definition = new SessionDefinition(_id, Name.Trim(), Description.Trim(),
            Apps.Select(app => app.BuildAction()).ToArray(), EndWithApp ? MainApp?.Id : null);
        if (!CanSave)
            throw new ArgumentException("Add a name and choose an app if the Session should end with it.");
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

    private void AppChanged(object? sender, PropertyChangedEventArgs e) => NotifyValidation();
    private void NotifyValidation()
    {
        OnPropertyChanged(nameof(CanSave));
        ValidationMessage = EndWithApp && MainApp is null
            ? "Choose which app should end this Session." : null;
    }
    private void NotifyAppCommands()
    {
        RemoveAppCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
    partial void OnNameChanged(string value) => NotifyValidation();
    partial void OnEndWithAppChanged(bool value) => NotifyValidation();
    partial void OnMainAppChanged(AppEditorViewModel? value) => NotifyValidation();
    partial void OnValidationMessageChanged(string? value) => OnPropertyChanged(nameof(HasValidationMessage));
    partial void OnSelectedAppChanged(AppEditorViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedApp));
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

    public AppEditorViewModel(StartProcessAction app)
    {
        Id = app.Id;
        _name = app.Name;
        _executablePath = app.ExecutablePath;
        _arguments = app.Arguments;
        _workingDirectory = app.WorkingDirectory;
        _runAsAdministrator = app.RunAsAdministrator;
    }

    public StartProcessAction BuildAction() => new(Id, Name.Trim(), ExecutablePath.Trim(), Arguments, WorkingDirectory.Trim(), RunAsAdministrator);
}
