using CodexWidget.Core;

namespace CodexWidget.Presentation;

public enum WidgetPresentationSeverity
{
    Unknown = 0,
    Normal = 1,
    Warning = 2,
    Critical = 3,
    Unavailable = 4,
    Error = 5,
}

public enum WidgetRefreshVisualState
{
    Idle = 0,
    Refreshing = 1,
    Stale = 2,
    Unavailable = 3,
    Warning = 4,
    Critical = 5,
    Error = 6,
}

public sealed record WidgetDiagnosticPresentation
{
    public SourceDiagnosticCode Code { get; init; } = SourceDiagnosticCode.Unknown;

    public SourceDiagnosticSeverity Severity { get; init; } = SourceDiagnosticSeverity.Info;

    public string SummaryText { get; init; } = string.Empty;

    public string? DetailText { get; init; }

    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>(0, StringComparer.Ordinal);

    public DateTimeOffset? ObservedAtUtc { get; init; }

    public string ObservedAtText { get; init; } = "Observed: unavailable.";
}

public sealed record WidgetSourcePresentation
{
    public StatusSourceKind Source { get; init; } = StatusSourceKind.Unknown;

    public SourceStatusState State { get; init; } = SourceStatusState.Unknown;

    public string SourceText { get; init; } = "Source: unknown.";

    public string StateText { get; init; } = "State: unknown.";

    public string AvailabilityText { get; init; } = "Availability: unavailable.";

    public string ObservedAtText { get; init; } = "Observed: unavailable.";

    public IReadOnlyList<WidgetDiagnosticPresentation> Diagnostics { get; init; } = Array.Empty<WidgetDiagnosticPresentation>();
}

public sealed record WidgetRefreshPresentation
{
    public WidgetRefreshVisualState State { get; init; } = WidgetRefreshVisualState.Unavailable;

    public string StateText { get; init; } = "Status unavailable.";

    public string DetailText { get; init; } = "Status data is unavailable.";

    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;

    public string CapturedAtText { get; init; } = "Captured: unavailable.";

    public TimeSpan SnapshotAge { get; init; }

    public string SnapshotAgeText { get; init; } = "Snapshot age: unavailable.";

    public DateTimeOffset? NextScheduledRefreshAtUtc { get; init; }

    public string NextScheduledRefreshText { get; init; } = "Next refresh: unavailable.";

    public IReadOnlyList<WidgetSourcePresentation> Sources { get; init; } = Array.Empty<WidgetSourcePresentation>();

    public IReadOnlyList<WidgetDiagnosticPresentation> Diagnostics { get; init; } = Array.Empty<WidgetDiagnosticPresentation>();
}

public sealed record WidgetWindowPresentation
{
    public UsageWindowKind WindowKind { get; init; } = UsageWindowKind.Unknown;

    public string WindowIdentityText { get; init; } = "Window: unknown.";

    public StatusAvailability Availability { get; init; } = StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable);

    public bool IsAvailable { get; init; }

    public bool HasQuotaLeft { get; init; }

    public bool HasTimeLeft { get; init; }

    public string AvailabilityText { get; init; } = "Window unavailable.";

    public int? QuotaLeftPercent { get; init; }

    public string QuotaText { get; init; } = "Quota left unavailable.";

    public int? TimeLeftPercent { get; init; }

    public string TimeText { get; init; } = "Time left unavailable.";

    public long? EndsAtUnixSeconds { get; init; }

    public string EndsAtText { get; init; } = "Ends: unavailable.";

    public string EndsAtCompactText { get; init; } = WidgetPresentationFormatter.CompactUnavailableTimestampToken;
}

public sealed record WidgetBucketPresentation
{
    public string BucketId { get; init; } = string.Empty;

    public string BucketLabel { get; init; } = string.Empty;

    public string BucketIdentityText { get; init; } = "Bucket: unknown.";

    public UsageBucketKind BucketKind { get; init; } = UsageBucketKind.Unknown;

    public UsageBucketFetchStatus FetchStatus { get; init; } = UsageBucketFetchStatus.Unknown;

    public string FetchStatusText { get; init; } = "Fetch status: unknown.";

    public StatusAvailability Availability { get; init; } = new(StatusAvailabilityState.Unknown);

    public string AvailabilityText { get; init; } = "Bucket availability unknown.";

    public WidgetWindowPresentation? FiveHourWindow { get; init; }

    public WidgetWindowPresentation? WeeklyWindow { get; init; }

    public IReadOnlyList<WidgetWindowPresentation> Windows { get; init; } = Array.Empty<WidgetWindowPresentation>();
}

public sealed record WidgetProfilePresentation
{
    public string? ProfileId { get; init; }

    public string ProfileDisplayName { get; init; } = "Unknown profile";

    public string ProfileIdentityText { get; init; } = "Profile: unknown.";

    public bool IsCurrent { get; init; }

    public string ActiveProfileText { get; init; } = "Profile is not active.";

    public SubscriptionTier SubscriptionTier { get; init; } = SubscriptionTier.Unknown;

    public string SubscriptionText { get; init; } = "Subscription: unknown.";

    public WidgetBucketPresentation? MainBucket { get; init; }

    public WidgetBucketPresentation? SparkBucket { get; init; }

    public IReadOnlyList<WidgetBucketPresentation> AdditionalBuckets { get; init; } = Array.Empty<WidgetBucketPresentation>();

    public IReadOnlyList<WidgetDiagnosticPresentation> Diagnostics { get; init; } = Array.Empty<WidgetDiagnosticPresentation>();
}

public sealed record MinimalWidgetPresentation
{
    public WidgetProfilePresentation? CurrentProfile { get; init; }

    public string SummaryText { get; init; } = "Current profile is unavailable.";
}

public sealed record CompactWidgetPresentation
{
    public IReadOnlyList<WidgetProfilePresentation> Profiles { get; init; } = Array.Empty<WidgetProfilePresentation>();

    public string SummaryText { get; init; } = "No profile data available.";
}

public sealed record FullWidgetPresentation
{
    public IReadOnlyList<WidgetProfilePresentation> Profiles { get; init; } = Array.Empty<WidgetProfilePresentation>();

    public string SummaryText { get; init; } = "No profile data available.";
}

public sealed record WidgetPresentationState
{
    public WidgetViewKind SelectedView { get; init; } = WidgetViewKind.Compact;

    public CompactAccountLayout CompactAccountLayout { get; init; } = WidgetPreferenceDefaults.DefaultCompactAccountLayout;

    public int WidgetScalePercent { get; init; } = WidgetPreferenceDefaults.DefaultWidgetScalePercent;

    public WidgetRefreshPresentation Refresh { get; init; } = new();

    public MinimalWidgetPresentation Minimal { get; init; } = new();

    public CompactWidgetPresentation Compact { get; init; } = new();

    public FullWidgetPresentation Full { get; init; } = new();

    public string SelectedViewSummaryText { get; init; } = "Status data is unavailable.";
}
