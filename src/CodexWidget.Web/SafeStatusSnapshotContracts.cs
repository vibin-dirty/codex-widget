using CodexWidget.Core;
using System.Text.Json.Serialization;

namespace CodexWidget.Web;

public sealed record SafeStatusAvailabilityResponse
{
    public StatusAvailabilityState State { get; init; } = StatusAvailabilityState.Unknown;

    public StatusAvailabilityCode Code { get; init; } = StatusAvailabilityCode.None;

    public bool IsAvailable { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }

    public static SafeStatusAvailabilityResponse FromAvailability(StatusAvailability availability)
    {
        return new SafeStatusAvailabilityResponse
        {
            State = availability.State,
            Code = availability.Code,
            IsAvailable = availability.IsAvailable,
            Detail = WebApiRedaction.RedactOptionalText(availability.Detail),
        };
    }
}

public sealed record SafeSourceDiagnosticResponse
{
    public SourceDiagnosticCode Code { get; init; } = SourceDiagnosticCode.Unknown;

    public SourceDiagnosticSeverity Severity { get; init; } = SourceDiagnosticSeverity.Info;

    public string Summary { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }

    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>(0, StringComparer.Ordinal);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ObservedAtUtc { get; init; }

    public static SafeSourceDiagnosticResponse FromDiagnostic(SourceDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        return new SafeSourceDiagnosticResponse
        {
            Code = diagnostic.Code,
            Severity = diagnostic.Severity,
            Summary = WebApiRedaction.RedactText(diagnostic.Summary),
            Detail = WebApiRedaction.RedactOptionalText(diagnostic.Detail),
            Context = WebApiRedaction.RedactContext(diagnostic.Context),
            ObservedAtUtc = diagnostic.ObservedAtUtc,
        };
    }
}

public sealed record SafeSourceStatusResponse
{
    public StatusSourceKind Source { get; init; } = StatusSourceKind.Unknown;

    public SourceStatusState State { get; init; } = SourceStatusState.Unknown;

    public SafeStatusAvailabilityResponse Availability { get; init; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ObservedAtUtc { get; init; }

    public IReadOnlyList<SafeSourceDiagnosticResponse> Diagnostics { get; init; } = Array.Empty<SafeSourceDiagnosticResponse>();

    public static SafeSourceStatusResponse FromSourceStatus(SourceStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new SafeSourceStatusResponse
        {
            Source = status.Source,
            State = status.State,
            Availability = SafeStatusAvailabilityResponse.FromAvailability(status.Availability),
            ObservedAtUtc = status.ObservedAtUtc,
            Diagnostics = status.Diagnostics.Select(SafeSourceDiagnosticResponse.FromDiagnostic).ToArray(),
        };
    }
}

public sealed record SafeUsageWindowSnapshotResponse
{
    public UsageWindowKind WindowKind { get; init; } = UsageWindowKind.Unknown;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DurationSeconds { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ResetAtUnixSeconds { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? UsedPercent { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? QuotaLeftPercent { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimeLeftPercent { get; init; }

    public SafeStatusAvailabilityResponse Availability { get; init; } = new();

    public static SafeUsageWindowSnapshotResponse FromUsageWindow(UsageWindowSnapshot window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new SafeUsageWindowSnapshotResponse
        {
            WindowKind = window.WindowKind,
            DurationSeconds = window.DurationSeconds,
            ResetAtUnixSeconds = window.ResetAtUnixSeconds,
            UsedPercent = window.UsedPercent,
            QuotaLeftPercent = window.QuotaLeftPercent,
            TimeLeftPercent = window.TimeLeftPercent,
            Availability = SafeStatusAvailabilityResponse.FromAvailability(window.Availability),
        };
    }
}

public sealed record SafeUsageBucketSnapshotResponse
{
    public string BucketId { get; init; } = string.Empty;

    public string BucketLabel { get; init; } = string.Empty;

    public UsageBucketKind BucketKind { get; init; } = UsageBucketKind.Unknown;

    public IReadOnlyList<SafeUsageWindowSnapshotResponse> Windows { get; init; } = Array.Empty<SafeUsageWindowSnapshotResponse>();

    public UsageBucketFetchStatus FetchStatus { get; init; } = UsageBucketFetchStatus.Unknown;

    public SafeStatusAvailabilityResponse Availability { get; init; } = new();

    public static SafeUsageBucketSnapshotResponse FromUsageBucket(UsageBucketSnapshot bucket)
    {
        ArgumentNullException.ThrowIfNull(bucket);

        return new SafeUsageBucketSnapshotResponse
        {
            BucketId = bucket.BucketId,
            BucketLabel = bucket.BucketLabel,
            BucketKind = bucket.BucketKind,
            Windows = bucket.Windows.Select(FromUsageWindow).ToArray(),
            FetchStatus = bucket.FetchStatus,
            Availability = SafeStatusAvailabilityResponse.FromAvailability(bucket.Availability),
        };

        static SafeUsageWindowSnapshotResponse FromUsageWindow(UsageWindowSnapshot window)
        {
            return SafeUsageWindowSnapshotResponse.FromUsageWindow(window);
        }
    }
}

public sealed record SafeProfileDescriptorResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }

    public SubscriptionTier SubscriptionTier { get; init; } = SubscriptionTier.Unknown;

    public bool IsCurrent { get; init; }

    public ProfileAuthKind AuthKind { get; init; } = ProfileAuthKind.Unknown;

    public ProfileUsageEligibility UsageEligibility { get; init; } = ProfileUsageEligibility.Unknown;

    public SafeSourceStatusResponse SourceStatus { get; init; } = new();

    public static SafeProfileDescriptorResponse FromDescriptor(ProfileDescriptor profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new SafeProfileDescriptorResponse
        {
            ProfileId = profile.ProfileId,
            DisplayName = profile.DisplayName,
            SubscriptionTier = profile.SubscriptionTier,
            IsCurrent = profile.IsCurrent,
            AuthKind = profile.AuthKind,
            UsageEligibility = profile.UsageEligibility,
            SourceStatus = SafeSourceStatusResponse.FromSourceStatus(profile.SourceStatus),
        };
    }
}

public sealed record SafeProfileStatusResponse
{
    public SafeProfileDescriptorResponse Profile { get; init; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SafeUsageBucketSnapshotResponse? MainBucket { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SafeUsageBucketSnapshotResponse? SparkBucket { get; init; }

    public IReadOnlyList<SafeUsageBucketSnapshotResponse> AllBuckets { get; init; } = Array.Empty<SafeUsageBucketSnapshotResponse>();

    public IReadOnlyList<SafeSourceDiagnosticResponse> Diagnostics { get; init; } = Array.Empty<SafeSourceDiagnosticResponse>();

    public static SafeProfileStatusResponse FromProfileStatus(ProfileStatus profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new SafeProfileStatusResponse
        {
            Profile = SafeProfileDescriptorResponse.FromDescriptor(profile.Profile),
            MainBucket = profile.MainBucket is null ? null : SafeUsageBucketSnapshotResponse.FromUsageBucket(profile.MainBucket),
            SparkBucket = profile.SparkBucket is null ? null : SafeUsageBucketSnapshotResponse.FromUsageBucket(profile.SparkBucket),
            AllBuckets = profile.AllBuckets.Select(SafeUsageBucketSnapshotResponse.FromUsageBucket).ToArray(),
            Diagnostics = profile.Diagnostics.Select(SafeSourceDiagnosticResponse.FromDiagnostic).ToArray(),
        };
    }
}

public sealed record SafeStatusRefreshStateResponse
{
    public StatusRefreshReason Reason { get; init; } = StatusRefreshReason.Unknown;

    public string Scope { get; init; } = StatusRefreshScopeParser.Full;

    public StatusRefreshOutcome Outcome { get; init; } = StatusRefreshOutcome.Idle;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RequestedAtUtc { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? StartedAtUtc { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CompletedAtUtc { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SafeSourceDiagnosticResponse? Failure { get; init; }

    public static SafeStatusRefreshStateResponse FromRefreshState(StatusRefreshState refreshState)
    {
        ArgumentNullException.ThrowIfNull(refreshState);

        return new SafeStatusRefreshStateResponse
        {
            Reason = refreshState.Reason,
            Scope = StatusRefreshScopeParser.ToContractValue(refreshState.Scope),
            Outcome = refreshState.Outcome,
            RequestedAtUtc = refreshState.RequestedAtUtc,
            StartedAtUtc = refreshState.StartedAtUtc,
            CompletedAtUtc = refreshState.CompletedAtUtc,
            Failure = refreshState.Failure is null ? null : SafeSourceDiagnosticResponse.FromDiagnostic(refreshState.Failure),
        };
    }
}

public sealed record SafeStatusSnapshotResponse
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentProfileId { get; init; }

    public SafeStatusRefreshStateResponse RefreshState { get; init; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? NextScheduledRefreshAtUtc { get; init; }

    public IReadOnlyList<SafeProfileStatusResponse> Profiles { get; init; } = Array.Empty<SafeProfileStatusResponse>();

    public IReadOnlyList<SafeSourceStatusResponse> Sources { get; init; } = Array.Empty<SafeSourceStatusResponse>();

    public static SafeStatusSnapshotResponse FromSnapshot(StatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new SafeStatusSnapshotResponse
        {
            CapturedAtUtc = snapshot.CapturedAtUtc,
            CurrentProfileId = snapshot.CurrentProfileId,
            RefreshState = SafeStatusRefreshStateResponse.FromRefreshState(snapshot.RefreshState),
            NextScheduledRefreshAtUtc = snapshot.NextScheduledRefreshAtUtc,
            Profiles = snapshot.Profiles.Select(SafeProfileStatusResponse.FromProfileStatus).ToArray(),
            Sources = snapshot.Sources.Select(SafeSourceStatusResponse.FromSourceStatus).ToArray(),
        };
    }
}
