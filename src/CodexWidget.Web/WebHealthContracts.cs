using CodexWidget.Core;
using CodexWidget.Runtime;
using System.Text.Json.Serialization;

namespace CodexWidget.Web;

public sealed record WebLivenessResponse
{
    public string Status { get; init; } = "ok";
}

public enum WebSchedulerHealthState
{
    Unknown = 0,
    Disabled = 1,
    Starting = 2,
    Running = 3,
    Unavailable = 4,
}

public enum WebRefreshStalenessState
{
    Unknown = 0,
    Fresh = 1,
    Stale = 2,
}

public sealed record WebSchedulerHealthResponse
{
    public bool Enabled { get; init; }

    public WebSchedulerHealthState State { get; init; } = WebSchedulerHealthState.Unknown;
}

public sealed record WebRefreshHealthResponse
{
    public StatusRefreshOutcome LatestOutcome { get; init; } = StatusRefreshOutcome.Idle;

    public WebRefreshStalenessState Staleness { get; init; } = WebRefreshStalenessState.Unknown;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CapturedAtUtc { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? NextScheduledRefreshAtUtc { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SnapshotAgeSeconds { get; init; }

    public bool IsRefreshRunning { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureSummary { get; init; }
}

public sealed record WebHealthStatusResponse
{
    public string Status { get; init; } = "ok";

    public WebRuntimeInitializationStatus RuntimeInitialization { get; init; } = WebRuntimeInitializationStatus.Pending;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RuntimeInitializationAttemptedAtUtc { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RuntimeReadyAtUtc { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuntimeFailure { get; init; }

    public WebSchedulerHealthResponse Scheduler { get; init; } = new();

    public WebRefreshHealthResponse Refresh { get; init; } = new();

    public static WebHealthStatusResponse FromRuntimeState(
        WebRuntimeInitializationSnapshot initialization,
        ResolvedCodexWidgetWebOptions webOptions,
        RefreshMetadata? refreshMetadata,
        string? runtimeFailureOverride = null)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(webOptions);

        var schedulerState = ResolveSchedulerState(initialization.Status, webOptions.EnableScheduler);
        var safeRuntimeFailure = WebApiRedaction.RedactOptionalText(runtimeFailureOverride ?? initialization.FailureSummary);
        var refreshStatus = BuildRefreshStatus(refreshMetadata, webOptions.PollingIntervalSeconds);
        var isDegraded = initialization.Status == WebRuntimeInitializationStatus.Failed
            || !string.IsNullOrWhiteSpace(safeRuntimeFailure);

        return new WebHealthStatusResponse
        {
            Status = isDegraded ? "degraded" : "ok",
            RuntimeInitialization = initialization.Status,
            RuntimeInitializationAttemptedAtUtc = initialization.AttemptedAtUtc,
            RuntimeReadyAtUtc = initialization.ReadyAtUtc,
            RuntimeFailure = safeRuntimeFailure,
            Scheduler = new WebSchedulerHealthResponse
            {
                Enabled = webOptions.EnableScheduler,
                State = schedulerState,
            },
            Refresh = refreshStatus,
        };
    }

    private static WebSchedulerHealthState ResolveSchedulerState(WebRuntimeInitializationStatus initializationStatus, bool schedulerEnabled)
    {
        if (!schedulerEnabled)
        {
            return WebSchedulerHealthState.Disabled;
        }

        return initializationStatus switch
        {
            WebRuntimeInitializationStatus.Ready => WebSchedulerHealthState.Running,
            WebRuntimeInitializationStatus.Failed => WebSchedulerHealthState.Unavailable,
            _ => WebSchedulerHealthState.Starting,
        };
    }

    private static WebRefreshHealthResponse BuildRefreshStatus(
        RefreshMetadata? metadata,
        int pollingIntervalSeconds)
    {
        if (metadata is null)
        {
            return new WebRefreshHealthResponse();
        }

        var snapshotAge = metadata.SnapshotAge < TimeSpan.Zero ? TimeSpan.Zero : metadata.SnapshotAge;
        var staleThreshold = TimeSpan.FromSeconds(Math.Max(1, pollingIntervalSeconds) * 2);
        var staleness = snapshotAge > staleThreshold
            ? WebRefreshStalenessState.Stale
            : WebRefreshStalenessState.Fresh;

        return new WebRefreshHealthResponse
        {
            LatestOutcome = metadata.LatestOutcome,
            Staleness = staleness,
            CapturedAtUtc = metadata.CapturedAtUtc,
            NextScheduledRefreshAtUtc = metadata.NextScheduledRefreshAtUtc,
            SnapshotAgeSeconds = Math.Round(snapshotAge.TotalSeconds, 3),
            IsRefreshRunning = metadata.IsRefreshRunning,
            FailureSummary = WebApiRedaction.RedactOptionalText(metadata.LatestSafeFailureSummary),
        };
    }
}
