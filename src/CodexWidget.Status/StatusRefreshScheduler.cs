using CodexWidget.Core;

namespace CodexWidget.Status;

public sealed class StatusRefreshScheduler : IStatusRefreshScheduler
{
    private readonly IStatusCacheService cacheService;
    private readonly IProfileChangeMonitor changeMonitor;
    private readonly IClock clock;
    private readonly IAsyncDelay delay;
    private readonly WidgetPreferences preferences;
    private readonly SemaphoreSlim workSignal = new(0);
    private readonly CancellationTokenSource disposeCts = new();
    private readonly object sync = new();

    private PendingRefreshRequest? pendingRequest;
    private Task? workerTask;
    private DateTimeOffset? lastRefreshStartedAtUtc;
    private bool started;
    private bool isDisposed;

    public StatusRefreshScheduler(
        IStatusCacheService cacheService,
        IProfileChangeMonitor changeMonitor,
        IClock? clock = null,
        StatusRefreshSchedulerOptions? options = null)
        : this(
            cacheService,
            changeMonitor,
            clock,
            options,
            TaskAsyncDelay.Instance)
    {
    }

    internal StatusRefreshScheduler(
        IStatusCacheService cacheService,
        IProfileChangeMonitor changeMonitor,
        IClock? clock,
        StatusRefreshSchedulerOptions? options,
        IAsyncDelay delay)
    {
        this.cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        this.changeMonitor = changeMonitor ?? throw new ArgumentNullException(nameof(changeMonitor));
        this.clock = clock ?? SystemClock.Instance;
        this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
        preferences = (options ?? new StatusRefreshSchedulerOptions()).Preferences;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (started)
        {
            return;
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(disposeCts.Token, cancellationToken);
            var linkedToken = linkedCts.Token;

            var startupSnapshot = await cacheService.InitializeAsync(linkedToken).ConfigureAwait(false);
            if (startupSnapshot.RefreshState.StartedAtUtc is not null)
            {
                lastRefreshStartedAtUtc = startupSnapshot.RefreshState.StartedAtUtc.Value;
            }
            else
            {
                lastRefreshStartedAtUtc = clock.UtcNow;
            }

            UpdateMonitorPathsFromCache();

            cacheService.SnapshotChanged += HandleCacheSnapshotChanged;
            changeMonitor.Changed += HandleProfileChanged;
            changeMonitor.Start(linkedToken);

            workerTask = Task.Run(() => RunAsync(disposeCts.Token), CancellationToken.None);
            started = true;
        }
        catch
        {
            started = false;
            throw;
        }
    }

    public Task RequestStaleWidgetOpenRefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotStarted();

        if (!cacheService.IsInitialized)
        {
            return RequestRefreshAsync(StatusRefreshReason.Startup, StatusRefreshScope.Full, cancellationToken);
        }

        if (!IsStaleForWidgetOpen(cacheService.CurrentSnapshot, clock.UtcNow))
        {
            return Task.CompletedTask;
        }

        return RequestRefreshAsync(StatusRefreshReason.StaleWidgetOpen, StatusRefreshScope.UsageOnly, cancellationToken);
    }

    public async Task RequestRefreshAsync(StatusRefreshReason reason, StatusRefreshScope scope, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotStarted();

        var waiter = EnqueueRefresh(reason, scope);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(disposeCts.Token, cancellationToken);
        await waiter.WaitAsync(linkedCts.Token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        disposeCts.Cancel();

        cacheService.SnapshotChanged -= HandleCacheSnapshotChanged;
        changeMonitor.Changed -= HandleProfileChanged;
        changeMonitor.Dispose();

        lock (sync)
        {
            pendingRequest?.CompleteCanceled();
            pendingRequest = null;
        }

        try
        {
            workerTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            disposeCts.Dispose();
            workSignal.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            PendingRefreshRequest? next;

            lock (sync)
            {
                next = pendingRequest;
                pendingRequest = null;
            }

            if (next is not null)
            {
                try
                {
                    await ExecuteRefreshAsync(next, cancellationToken).ConfigureAwait(false);
                    next.CompleteSucceeded();
                }
                catch (OperationCanceledException)
                {
                    next.CompleteCanceled();
                    throw;
                }
                catch (Exception exception)
                {
                    next.CompleteFailed(exception);
                }

                continue;
            }

            var dueAtUtc = cacheService.CurrentSnapshot.NextScheduledRefreshAtUtc;
            if (dueAtUtc.HasValue && dueAtUtc.Value <= clock.UtcNow)
            {
                var scheduledReason = SelectScheduledReason(cacheService.CurrentSnapshot);
                var request = new PendingRefreshRequest(
                    scheduledReason,
                    StatusRefreshScope.UsageOnly,
                    clock.UtcNow);
                await ExecuteRefreshAsync(request, cancellationToken).ConfigureAwait(false);
                request.CompleteSucceeded();
                continue;
            }

            if (dueAtUtc.HasValue)
            {
                var delayUntilDue = dueAtUtc.Value - clock.UtcNow;
                if (delayUntilDue < TimeSpan.Zero)
                {
                    delayUntilDue = TimeSpan.Zero;
                }

                var wasSignaled = await workSignal.WaitAsync(delayUntilDue, cancellationToken).ConfigureAwait(false);
                if (!wasSignaled)
                {
                    var scheduledReason = SelectScheduledReason(cacheService.CurrentSnapshot);
                    var request = new PendingRefreshRequest(
                        scheduledReason,
                        StatusRefreshScope.UsageOnly,
                        clock.UtcNow);
                    await ExecuteRefreshAsync(request, cancellationToken).ConfigureAwait(false);
                    request.CompleteSucceeded();
                }
            }
            else
            {
                await workSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteRefreshAsync(PendingRefreshRequest request, CancellationToken cancellationToken)
    {
        var minimumInterval = TimeSpan.FromSeconds(WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds);
        var nowUtc = clock.UtcNow;
        if (request.Reason != StatusRefreshReason.Startup && lastRefreshStartedAtUtc.HasValue)
        {
            var earliestNextStart = lastRefreshStartedAtUtc.Value + minimumInterval;
            if (earliestNextStart > nowUtc)
            {
                await delay.DelayAsync(earliestNextStart - nowUtc, cancellationToken).ConfigureAwait(false);
            }
        }

        lastRefreshStartedAtUtc = clock.UtcNow;
        await cacheService.RefreshAsync(request.Reason, request.Scope, cancellationToken).ConfigureAwait(false);
    }

    private Task EnqueueRefresh(StatusRefreshReason reason, StatusRefreshScope scope)
    {
        Task waitTask;

        lock (sync)
        {
            if (pendingRequest is null)
            {
                pendingRequest = new PendingRefreshRequest(reason, scope, clock.UtcNow);
            }
            else
            {
                pendingRequest = pendingRequest.Merge(reason, scope, clock.UtcNow);
            }

            waitTask = pendingRequest.CreateWaiterTask();
        }

        workSignal.Release();
        return waitTask;
    }

    private void HandleCacheSnapshotChanged(object? sender, StatusSnapshotChangedEventArgs e)
    {
        UpdateMonitorPathsFromCache();
        workSignal.Release();
    }

    private void HandleProfileChanged(object? sender, ProfileChangeSet e)
    {
        _ = EnqueueRefresh(e.Reason, e.Scope);
    }

    private void UpdateMonitorPathsFromCache()
    {
        var paths = cacheService.CurrentPaths;
        if (paths is null)
        {
            return;
        }

        changeMonitor.UpdatePaths(ProfileChangeMonitorPaths.FromCodexHomePaths(paths));
    }

    private bool IsStaleForWidgetOpen(StatusSnapshot snapshot, DateTimeOffset nowUtc)
    {
        var refreshPeriodSeconds = Math.Clamp(
            preferences.RefreshPeriodSeconds,
            WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds,
            WidgetPreferenceDefaults.MaximumRefreshPeriodSeconds);

        var fallbackDueAt = snapshot.CapturedAtUtc + TimeSpan.FromSeconds(refreshPeriodSeconds);
        var dueAt = snapshot.NextScheduledRefreshAtUtc ?? fallbackDueAt;
        return nowUtc >= dueAt;
    }

    private StatusRefreshReason SelectScheduledReason(StatusSnapshot snapshot)
    {
        var refreshPeriodSeconds = Math.Clamp(
            preferences.RefreshPeriodSeconds,
            WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds,
            WidgetPreferenceDefaults.MaximumRefreshPeriodSeconds);
        var periodicDueAt = snapshot.CapturedAtUtc + TimeSpan.FromSeconds(refreshPeriodSeconds);
        var minimumDueAt = snapshot.CapturedAtUtc + TimeSpan.FromSeconds(WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds);

        DateTimeOffset? earliestResetAtUtc = null;
        foreach (var resetAtUtc in snapshot.Profiles
                     .SelectMany(profile => profile.AllBuckets)
                     .SelectMany(bucket => bucket.Windows)
                     .Where(window => window.Availability.IsAvailable && window.ResetAtUnixSeconds.HasValue)
                     .Select(window => DateTimeOffset.FromUnixTimeSeconds(window.ResetAtUnixSeconds!.Value)))
        {
            if (!earliestResetAtUtc.HasValue || resetAtUtc < earliestResetAtUtc.Value)
            {
                earliestResetAtUtc = resetAtUtc;
            }
        }

        if (!earliestResetAtUtc.HasValue)
        {
            return StatusRefreshReason.Periodic;
        }

        var resetDueAt = earliestResetAtUtc.Value <= minimumDueAt
            ? minimumDueAt
            : earliestResetAtUtc.Value;

        return resetDueAt <= periodicDueAt
            ? StatusRefreshReason.ResetTimeElapsed
            : StatusRefreshReason.Periodic;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }

    private void ThrowIfNotStarted()
    {
        if (!started)
        {
            throw new InvalidOperationException("StartAsync must be called before requesting refreshes.");
        }
    }

    private sealed class PendingRefreshRequest(
        StatusRefreshReason reason,
        StatusRefreshScope scope,
        DateTimeOffset requestedAtUtc)
    {
        private readonly List<TaskCompletionSource<object?>> waiters = [];

        public StatusRefreshReason Reason { get; private set; } = reason;

        public StatusRefreshScope Scope { get; private set; } = scope;

        public DateTimeOffset RequestedAtUtc { get; private set; } = requestedAtUtc;

        public PendingRefreshRequest Merge(StatusRefreshReason reason, StatusRefreshScope scope, DateTimeOffset requestedAtUtc)
        {
            Reason = reason;
            Scope = PromoteScope(Scope, scope);
            RequestedAtUtc = requestedAtUtc;
            return this;
        }

        public Task CreateWaiterTask()
        {
            var waiter = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiters.Add(waiter);
            return waiter.Task;
        }

        public void CompleteSucceeded()
        {
            foreach (var waiter in waiters)
            {
                waiter.TrySetResult(null);
            }
        }

        public void CompleteFailed(Exception exception)
        {
            foreach (var waiter in waiters)
            {
                waiter.TrySetException(exception);
            }
        }

        public void CompleteCanceled()
        {
            foreach (var waiter in waiters)
            {
                waiter.TrySetCanceled();
            }
        }

        private static StatusRefreshScope PromoteScope(StatusRefreshScope existing, StatusRefreshScope incoming)
        {
            if (existing == StatusRefreshScope.Full || incoming == StatusRefreshScope.Full)
            {
                return StatusRefreshScope.Full;
            }

            if (existing == StatusRefreshScope.ProfileOnly || incoming == StatusRefreshScope.ProfileOnly)
            {
                return StatusRefreshScope.ProfileOnly;
            }

            return StatusRefreshScope.UsageOnly;
        }
    }
}
