using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Sessions.App.Services;
using Sessions.App.ViewModels;

namespace Sessions.App.Views;

/// <summary>Presentation-only executable icon with a name fallback and a bounded visual lifetime.</summary>
public partial class AppIcon : UserControl
{
    public static readonly StyledProperty<string?> ExecutablePathProperty =
        AvaloniaProperty.Register<AppIcon, string?>(nameof(ExecutablePath));
    public static readonly StyledProperty<string?> AppNameProperty =
        AvaloniaProperty.Register<AppIcon, string?>(nameof(AppName));
    public string? ExecutablePath { get => GetValue(ExecutablePathProperty); set => SetValue(ExecutablePathProperty, value); }
    public string? AppName { get => GetValue(AppNameProperty); set => SetValue(AppNameProperty, value); }
    public IAppIconSource IconSource { get; init; } = WindowsAppIconSource.Shared;
    private Bitmap? _bitmap;
    private int _generation;
    private bool _attached;

    public AppIcon() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AppNameProperty)
            Fallback.Text = string.IsNullOrWhiteSpace(AppName) ? "?" : System.Globalization.StringInfo.GetNextTextElement(AppName).ToUpperInvariant();
        if (change.Property == ExecutablePathProperty && _attached) _ = LoadIconAsync();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _ = LoadIconAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _generation++;
        ClearIcon();
        base.OnDetachedFromVisualTree(e);
    }

    private async Task LoadIconAsync()
    {
        var generation = ++_generation;
        ClearIcon();
        if (string.IsNullOrWhiteSpace(ExecutablePath)) return;
        try
        {
            var bytes = await IconSource.GetIconAsync(ExecutablePath);
            if (!_attached || generation != _generation || bytes is null) return;
            using var stream = new MemoryStream(bytes);
            _bitmap = new Bitmap(stream);
            IconImage.Source = _bitmap;
            IconImage.IsVisible = true;
            Fallback.IsVisible = false;
        }
        catch (Exception exception) when (SessionAppRow.IsObservationError(exception) || exception is NotSupportedException)
        {
            // Missing, inaccessible or malformed artwork never changes app settings or launch availability.
        }
    }

    private void ClearIcon()
    {
        IconImage.Source = null;
        IconImage.IsVisible = false;
        Fallback.IsVisible = true;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
