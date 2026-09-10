using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sessions.App.Services;
using Sessions.Plugins;

namespace Sessions.App.ViewModels;

public sealed partial class PluginSettingsViewModel : ViewModelBase, IDisposable
{
    public PluginService Service { get; }
    [ObservableProperty] private IReadOnlyList<PluginPreferenceViewModel> _plugins = [];
    [ObservableProperty] private string? _status;
    public bool CanApply => Service.IsLoaded && !Service.IsBusy && !Service.HasLoadError;
    public bool CanChange => !Service.IsBusy && (Service.IsLoaded || Service.HasLoadError);

    public PluginSettingsViewModel(PluginService service)
    {
        Service = service;
        CopyCurrent();
        Service.PropertyChanged += Changed;
    }

    private void CopyCurrent()
    {
        var rows = Service.Catalog.Plugins.Select(plugin =>
        {
            Service.Current.TryGetValue(plugin.Descriptor.Id, out var configuration);
            return new PluginPreferenceViewModel(plugin.Descriptor, plugin.Settings, configuration,
                plugin.Descriptor.ApiVersion != PluginCatalog.ApiVersion);
        }).ToList();
        foreach (var (id, configuration) in Service.Current.Where(pair => rows.All(row => row.Id != pair.Key)))
            rows.Add(new(new(id, id, "Unavailable"), [], configuration, missing: true));
        Plugins = rows;
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginService.Current)) CopyCurrent();
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanChange));
        ApplyCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        RetryLoadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        Status = null;
        if (await Service.SaveAsync(Plugins.ToDictionary(plugin => plugin.Id, plugin => plugin.BuildConfiguration(), StringComparer.Ordinal)))
            Status = "Plugin preferences saved. Changes apply to the next launch; active Sessions keep their captured settings.";
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task ResetAsync()
    {
        Status = null;
        if (await Service.SaveAsync(new Dictionary<string, PluginConfiguration>(), reset: true))
            Status = "Plugin preferences reset. Your saved Sessions and appearance are unchanged.";
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task RetryLoadAsync() => await Service.LoadAsync();
    public void Dispose() => Service.PropertyChanged -= Changed;
}

public sealed partial class PluginPreferenceViewModel : ViewModelBase
{
    private readonly PluginConfiguration _original;
    private readonly string? _unavailable;
    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Status))] private bool _enabled;
    public string Status => _unavailable ?? (Enabled ? "Enabled · bundled plugin" : "Disabled · saved Session entries are kept");
    public IReadOnlyList<PluginSettingViewModel> Fields { get; }
    public bool CanEditFields => _unavailable is null;
    public string EnabledLabel => "Enable " + Name;

    public PluginPreferenceViewModel(PluginDescriptor descriptor, IReadOnlyList<PluginSetting> fields,
        PluginConfiguration? configuration, bool incompatible = false, bool missing = false)
    {
        Id = descriptor.Id;
        Name = descriptor.Name;
        Version = "Version " + descriptor.Version;
        _original = configuration ?? new(descriptor.EnabledByDefault, descriptor.SettingsVersion);
        Enabled = _original.Enabled;
        _unavailable = missing ? "Plugin missing. Your saved settings and Session entries are kept." : incompatible ?
            "Plugin API is incompatible. Install a compatible Sessions version." : _original.Version != descriptor.SettingsVersion ?
            "Plugin settings are incompatible. Your saved settings are kept." : null;
        Fields = fields.Select(field => new PluginSettingViewModel(field, _original.Settings?.GetValueOrDefault(field.Key) ?? "")).ToArray();
    }

    public PluginConfiguration BuildConfiguration()
    {
        var settings = _original.CaptureSettings().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (CanEditFields) foreach (var field in Fields) settings[field.Key] = field.Value;
        return _original with { Enabled = Enabled, Settings = settings };
    }
}

public sealed partial class PluginSettingViewModel(PluginSetting definition, string value) : ViewModelBase
{
    public string Key => definition.Key;
    public string Label => definition.Label;
    public string Help => definition.Help;
    [ObservableProperty] private string _value = value;
}
