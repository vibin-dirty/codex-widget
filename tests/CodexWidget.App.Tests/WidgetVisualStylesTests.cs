using Avalonia.Media;
using Avalonia.Media.Immutable;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App.Tests;

public sealed class WidgetVisualStylesTests
{
    [Theory]
    [InlineData(WidgetRefreshVisualState.Idle, "●", "Idle")]
    [InlineData(WidgetRefreshVisualState.Refreshing, "↻", "Refreshing")]
    [InlineData(WidgetRefreshVisualState.Stale, "◴", "Stale")]
    [InlineData(WidgetRefreshVisualState.Warning, "▲", "Warning")]
    [InlineData(WidgetRefreshVisualState.Critical, "◆", "Critical")]
    [InlineData(WidgetRefreshVisualState.Error, "✕", "Error")]
    [InlineData((WidgetRefreshVisualState)999, "□", "Unavailable")]
    public void ResolveRefreshToken_MapsExpectedGlyphAndLabel(WidgetRefreshVisualState state, string glyph, string label)
    {
        var token = WidgetVisualStyles.ResolveRefreshToken(state);

        Assert.Equal(glyph, token.Glyph);
        Assert.Equal(label, token.Label);
    }

    [Theory]
    [InlineData(WidgetPresentationSeverity.Normal, "●", "Normal")]
    [InlineData(WidgetPresentationSeverity.Warning, "▲", "Warning")]
    [InlineData(WidgetPresentationSeverity.Critical, "◆", "Critical")]
    [InlineData(WidgetPresentationSeverity.Error, "✕", "Error")]
    [InlineData(WidgetPresentationSeverity.Unavailable, "□", "Unavailable")]
    [InlineData((WidgetPresentationSeverity)999, "□", "Unavailable")]
    public void ResolveMetricToken_MapsExpectedGlyphAndLabel(WidgetPresentationSeverity severity, string glyph, string label)
    {
        var token = WidgetVisualStyles.ResolveMetricToken(severity);

        Assert.Equal(glyph, token.Glyph);
        Assert.Equal(label, token.Label);
    }

    [Theory]
    [InlineData(true, "●", "Available")]
    [InlineData(false, "□", "Unavailable")]
    public void ResolveAvailabilityToken_MapsExpectedGlyphAndLabel(bool isAvailable, string glyph, string label)
    {
        var availability = isAvailable
            ? StatusAvailability.Available()
            : StatusAvailability.Unavailable(StatusAvailabilityCode.MissingRequiredField);

        var token = WidgetVisualStyles.ResolveAvailabilityToken(availability);

        Assert.Equal(glyph, token.Glyph);
        Assert.Equal(label, token.Label);
    }

    [Fact]
    public void ResolvePalette_DarkUsesGraphiteSurfacesAndReadableText()
    {
        var light = WidgetVisualStyles.ResolvePalette(WidgetThemePreference.Light);
        var dark = WidgetVisualStyles.ResolvePalette(WidgetThemePreference.Dark);

        AssertBrushColor(light.WidgetSurfaceBrush, Color.Parse("#FFE9EDF2"));
        AssertBrushColor(dark.WidgetSurfaceBrush, Color.Parse("#FF151A20"));
        AssertBrushColor(dark.WidgetCardBrush, Color.Parse("#FF1B222B"));
        AssertBrushColor(dark.PrimaryTextBrush, Color.Parse("#FFE8EEF6"));
        AssertBrushColor(dark.ActiveTextBrush, Color.Parse("#FF78E6A0"));
        AssertBrushColor(dark.ProgressTrackBrush, Color.Parse("#FF2A3441"));
    }

    private static void AssertBrushColor(IBrush brush, Color expected)
    {
        switch (brush)
        {
            case SolidColorBrush solidColorBrush:
                Assert.Equal(expected, solidColorBrush.Color);
                break;
            case ImmutableSolidColorBrush immutableSolidColorBrush:
                Assert.Equal(expected, immutableSolidColorBrush.Color);
                break;
            default:
                throw new Xunit.Sdk.XunitException($"Unexpected brush type: {brush.GetType().FullName}");
        }
    }
}
