namespace CodexWidget.Core;

public static class UsageCalculations
{
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
        return CalculateWindowTimeLeftPercent(
            windowKind,
            resetAtUnixSeconds,
            limitWindowSeconds,
            nowUtc,
            UsageConfigurationDefaults.CreateDefaultWeeklyWorkSchedule());
    }

    public static int? CalculateWindowTimeLeftPercent(
        UsageWindowKind windowKind,
        long? resetAtUnixSeconds,
        int? limitWindowSeconds,
        DateTimeOffset nowUtc,
        WeeklyWorkSchedule workSchedule)
    {
        if (!resetAtUnixSeconds.HasValue || !limitWindowSeconds.HasValue || limitWindowSeconds.Value <= 0)
        {
            return null;
        }

        return windowKind == UsageWindowKind.Weekly
            ? CalculateWeeklyWorkTimeLeftPercent(resetAtUnixSeconds.Value, nowUtc, workSchedule)
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

    private static int? CalculateWeeklyWorkTimeLeftPercent(
        long resetAtUnixSeconds,
        DateTimeOffset nowUtc,
        WeeklyWorkSchedule workSchedule)
    {
        ArgumentNullException.ThrowIfNull(workSchedule);

        var totalWorkSeconds = UsageConfigurationRules.GetTotalWeeklyMinutes(workSchedule) * 60d;
        if (totalWorkSeconds <= 0d)
        {
            return null;
        }

        var resetAtUtc = DateTimeOffset.FromUnixTimeSeconds(resetAtUnixSeconds);
        var startUtc = nowUtc.ToUniversalTime();
        if (resetAtUtc <= startUtc)
        {
            return 0;
        }

        var workSecondsLeft = CountWarsawWorkSeconds(startUtc, resetAtUtc, workSchedule, totalWorkSeconds);
        return RoundAndClamp(workSecondsLeft / totalWorkSeconds * 100.0);
    }

    private static double CountWarsawWorkSeconds(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        WeeklyWorkSchedule workSchedule,
        double maxWorkSeconds)
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
            foreach (var workWindow in GetWorkWindows(date, workSchedule))
            {
                var workStartUtc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(workWindow.Start, DateTimeKind.Unspecified), timeZone);
                var workEndUtc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(workWindow.End, DateTimeKind.Unspecified), timeZone);
                var overlapStart = startUtcDateTime > workStartUtc ? startUtcDateTime : workStartUtc;
                var overlapEnd = endUtcDateTime < workEndUtc ? endUtcDateTime : workEndUtc;
                if (overlapEnd > overlapStart)
                {
                    workSeconds += (overlapEnd - overlapStart).TotalSeconds;
                    if (workSeconds >= maxWorkSeconds)
                    {
                        return maxWorkSeconds;
                    }
                }
            }
        }

        return workSeconds;
    }

    private static IEnumerable<WorkWindow> GetWorkWindows(DateOnly date, WeeklyWorkSchedule workSchedule)
    {
        var dayOfWeek = date.ToDateTime(TimeOnly.MinValue).DayOfWeek;
        return workSchedule
            .GetDaySchedule(dayOfWeek)
            .Windows
            .OrderBy(window => window.Start)
            .ThenBy(window => window.End);
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
