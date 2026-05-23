using CodexWidget.Core;
using CodexWidget.Profiles;

namespace CodexWidget.Status;

public sealed record ProfileChangeSet
{
    public StatusRefreshReason Reason { get; init; } = StatusRefreshReason.ProfileChanged;

    public StatusRefreshScope Scope { get; init; } = StatusRefreshScope.Full;

    public IReadOnlyList<string> ChangedPaths { get; init; } = Array.Empty<string>();
}

public sealed record ProfileChangeMonitorPaths
{
    public string? CurrentAuthPath { get; init; }

    public string? ConfigPath { get; init; }

    public string? ProfilesIndexPath { get; init; }

    public string? ProfilesDirectory { get; init; }

    public static ProfileChangeMonitorPaths FromCodexHomePaths(CodexHomePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return new ProfileChangeMonitorPaths
        {
            CurrentAuthPath = paths.CurrentAuthPath,
            ConfigPath = paths.ConfigPath,
            ProfilesIndexPath = paths.ProfilesIndexPath,
            ProfilesDirectory = paths.ProfilesDirectory,
        };
    }
}

public sealed record ProfileChangeMonitorOptions
{
    public TimeSpan DebouncePeriod { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(5);
}

public interface IProfileChangeMonitor : IDisposable
{
    event EventHandler<ProfileChangeSet>? Changed;

    void UpdatePaths(ProfileChangeMonitorPaths paths);

    void Start(CancellationToken cancellationToken = default);
}

public sealed record StatusRefreshSchedulerOptions
{
    public WidgetPreferences Preferences { get; init; } = WidgetPreferenceDefaults.Create();
}

public interface IStatusRefreshScheduler : IDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task RequestStaleWidgetOpenRefreshAsync(CancellationToken cancellationToken = default);

    Task RequestRefreshAsync(StatusRefreshReason reason, StatusRefreshScope scope, CancellationToken cancellationToken = default);
}
