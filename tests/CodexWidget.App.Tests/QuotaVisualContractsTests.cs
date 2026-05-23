using Avalonia.Media;
using Avalonia.Media.Immutable;
using CodexWidget.App.Presentation.QuotaVisuals;

namespace CodexWidget.App.Tests;

public sealed class QuotaVisualContractsTests
{
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(42, 42)]
    [InlineData(100, 100)]
    [InlineData(155, 100)]
    public void ClampPercent_ClampsToZeroThroughOneHundred(int input, int expected)
    {
        var actual = QuotaVisualGeometry.ClampPercent(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CreateRing_NullQuota_ProducesUnavailableResultInsteadOfZeroPercent()
    {
        var geometry = QuotaVisualGeometry.CreateRing(null, 25);

        Assert.Equal(QuotaVisualAvailability.Unavailable, geometry.Availability);
        Assert.Null(geometry.ClampedQuotaPercent);
        Assert.Equal(25, geometry.ClampedTimePercent);
        Assert.Equal(0d, geometry.SweepAngleDegrees);
        Assert.Null(geometry.MarkerAngleDegrees);
    }

    [Fact]
    public void CreateRing_NullTimeLeft_OmitsMarker()
    {
        var geometry = QuotaVisualGeometry.CreateRing(50, null);

        Assert.Equal(QuotaVisualAvailability.Available, geometry.Availability);
        Assert.Equal(50, geometry.ClampedQuotaPercent);
        Assert.Null(geometry.ClampedTimePercent);
        Assert.Equal(180d, geometry.SweepAngleDegrees);
        Assert.Null(geometry.MarkerAngleDegrees);
    }

    [Fact]
    public void CreateRing_UsesBottomStartClockwiseWithClampedMarkerAngle()
    {
        var geometry = QuotaVisualGeometry.CreateRing(150, -10);

        Assert.Equal(QuotaVisualAvailability.Available, geometry.Availability);
        Assert.Equal(100, geometry.ClampedQuotaPercent);
        Assert.Equal(0, geometry.ClampedTimePercent);
        Assert.Equal(QuotaVisualGeometry.RingStartAngleDegrees, geometry.StartAngleDegrees);
        Assert.Equal(RingSweepDirection.Clockwise, geometry.SweepDirection);
        Assert.Equal(360d, geometry.SweepAngleDegrees);
        Assert.Equal(90d, geometry.MarkerAngleDegrees);
    }

    [Fact]
    public void CreateBar_NullQuota_ProducesUnavailableResult()
    {
        var geometry = QuotaVisualGeometry.CreateBar(null, 10);

        Assert.Equal(QuotaVisualAvailability.Unavailable, geometry.Availability);
        Assert.Null(geometry.ClampedQuotaPercent);
        Assert.Equal(10, geometry.ClampedTimePercent);
        Assert.Equal(0d, geometry.FillFraction);
        Assert.Null(geometry.MarkerFraction);
    }

    [Fact]
    public void CreateBar_ClampsFillAndMarkerFractions()
    {
        var geometry = QuotaVisualGeometry.CreateBar(-12, 190);

        Assert.Equal(QuotaVisualAvailability.Available, geometry.Availability);
        Assert.Equal(0, geometry.ClampedQuotaPercent);
        Assert.Equal(100, geometry.ClampedTimePercent);
        Assert.Equal(0d, geometry.FillFraction);
        Assert.Equal(1d, geometry.MarkerFraction);
    }

    [Fact]
    public void QuotaVisualStyles_LightModeTokenValuesRemainStable()
    {
        Assert.Equal(Color.Parse("#FF18A24A"), QuotaVisualStyles.QuotaFillColor);
        Assert.Equal(Color.Parse("#FF2563EB"), QuotaVisualStyles.QuotaFillBlueColor);
        Assert.Equal(Color.Parse("#FFEC4899"), QuotaVisualStyles.QuotaFillPinkColor);
        Assert.Equal(Color.Parse("#FFEAB308"), QuotaVisualStyles.QuotaFillYellowColor);
        Assert.Equal(Color.Parse("#FFDC2626"), QuotaVisualStyles.QuotaFillRedColor);
        Assert.Equal(Color.Parse("#FF6B7280"), QuotaVisualStyles.QuotaTrackColor);
        Assert.Equal(Color.Parse("#FF111111"), QuotaVisualStyles.QuotaMarkerColor);
        Assert.Equal(Color.Parse("#FF161616"), QuotaVisualStyles.QuotaTextColor);
        Assert.Equal(Color.Parse("#FF8A94A3"), QuotaVisualStyles.QuotaUnavailableColor);

        AssertBrushColor(QuotaVisualStyles.QuotaFillBrush, QuotaVisualStyles.QuotaFillColor);
        AssertBrushColor(QuotaVisualStyles.QuotaFillBlueBrush, QuotaVisualStyles.QuotaFillBlueColor);
        AssertBrushColor(QuotaVisualStyles.QuotaFillPinkBrush, QuotaVisualStyles.QuotaFillPinkColor);
        AssertBrushColor(QuotaVisualStyles.QuotaFillYellowBrush, QuotaVisualStyles.QuotaFillYellowColor);
        AssertBrushColor(QuotaVisualStyles.QuotaFillRedBrush, QuotaVisualStyles.QuotaFillRedColor);
        AssertBrushColor(QuotaVisualStyles.QuotaTrackBrush, QuotaVisualStyles.QuotaTrackColor);
        AssertBrushColor(QuotaVisualStyles.QuotaMarkerBrush, QuotaVisualStyles.QuotaMarkerColor);
        AssertBrushColor(QuotaVisualStyles.QuotaTextBrush, QuotaVisualStyles.QuotaTextColor);
        AssertBrushColor(QuotaVisualStyles.QuotaUnavailableBrush, QuotaVisualStyles.QuotaUnavailableColor);
    }

    [Theory]
    [InlineData(4, 20, true, "#FFDC2626")]
    [InlineData(5, 10, true, "#FFDC2626")]
    [InlineData(10, 20, true, "#FFEAB308")]
    [InlineData(10, 10, true, "#FF18A24A")]
    [InlineData(15, 20, true, "#FF18A24A")]
    [InlineData(20, 80, true, "#FFDC2626")]
    [InlineData(50, 80, true, "#FFEAB308")]
    [InlineData(80, 50, true, "#FF2563EB")]
    [InlineData(100, 40, true, "#FFEC4899")]
    [InlineData(1, 0, true, "#FFEC4899")]
    [InlineData(80, 50, false, "#FF18A24A")]
    [InlineData(50, 80, false, "#FFEAB308")]
    [InlineData(20, 80, false, "#FFDC2626")]
    [InlineData(1, 0, false, "#FF18A24A")]
    [InlineData(0, 0, false, "#FF18A24A")]
    [InlineData(90, 80, true, "#FF18A24A")]
    [InlineData(91, 80, true, "#FF2563EB")]
    [InlineData(100, 69, true, "#FFEC4899")]
    public void ResolveQuotaFillColor_UsesUsageGateThresholds(
        int quotaLeftPercent,
        int timeLeftPercent,
        bool useSurplusFillColors,
        string expectedColor)
    {
        var color = QuotaVisualStyles.ResolveQuotaFillColor(
            quotaLeftPercent,
            timeLeftPercent,
            useSurplusFillColors);

        Assert.Equal(Color.Parse(expectedColor), color);
    }

    [Fact]
    public void ResolveQuotaFillColor_MissingTimeOrQuotaUsesDefaultFill()
    {
        Assert.Equal(QuotaVisualStyles.QuotaFillColor, QuotaVisualStyles.ResolveQuotaFillColor(null, 50, useSurplusFillColors: true));
        Assert.Equal(QuotaVisualStyles.QuotaFillColor, QuotaVisualStyles.ResolveQuotaFillColor(50, null, useSurplusFillColors: true));
        Assert.Equal(QuotaVisualStyles.QuotaFillColor, QuotaVisualStyles.ResolveQuotaFillColor(null, 50, useSurplusFillColors: false));
        Assert.Equal(QuotaVisualStyles.QuotaFillColor, QuotaVisualStyles.ResolveQuotaFillColor(50, null, useSurplusFillColors: false));
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
