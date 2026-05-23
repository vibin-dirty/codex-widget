using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using CodexWidget.App.Presentation.QuotaVisuals;

namespace CodexWidget.App.Controls;

internal sealed class QuotaRingControl : Control
{
    private const double DefaultDiameter = 72d;
    private const double RingThickness = 10d;
    private const double MarkerThickness = 2d;
    private const double RingPadding = 2d;
    private const string DefaultUnavailableText = "??";
    internal const PenLineCap QuotaArcLineCap = PenLineCap.Flat;

    public static readonly StyledProperty<int?> QuotaLeftPercentProperty =
        AvaloniaProperty.Register<QuotaRingControl, int?>(nameof(QuotaLeftPercent));

    public static readonly StyledProperty<int?> TimeLeftPercentProperty =
        AvaloniaProperty.Register<QuotaRingControl, int?>(nameof(TimeLeftPercent));

    public static readonly StyledProperty<string?> CenterTextProperty =
        AvaloniaProperty.Register<QuotaRingControl, string?>(nameof(CenterText));

    public static readonly StyledProperty<double> CenterFontSizeProperty =
        AvaloniaProperty.Register<QuotaRingControl, double>(nameof(CenterFontSize), 14d);

    public static readonly StyledProperty<string?> AutomationNameProperty =
        AvaloniaProperty.Register<QuotaRingControl, string?>(nameof(AutomationName));

    public static readonly StyledProperty<bool> IsUnavailableProperty =
        AvaloniaProperty.Register<QuotaRingControl, bool>(nameof(IsUnavailable));

    public static readonly StyledProperty<bool> UseSurplusFillColorsProperty =
        AvaloniaProperty.Register<QuotaRingControl, bool>(nameof(UseSurplusFillColors));

    static QuotaRingControl()
    {
        AffectsRender<QuotaRingControl>(
            QuotaLeftPercentProperty,
            TimeLeftPercentProperty,
            CenterTextProperty,
            CenterFontSizeProperty,
            IsUnavailableProperty,
            UseSurplusFillColorsProperty);
    }

    public QuotaRingControl()
    {
        MinWidth = DefaultDiameter;
        MinHeight = DefaultDiameter;
    }

    public int? QuotaLeftPercent
    {
        get => GetValue(QuotaLeftPercentProperty);
        set => SetValue(QuotaLeftPercentProperty, value);
    }

    public int? TimeLeftPercent
    {
        get => GetValue(TimeLeftPercentProperty);
        set => SetValue(TimeLeftPercentProperty, value);
    }

    public string? CenterText
    {
        get => GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    public double CenterFontSize
    {
        get => GetValue(CenterFontSizeProperty);
        set => SetValue(CenterFontSizeProperty, value);
    }

    public string? AutomationName
    {
        get => GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    public bool IsUnavailable
    {
        get => GetValue(IsUnavailableProperty);
        set => SetValue(IsUnavailableProperty, value);
    }

    public bool UseSurplusFillColors
    {
        get => GetValue(UseSurplusFillColorsProperty);
        set => SetValue(UseSurplusFillColorsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Render(context);

        var model = CreateRenderModel(QuotaLeftPercent, TimeLeftPercent, CenterText, AutomationName, IsUnavailable);
        AutomationProperties.SetName(this, model.AutomationName);

        // Render in the control's local coordinate space; Bounds can include the arranged parent offset.
        var bounds = new Rect(Bounds.Size).Deflate(RingPadding);
        if (bounds.Width <= 0d || bounds.Height <= 0d)
        {
            return;
        }

        var center = bounds.Center;
        var radius = (Math.Min(bounds.Width, bounds.Height) / 2d) - (RingThickness / 2d);
        if (radius <= 0d)
        {
            return;
        }

        DrawTrack(context, model, center, radius);
        DrawQuotaArc(context, model, center, radius, UseSurplusFillColors);
        DrawMarker(context, model, center, radius);
        DrawCenterText(context, model, center, CenterFontSize);
    }

    internal static QuotaRingRenderModel CreateRenderModel(
        int? quotaLeftPercent,
        int? timeLeftPercent,
        string? centerText,
        string? automationName,
        bool isUnavailable)
    {
        var geometry = QuotaVisualGeometry.CreateRing(
            isUnavailable ? null : quotaLeftPercent,
            timeLeftPercent);

        var displayText = ResolveCenterText(centerText, geometry);
        var name = ResolveAutomationName(automationName, displayText, geometry);
        return new QuotaRingRenderModel(geometry, displayText, name);
    }

    private static void DrawTrack(DrawingContext context, QuotaRingRenderModel model, Point center, double radius)
    {
        var trackBrush = model.Geometry.Availability == QuotaVisualAvailability.Available
            ? QuotaVisualStyles.QuotaTrackBrush
            : QuotaVisualStyles.QuotaUnavailableBrush;

        var trackPen = new Pen(trackBrush, RingThickness)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        context.DrawEllipse(null, trackPen, center, radius, radius);
    }

    private static void DrawQuotaArc(
        DrawingContext context,
        QuotaRingRenderModel model,
        Point center,
        double radius,
        bool useSurplusFillColors)
    {
        if (model.Geometry.Availability != QuotaVisualAvailability.Available || model.Geometry.SweepAngleDegrees <= 0d)
        {
            return;
        }

        var fillBrush = QuotaVisualStyles.ResolveQuotaFillBrush(
            model.Geometry.ClampedQuotaPercent,
            model.Geometry.ClampedTimePercent,
            useSurplusFillColors);
        var quotaPen = new Pen(fillBrush, RingThickness)
        {
            LineCap = QuotaArcLineCap,
            LineJoin = PenLineJoin.Round,
        };

        if (model.Geometry.SweepAngleDegrees >= QuotaVisualGeometry.FullCircleDegrees)
        {
            context.DrawEllipse(null, quotaPen, center, radius, radius);
            return;
        }

        var start = AngleToPoint(center, radius, model.Geometry.StartAngleDegrees);
        var end = AngleToPoint(center, radius, model.Geometry.StartAngleDegrees + model.Geometry.SweepAngleDegrees);
        var isLargeArc = model.Geometry.SweepAngleDegrees > 180d;
        var sweepDirection = model.Geometry.SweepDirection == RingSweepDirection.Clockwise
            ? SweepDirection.Clockwise
            : SweepDirection.CounterClockwise;

        var arcGeometry = new StreamGeometry();
        using (var arc = arcGeometry.Open())
        {
            arc.BeginFigure(start, false);
            arc.ArcTo(end, new Size(radius, radius), 0d, isLargeArc, sweepDirection);
            arc.EndFigure(false);
        }

        context.DrawGeometry(null, quotaPen, arcGeometry);
    }

    private static void DrawMarker(DrawingContext context, QuotaRingRenderModel model, Point center, double radius)
    {
        if (model.Geometry.Availability != QuotaVisualAvailability.Available || !model.Geometry.MarkerAngleDegrees.HasValue)
        {
            return;
        }

        var markerRadiusOffset = RingThickness / 2d;
        var markerInner = AngleToPoint(center, radius - markerRadiusOffset, model.Geometry.MarkerAngleDegrees.Value);
        var markerOuter = AngleToPoint(center, radius + markerRadiusOffset, model.Geometry.MarkerAngleDegrees.Value);

        var markerPen = new Pen(QuotaVisualStyles.QuotaMarkerBrush, MarkerThickness)
        {
            LineCap = PenLineCap.Round,
        };

        context.DrawLine(markerPen, markerInner, markerOuter);
    }

    private static void DrawCenterText(DrawingContext context, QuotaRingRenderModel model, Point center, double fontSize)
    {
        var text = new FormattedText(
            model.CenterText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
            Math.Max(1d, fontSize),
            QuotaVisualStyles.QuotaTextBrush);

        var location = new Point(
            center.X - (text.Width / 2d),
            center.Y - (text.Height / 2d));

        context.DrawText(text, location);
    }

    private static Point AngleToPoint(Point center, double radius, double angleDegrees)
    {
        var angleRadians = angleDegrees * (Math.PI / 180d);
        return new Point(
            center.X + (Math.Cos(angleRadians) * radius),
            center.Y + (Math.Sin(angleRadians) * radius));
    }

    private static string ResolveCenterText(string? centerText, QuotaRingGeometry geometry)
    {
        if (!string.IsNullOrWhiteSpace(centerText))
        {
            return centerText.Trim();
        }

        return geometry.Availability == QuotaVisualAvailability.Available && geometry.ClampedQuotaPercent.HasValue
            ? $"{geometry.ClampedQuotaPercent.Value}%"
            : DefaultUnavailableText;
    }

    private static string ResolveAutomationName(string? automationName, string centerText, QuotaRingGeometry geometry)
    {
        if (!string.IsNullOrWhiteSpace(automationName))
        {
            return automationName.Trim();
        }

        return geometry.Availability == QuotaVisualAvailability.Available
            ? $"Quota ring {centerText}"
            : "Quota ring unavailable";
    }
}

internal readonly record struct QuotaRingRenderModel(
    QuotaRingGeometry Geometry,
    string CenterText,
    string AutomationName);
