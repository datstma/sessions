using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sessions.App.Services;

/// <summary>Serializes preference operations; only successful saves change the applied appearance.</summary>
public sealed partial class PreferencesService(IPreferencesStore? store = null) : ObservableObject
{
    [ObservableProperty] private AppPreferences _current = new();
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasLoadError;
    [ObservableProperty] private string? _errorMessage;

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Current = store is null ? new() : await store.LoadAsync();
            HasLoadError = false;
            ErrorMessage = null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            HasLoadError = true;
            ErrorMessage = "Your preferences couldn't be loaded. Default appearance is available. Try again, or reset preferences to keep a backup and start fresh. " + exception.Message;
        }
        finally { IsBusy = false; }
    }

    public async Task<bool> SaveAsync(AppPreferences preferences, bool reset = false)
    {
        if (IsBusy || HasLoadError && !reset) return false;
        preferences.Validate();
        IsBusy = true;
        try
        {
            if (store is not null) await store.SaveAsync(preferences, HasLoadError);
            Current = preferences;
            HasLoadError = false;
            ErrorMessage = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = "Your preferences couldn't be saved. Your current appearance is unchanged. Try again. " + exception.Message;
            return false;
        }
        finally { IsBusy = false; }
    }
}
