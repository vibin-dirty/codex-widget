namespace CodexWidget.Core;

public sealed record WorkWindow
{
    public TimeOnly Start { get; init; }

    public TimeOnly End { get; init; }

    public int DurationMinutes => (int)(End.ToTimeSpan() - Start.ToTimeSpan()).TotalMinutes;
}

public sealed record DayWorkSchedule : IEquatable<DayWorkSchedule>
{
    public static DayWorkSchedule Empty { get; } = new();

    public WorkWindow[] Windows { get; init; } = [];

    public bool Equals(DayWorkSchedule? other)
    {
        return ReferenceEquals(this, other)
               || (other is not null && Windows.SequenceEqual(other.Windows));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var window in Windows)
        {
            hash.Add(window);
        }

        return hash.ToHashCode();
    }
}

public sealed record WeeklyWorkSchedule
{
    public DayWorkSchedule Monday { get; init; } = DayWorkSchedule.Empty;

    public DayWorkSchedule Tuesday { get; init; } = DayWorkSchedule.Empty;

    public DayWorkSchedule Wednesday { get; init; } = DayWorkSchedule.Empty;

    public DayWorkSchedule Thursday { get; init; } = DayWorkSchedule.Empty;

    public DayWorkSchedule Friday { get; init; } = DayWorkSchedule.Empty;

    public DayWorkSchedule Saturday { get; init; } = DayWorkSchedule.Empty;

    public DayWorkSchedule Sunday { get; init; } = DayWorkSchedule.Empty;

    public DayWorkSchedule GetDaySchedule(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => Monday,
            DayOfWeek.Tuesday => Tuesday,
            DayOfWeek.Wednesday => Wednesday,
            DayOfWeek.Thursday => Thursday,
            DayOfWeek.Friday => Friday,
            DayOfWeek.Saturday => Saturday,
            DayOfWeek.Sunday => Sunday,
            _ => DayWorkSchedule.Empty,
        };
    }
}

public sealed record QuotaThresholds
{
    public int RedBelowPercent { get; init; } = UsageConfigurationDefaults.DefaultRedBelowPercent;

    public int YellowBelowPercent { get; init; } = UsageConfigurationDefaults.DefaultYellowBelowPercent;

    public int BlueAbovePercent { get; init; } = UsageConfigurationDefaults.DefaultBlueAbovePercent;

    public int PinkAbovePercent { get; init; } = UsageConfigurationDefaults.DefaultPinkAbovePercent;
}

public sealed record UsageConfigurationIssue(string Path, string Message);

public static class UsageConfigurationDefaults
{
    public const int DefaultRedBelowPercent = 70;
    public const int DefaultYellowBelowPercent = 90;
    public const int DefaultBlueAbovePercent = 110;
    public const int DefaultPinkAbovePercent = 130;

    public static WeeklyWorkSchedule CreateDefaultWeeklyWorkSchedule()
    {
        return new WeeklyWorkSchedule
        {
            Monday = CreateSingleWindowDay(7, 0, 17, 0),
            Tuesday = CreateSingleWindowDay(7, 0, 17, 0),
            Wednesday = CreateSingleWindowDay(7, 0, 17, 0),
            Thursday = CreateSingleWindowDay(7, 0, 17, 0),
            Friday = CreateSingleWindowDay(7, 0, 17, 0),
            Saturday = CreateSingleWindowDay(20, 0, 23, 0),
            Sunday = CreateSingleWindowDay(20, 0, 23, 0),
        };
    }

    public static QuotaThresholds CreateDefaultQuotaThresholds()
    {
        return new QuotaThresholds();
    }

    private static DayWorkSchedule CreateSingleWindowDay(int startHour, int startMinute, int endHour, int endMinute)
    {
        return new DayWorkSchedule
        {
            Windows =
            [
                new WorkWindow
                {
                    Start = new TimeOnly(startHour, startMinute),
                    End = new TimeOnly(endHour, endMinute),
                },
            ],
        };
    }
}

public static class UsageConfigurationRules
{
    public static IReadOnlyList<UsageConfigurationIssue> ValidateWeeklyWorkSchedule(WeeklyWorkSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var issues = new List<UsageConfigurationIssue>();

        foreach (var (dayName, daySchedule) in EnumerateDays(schedule))
        {
            ArgumentNullException.ThrowIfNull(daySchedule);

            var orderedWindows = daySchedule.Windows
                .OrderBy(window => window.Start)
                .ThenBy(window => window.End)
                .ToArray();

            for (var index = 0; index < orderedWindows.Length; index++)
            {
                var window = orderedWindows[index];
                if (window.End <= window.Start)
                {
                    issues.Add(new UsageConfigurationIssue(
                        $"{dayName}[{index}]",
                        "Work window end must be later than start."));
                }

                if (index == 0)
                {
                    continue;
                }

                var previous = orderedWindows[index - 1];
                if (window.Start < previous.End)
                {
                    issues.Add(new UsageConfigurationIssue(
                        $"{dayName}[{index}]",
                        "Work windows for a day must not overlap."));
                }
            }
        }

        return issues;
    }

    public static IReadOnlyList<UsageConfigurationIssue> ValidateQuotaThresholds(QuotaThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        var issues = new List<UsageConfigurationIssue>();
        if (thresholds.RedBelowPercent >= thresholds.YellowBelowPercent)
        {
            issues.Add(new UsageConfigurationIssue(
                "yellowBelowPercent",
                "Yellow threshold must be greater than the red threshold."));
        }

        if (thresholds.YellowBelowPercent >= thresholds.BlueAbovePercent)
        {
            issues.Add(new UsageConfigurationIssue(
                "blueAbovePercent",
                "Blue threshold must be greater than the yellow threshold."));
        }

        if (thresholds.BlueAbovePercent >= thresholds.PinkAbovePercent)
        {
            issues.Add(new UsageConfigurationIssue(
                "pinkAbovePercent",
                "Pink threshold must be greater than the blue threshold."));
        }

        return issues;
    }

    public static int GetTotalWeeklyMinutes(WeeklyWorkSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return EnumerateDays(schedule)
            .Sum(day => day.Schedule.Windows.Sum(window => Math.Max(0, window.DurationMinutes)));
    }

    public static double GetTotalWeeklyHours(WeeklyWorkSchedule schedule)
    {
        return GetTotalWeeklyMinutes(schedule) / 60d;
    }

    private static IEnumerable<(string DayName, DayWorkSchedule Schedule)> EnumerateDays(WeeklyWorkSchedule schedule)
    {
        yield return ("monday", schedule.Monday);
        yield return ("tuesday", schedule.Tuesday);
        yield return ("wednesday", schedule.Wednesday);
        yield return ("thursday", schedule.Thursday);
        yield return ("friday", schedule.Friday);
        yield return ("saturday", schedule.Saturday);
        yield return ("sunday", schedule.Sunday);
    }
}
