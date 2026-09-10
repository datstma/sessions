using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sessions.App.Services;

namespace Sessions.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase, IDisposable
{
    public PreferencesService Preferences { get; }
    public IReadOnlyList<AppTheme> Themes { get; } = Array.AsReadOnly(Enum.GetValues<AppTheme>());
    public IReadOnlyList<int> InterfaceSizes => AppPreferences.InterfaceSizes;
    public IReadOnlyList<int> TextSizes => AppPreferences.TextSizes;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Preview))] private AppTheme _theme;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Preview))] private int _interfacePercent;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Preview))] private int _textPercent;
    [ObservableProperty] private string? _status;
    public AppPreferences Preview => new(Theme, InterfacePercent, TextPercent);
    public bool CanApply => !Preferences.IsBusy && !Preferences.HasLoadError;
    public bool CanChange => !Preferences.IsBusy;

    public SettingsViewModel(PreferencesService preferences)
    {
        Preferences = preferences;
        CopyCurrent();
        Preferences.PropertyChanged += PreferencesChanged;
    }

    private void CopyCurrent()
    {
        Theme = Preferences.Current.Theme;
        InterfacePercent = Preferences.Current.InterfacePercent;
        TextPercent = Preferences.Current.TextPercent;
    }

    private void PreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanChange));
        ApplyCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        RetryLoadCommand.NotifyCanExecuteChanged();
        if (e.PropertyName == nameof(PreferencesService.Current)) CopyCurrent();
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        Status = null;
        if (await Preferences.SaveAsync(Preview)) Status = "Preferences saved.";
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task ResetAsync()
    {
        Status = null;
        if (await Preferences.SaveAsync(new(), reset: true))
        {
            CopyCurrent();
            Status = "Preferences reset. Your Sessions are unchanged.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task RetryLoadAsync()
    {
        Status = null;
        await Preferences.LoadAsync();
        if (!Preferences.HasLoadError) CopyCurrent();
    }

    public void Dispose() => Preferences.PropertyChanged -= PreferencesChanged;
}
