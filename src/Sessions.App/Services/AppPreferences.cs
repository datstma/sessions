using System;
using System.Collections.Generic;

namespace Sessions.App.Services;

public enum AppTheme { System, Light, Dark }

/// <summary>Presentation preferences, independent of Session definitions and runtime state.</summary>
public sealed record AppPreferences(AppTheme Theme = AppTheme.System, int InterfacePercent = 100, int TextPercent = 100)
{
    public static IReadOnlyList<int> InterfaceSizes { get; } = Array.AsReadOnly(new[] { 100, 110, 125, 150 });
    public static IReadOnlyList<int> TextSizes { get; } = Array.AsReadOnly(new[] { 100, 110, 125 });

    public void Validate()
    {
        if (!Enum.IsDefined(Theme) || !Contains(InterfaceSizes, InterfacePercent) || !Contains(TextSizes, TextPercent))
            throw new ArgumentException("These appearance preferences are not supported.");
    }

    private static bool Contains(IReadOnlyList<int> values, int value)
    {
        foreach (var candidate in values) if (candidate == value) return true;
        return false;
    }
}
