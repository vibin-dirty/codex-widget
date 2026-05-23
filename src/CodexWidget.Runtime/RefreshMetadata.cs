using CodexWidget.Core;

namespace CodexWidget.Runtime;

public sealed record RefreshMetadata
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;

    public DateTimeOffset? NextScheduledRefreshAtUtc { get; init; }

    public TimeSpan SnapshotAge { get; init; }

    public StatusRefreshOutcome LatestOutcome { get; init; } = StatusRefreshOutcome.Idle;

    public bool IsRefreshRunning { get; init; }

    public string? LatestSafeFailureSummary { get; init; }
}
