using Avalonia.Automation;
using Avalonia.Media;
using CodexWidget.App.Controls;
using CodexWidget.App.Presentation.QuotaVisuals;

namespace CodexWidget.App.Tests;

public sealed class QuotaRingControlTests
{
    [Fact]
    public void CreateRenderModel_UsesBottomStartAndClockwiseSweep()
    {
        var model = QuotaRingControl.CreateRenderModel(25, 50, "25%", "Main ring", isUnavailable: false);

        Assert.Equal(QuotaVisualAvailability.Available, model.Geometry.Availability);
        Assert.Equal(QuotaVisualGeometry.RingStartAngleDegrees, model.Geometry.StartAngleDegrees);
        Assert.Equal(RingSweepDirection.Clockwise, model.Geometry.SweepDirection);
        Assert.Equal(90d, model.Geometry.SweepAngleDegrees);
    }

    [Fact]
    public void CreateRenderModel_ClampsQuotaPercentAndSweep()
    {
        var zero = QuotaRingControl.CreateRenderModel(-15, 10, "0%", null, isUnavailable: false);
        var full = QuotaRingControl.CreateRenderModel(140, 10, "100%", null, isUnavailable: false);

        Assert.Equal(0, zero.Geometry.ClampedQuotaPercent);
        Assert.Equal(0d, zero.Geometry.SweepAngleDegrees);

        Assert.Equal(100, full.Geometry.ClampedQuotaPercent);
        Assert.Equal(360d, full.Geometry.SweepAngleDegrees);
    }

    [Fact]
    public void CreateRenderModel_CalculatesMarkerAngleFromTimeLeftPercent()
    {
        var model = QuotaRingControl.CreateRenderModel(80, 25, "80%", null, isUnavailable: false);

        Assert.Equal(25, model.Geometry.ClampedTimePercent);
        Assert.Equal(180d, model.Geometry.MarkerAngleDegrees);
    }

    [Fact]
    public void CreateRenderModel_OmitsMarkerWhenTimeLeftIsUnavailable()
    {
        var model = QuotaRingControl.CreateRenderModel(80, null, "80%", null, isUnavailable: false);

        Assert.Null(model.Geometry.ClampedTimePercent);
        Assert.Null(model.Geometry.MarkerAngleDegrees);
    }

    [Fact]
    public void CreateRenderModel_UnavailableQuotaUsesNeutralModelAndQuestionMarkText()
    {
        var model = QuotaRingControl.CreateRenderModel(null, 40, null, null, isUnavailable: false);

        Assert.Equal(QuotaVisualAvailability.Unavailable, model.Geometry.Availability);
        Assert.Null(model.Geometry.ClampedQuotaPercent);
        Assert.Null(model.Geometry.MarkerAngleDegrees);
        Assert.Equal("??", model.CenterText);
        Assert.Equal("Quota ring unavailable", model.AutomationName);
    }

    [Fact]
    public void CreateRenderModel_IsUnavailableForcesUnavailableBehavior()
    {
        var model = QuotaRingControl.CreateRenderModel(88, 33, null, null, isUnavailable: true);

        Assert.Equal(QuotaVisualAvailability.Unavailable, model.Geometry.Availability);
        Assert.Null(model.Geometry.ClampedQuotaPercent);
        Assert.Null(model.Geometry.MarkerAngleDegrees);
        Assert.Equal("??", model.CenterText);
    }

    [Fact]
    public void AutomationNameProperty_SetsAutomationNameOnRender()
    {
        var control = new QuotaRingControl
        {
            QuotaLeftPercent = 64,
            TimeLeftPercent = 40,
            CenterText = "64%",
            AutomationName = "Primary quota ring",
        };

        var model = QuotaRingControl.CreateRenderModel(
            control.QuotaLeftPercent,
            control.TimeLeftPercent,
            control.CenterText,
            control.AutomationName,
            control.IsUnavailable);

        AutomationProperties.SetName(control, model.AutomationName);
        Assert.Equal("Primary quota ring", AutomationProperties.GetName(control));
    }

    [Fact]
    public void QuotaRingControl_UsesStraightQuotaArcEdges()
    {
        Assert.Equal(PenLineCap.Flat, QuotaRingControl.QuotaArcLineCap);
    }
}
