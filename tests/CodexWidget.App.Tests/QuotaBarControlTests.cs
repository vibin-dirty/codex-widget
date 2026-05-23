using Avalonia.Automation;
using Avalonia.Media;
using CodexWidget.App.Controls;
using CodexWidget.App.Presentation.QuotaVisuals;

namespace CodexWidget.App.Tests;

public sealed class QuotaBarControlTests
{
    [Fact]
    public void CreateRenderModel_ClampsFillFractionFromQuotaPercent()
    {
        var empty = QuotaBarControl.CreateRenderModel(-30, 20, null, isUnavailable: false);
        var full = QuotaBarControl.CreateRenderModel(150, 20, null, isUnavailable: false);

        Assert.Equal(QuotaVisualAvailability.Available, empty.Geometry.Availability);
        Assert.Equal(0, empty.Geometry.ClampedQuotaPercent);
        Assert.Equal(0d, empty.Geometry.FillFraction);

        Assert.Equal(100, full.Geometry.ClampedQuotaPercent);
        Assert.Equal(1d, full.Geometry.FillFraction);
    }

    [Fact]
    public void CreateRenderModel_ClampsMarkerFractionFromTimeLeftPercent()
    {
        var atStart = QuotaBarControl.CreateRenderModel(70, -10, null, isUnavailable: false);
        var atEnd = QuotaBarControl.CreateRenderModel(70, 180, null, isUnavailable: false);

        Assert.Equal(0, atStart.Geometry.ClampedTimePercent);
        Assert.Equal(0d, atStart.Geometry.MarkerFraction);

        Assert.Equal(100, atEnd.Geometry.ClampedTimePercent);
        Assert.Equal(1d, atEnd.Geometry.MarkerFraction);
    }

    [Fact]
    public void CreateRenderModel_OmitsMarkerWhenTimeLeftIsUnavailable()
    {
        var model = QuotaBarControl.CreateRenderModel(60, null, null, isUnavailable: false);

        Assert.Null(model.Geometry.ClampedTimePercent);
        Assert.Null(model.Geometry.MarkerFraction);
    }

    [Fact]
    public void CreateRenderModel_UnavailableQuotaUsesNeutralGeometryAndName()
    {
        var model = QuotaBarControl.CreateRenderModel(null, 50, null, isUnavailable: false);

        Assert.Equal(QuotaVisualAvailability.Unavailable, model.Geometry.Availability);
        Assert.Null(model.Geometry.ClampedQuotaPercent);
        Assert.Equal(0d, model.Geometry.FillFraction);
        Assert.Null(model.Geometry.MarkerFraction);
        Assert.Equal("Quota bar unavailable", model.AutomationName);
    }

    [Fact]
    public void CreateRenderModel_IsUnavailableForcesUnavailableBehavior()
    {
        var model = QuotaBarControl.CreateRenderModel(82, 44, null, isUnavailable: true);

        Assert.Equal(QuotaVisualAvailability.Unavailable, model.Geometry.Availability);
        Assert.Null(model.Geometry.ClampedQuotaPercent);
        Assert.Equal(0d, model.Geometry.FillFraction);
        Assert.Null(model.Geometry.MarkerFraction);
    }

    [Fact]
    public void AutomationNameProperty_SetsAutomationNameOnRender()
    {
        var control = new QuotaBarControl
        {
            QuotaLeftPercent = 64,
            TimeLeftPercent = 40,
            AutomationName = "Primary quota bar",
        };

        var model = QuotaBarControl.CreateRenderModel(
            control.QuotaLeftPercent,
            control.TimeLeftPercent,
            control.AutomationName,
            control.IsUnavailable);

        AutomationProperties.SetName(control, model.AutomationName);
        Assert.Equal("Primary quota bar", AutomationProperties.GetName(control));
    }

    [Fact]
    public void QuotaBarControl_UsesStableSharedLightModeColors()
    {
        Assert.Equal(Color.Parse("#FF6B7280"), QuotaVisualStyles.QuotaTrackColor);
        Assert.Equal(Color.Parse("#FF18A24A"), QuotaVisualStyles.QuotaFillColor);
        Assert.Equal(Color.Parse("#FF2563EB"), QuotaVisualStyles.QuotaFillBlueColor);
        Assert.Equal(Color.Parse("#FFEC4899"), QuotaVisualStyles.QuotaFillPinkColor);
        Assert.Equal(Color.Parse("#FFEAB308"), QuotaVisualStyles.QuotaFillYellowColor);
        Assert.Equal(Color.Parse("#FFDC2626"), QuotaVisualStyles.QuotaFillRedColor);
        Assert.Equal(Color.Parse("#FF111111"), QuotaVisualStyles.QuotaMarkerColor);
        Assert.Equal(Color.Parse("#FF8A94A3"), QuotaVisualStyles.QuotaUnavailableColor);
    }

    [Fact]
    public void QuotaBarControl_UsesStraightIndicatorEdges()
    {
        Assert.Equal(0d, QuotaBarControl.BarCornerRadius);
    }
}
