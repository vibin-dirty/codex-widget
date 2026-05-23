namespace CodexWidget.Core;

public enum StatusRefreshReason
{
    Unknown = 0,
    Startup = 1,
    StaleWidgetOpen = 2,
    ProfileChanged = 3,
    ConfigChanged = 4,
    Periodic = 5,
    ResetTimeElapsed = 6,
    Manual = 7,
    Scheduled = 8,
}

public enum StatusRefreshScope
{
    Full = 0,
    ProfileOnly = 1,
    UsageOnly = 2,
}

public enum StatusRefreshOutcome
{
    Idle = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}

public sealed record StatusRefreshState
{
    public StatusRefreshReason Reason { get; init; } = StatusRefreshReason.Unknown;

    public StatusRefreshScope Scope { get; init; } = StatusRefreshScope.Full;

    public StatusRefreshOutcome Outcome { get; init; } = StatusRefreshOutcome.Idle;

    public DateTimeOffset? RequestedAtUtc { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public SourceDiagnostic? Failure { get; init; }
}

public sealed record StatusSnapshot
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;

    public IReadOnlyList<ProfileStatus> Profiles { get; init; } = Array.Empty<ProfileStatus>();

    public string? CurrentProfileId { get; init; }

    public StatusRefreshState RefreshState { get; init; } = new();

    public DateTimeOffset? NextScheduledRefreshAtUtc { get; init; }

    public IReadOnlyList<SourceStatus> Sources { get; init; } = Array.Empty<SourceStatus>();
}
