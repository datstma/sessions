using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace Sessions.App.Services;

/// <summary>The main window's normal (restored) bounds: position in physical pixels, size in logical pixels.</summary>
public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool IsMaximized);

/// <summary>How the main window was left, restored on the next start. Not a Session or appearance preference.</summary>
public sealed record MainWindowState(WindowPlacement? Placement, Guid? SelectedSessionId);

public readonly record struct ScreenArea(PixelRect WorkingArea, double Scaling, bool IsPrimary);

public static class WindowPlacementPolicy
{
    /// <summary>
    /// Fits saved bounds to the current displays: the screen the window overlaps most, or the primary
    /// screen when that display is gone. Size is capped by the working area and never below the minimum.
    /// </summary>
    public static WindowPlacement? Fit(WindowPlacement saved, IReadOnlyList<ScreenArea> screens,
        Size minimum, Size decorationAllowance)
    {
        if (screens.Count == 0 || !double.IsFinite(saved.Width) || !double.IsFinite(saved.Height) ||
            saved.Width <= 0 || saved.Height <= 0)
            return null;

        var overlaps = screens.Select(screen => (Screen: screen, Area: Overlap(saved, screen))).ToArray();
        var best = overlaps.MaxBy(candidate => candidate.Area);
        var screen = best.Area > 0 ? best.Screen : screens.FirstOrDefault(candidate => candidate.IsPrimary, screens[0]);
        var area = screen.WorkingArea;

        var width = Math.Max(minimum.Width, Math.Min(saved.Width, area.Width / screen.Scaling - decorationAllowance.Width));
        var height = Math.Max(minimum.Height, Math.Min(saved.Height, area.Height / screen.Scaling - decorationAllowance.Height));
        var pixelWidth = (int)Math.Ceiling(width * screen.Scaling);
        var pixelHeight = (int)Math.Ceiling(height * screen.Scaling);

        int x, y;
        if (best.Area > 0)
        {
            // Keep the whole window, including its title bar, inside the working area.
            x = Clamp(saved.X, area.X, area.Right - pixelWidth);
            y = Clamp(saved.Y, area.Y, area.Bottom - pixelHeight);
        }
        else
        {
            x = area.X + Math.Max(0, (area.Width - pixelWidth) / 2);
            y = area.Y + Math.Max(0, (area.Height - pixelHeight) / 2);
        }
        return new WindowPlacement(x, y, width, height, saved.IsMaximized);
    }

    private static long Overlap(WindowPlacement saved, ScreenArea screen)
    {
        var bounds = new PixelRect(saved.X, saved.Y, (int)Math.Ceiling(saved.Width * screen.Scaling),
            (int)Math.Ceiling(saved.Height * screen.Scaling));
        var overlap = bounds.Intersect(screen.WorkingArea);
        return overlap.Width <= 0 || overlap.Height <= 0 ? 0 : (long)overlap.Width * overlap.Height;
    }

    // A window larger than the area starts at its top-left edge rather than above or left of it.
    private static int Clamp(int value, int minimum, int maximum) => Math.Max(minimum, Math.Min(value, maximum));
}
