namespace CodexWidget.Web;

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
}
