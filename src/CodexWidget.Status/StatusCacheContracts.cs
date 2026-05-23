using CodexWidget.Core;
using CodexWidget.Profiles;

namespace CodexWidget.Status;

public sealed class StatusSnapshotChangedEventArgs : EventArgs
{
    public StatusSnapshotChangedEventArgs(StatusSnapshot previousSnapshot, StatusSnapshot currentSnapshot)
    {
        PreviousSnapshot = previousSnapshot ?? throw new ArgumentNullException(nameof(previousSnapshot));
        CurrentSnapshot = currentSnapshot ?? throw new ArgumentNullException(nameof(currentSnapshot));
    }

    public StatusSnapshot PreviousSnapshot { get; }

    public StatusSnapshot CurrentSnapshot { get; }
}

public sealed record StatusCacheServiceOptions
{
    public WidgetPreferences Preferences { get; init; } = WidgetPreferenceDefaults.Create();

    public ProfileSnapshotReadOptions? ProfileSnapshotReadOptions { get; init; }

    public int MaxConcurrentUsageFetches { get; init; } = 3;
}

public interface IStatusCacheService : IDisposable
{
    StatusSnapshot CurrentSnapshot { get; }

    CodexHomePaths? CurrentPaths { get; }

    bool IsInitialized { get; }

    event EventHandler<StatusSnapshotChangedEventArgs>? SnapshotChanged;

    Task<StatusSnapshot> InitializeAsync(CancellationToken cancellationToken = default);

    Task<StatusSnapshot> RefreshAsync(StatusRefreshReason reason, StatusRefreshScope scope, CancellationToken cancellationToken = default);
}
