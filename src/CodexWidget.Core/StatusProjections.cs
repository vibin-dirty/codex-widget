namespace CodexWidget.Core;

public sealed record ProjectedUsageWindow
{
    public UsageWindowKind WindowKind { get; init; } = UsageWindowKind.Unknown;

    public int? DurationSeconds { get; init; }

    public long? EndsAtUnixSeconds { get; init; }

    public double? UsedPercent { get; init; }

    public int? QuotaLeftPercent { get; init; }

    public int? TimeLeftPercent { get; init; }

    public StatusAvailability Availability { get; init; } = StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable);
}

public sealed record ProjectedUsageBucket
{
    public string BucketId { get; init; } = string.Empty;

    public string BucketLabel { get; init; } = string.Empty;

    public UsageBucketKind BucketKind { get; init; } = UsageBucketKind.Unknown;

    public UsageBucketFetchStatus FetchStatus { get; init; } = UsageBucketFetchStatus.Unknown;

    public StatusAvailability Availability { get; init; } = new(StatusAvailabilityState.Unknown);

    public ProjectedUsageWindow? FiveHourWindow { get; init; }

    public ProjectedUsageWindow? WeeklyWindow { get; init; }

    public IReadOnlyList<ProjectedUsageWindow> Windows { get; init; } = Array.Empty<ProjectedUsageWindow>();
}

public sealed record StatusAllProfileProjection
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public string? LoginName { get; init; }

    public bool IsCurrent { get; init; }

    public SubscriptionTier SubscriptionTier { get; init; } = SubscriptionTier.Unknown;

    public int? Main5HourLeftPercent { get; init; }

    public int? MainWeeklyLeftPercent { get; init; }

    public int? Spark5HourLeftPercent { get; init; }

    public int? SparkWeeklyLeftPercent { get; init; }

    public long? Main5HourEndsAtUnixSeconds { get; init; }

    public long? MainWeeklyEndsAtUnixSeconds { get; init; }

    public long? Spark5HourEndsAtUnixSeconds { get; init; }

    public long? SparkWeeklyEndsAtUnixSeconds { get; init; }

    public int? Main5HourTimeLeftPercent { get; init; }

    public int? MainWeeklyTimeLeftPercent { get; init; }

    public int? Spark5HourTimeLeftPercent { get; init; }

    public int? SparkWeeklyTimeLeftPercent { get; init; }
}

public sealed record MinimalProfileProjection
{
    public StatusAllProfileProjection StatusAll { get; init; } = new();

    public ProjectedUsageBucket? MainBucket { get; init; }
}

public sealed record MinimalStatusViewProjection
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;

    public MinimalProfileProjection? CurrentProfile { get; init; }
}

public sealed record CompactProfileProjection
{
    public StatusAllProfileProjection StatusAll { get; init; } = new();

    public ProjectedUsageBucket? MainBucket { get; init; }

    public ProjectedUsageBucket? SparkBucket { get; init; }
}

public sealed record CompactStatusViewProjection
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;

    public IReadOnlyList<CompactProfileProjection> Profiles { get; init; } = Array.Empty<CompactProfileProjection>();
}

public sealed record FullProfileProjection
{
    public StatusAllProfileProjection StatusAll { get; init; } = new();

    public ProjectedUsageBucket? MainBucket { get; init; }

    public ProjectedUsageBucket? SparkBucket { get; init; }

    public IReadOnlyList<ProjectedUsageBucket> AdditionalBuckets { get; init; } = Array.Empty<ProjectedUsageBucket>();

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();
}

public sealed record FullStatusViewProjection
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;

    public IReadOnlyList<FullProfileProjection> Profiles { get; init; } = Array.Empty<FullProfileProjection>();
}
