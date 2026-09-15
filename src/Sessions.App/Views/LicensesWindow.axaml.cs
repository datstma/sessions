using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Sessions.App.Services;
using Sessions.App.ViewModels;

namespace Sessions.App.Views;

public partial class LicensesWindow : Window
{
    public LicensesWindow() : this(new LicensesViewModel(System.AppContext.BaseDirectory)) { }

    public LicensesWindow(LicensesViewModel model, PreferencesService? preferences = null)
    {
        InitializeComponent();
        DataContext = model;
        var appearance = preferences is null ? null : new WindowAppearance(this, null, preferences);
        Opened += (_, _) => DocumentChoice.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || DocumentChoice.IsDropDownOpen) return;
            Close();
            e.Handled = true;
        };
        Closed += (_, _) => appearance?.Dispose();
    }

    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();
}
