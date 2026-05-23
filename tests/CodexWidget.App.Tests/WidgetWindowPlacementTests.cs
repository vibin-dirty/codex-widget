using Avalonia;
using CodexWidget.Core;

namespace CodexWidget.App.Tests;

public sealed class WidgetWindowPlacementTests
{
    [Fact]
    public void ShouldRestorePersistedPlacement_ReturnsTrue_ForLoadedPreferenceFile()
    {
        var loadResult = new PreferenceLoadResult
        {
            UsedDefaults = false,
            Availability = StatusAvailability.Available(),
        };

        var shouldRestore = WidgetWindowPlacement.ShouldRestorePersistedPlacement(loadResult);

        Assert.True(shouldRestore);
    }

    [Fact]
    public void ShouldRestorePersistedPlacement_ReturnsFalse_WhenDefaultsWereUsed()
    {
        var loadResult = new PreferenceLoadResult
        {
            UsedDefaults = true,
            Availability = StatusAvailability.Available(),
        };

        var shouldRestore = WidgetWindowPlacement.ShouldRestorePersistedPlacement(loadResult);

        Assert.False(shouldRestore);
    }

    [Fact]
    public void ShouldRestorePersistedPlacement_ReturnsFalse_WhenLoadWasUnavailable()
    {
        var loadResult = new PreferenceLoadResult
        {
            UsedDefaults = false,
            Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Malformed),
        };

        var shouldRestore = WidgetWindowPlacement.ShouldRestorePersistedPlacement(loadResult);

        Assert.False(shouldRestore);
    }

    [Fact]
    public void TryResolvePosition_ReturnsTrue_WhenPlacementIntersectsWorkingArea()
    {
        var placement = new WindowPlacementPreferences
        {
            X = 120,
            Y = 80,
            Width = 460,
            Height = 560,
            ScreenKey = "primary",
        };
        var screens = new[]
        {
            new WidgetScreenWorkArea("primary", new PixelRect(0, 0, 1920, 1080), IsPrimary: true),
        };

        var resolved = WidgetWindowPlacement.TryResolvePosition(placement, screens, out var position);

        Assert.True(resolved);
        Assert.Equal(new PixelPoint(120, 80), position);
    }

    [Fact]
    public void TryResolvePosition_ReturnsFalse_WhenPlacementIsOffScreen()
    {
        var placement = new WindowPlacementPreferences
        {
            X = 4000,
            Y = 3000,
            Width = 460,
            Height = 560,
            ScreenKey = "primary",
        };
        var screens = new[]
        {
            new WidgetScreenWorkArea("primary", new PixelRect(0, 0, 1920, 1080), IsPrimary: true),
        };

        var resolved = WidgetWindowPlacement.TryResolvePosition(placement, screens, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolvePosition_UsesAnyVisibleScreen_WhenScreenKeyChanges()
    {
        var placement = new WindowPlacementPreferences
        {
            X = 1940,
            Y = 120,
            Width = 460,
            Height = 560,
            ScreenKey = "stale-key",
        };
        var screens = new[]
        {
            new WidgetScreenWorkArea("primary", new PixelRect(0, 0, 1920, 1080), IsPrimary: true),
            new WidgetScreenWorkArea("secondary", new PixelRect(1920, 0, 1920, 1080), IsPrimary: false),
        };

        var resolved = WidgetWindowPlacement.TryResolvePosition(placement, screens, out var position);

        Assert.True(resolved);
        Assert.Equal(new PixelPoint(1940, 120), position);
    }

    [Fact]
    public void ResetPosition_PlacesWindowInsideWorkAreaBottomRight()
    {
        var workArea = new PixelRect(100, 50, 1200, 900);
        var position = WidgetWindowPlacement.ResetPosition(
            workArea,
            new PixelSize(460, 560),
            margin: 16);

        Assert.Equal(new PixelPoint(824, 374), position);
    }

    [Fact]
    public void Capture_NormalizesSizeAndScreenKey()
    {
        var placement = WidgetWindowPlacement.Capture(
            new PixelPoint(20, 30),
            new PixelSize(0, -5),
            "  screen-a ");

        Assert.Equal(20, placement.X);
        Assert.Equal(30, placement.Y);
        Assert.Equal(1, placement.Width);
        Assert.Equal(1, placement.Height);
        Assert.Equal("screen-a", placement.ScreenKey);
    }
}
