using CodexWidget.Core;

namespace CodexWidget.Status;

public sealed class ProfileChangeMonitor : IProfileChangeMonitor
{
    private readonly ProfileChangeMonitorOptions options;
    private readonly IProfileFileWatcherFactory watcherFactory;
    private readonly IProfileFileSystem fileSystem;
    private readonly IAsyncDelay delay;
    private readonly CancellationTokenSource disposeCts = new();
    private readonly object sync = new();

    private ProfileChangeMonitorPaths? currentPaths;
    private IProfileFileWatcher? watcher;
    private Task? pollingTask;
    private CancellationTokenSource? debounceCts;
    private Dictionary<string, FileMetadataSnapshot> baselineMetadata = new(StringComparer.OrdinalIgnoreCase);
    private ProfileChangeSet? pendingChangeSet;
    private bool started;
    private bool usePollingFallback;
    private bool isDisposed;

    public ProfileChangeMonitor(ProfileChangeMonitorOptions? options = null)
        : this(
            options,
            FileSystemProfileFileWatcherFactory.Instance,
            SystemProfileFileSystem.Instance,
            TaskAsyncDelay.Instance)
    {
    }

    internal ProfileChangeMonitor(
        ProfileChangeMonitorOptions? options,
        IProfileFileWatcherFactory watcherFactory,
        IProfileFileSystem fileSystem,
        IAsyncDelay delay)
    {
        this.options = options ?? new ProfileChangeMonitorOptions();
        this.watcherFactory = watcherFactory ?? throw new ArgumentNullException(nameof(watcherFactory));
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        this.delay = delay ?? throw new ArgumentNullException(nameof(delay));

        if (this.options.DebouncePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "DebouncePeriod must be greater than zero.");
        }

        if (this.options.PollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "PollingInterval must be greater than zero.");
        }
    }

    public event EventHandler<ProfileChangeSet>? Changed;

    public void UpdatePaths(ProfileChangeMonitorPaths paths)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(paths);

        lock (sync)
        {
            currentPaths = paths;
            baselineMetadata = CaptureMetadataSnapshot(paths);
        }

        if (!started)
        {
            return;
        }

        if (!TryStartWatcher())
        {
            EnablePollingFallback();
        }
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (started)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        started = true;

        lock (sync)
        {
            if (currentPaths is not null)
            {
                baselineMetadata = CaptureMetadataSnapshot(currentPaths);
            }
        }

        if (!TryStartWatcher())
        {
            EnablePollingFallback();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        disposeCts.Cancel();

        lock (sync)
        {
            watcher?.Dispose();
            watcher = null;

            debounceCts?.Cancel();
            debounceCts?.Dispose();
            debounceCts = null;
            pendingChangeSet = null;
        }

        try
        {
            pollingTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            disposeCts.Dispose();
        }
    }

    private void HandleWatcherPathChanged(string changedPath)
    {
        var changeSet = ClassifyChange(changedPath);
        if (changeSet is null)
        {
            return;
        }

        QueueDebouncedChange(changeSet);
    }

    private void HandleWatcherError(Exception _)
    {
        EnablePollingFallback();
    }

    private bool TryStartWatcher()
    {
        ProfileChangeMonitorPaths? paths;
        lock (sync)
        {
            paths = currentPaths;
        }

        if (paths is null)
        {
            return false;
        }

        try
        {
            var nextWatcher = watcherFactory.TryCreate(paths, HandleWatcherPathChanged, HandleWatcherError);
            if (nextWatcher is null)
            {
                return false;
            }

            lock (sync)
            {
                watcher?.Dispose();
                watcher = nextWatcher;
            }

            nextWatcher.Start();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EnablePollingFallback()
    {
        lock (sync)
        {
            if (usePollingFallback || isDisposed)
            {
                return;
            }

            usePollingFallback = true;
            watcher?.Dispose();
            watcher = null;
        }

        pollingTask = Task.Run(async () =>
        {
            while (!disposeCts.IsCancellationRequested)
            {
                try
                {
                    await delay.DelayAsync(options.PollingInterval, disposeCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                PollForChanges();
            }
        }, CancellationToken.None);
    }

    private void PollForChanges()
    {
        ProfileChangeMonitorPaths? paths;
        Dictionary<string, FileMetadataSnapshot> previousBaseline;

        lock (sync)
        {
            paths = currentPaths;
            previousBaseline = baselineMetadata;
        }

        if (paths is null)
        {
            return;
        }

        var nextBaseline = CaptureMetadataSnapshot(paths);
        var changedPaths = nextBaseline
            .Where(entry => !previousBaseline.TryGetValue(entry.Key, out var previous) || !entry.Value.Equals(previous))
            .Select(entry => entry.Key)
            .ToArray();

        if (changedPaths.Length == 0)
        {
            return;
        }

        lock (sync)
        {
            baselineMetadata = nextBaseline;
        }

        foreach (var changedPath in changedPaths)
        {
            var changeSet = ClassifyChange(changedPath);
            if (changeSet is not null)
            {
                QueueDebouncedChange(changeSet);
            }
        }
    }

    internal void PollForChangesOnceForTesting()
    {
        PollForChanges();
    }

    private Dictionary<string, FileMetadataSnapshot> CaptureMetadataSnapshot(ProfileChangeMonitorPaths paths)
    {
        var metadataByPath = new Dictionary<string, FileMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);

        TrackPath(paths.CurrentAuthPath);
        TrackPath(paths.ConfigPath);
        TrackPath(paths.ProfilesIndexPath);

        if (!string.IsNullOrWhiteSpace(paths.ProfilesDirectory))
        {
            foreach (var profilePath in fileSystem.EnumerateSavedProfileJsonFiles(paths.ProfilesDirectory))
            {
                TrackPath(profilePath);
            }
        }

        return metadataByPath;

        void TrackPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            metadataByPath[path] = fileSystem.GetMetadata(path);
        }
    }

    private ProfileChangeSet? ClassifyChange(string? changedPath)
    {
        if (string.IsNullOrWhiteSpace(changedPath))
        {
            return null;
        }

        var fileName = Path.GetFileName(changedPath);
        if (fileName.Equals("config.toml", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileChangeSet
            {
                Reason = StatusRefreshReason.ConfigChanged,
                Scope = StatusRefreshScope.Full,
                ChangedPaths = [changedPath],
            };
        }

        if (fileName.Equals("auth.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("profiles.json", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileChangeSet
            {
                Reason = StatusRefreshReason.ProfileChanged,
                Scope = StatusRefreshScope.Full,
                ChangedPaths = [changedPath],
            };
        }

        if (Path.GetExtension(fileName).Equals(".json", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("update.json", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileChangeSet
            {
                Reason = StatusRefreshReason.ProfileChanged,
                Scope = StatusRefreshScope.Full,
                ChangedPaths = [changedPath],
            };
        }

        if (currentPaths?.ProfilesDirectory is not null
            && string.Equals(changedPath.TrimEnd(Path.DirectorySeparatorChar), currentPaths.ProfilesDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileChangeSet
            {
                Reason = StatusRefreshReason.ProfileChanged,
                Scope = StatusRefreshScope.Full,
                ChangedPaths = [changedPath],
            };
        }

        return null;
    }

    private void QueueDebouncedChange(ProfileChangeSet changeSet)
    {
        CancellationTokenSource? nextDebounceCts;

        lock (sync)
        {
            if (pendingChangeSet is null)
            {
                pendingChangeSet = changeSet;
            }
            else
            {
                pendingChangeSet = MergeChangeSets(pendingChangeSet, changeSet);
            }

            debounceCts?.Cancel();
            debounceCts?.Dispose();
            debounceCts = CancellationTokenSource.CreateLinkedTokenSource(disposeCts.Token);
            nextDebounceCts = debounceCts;
        }

        _ = DebounceAndPublishAsync(nextDebounceCts.Token);
    }

    private async Task DebounceAndPublishAsync(CancellationToken cancellationToken)
    {
        try
        {
            await delay.DelayAsync(options.DebouncePeriod, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ProfileChangeSet? next;
        lock (sync)
        {
            if (isDisposed || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            next = pendingChangeSet;
            pendingChangeSet = null;
            debounceCts?.Dispose();
            debounceCts = null;
        }

        if (next is not null)
        {
            Changed?.Invoke(this, next);
        }
    }

    private static ProfileChangeSet MergeChangeSets(ProfileChangeSet existing, ProfileChangeSet incoming)
    {
        var reason = incoming.Reason;
        var scope = PromoteScope(existing.Scope, incoming.Scope);
        var changedPaths = existing.ChangedPaths
            .Concat(incoming.ChangedPaths)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return incoming with
        {
            Reason = reason,
            Scope = scope,
            ChangedPaths = changedPaths,
        };
    }

    private static StatusRefreshScope PromoteScope(StatusRefreshScope left, StatusRefreshScope right)
    {
        if (left == StatusRefreshScope.Full || right == StatusRefreshScope.Full)
        {
            return StatusRefreshScope.Full;
        }

        if (left == StatusRefreshScope.ProfileOnly || right == StatusRefreshScope.ProfileOnly)
        {
            return StatusRefreshScope.ProfileOnly;
        }

        return StatusRefreshScope.UsageOnly;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }
}
