using CodexWidget.Core;

namespace CodexWidget.Web;

public sealed record WebWeeklyWorkScheduleOptions
{
    public WorkWindow[]? Monday { get; init; }

    public WorkWindow[]? Tuesday { get; init; }

    public WorkWindow[]? Wednesday { get; init; }

    public WorkWindow[]? Thursday { get; init; }

    public WorkWindow[]? Friday { get; init; }

    public WorkWindow[]? Saturday { get; init; }

    public WorkWindow[]? Sunday { get; init; }

    public WeeklyWorkSchedule ToCoreModel(WeeklyWorkSchedule? fallback = null)
    {
        fallback ??= UsageConfigurationDefaults.CreateDefaultWeeklyWorkSchedule();

        return new WeeklyWorkSchedule
        {
            Monday = new DayWorkSchedule { Windows = Monday ?? fallback.Monday.Windows },
            Tuesday = new DayWorkSchedule { Windows = Tuesday ?? fallback.Tuesday.Windows },
            Wednesday = new DayWorkSchedule { Windows = Wednesday ?? fallback.Wednesday.Windows },
            Thursday = new DayWorkSchedule { Windows = Thursday ?? fallback.Thursday.Windows },
            Friday = new DayWorkSchedule { Windows = Friday ?? fallback.Friday.Windows },
            Saturday = new DayWorkSchedule { Windows = Saturday ?? fallback.Saturday.Windows },
            Sunday = new DayWorkSchedule { Windows = Sunday ?? fallback.Sunday.Windows },
        };
    }
}

public sealed record CodexWidgetWebOptions
{
    public const string SectionName = "CodexWidgetWeb";

    public string[] BindUrls { get; init; } = Array.Empty<string>();

    public bool AllowLanBinding { get; init; }

    public bool EnableScheduler { get; init; } = true;

    public bool ServeStaticFiles { get; init; } = true;

    public bool EnableCors { get; init; }

    public string[] AllowedCorsOrigins { get; init; } = Array.Empty<string>();

    public int PollingIntervalSeconds { get; init; } = 15;

    public string? CodexProfilesHome { get; init; }

    public WebWeeklyWorkScheduleOptions WorkSchedule { get; init; } = new();

    public QuotaThresholds QuotaThresholds { get; init; } = UsageConfigurationDefaults.CreateDefaultQuotaThresholds();
}
