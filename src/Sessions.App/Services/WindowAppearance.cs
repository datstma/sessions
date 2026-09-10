using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Sessions.App.Services;

/// <summary>Applies presentation preferences without changing OS DPI or Session state.</summary>
public sealed class WindowAppearance : IDisposable
{
    private readonly Window _window;
    private readonly LayoutTransformControl? _root;
    private readonly PreferencesService _preferences;
    public double EffectiveScale { get; private set; } = 1;
    private static readonly string[] FontRoles = ["DisplayL", "DisplayM", "Title", "Subtitle", "BodyStrong", "Body", "Caption", "Micro", "Label"];

    public WindowAppearance(Window window, LayoutTransformControl? root, PreferencesService preferences)
    {
        _window = window;
        _root = root;
        _preferences = preferences;
        preferences.PropertyChanged += Changed;
        window.SizeChanged += Resized;
        Apply();
    }

    public static ThemeVariant ThemeVariantFor(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };

    public static void ApplyTypography(StyledElement target, int percent)
    {
        foreach (var role in FontRoles)
        {
            foreach (var suffix in new[] { "Size", "LineHeight" })
            {
                var key = "Font" + role + suffix;
                if (Application.Current!.TryFindResource(key, out var value) && value is double size)
                    target.Resources[key] = size * percent / 100d;
            }
        }
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreferencesService.Current)) Apply();
    }

    private void Apply()
    {
        _window.RequestedThemeVariant = ThemeVariantFor(_preferences.Current.Theme);
        ApplyTypography(_window, _preferences.Current.TextPercent);
        UpdateScale();
    }

    private void Resized(object? sender, SizeChangedEventArgs e) => UpdateScale();

    private void UpdateScale()
    {
        if (_root is null) return;
        var width = _window.Bounds.Width > 0 ? _window.Bounds.Width : _window.Width;
        var height = _window.Bounds.Height > 0 ? _window.Bounds.Height : _window.Height;
        // Preserve the established minimum logical viewport instead of hiding fixed run/editor controls.
        EffectiveScale = Math.Max(1, Math.Min(_preferences.Current.InterfacePercent / 100d,
            Math.Min(width / _window.MinWidth, height / _window.MinHeight)));
        _root.LayoutTransform = new ScaleTransform(EffectiveScale, EffectiveScale);
        _window.Classes.Set("compact", width / EffectiveScale < (double)_window.FindResource("CompactBreakpoint")!);
    }

    public void Dispose()
    {
        _preferences.PropertyChanged -= Changed;
        _window.SizeChanged -= Resized;
    }
}
