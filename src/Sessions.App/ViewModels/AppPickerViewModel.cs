using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sessions.App.Services;

namespace Sessions.App.ViewModels;

public partial class AppPickerViewModel : ViewModelBase, IDisposable
{
    private readonly IAppSource _source;
    private readonly IAppSource? _startMenuSource;
    private readonly IAppSource? _pluginSource;
    private readonly HashSet<(string, string)> _existingPluginTargets;
    private IReadOnlyList<DiscoveredApp> _pluginApps = [];
    private IReadOnlyList<DiscoveredApp> _runningApps = [];
    private IReadOnlyList<DiscoveredApp> _startMenuApps = [];
    private readonly HashSet<string> _existingPaths;
    private readonly List<AppChoiceViewModel> _choices = [];
    private readonly List<DiscoveredApp> _browsed = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public ObservableCollection<AppChoiceViewModel> VisibleApps { get; } = [];
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _showStartMenu;
    [ObservableProperty] private bool _showPlugins;
    public bool HasPluginSource => _pluginSource is not null;
    public bool HasStartMenuSource => _startMenuSource is not null;
    public bool ShowRunningApps { get => !ShowStartMenu && !ShowPlugins; set { if (value) { ShowStartMenu = false; ShowPlugins = false; } } }
    public string SourceDescription => ShowPlugins ? "Manage plugins in Settings. Choose optional closing in app options." : ShowStartMenu
        ? "Desktop shortcuts from your Start menu. Shortcut launch settings are kept when you add an app."
        : "Apps with open windows. Adds the app without its open documents or browser tabs.";
    public bool HasError => ErrorMessage is not null;
    public bool IsEmpty => !IsLoading && VisibleApps.Count == 0;
    public string EmptyMessage => string.IsNullOrWhiteSpace(SearchText)
        ? (ShowPlugins ? "No plugin apps found. Check Settings → Plugins, then refresh." : ShowStartMenu ? "No Start menu apps found. Try Refresh or Browse files." : "No apps with open windows found. Open an app and refresh, or browse for it.")
        : "No matching apps. Try another name, or browse for an app.";
    public int SelectedCount => _choices.Count(app => app.IsSelected && app.CanSelect);
    public bool CanAdd => !IsLoading && SelectedCount > 0;
    public string AddLabel => SelectedCount == 1 ? "Add 1 app" : $"Add {SelectedCount} apps";

    public AppPickerViewModel(IAppSource source, IEnumerable<string> existingPaths, IAppSource? startMenuSource = null,
        IAppSource? pluginSource = null, IEnumerable<Sessions.Core.PluginAppReference>? existingPlugins = null)
    {
        _source = source;
        _startMenuSource = startMenuSource;
        _pluginSource = pluginSource;
        _existingPluginTargets = (existingPlugins ?? []).Select(plugin => (plugin.PluginId, plugin.TargetId)).ToHashSet();
        _showStartMenu = startMenuSource is not null;
        _existingPaths = new HashSet<string>(existingPaths.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
    }

    private bool CanRefresh() => !IsLoading && !_disposed;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!CanRefresh()) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            // Each source can fail independently without discarding the other list or prior selections.
            var errors = await Task.WhenAll(LoadSourceAsync(_source, AppDiscoveryOrigin.Running),
                LoadSourceAsync(_startMenuSource, AppDiscoveryOrigin.StartMenu), LoadSourceAsync(_pluginSource, AppDiscoveryOrigin.Plugin));
            if (_disposed) return;
            ErrorMessage = string.Join(" ", errors.Where(error => error is not null));
            if (ErrorMessage.Length == 0) ErrorMessage = null;
            var selected = GetSelection().Select(ChoiceKey).ToHashSet(StringComparer.Ordinal);
            ClearChoices();
            // Keep different shortcut launch settings distinct; duplicate windows/identical shortcuts merge.
            foreach (var app in _startMenuApps.Concat(_runningApps).Concat(_pluginApps).Concat(_browsed)
                         .DistinctBy(ChoiceKey, StringComparer.Ordinal)
                         .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase))
                AddChoice(app, selected.Contains(ChoiceKey(app)));
            ApplyFilter();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               Win32Exception or PlatformNotSupportedException)
        {
            if (!_disposed) ErrorMessage = "Couldn't refresh apps. Try again or use Browse files. " + exception.Message;
        }
        finally { if (!_disposed) IsLoading = false; }
    }

    private async Task<string?> LoadSourceAsync(IAppSource? source, AppDiscoveryOrigin origin)
    {
        if (source is null) return null;
        try
        {
            var apps = await source.GetAppsAsync(_lifetime.Token);
            if (!_disposed)
            {
                var tagged = apps.Select(app => app with { Origin = origin }).ToArray();
                if (origin == AppDiscoveryOrigin.Plugin) _pluginApps = tagged;
                else if (origin == AppDiscoveryOrigin.StartMenu) _startMenuApps = tagged;
                else _runningApps = tagged;
            }
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or COMException or PlatformNotSupportedException)
        {
            // A failed plugin refresh must not leave formerly enabled plugin choices selectable.
            if (origin == AppDiscoveryOrigin.Plugin) _pluginApps = [];
            return $"Couldn't refresh {(origin == AppDiscoveryOrigin.Plugin ? "plugin" : origin == AppDiscoveryOrigin.StartMenu ? "Start menu" : "running")} apps. Try again or use Browse files. {exception.Message}";
        }
    }

    private static string ChoiceKey(DiscoveredApp app) => string.Join('\0', app.Origin.ToString(),
        app.Plugin is { } plugin ? plugin.PluginId + "\0" + plugin.TargetId : app.ExecutablePath is { } path ? NormalizePath(path).ToUpperInvariant() : app.Name + app.Location,
        app.Arguments, NormalizeDirectory(app.WorkingDirectory), app.RunAsAdministrator.ToString());
    private static string NormalizeDirectory(string path) => string.IsNullOrWhiteSpace(path) ? "" : NormalizePath(path).ToUpperInvariant();

    public void AddBrowsedApps(IEnumerable<string> paths)
    {
        if (_disposed || IsLoading) return;
        foreach (var path in paths)
        {
            var normalized = NormalizePath(path);
            var existing = _choices.FirstOrDefault(app => app.App.ExecutablePath is { } item &&
                string.Equals(NormalizePath(item), normalized, StringComparison.OrdinalIgnoreCase) &&
                app.App.Arguments.Length == 0 && app.App.WorkingDirectory.Length == 0 && !app.App.RunAsAdministrator);
            if (existing is not null)
            {
                if (existing.CanSelect) existing.IsSelected = true;
                continue;
            }
            var app = new DiscoveredApp(Path.GetFileNameWithoutExtension(path), path, Origin: AppDiscoveryOrigin.Browsed);
            _browsed.Add(app);
            AddChoice(app, selected: true);
        }
        SearchText = "";
        ApplyFilter();
    }

    public IReadOnlyList<DiscoveredApp> GetSelection() => _choices.Where(app => app.IsSelected && app.CanSelect)
        .Select(app => app.App).ToArray();

    private void AddChoice(DiscoveredApp app, bool selected)
    {
        var alreadyAdded = app.Plugin is { } plugin ? _existingPluginTargets.Contains((plugin.PluginId, plugin.TargetId)) :
            app.ExecutablePath is { } path && _existingPaths.Contains(NormalizePath(path));
        var choice = new AppChoiceViewModel(app, alreadyAdded);
        choice.PropertyChanged += ChoiceChanged;
        _choices.Add(choice);
        choice.IsSelected = selected && choice.CanSelect;
    }

    private void ChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppChoiceViewModel.IsSelected) && sender is AppChoiceViewModel { IsSelected: true, CanSelect: true } selected &&
            selected.App.ExecutablePath is { } path)
            foreach (var other in _choices.Where(other => !ReferenceEquals(other, selected) && other.IsSelected &&
                         other.App.ExecutablePath is { } candidate && string.Equals(NormalizePath(candidate), NormalizePath(path), StringComparison.OrdinalIgnoreCase)))
                other.IsSelected = false; // One executable per batch; selecting a shortcut replaces the bare running-app choice.
        NotifySelection();
    }
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(AddLabel));
    }

    private void ApplyFilter()
    {
        VisibleApps.Clear();
        var search = SearchText.Trim();
        foreach (var app in _choices.Where(app => (app.App.Origin == AppDiscoveryOrigin.Browsed ||
                     app.App.Origin == (ShowPlugins ? AppDiscoveryOrigin.Plugin : ShowStartMenu ? AppDiscoveryOrigin.StartMenu : AppDiscoveryOrigin.Running)) &&
                     (app.App.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                     app.App.Location.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                     (app.App.ExecutablePath?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))))
            VisibleApps.Add(app);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        NotifySelection();
    }

    internal static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException) { return path.Trim(); }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShowStartMenuChanged(bool value)
    {
        if (value) ShowPlugins = false;
        OnPropertyChanged(nameof(ShowRunningApps));
        OnPropertyChanged(nameof(SourceDescription));
        ApplyFilter();
    }
    partial void OnShowPluginsChanged(bool value)
    {
        if (value) ShowStartMenu = false;
        OnPropertyChanged(nameof(ShowRunningApps));
        OnPropertyChanged(nameof(SourceDescription));
        ApplyFilter();
    }
    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        NotifySelection();
        RefreshCommand.NotifyCanExecuteChanged();
    }
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void ClearChoices()
    {
        VisibleApps.Clear();
        foreach (var choice in _choices) { choice.PropertyChanged -= ChoiceChanged; choice.Dispose(); }
        _choices.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        ClearChoices();
    }
}

public partial class AppChoiceViewModel : ViewModelBase, IDisposable
{
    public DiscoveredApp App { get; }
    public bool CanSelect { get; }
    public string Status { get; }
    public Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;
    public string Initial => System.Globalization.StringInfo.GetNextTextElement(App.Name).ToUpperInvariant();
    [ObservableProperty] private bool _isSelected;

    public AppChoiceViewModel(DiscoveredApp app, bool alreadyAdded)
    {
        App = app;
        CanSelect = !alreadyAdded && (app.ExecutablePath is not null || app.Plugin is not null) && app.UnavailableReason is null;
        Status = alreadyAdded ? "Already in this Session" : app.UnavailableReason ?? (app.Origin switch
        {
            AppDiscoveryOrigin.StartMenu => string.IsNullOrWhiteSpace(app.Location) || app.Location == "Start menu" ? "Start menu" : "Start menu · " + app.Location,
            AppDiscoveryOrigin.Running => "Running now",
            AppDiscoveryOrigin.Plugin => "Plugin app · optional closing in app options",
            _ => "Chosen file"
        });
        if (app.IconPng is not null)
        {
            try { using var stream = new MemoryStream(app.IconPng); Icon = new Bitmap(stream); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                                   NotSupportedException or IOException) { }
        }
    }

    public void Dispose() => Icon?.Dispose();
}
