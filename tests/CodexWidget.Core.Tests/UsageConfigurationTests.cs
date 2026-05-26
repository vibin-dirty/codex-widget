namespace CodexWidget.Core.Tests;

public sealed class UsageConfigurationTests
{
    [Fact]
    public void DefaultWeeklySchedule_PreservesHistoricFiftySixHourWeek()
    {
        var schedule = UsageConfigurationDefaults.CreateDefaultWeeklyWorkSchedule();

        Assert.Equal(56, UsageConfigurationRules.GetTotalWeeklyHours(schedule));
        Assert.Equal(new TimeOnly(7, 0), Assert.Single(schedule.Monday.Windows).Start);
        Assert.Equal(new TimeOnly(17, 0), Assert.Single(schedule.Monday.Windows).End);
        Assert.Equal(new TimeOnly(20, 0), Assert.Single(schedule.Sunday.Windows).Start);
        Assert.Equal(new TimeOnly(23, 0), Assert.Single(schedule.Sunday.Windows).End);
    }

    [Fact]
    public void ValidateWeeklyWorkSchedule_RejectsOverlapAndEndBeforeStart()
    {
        var schedule = new WeeklyWorkSchedule
        {
            Monday = new DayWorkSchedule
            {
                Windows =
                [
                    new WorkWindow { Start = new TimeOnly(9, 0), End = new TimeOnly(11, 0) },
                    new WorkWindow { Start = new TimeOnly(10, 30), End = new TimeOnly(12, 0) },
                ],
            },
            Tuesday = new DayWorkSchedule
            {
                Windows =
                [
                    new WorkWindow { Start = new TimeOnly(14, 0), End = new TimeOnly(14, 0) },
                ],
            },
        };

        var issues = UsageConfigurationRules.ValidateWeeklyWorkSchedule(schedule);

        Assert.Contains(issues, issue => issue.Path == "monday[1]" && issue.Message.Contains("overlap", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Path == "tuesday[0]" && issue.Message.Contains("later than start", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetTotalWeeklyMinutes_SumsMultipleWindowsAcrossDays()
    {
        var schedule = new WeeklyWorkSchedule
        {
            Monday = new DayWorkSchedule
            {
                Windows =
                [
                    new WorkWindow { Start = new TimeOnly(9, 0), End = new TimeOnly(10, 30) },
                    new WorkWindow { Start = new TimeOnly(12, 0), End = new TimeOnly(13, 0) },
                ],
            },
            Wednesday = new DayWorkSchedule
            {
                Windows =
                [
                    new WorkWindow { Start = new TimeOnly(8, 15), End = new TimeOnly(9, 0) },
                ],
            },
        };

        Assert.Equal(195, UsageConfigurationRules.GetTotalWeeklyMinutes(schedule));
    }

    [Fact]
    public void ValidateQuotaThresholds_RequiresStrictAscendingOrder()
    {
        var issues = UsageConfigurationRules.ValidateQuotaThresholds(new QuotaThresholds
        {
            RedBelowPercent = 90,
            YellowBelowPercent = 90,
            BlueAbovePercent = 80,
            PinkAbovePercent = 80,
        });

        Assert.Contains(issues, issue => issue.Path == "yellowBelowPercent");
        Assert.Contains(issues, issue => issue.Path == "blueAbovePercent");
        Assert.Contains(issues, issue => issue.Path == "pinkAbovePercent");
    }
}
