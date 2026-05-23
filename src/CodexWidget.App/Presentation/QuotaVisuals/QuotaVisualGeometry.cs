namespace CodexWidget.App.Presentation.QuotaVisuals;

internal enum QuotaVisualAvailability
{
    Unavailable = 0,
    Available = 1,
}

internal enum RingSweepDirection
{
    Clockwise = 0,
}

internal readonly record struct QuotaRingGeometry(
    QuotaVisualAvailability Availability,
    int? ClampedQuotaPercent,
    int? ClampedTimePercent,
    double StartAngleDegrees,
    RingSweepDirection SweepDirection,
    double SweepAngleDegrees,
    double? MarkerAngleDegrees);

internal readonly record struct QuotaBarGeometry(
    QuotaVisualAvailability Availability,
    int? ClampedQuotaPercent,
    int? ClampedTimePercent,
    double FillFraction,
    double? MarkerFraction);

internal static class QuotaVisualGeometry
{
    public const double RingStartAngleDegrees = 90d;
    public const double FullCircleDegrees = 360d;

    public static QuotaRingGeometry CreateRing(int? quotaLeftPercent, int? timeLeftPercent)
    {
        var clampedQuotaPercent = ClampNullablePercent(quotaLeftPercent);
        var clampedTimePercent = ClampNullablePercent(timeLeftPercent);
        if (!clampedQuotaPercent.HasValue)
        {
            return new QuotaRingGeometry(
                QuotaVisualAvailability.Unavailable,
                null,
                clampedTimePercent,
                RingStartAngleDegrees,
                RingSweepDirection.Clockwise,
                0d,
                null);
        }

        var sweepAngleDegrees = PercentToFraction(clampedQuotaPercent.Value) * FullCircleDegrees;
        double? markerAngleDegrees = clampedTimePercent.HasValue
            ? NormalizeAngle(RingStartAngleDegrees + (PercentToFraction(clampedTimePercent.Value) * FullCircleDegrees))
            : null;

        return new QuotaRingGeometry(
            QuotaVisualAvailability.Available,
            clampedQuotaPercent,
            clampedTimePercent,
            RingStartAngleDegrees,
            RingSweepDirection.Clockwise,
            sweepAngleDegrees,
            markerAngleDegrees);
    }

    public static QuotaBarGeometry CreateBar(int? quotaLeftPercent, int? timeLeftPercent)
    {
        var clampedQuotaPercent = ClampNullablePercent(quotaLeftPercent);
        var clampedTimePercent = ClampNullablePercent(timeLeftPercent);
        if (!clampedQuotaPercent.HasValue)
        {
            return new QuotaBarGeometry(
                QuotaVisualAvailability.Unavailable,
                null,
                clampedTimePercent,
                0d,
                null);
        }

        double? markerFraction = clampedTimePercent.HasValue
            ? PercentToFraction(clampedTimePercent.Value)
            : null;

        return new QuotaBarGeometry(
            QuotaVisualAvailability.Available,
            clampedQuotaPercent,
            clampedTimePercent,
            PercentToFraction(clampedQuotaPercent.Value),
            markerFraction);
    }

    public static int ClampPercent(int percent)
    {
        return Math.Clamp(percent, 0, 100);
    }

    private static int? ClampNullablePercent(int? percent)
    {
        return percent.HasValue ? ClampPercent(percent.Value) : null;
    }

    private static double PercentToFraction(int percent)
    {
        return ClampPercent(percent) / 100d;
    }

    private static double NormalizeAngle(double angleDegrees)
    {
        var normalized = angleDegrees % FullCircleDegrees;
        return normalized < 0d ? normalized + FullCircleDegrees : normalized;
    }
}
