using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using CodexWidget.App.Presentation.QuotaVisuals;

namespace CodexWidget.App.Controls;

internal sealed class QuotaBarControl : Control
{
    private const double DefaultWidth = 140d;
    private const double DefaultHeight = 12d;
    private const double MarkerThickness = 1d;
    private const double BarPadding = 1d;
    internal const double BarCornerRadius = 0d;

    public static readonly StyledProperty<int?> QuotaLeftPercentProperty =
        AvaloniaProperty.Register<QuotaBarControl, int?>(nameof(QuotaLeftPercent));

    public static readonly StyledProperty<int?> TimeLeftPercentProperty =
        AvaloniaProperty.Register<QuotaBarControl, int?>(nameof(TimeLeftPercent));

    public static readonly StyledProperty<string?> AutomationNameProperty =
        AvaloniaProperty.Register<QuotaBarControl, string?>(nameof(AutomationName));

    public static readonly StyledProperty<bool> IsUnavailableProperty =
        AvaloniaProperty.Register<QuotaBarControl, bool>(nameof(IsUnavailable));

    public static readonly StyledProperty<bool> UseSurplusFillColorsProperty =
        AvaloniaProperty.Register<QuotaBarControl, bool>(nameof(UseSurplusFillColors));

    static QuotaBarControl()
    {
        AffectsRender<QuotaBarControl>(
            QuotaLeftPercentProperty,
            TimeLeftPercentProperty,
            AutomationNameProperty,
            IsUnavailableProperty,
            UseSurplusFillColorsProperty);
    }

    public QuotaBarControl()
    {
        MinWidth = DefaultWidth;
        MinHeight = DefaultHeight;
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

        var model = CreateRenderModel(QuotaLeftPercent, TimeLeftPercent, AutomationName, IsUnavailable);
        AutomationProperties.SetName(this, model.AutomationName);

        // Render in the control's local coordinate space; Bounds can include the arranged parent offset.
        var barRect = new Rect(Bounds.Size).Deflate(BarPadding);
        if (barRect.Width <= 0d || barRect.Height <= 0d)
        {
            return;
        }

        DrawTrack(context, model, barRect);
        DrawFill(context, model, barRect, UseSurplusFillColors);
        DrawMarker(context, model, barRect);
    }

    internal static QuotaBarRenderModel CreateRenderModel(
        int? quotaLeftPercent,
        int? timeLeftPercent,
        string? automationName,
        bool isUnavailable)
    {
        var geometry = QuotaVisualGeometry.CreateBar(
            isUnavailable ? null : quotaLeftPercent,
            timeLeftPercent);

        var name = ResolveAutomationName(automationName, geometry);
        return new QuotaBarRenderModel(geometry, name);
    }

    private static void DrawTrack(DrawingContext context, QuotaBarRenderModel model, Rect barRect)
    {
        var trackBrush = model.Geometry.Availability == QuotaVisualAvailability.Available
            ? QuotaVisualStyles.QuotaTrackBrush
            : QuotaVisualStyles.QuotaUnavailableBrush;
        context.DrawRectangle(trackBrush, null, new RoundedRect(barRect, BarCornerRadius));
    }

    private static void DrawFill(
        DrawingContext context,
        QuotaBarRenderModel model,
        Rect barRect,
        bool useSurplusFillColors)
    {
        if (model.Geometry.Availability != QuotaVisualAvailability.Available || model.Geometry.FillFraction <= 0d)
        {
            return;
        }

        var fillWidth = barRect.Width * model.Geometry.FillFraction;
        var fillRect = new Rect(barRect.X, barRect.Y, fillWidth, barRect.Height);
        var fillBrush = QuotaVisualStyles.ResolveQuotaFillBrush(
            model.Geometry.ClampedQuotaPercent,
            model.Geometry.ClampedTimePercent,
            useSurplusFillColors);
        context.DrawRectangle(fillBrush, null, new RoundedRect(fillRect, BarCornerRadius));
    }

    private static void DrawMarker(DrawingContext context, QuotaBarRenderModel model, Rect barRect)
    {
        if (model.Geometry.Availability != QuotaVisualAvailability.Available || !model.Geometry.MarkerFraction.HasValue)
        {
            return;
        }

        var markerX = Math.Clamp(
            barRect.X + (barRect.Width * model.Geometry.MarkerFraction.Value),
            barRect.X,
            barRect.Right);

        var markerPen = new Pen(QuotaVisualStyles.QuotaMarkerBrush, MarkerThickness)
        {
            LineCap = PenLineCap.Flat,
        };

        context.DrawLine(
            markerPen,
            new Point(markerX, barRect.Y),
            new Point(markerX, barRect.Bottom));
    }

    private static string ResolveAutomationName(string? automationName, QuotaBarGeometry geometry)
    {
        if (!string.IsNullOrWhiteSpace(automationName))
        {
            return automationName.Trim();
        }

        return geometry.Availability == QuotaVisualAvailability.Available && geometry.ClampedQuotaPercent.HasValue
            ? $"Quota bar {geometry.ClampedQuotaPercent.Value}%"
            : "Quota bar unavailable";
    }
}

internal readonly record struct QuotaBarRenderModel(
    QuotaBarGeometry Geometry,
    string AutomationName);
