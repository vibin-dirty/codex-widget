namespace CodexWidget.Core;

public static class UsageCalculations
{
    private const double WeeklyWorkTimeSeconds = 56 * 60 * 60;
    private static readonly Lazy<TimeZoneInfo> WarsawTimeZone = new(FindWarsawTimeZone);

    public static int? CalculateQuotaLeftPercent(double? usedPercent)
    {
        if (!usedPercent.HasValue)
        {
            return null;
        }

        return RoundAndClamp(100.0 - usedPercent.Value);
    }

    public static int? CalculateTimeLeftPercent(long? resetAtUnixSeconds, int? limitWindowSeconds, DateTimeOffset nowUtc)
    {
        return CalculateDurationTimeLeftPercent(resetAtUnixSeconds, limitWindowSeconds, nowUtc);
    }

    public static int? CalculateWindowTimeLeftPercent(
        UsageWindowKind windowKind,
        long? resetAtUnixSeconds,
        int? limitWindowSeconds,
        DateTimeOffset nowUtc)
    {
        if (!resetAtUnixSeconds.HasValue || !limitWindowSeconds.HasValue || limitWindowSeconds.Value <= 0)
        {
            return null;
        }

        return windowKind == UsageWindowKind.Weekly
            ? CalculateWeeklyWorkTimeLeftPercent(resetAtUnixSeconds.Value, nowUtc)
            : CalculateDurationTimeLeftPercent(resetAtUnixSeconds, limitWindowSeconds, nowUtc);
    }

    private static int? CalculateDurationTimeLeftPercent(long? resetAtUnixSeconds, int? limitWindowSeconds, DateTimeOffset nowUtc)
    {
        if (!resetAtUnixSeconds.HasValue || !limitWindowSeconds.HasValue || limitWindowSeconds.Value <= 0)
        {
            return null;
        }

        var remainingSeconds = DateTimeOffset.FromUnixTimeSeconds(resetAtUnixSeconds.Value) - nowUtc;
        var percent = remainingSeconds.TotalSeconds / limitWindowSeconds.Value * 100.0;
        return RoundAndClamp(percent);
    }

    private static int CalculateWeeklyWorkTimeLeftPercent(long resetAtUnixSeconds, DateTimeOffset nowUtc)
    {
        var resetAtUtc = DateTimeOffset.FromUnixTimeSeconds(resetAtUnixSeconds);
        var startUtc = nowUtc.ToUniversalTime();
        if (resetAtUtc <= startUtc)
        {
            return 0;
        }

        var workSecondsLeft = CountWarsawWorkSeconds(startUtc, resetAtUtc);
        return RoundAndClamp(workSecondsLeft / WeeklyWorkTimeSeconds * 100.0);
    }

    private static double CountWarsawWorkSeconds(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var timeZone = WarsawTimeZone.Value;
        var startLocal = TimeZoneInfo.ConvertTime(startUtc, timeZone);
        var endLocal = TimeZoneInfo.ConvertTime(endUtc, timeZone);
        var startDate = DateOnly.FromDateTime(startLocal.DateTime);
        var endDate = DateOnly.FromDateTime(endLocal.DateTime);
        var startUtcDateTime = startUtc.UtcDateTime;
        var endUtcDateTime = endUtc.UtcDateTime;
        var workSeconds = 0.0;

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (!TryGetWarsawWorkWindow(date, out var workStart, out var workEnd))
            {
                continue;
            }

            var workStartUtc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(workStart, DateTimeKind.Unspecified), timeZone);
            var workEndUtc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(workEnd, DateTimeKind.Unspecified), timeZone);
            var overlapStart = startUtcDateTime > workStartUtc ? startUtcDateTime : workStartUtc;
            var overlapEnd = endUtcDateTime < workEndUtc ? endUtcDateTime : workEndUtc;
            if (overlapEnd > overlapStart)
            {
                workSeconds += (overlapEnd - overlapStart).TotalSeconds;
                if (workSeconds >= WeeklyWorkTimeSeconds)
                {
                    return WeeklyWorkTimeSeconds;
                }
            }
        }

        return workSeconds;
    }

    private static bool TryGetWarsawWorkWindow(DateOnly date, out TimeOnly start, out TimeOnly end)
    {
        var dayOfWeek = date.ToDateTime(TimeOnly.MinValue).DayOfWeek;
        if (dayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday)
        {
            start = new TimeOnly(7, 0);
            end = new TimeOnly(17, 0);
            return true;
        }

        start = new TimeOnly(20, 0);
        end = new TimeOnly(23, 0);
        return true;
    }

    private static TimeZoneInfo FindWarsawTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
    }

    private static int RoundAndClamp(double value)
    {
        var clamped = double.Clamp(value, 0.0, 100.0);
        return (int)Math.Round(clamped, MidpointRounding.AwayFromZero);
    }
}
