using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Sessions.App.Services;

namespace Sessions.App.ViewModels;

public sealed partial class LicensesViewModel : ViewModelBase
{
    private int _loadVersion;
    public IReadOnlyList<LicenseDocument> Documents { get; }
    [ObservableProperty] private LicenseDocument? _selectedDocument;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string? _problem;
    public bool HasProblem => Problem is not null;
    /// <summary>The most recent read, so callers can wait for the shown document.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    public LicensesViewModel(string installFolder)
    {
        Documents = LicenseCatalog.Discover(installFolder);
        SelectedDocument = Documents[0];
    }

    partial void OnProblemChanged(string? value) => OnPropertyChanged(nameof(HasProblem));
    partial void OnSelectedDocumentChanged(LicenseDocument? value) => Loading = LoadAsync(value);

    private async Task LoadAsync(LicenseDocument? document)
    {
        var version = ++_loadVersion;
        Text = "";
        Problem = null;
        if (document is null) return;
        var result = await LicenseCatalog.ReadAsync(document);
        // A slower read must not replace a document chosen later.
        if (version != _loadVersion) return;
        Text = result.Text ?? "";
        Problem = result.Problem;
    }
}
