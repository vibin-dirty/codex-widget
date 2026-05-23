namespace CodexWidget.Core;

public enum UsageBucketKind
{
    Unknown = 0,
    MainCodex = 1,
    Additional = 2,
    Spark = 3,
}

public enum UsageBucketFetchStatus
{
    Unknown = 0,
    NotRequested = 1,
    Succeeded = 2,
    Partial = 3,
    Unavailable = 4,
    Unauthorized = 5,
    Error = 6,
}

public enum UsageWindowKind
{
    Unknown = 0,
    FiveHour = 1,
    Weekly = 2,
    Additional = 3,
}

public sealed record UsageWindowSnapshot
{
    public UsageWindowKind WindowKind { get; init; } = UsageWindowKind.Unknown;

    public int? DurationSeconds { get; init; }

    public long? ResetAtUnixSeconds { get; init; }

    public double? UsedPercent { get; init; }

    public int? QuotaLeftPercent { get; init; }

    public int? TimeLeftPercent { get; init; }

    public StatusAvailability Availability { get; init; } = StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable);
}

public sealed record UsageBucketSnapshot
{
    public string BucketId { get; init; } = string.Empty;

    public string BucketLabel { get; init; } = string.Empty;

    public UsageBucketKind BucketKind { get; init; } = UsageBucketKind.Unknown;

    public IReadOnlyList<UsageWindowSnapshot> Windows { get; init; } = Array.Empty<UsageWindowSnapshot>();

    public UsageBucketFetchStatus FetchStatus { get; init; } = UsageBucketFetchStatus.Unknown;

    public StatusAvailability Availability { get; init; } = new(StatusAvailabilityState.Unknown);
}
