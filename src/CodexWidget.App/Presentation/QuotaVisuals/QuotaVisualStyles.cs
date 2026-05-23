using Avalonia.Media;

namespace CodexWidget.App.Presentation.QuotaVisuals;

internal static class QuotaVisualStyles
{
    private const double RedGateThresholdPercent = 70d;
    private const double YellowGateThresholdPercent = 90d;
    private const double BlueSurplusGateThresholdPercent = 110d;
    private const double PinkSurplusGateThresholdPercent = 130d;

    public static Color QuotaFillColor { get; } = Color.Parse("#FF18A24A");
    public static Color QuotaFillBlueColor { get; } = Color.Parse("#FF2563EB");
    public static Color QuotaFillPinkColor { get; } = Color.Parse("#FFEC4899");
    public static Color QuotaFillYellowColor { get; } = Color.Parse("#FFEAB308");
    public static Color QuotaFillRedColor { get; } = Color.Parse("#FFDC2626");
    public static Color QuotaTrackColor { get; } = Color.Parse("#FF6B7280");
    public static Color QuotaMarkerColor { get; } = Color.Parse("#FF111111");
    public static Color QuotaTextColor { get; } = Color.Parse("#FF161616");
    public static Color QuotaUnavailableColor { get; } = Color.Parse("#FF8A94A3");

    public static IBrush QuotaFillBrush { get; } = CreateBrush(QuotaFillColor);
    public static IBrush QuotaFillBlueBrush { get; } = CreateBrush(QuotaFillBlueColor);
    public static IBrush QuotaFillPinkBrush { get; } = CreateBrush(QuotaFillPinkColor);
    public static IBrush QuotaFillYellowBrush { get; } = CreateBrush(QuotaFillYellowColor);
    public static IBrush QuotaFillRedBrush { get; } = CreateBrush(QuotaFillRedColor);
    public static IBrush QuotaTrackBrush { get; } = CreateBrush(QuotaTrackColor);
    public static IBrush QuotaMarkerBrush { get; } = CreateBrush(QuotaMarkerColor);
    public static IBrush QuotaTextBrush { get; } = CreateBrush(QuotaTextColor);
    public static IBrush QuotaUnavailableBrush { get; } = CreateBrush(QuotaUnavailableColor);

    public static Color ResolveQuotaFillColor(
        int? quotaLeftPercent,
        int? timeLeftPercent,
        bool useSurplusFillColors)
    {
        if (!quotaLeftPercent.HasValue || !timeLeftPercent.HasValue)
        {
            return QuotaFillColor;
        }

        var quota = QuotaVisualGeometry.ClampPercent(quotaLeftPercent.Value);
        var time = QuotaVisualGeometry.ClampPercent(timeLeftPercent.Value);
        var gatePercent = CalculateUsageGatePercent(quota, time);

        if (gatePercent < RedGateThresholdPercent)
        {
            return QuotaFillRedColor;
        }

        if (gatePercent < YellowGateThresholdPercent)
        {
            return QuotaFillYellowColor;
        }

        if (useSurplusFillColors && gatePercent > PinkSurplusGateThresholdPercent)
        {
            return QuotaFillPinkColor;
        }

        if (useSurplusFillColors && gatePercent > BlueSurplusGateThresholdPercent)
        {
            return QuotaFillBlueColor;
        }

        return QuotaFillColor;
    }

    public static IBrush ResolveQuotaFillBrush(
        int? quotaLeftPercent,
        int? timeLeftPercent,
        bool useSurplusFillColors)
    {
        var color = ResolveQuotaFillColor(quotaLeftPercent, timeLeftPercent, useSurplusFillColors);
        if (color == QuotaFillPinkColor)
        {
            return QuotaFillPinkBrush;
        }

        if (color == QuotaFillBlueColor)
        {
            return QuotaFillBlueBrush;
        }

        if (color == QuotaFillRedColor)
        {
            return QuotaFillRedBrush;
        }

        if (color == QuotaFillYellowColor)
        {
            return QuotaFillYellowBrush;
        }

        return QuotaFillBrush;
    }

    private static double CalculateUsageGatePercent(int quotaLeftPercent, int timeLeftPercent)
    {
        var oldGatePercent = 100d * quotaLeftPercent / timeLeftPercent;
        var newGatePercent = 100d + (quotaLeftPercent - timeLeftPercent);
        if (quotaLeftPercent <= 5)
        {
            return oldGatePercent;
        }

        if (quotaLeftPercent >= 15)
        {
            return newGatePercent;
        }

        var transitionWeight = (quotaLeftPercent - 5d) / 10d;
        return (oldGatePercent * (1d - transitionWeight)) + (newGatePercent * transitionWeight);
    }

    private static IBrush CreateBrush(Color color)
    {
        return new SolidColorBrush(color).ToImmutable();
    }
}
