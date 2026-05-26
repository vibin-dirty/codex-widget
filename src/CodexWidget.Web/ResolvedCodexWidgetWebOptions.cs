using CodexWidget.Core;

namespace CodexWidget.Web;

public sealed record ResolvedCodexWidgetWebOptions
{
    public static readonly string CorsPolicyName = "ExplicitOrigins";

    public IReadOnlyList<string> BindUrls { get; init; } = Array.Empty<string>();

    public bool AllowLanBinding { get; init; }

    public bool EnableScheduler { get; init; }

    public bool ServeStaticFiles { get; init; }

    public bool EnableCors { get; init; }

    public IReadOnlyList<string> AllowedCorsOrigins { get; init; } = Array.Empty<string>();

    public int PollingIntervalSeconds { get; init; }

    public string? CodexProfilesHome { get; init; }

    public WeeklyWorkSchedule WorkSchedule { get; init; } = UsageConfigurationDefaults.CreateDefaultWeeklyWorkSchedule();

    public QuotaThresholds QuotaThresholds { get; init; } = UsageConfigurationDefaults.CreateDefaultQuotaThresholds();
}
