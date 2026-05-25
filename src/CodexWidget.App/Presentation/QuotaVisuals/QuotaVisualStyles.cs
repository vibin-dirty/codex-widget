using Avalonia.Media;
using CodexWidget.Core;

namespace CodexWidget.App.Presentation.QuotaVisuals;

internal static class QuotaVisualStyles
{
    private const double RedGateThresholdPercent = 70d;
    private const double YellowGateThresholdPercent = 90d;
    private const double BlueSurplusGateThresholdPercent = 110d;
    private const double PinkSurplusGateThresholdPercent = 130d;

    private static readonly Color LightQuotaFillColor = Color.Parse("#FF18A24A");
    private static readonly Color LightQuotaFillBlueColor = Color.Parse("#FF2563EB");
    private static readonly Color LightQuotaFillPinkColor = Color.Parse("#FFEC4899");
    private static readonly Color LightQuotaFillYellowColor = Color.Parse("#FFEAB308");
    private static readonly Color LightQuotaFillRedColor = Color.Parse("#FFDC2626");
    private static readonly Color LightQuotaTrackColor = Color.Parse("#FF6B7280");
    private static readonly Color LightQuotaMarkerColor = Color.Parse("#FF111111");
    private static readonly Color LightQuotaTextColor = Color.Parse("#FF161616");
    private static readonly Color LightQuotaUnavailableColor = Color.Parse("#FF8A94A3");

    private static readonly Color DarkQuotaFillColor = Color.Parse("#FF35D07F");
    private static readonly Color DarkQuotaFillBlueColor = Color.Parse("#FF60A5FA");
    private static readonly Color DarkQuotaFillPinkColor = Color.Parse("#FFF472B6");
    private static readonly Color DarkQuotaFillYellowColor = Color.Parse("#FFFACC15");
    private static readonly Color DarkQuotaFillRedColor = Color.Parse("#FFFF6B6B");
    private static readonly Color DarkQuotaTrackColor = Color.Parse("#FF344253");
    private static readonly Color DarkQuotaMarkerColor = Color.Parse("#FFE8EEF6");
    private static readonly Color DarkQuotaTextColor = Color.Parse("#FFE8EEF6");
    private static readonly Color DarkQuotaUnavailableColor = Color.Parse("#FF536171");

    public static Color QuotaFillColor => ResolveThemeColor(LightQuotaFillColor, DarkQuotaFillColor);
    public static Color QuotaFillBlueColor => ResolveThemeColor(LightQuotaFillBlueColor, DarkQuotaFillBlueColor);
    public static Color QuotaFillPinkColor => ResolveThemeColor(LightQuotaFillPinkColor, DarkQuotaFillPinkColor);
    public static Color QuotaFillYellowColor => ResolveThemeColor(LightQuotaFillYellowColor, DarkQuotaFillYellowColor);
    public static Color QuotaFillRedColor => ResolveThemeColor(LightQuotaFillRedColor, DarkQuotaFillRedColor);
    public static Color QuotaTrackColor => ResolveThemeColor(LightQuotaTrackColor, DarkQuotaTrackColor);
    public static Color QuotaMarkerColor => ResolveThemeColor(LightQuotaMarkerColor, DarkQuotaMarkerColor);
    public static Color QuotaTextColor => ResolveThemeColor(LightQuotaTextColor, DarkQuotaTextColor);
    public static Color QuotaUnavailableColor => ResolveThemeColor(LightQuotaUnavailableColor, DarkQuotaUnavailableColor);

    public static IBrush QuotaFillBrush => CreateBrush(QuotaFillColor);
    public static IBrush QuotaFillBlueBrush => CreateBrush(QuotaFillBlueColor);
    public static IBrush QuotaFillPinkBrush => CreateBrush(QuotaFillPinkColor);
    public static IBrush QuotaFillYellowBrush => CreateBrush(QuotaFillYellowColor);
    public static IBrush QuotaFillRedBrush => CreateBrush(QuotaFillRedColor);
    public static IBrush QuotaTrackBrush => CreateBrush(QuotaTrackColor);
    public static IBrush QuotaMarkerBrush => CreateBrush(QuotaMarkerColor);
    public static IBrush QuotaTextBrush => CreateBrush(QuotaTextColor);
    public static IBrush QuotaUnavailableBrush => CreateBrush(QuotaUnavailableColor);

    public static Color ResolveQuotaTrackColor(WidgetThemePreference theme)
    {
        return ResolveThemeColor(theme, LightQuotaTrackColor, DarkQuotaTrackColor);
    }

    public static Color ResolveQuotaMarkerColor(WidgetThemePreference theme)
    {
        return ResolveThemeColor(theme, LightQuotaMarkerColor, DarkQuotaMarkerColor);
    }

    public static Color ResolveQuotaTextColor(WidgetThemePreference theme)
    {
        return ResolveThemeColor(theme, LightQuotaTextColor, DarkQuotaTextColor);
    }

    public static Color ResolveQuotaUnavailableColor(WidgetThemePreference theme)
    {
        return ResolveThemeColor(theme, LightQuotaUnavailableColor, DarkQuotaUnavailableColor);
    }

    public static Color ResolveQuotaFillColor(
        int? quotaLeftPercent,
        int? timeLeftPercent,
        bool useSurplusFillColors)
    {
        return ResolveQuotaFillColor(
            WidgetVisualStyles.CurrentTheme,
            quotaLeftPercent,
            timeLeftPercent,
            useSurplusFillColors);
    }

    public static Color ResolveQuotaFillColor(
        WidgetThemePreference theme,
        int? quotaLeftPercent,
        int? timeLeftPercent,
        bool useSurplusFillColors)
    {
        if (!quotaLeftPercent.HasValue || !timeLeftPercent.HasValue)
        {
            return ResolveQuotaFillColor(theme, isBlue: false, isPink: false, isYellow: false, isRed: false);
        }

        var quota = QuotaVisualGeometry.ClampPercent(quotaLeftPercent.Value);
        var time = QuotaVisualGeometry.ClampPercent(timeLeftPercent.Value);
        var gatePercent = CalculateUsageGatePercent(quota, time);

        if (gatePercent < RedGateThresholdPercent)
        {
            return ResolveQuotaFillColor(theme, isBlue: false, isPink: false, isYellow: false, isRed: true);
        }

        if (gatePercent < YellowGateThresholdPercent)
        {
            return ResolveQuotaFillColor(theme, isBlue: false, isPink: false, isYellow: true, isRed: false);
        }

        if (useSurplusFillColors && gatePercent > PinkSurplusGateThresholdPercent)
        {
            return ResolveQuotaFillColor(theme, isBlue: false, isPink: true, isYellow: false, isRed: false);
        }

        if (useSurplusFillColors && gatePercent > BlueSurplusGateThresholdPercent)
        {
            return ResolveQuotaFillColor(theme, isBlue: true, isPink: false, isYellow: false, isRed: false);
        }

        return ResolveQuotaFillColor(theme, isBlue: false, isPink: false, isYellow: false, isRed: false);
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

    private static Color ResolveQuotaFillColor(
        WidgetThemePreference theme,
        bool isBlue,
        bool isPink,
        bool isYellow,
        bool isRed)
    {
        if (isPink)
        {
            return ResolveThemeColor(theme, LightQuotaFillPinkColor, DarkQuotaFillPinkColor);
        }

        if (isBlue)
        {
            return ResolveThemeColor(theme, LightQuotaFillBlueColor, DarkQuotaFillBlueColor);
        }

        if (isYellow)
        {
            return ResolveThemeColor(theme, LightQuotaFillYellowColor, DarkQuotaFillYellowColor);
        }

        if (isRed)
        {
            return ResolveThemeColor(theme, LightQuotaFillRedColor, DarkQuotaFillRedColor);
        }

        return ResolveThemeColor(theme, LightQuotaFillColor, DarkQuotaFillColor);
    }

    private static Color ResolveThemeColor(Color lightColor, Color darkColor)
    {
        return ResolveThemeColor(WidgetVisualStyles.CurrentTheme, lightColor, darkColor);
    }

    private static Color ResolveThemeColor(WidgetThemePreference theme, Color lightColor, Color darkColor)
    {
        return theme == WidgetThemePreference.Dark ? darkColor : lightColor;
    }
}
