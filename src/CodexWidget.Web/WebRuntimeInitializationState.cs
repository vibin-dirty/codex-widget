namespace CodexWidget.Web;

public enum WebRuntimeInitializationStatus
{
    Pending = 0,
    Ready = 1,
    Failed = 2,
}

public sealed record WebRuntimeInitializationSnapshot
{
    public WebRuntimeInitializationStatus Status { get; init; } = WebRuntimeInitializationStatus.Pending;

    public DateTimeOffset? AttemptedAtUtc { get; init; }

    public DateTimeOffset? ReadyAtUtc { get; init; }

    public string? FailureSummary { get; init; }
}

public sealed class WebRuntimeInitializationState
{
    private readonly object gate = new();
    private WebRuntimeInitializationSnapshot snapshot = new();

    public WebRuntimeInitializationSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return snapshot;
            }
        }
    }

    public void MarkReady(DateTimeOffset attemptedAtUtc, DateTimeOffset readyAtUtc)
    {
        lock (gate)
        {
            snapshot = new WebRuntimeInitializationSnapshot
            {
                Status = WebRuntimeInitializationStatus.Ready,
                AttemptedAtUtc = attemptedAtUtc,
                ReadyAtUtc = readyAtUtc,
            };
        }
    }

    public void MarkAttempted(DateTimeOffset attemptedAtUtc)
    {
        lock (gate)
        {
            snapshot = new WebRuntimeInitializationSnapshot
            {
                Status = WebRuntimeInitializationStatus.Pending,
                AttemptedAtUtc = attemptedAtUtc,
                ReadyAtUtc = null,
                FailureSummary = null,
            };
        }
    }

    public void MarkFailed(DateTimeOffset attemptedAtUtc, string failureSummary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureSummary);

        lock (gate)
        {
            snapshot = new WebRuntimeInitializationSnapshot
            {
                Status = WebRuntimeInitializationStatus.Failed,
                AttemptedAtUtc = attemptedAtUtc,
                FailureSummary = failureSummary,
            };
        }
    }
}
