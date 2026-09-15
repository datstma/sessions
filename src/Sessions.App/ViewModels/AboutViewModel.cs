using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sessions.App.Services;

namespace Sessions.App.ViewModels;

/// <summary>Version, license and local-data information. Opening links or folders never sends anything.</summary>
public sealed partial class AboutViewModel(AppInfo info, ILauncher launcher) : ViewModelBase
{
    public string VersionLabel => string.IsNullOrWhiteSpace(info.Stage) ? $"Version {info.Version}" : $"Version {info.Version} {info.Stage}";
    public string DataFolder => info.DataFolder;
    public string InstallFolder => info.InstallFolder;
    public bool HasRepository => Uri.TryCreate(info.RepositoryUrl, UriKind.Absolute, out _);
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _isStatusError;

    [RelayCommand(CanExecute = nameof(HasRepository))]
    private Task OpenReleaseNotesAsync() => OpenWebAsync(info.RepositoryUrl + "/releases");

    [RelayCommand(CanExecute = nameof(HasRepository))]
    private Task ReportIssueAsync() => OpenWebAsync(info.RepositoryUrl + "/issues");

    [RelayCommand(CanExecute = nameof(HasRepository))]
    private Task OpenSourceCodeAsync() => OpenWebAsync(info.RepositoryUrl);

    [RelayCommand]
    private Task OpenDataFolderAsync()
    {
        if (!Directory.Exists(info.DataFolder))
        {
            SetStatus($"Sessions hasn't saved anything on this computer yet. Your data folder will be {info.DataFolder}", error: false);
            return Task.CompletedTask;
        }
        return OpenAsync(() => launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(info.DataFolder)),
            $"Couldn't open your data folder. You can find it at {info.DataFolder}");
    }

    private Task OpenWebAsync(string address) =>
        OpenAsync(() => launcher.LaunchUriAsync(new Uri(address)), $"Couldn't open your web browser. The address is {address}");

    private async Task OpenAsync(Func<Task<bool>> open, string failure)
    {
        SetStatus(null, error: false);
        bool opened;
        try { opened = await open(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { opened = false; }
        if (!opened) SetStatus(failure, error: true);
    }

    private void SetStatus(string? message, bool error)
    {
        Status = message;
        IsStatusError = error;
    }
}
