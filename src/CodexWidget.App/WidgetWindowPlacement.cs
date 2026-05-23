using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CodexWidget.Core;

namespace CodexWidget.App;

internal readonly record struct WidgetScreenWorkArea(
    string ScreenKey,
    PixelRect WorkingArea,
    bool IsPrimary);

internal static class WidgetWindowPlacement
{
    public static bool ShouldRestorePersistedPlacement(PreferenceLoadResult loadResult)
    {
        ArgumentNullException.ThrowIfNull(loadResult);
        return !loadResult.UsedDefaults && loadResult.Availability.IsAvailable;
    }

    public static IReadOnlyList<WidgetScreenWorkArea> CreateScreenWorkAreas(Screens? screens)
    {
        if (screens is null)
        {
            return Array.Empty<WidgetScreenWorkArea>();
        }

        return screens.All
            .Select(screen => new WidgetScreenWorkArea(
                BuildScreenKey(screen),
                screen.WorkingArea,
                screen.IsPrimary))
            .ToArray();
    }

    public static string BuildScreenKey(Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var bounds = screen.Bounds;
        var area = screen.WorkingArea;
        var name = string.IsNullOrWhiteSpace(screen.DisplayName) ? "screen" : screen.DisplayName.Trim();
        return $"{name}|{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}|{area.X},{area.Y},{area.Width},{area.Height}|{screen.Scaling:F3}";
    }

    public static bool TryResolvePosition(
        WindowPlacementPreferences placement,
        IReadOnlyList<WidgetScreenWorkArea> screens,
        out PixelPoint position)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(screens);

        position = default;
        if (placement.Width <= 0 || placement.Height <= 0 || screens.Count == 0)
        {
            return false;
        }

        if (!IsVisible(placement, screens))
        {
            return false;
        }

        position = new PixelPoint(placement.X, placement.Y);
        return true;
    }

    public static bool IsVisible(
        WindowPlacementPreferences placement,
        IReadOnlyList<WidgetScreenWorkArea> screens)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(screens);

        if (placement.Width <= 0 || placement.Height <= 0)
        {
            return false;
        }

        if (screens.Count == 0)
        {
            return false;
        }

        var bounds = new PixelRect(placement.X, placement.Y, placement.Width, placement.Height);
        if (!string.IsNullOrWhiteSpace(placement.ScreenKey))
        {
            var keyedScreen = screens.FirstOrDefault(screen =>
                string.Equals(screen.ScreenKey, placement.ScreenKey, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(keyedScreen.ScreenKey)
                && HasIntersection(bounds, keyedScreen.WorkingArea))
            {
                return true;
            }
        }

        return screens.Any(screen => HasIntersection(bounds, screen.WorkingArea));
    }

    public static PixelPoint ResetPosition(PixelRect workArea, PixelSize windowSize, int margin)
    {
        var x = workArea.Right - windowSize.Width - margin;
        var y = workArea.Bottom - windowSize.Height - margin;
        return new PixelPoint(
            Math.Max(workArea.X, x),
            Math.Max(workArea.Y, y));
    }

    public static WindowPlacementPreferences Capture(
        PixelPoint position,
        PixelSize windowSize,
        string? screenKey)
    {
        return new WindowPlacementPreferences
        {
            X = position.X,
            Y = position.Y,
            Width = Math.Max(1, windowSize.Width),
            Height = Math.Max(1, windowSize.Height),
            ScreenKey = string.IsNullOrWhiteSpace(screenKey) ? null : screenKey.Trim(),
        };
    }

    private static bool HasIntersection(PixelRect windowRect, PixelRect workArea)
    {
        return windowRect.X < workArea.Right
            && windowRect.Right > workArea.X
            && windowRect.Y < workArea.Bottom
            && windowRect.Bottom > workArea.Y;
    }
}
