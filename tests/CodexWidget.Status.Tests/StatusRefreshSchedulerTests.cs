using CodexWidget.Core;
using CodexWidget.Profiles;
using CodexWidget.Status;

namespace CodexWidget.Status.Tests;

public sealed class StatusRefreshSchedulerTests
{
    [Fact]
    public async Task StartAsync_PerformsStartupRefreshAndStartsMonitor()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var paths = CreatePaths();
        var cache = new FakeStatusCacheService(clock, paths)
        {
            RefreshHandler = (_, _, _) => Task.FromResult(CreateSnapshot(nowUtc, nowUtc + TimeSpan.FromMinutes(5))),
        };
        var monitor = new FakeProfileChangeMonitor();
        using var scheduler = new StatusRefreshScheduler(cache, monitor, clock, options: null, new AdvancingDelay(clock));

        await scheduler.StartAsync();

        var startupInvocation = Assert.Single(cache.Invocations);
        Assert.Equal(StatusRefreshReason.Startup, startupInvocation.Reason);
        Assert.Equal(StatusRefreshScope.Full, startupInvocation.Scope);
        Assert.True(monitor.Started);
        Assert.NotNull(monitor.LastPaths);
    }

    [Fact]
    public async Task RequestStaleWidgetOpenRefreshAsync_WhenStale_RequestsUsageRefresh()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var paths = CreatePaths();
        var cache = new FakeStatusCacheService(clock, paths)
        {
            RefreshHandler = (reason, _, _) =>
            {
                var nextDue = reason == StatusRefreshReason.Startup
                    ? nowUtc - TimeSpan.FromSeconds(1)
                    : nowUtc + TimeSpan.FromMinutes(5);
                return Task.FromResult(CreateSnapshot(clock.UtcNow, nextDue));
            },
        };
        var monitor = new FakeProfileChangeMonitor();
        using var scheduler = new StatusRefreshScheduler(cache, monitor, clock, options: null, new AdvancingDelay(clock));
        await scheduler.StartAsync();

        var initialInvocationCount = cache.Invocations.Count;
        await scheduler.RequestStaleWidgetOpenRefreshAsync();

        await WaitForAsync(() => cache.Invocations.Count > initialInvocationCount);

        Assert.Contains(
            cache.Invocations,
            invocation => invocation.Reason == StatusRefreshReason.StaleWidgetOpen
                          && invocation.Scope == StatusRefreshScope.UsageOnly);
    }

    [Fact]
    public async Task RequestRefreshAsync_EnforcesMinimumRefreshInterval()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var delay = new AdvancingDelay(clock);
        var cache = new FakeStatusCacheService(clock, CreatePaths())
        {
            RefreshHandler = (_, _, _) => Task.FromResult(CreateSnapshot(clock.UtcNow, clock.UtcNow + TimeSpan.FromMinutes(5))),
        };
        var monitor = new FakeProfileChangeMonitor();
        using var scheduler = new StatusRefreshScheduler(cache, monitor, clock, options: null, delay);
        await scheduler.StartAsync();

        await scheduler.RequestRefreshAsync(StatusRefreshReason.Periodic, StatusRefreshScope.UsageOnly);

        Assert.Equal(2, cache.Invocations.Count);
        Assert.Contains(delay.RequestedDelays, value => value >= TimeSpan.FromSeconds(WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds));
        Assert.True(cache.Invocations[1].StartedAtUtc - cache.Invocations[0].StartedAtUtc >= TimeSpan.FromSeconds(WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds));
    }

    [Fact]
    public async Task Scheduler_CoalescesQueuedTriggers_AndAvoidsOverlappingRefreshes()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var gate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nonStartupCallCount = 0;
        var cache = new FakeStatusCacheService(clock, CreatePaths())
        {
            RefreshHandler = async (reason, _, cancellationToken) =>
            {
                if (reason != StatusRefreshReason.Startup && Interlocked.Increment(ref nonStartupCallCount) == 1)
                {
                    await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                return CreateSnapshot(clock.UtcNow, clock.UtcNow + TimeSpan.FromMinutes(5));
            },
        };
        var monitor = new FakeProfileChangeMonitor();
        using var scheduler = new StatusRefreshScheduler(cache, monitor, clock, options: null, new AdvancingDelay(clock));
        await scheduler.StartAsync();

        var firstRequest = scheduler.RequestRefreshAsync(StatusRefreshReason.Manual, StatusRefreshScope.UsageOnly);
        await WaitForAsync(() => cache.Invocations.Count >= 2);

        monitor.Emit(new ProfileChangeSet
        {
            Reason = StatusRefreshReason.ProfileChanged,
            Scope = StatusRefreshScope.UsageOnly,
            ChangedPaths = ["/tmp/profile-a.json"],
        });
        monitor.Emit(new ProfileChangeSet
        {
            Reason = StatusRefreshReason.ConfigChanged,
            Scope = StatusRefreshScope.Full,
            ChangedPaths = ["/tmp/config.toml"],
        });

        gate.TrySetResult(null);
        await firstRequest;
        await WaitForAsync(() => cache.Invocations.Count >= 3);

        Assert.Equal(3, cache.Invocations.Count);
        Assert.Equal(StatusRefreshReason.ConfigChanged, cache.Invocations[2].Reason);
        Assert.Equal(StatusRefreshScope.Full, cache.Invocations[2].Scope);
        Assert.Equal(1, cache.MaxConcurrentRefreshes);
    }

    [Fact]
    public async Task ScheduledDueRefresh_WithResetWindow_UsesResetReason()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var cache = new FakeStatusCacheService(clock, CreatePaths())
        {
            RefreshHandler = (reason, _, _) =>
            {
                if (reason == StatusRefreshReason.Startup)
                {
                    var startupSnapshot = CreateSnapshot(
                        clock.UtcNow,
                        clock.UtcNow,
                        resetAtUnixSeconds: (clock.UtcNow + TimeSpan.FromSeconds(30)).ToUnixTimeSeconds());
                    return Task.FromResult(startupSnapshot);
                }

                return Task.FromResult(CreateSnapshot(clock.UtcNow, clock.UtcNow + TimeSpan.FromMinutes(5)));
            },
        };
        var monitor = new FakeProfileChangeMonitor();
        using var scheduler = new StatusRefreshScheduler(cache, monitor, clock, options: null, new AdvancingDelay(clock));

        await scheduler.StartAsync();
        await WaitForAsync(() => cache.Invocations.Count >= 2);

        Assert.Equal(StatusRefreshReason.ResetTimeElapsed, cache.Invocations[1].Reason);
        Assert.Equal(StatusRefreshScope.UsageOnly, cache.Invocations[1].Scope);
    }

    [Fact]
    public async Task ScheduledDueRefresh_WithoutResetWindow_UsesPeriodicReason()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var cache = new FakeStatusCacheService(clock, CreatePaths())
        {
            RefreshHandler = (reason, _, _) =>
            {
                if (reason == StatusRefreshReason.Startup)
                {
                    return Task.FromResult(CreateSnapshot(clock.UtcNow, clock.UtcNow));
                }

                return Task.FromResult(CreateSnapshot(clock.UtcNow, clock.UtcNow + TimeSpan.FromMinutes(5)));
            },
        };
        var monitor = new FakeProfileChangeMonitor();
        using var scheduler = new StatusRefreshScheduler(cache, monitor, clock, options: null, new AdvancingDelay(clock));

        await scheduler.StartAsync();
        await WaitForAsync(() => cache.Invocations.Count >= 2);

        Assert.Equal(StatusRefreshReason.Periodic, cache.Invocations[1].Reason);
        Assert.Equal(StatusRefreshScope.UsageOnly, cache.Invocations[1].Scope);
    }

    [Fact]
    public async Task Dispose_CancelsInFlightRefresh_AndStopsMonitor()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var nonStartupStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new FakeStatusCacheService(clock, CreatePaths())
        {
            RefreshHandler = async (reason, _, cancellationToken) =>
            {
                if (reason != StatusRefreshReason.Startup)
                {
                    nonStartupStarted.TrySetResult(null);
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }

                return CreateSnapshot(clock.UtcNow, clock.UtcNow + TimeSpan.FromMinutes(5));
            },
        };
        var monitor = new FakeProfileChangeMonitor();
        using var scheduler = new StatusRefreshScheduler(cache, monitor, clock, options: null, new AdvancingDelay(clock));
        await scheduler.StartAsync();

        var requestTask = scheduler.RequestRefreshAsync(StatusRefreshReason.Manual, StatusRefreshScope.UsageOnly);
        await nonStartupStarted.Task;
        scheduler.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestTask);
        Assert.True(monitor.Disposed);
    }

    private static CodexHomePaths CreatePaths()
    {
        return new CodexHomePaths
        {
            HomeDirectory = "/codex",
            CodexDirectory = "/codex/.codex",
            CurrentAuthPath = "/codex/.codex/auth.json",
            ProfilesDirectory = "/codex/.codex/profiles",
            ProfilesIndexPath = "/codex/.codex/profiles/profiles.json",
            ProfilesLockPath = "/codex/.codex/profiles/profiles.lock",
            ConfigPath = "/codex/.codex/config.toml",
        };
    }

    private static StatusSnapshot CreateSnapshot(
        DateTimeOffset capturedAtUtc,
        DateTimeOffset nextScheduledRefreshAtUtc,
        long? resetAtUnixSeconds = null)
    {
        var windows = resetAtUnixSeconds.HasValue
            ?
            [
                new UsageWindowSnapshot
                {
                    WindowKind = UsageWindowKind.FiveHour,
                    DurationSeconds = 5 * 60 * 60,
                    ResetAtUnixSeconds = resetAtUnixSeconds,
                    UsedPercent = 50,
                    QuotaLeftPercent = 50,
                    TimeLeftPercent = 50,
                    Availability = StatusAvailability.Available(),
                },
            ]
            : Array.Empty<UsageWindowSnapshot>();

        return new StatusSnapshot
        {
            CapturedAtUtc = capturedAtUtc,
            NextScheduledRefreshAtUtc = nextScheduledRefreshAtUtc,
            Profiles =
            [
                new ProfileStatus
                {
                    Profile = new ProfileDescriptor
                    {
                        ProfileId = "work",
                        DisplayName = "Work",
                        LoginName = "work@example.invalid",
                        SubscriptionTier = SubscriptionTier.Pro,
                        IsCurrent = true,
                        AuthKind = ProfileAuthKind.Login,
                        UsageEligibility = ProfileUsageEligibility.Eligible,
                        SourceStatus = new SourceStatus
                        {
                            Source = StatusSourceKind.CurrentAuth,
                            State = SourceStatusState.Available,
                            Availability = StatusAvailability.Available(),
                            ObservedAtUtc = capturedAtUtc,
                        },
                    },
                    AllBuckets =
                    [
                        new UsageBucketSnapshot
                        {
                            BucketId = "codex",
                            BucketLabel = "codex",
                            BucketKind = UsageBucketKind.MainCodex,
                            FetchStatus = windows.Length > 0 ? UsageBucketFetchStatus.Succeeded : UsageBucketFetchStatus.NotRequested,
                            Availability = windows.Length > 0 ? StatusAvailability.Available() : StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable),
                            Windows = windows,
                        },
                    ],
                },
            ],
            RefreshState = new StatusRefreshState
            {
                Reason = StatusRefreshReason.Periodic,
                Scope = StatusRefreshScope.UsageOnly,
                Outcome = StatusRefreshOutcome.Succeeded,
                RequestedAtUtc = capturedAtUtc,
                StartedAtUtc = capturedAtUtc,
                CompletedAtUtc = capturedAtUtc,
            },
        };
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        for (var index = 0; index < 100; index++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(predicate(), "Condition was not met within timeout.");
    }

    private sealed class FakeClock(DateTimeOffset nowUtc) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = nowUtc;

        public void Advance(TimeSpan amount)
        {
            UtcNow += amount;
        }
    }

    private sealed class AdvancingDelay(FakeClock clock) : IAsyncDelay
    {
        public List<TimeSpan> RequestedDelays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedDelays.Add(delay);
            clock.Advance(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProfileChangeMonitor : IProfileChangeMonitor
    {
        public event EventHandler<ProfileChangeSet>? Changed;

        public bool Started { get; private set; }

        public bool Disposed { get; private set; }

        public ProfileChangeMonitorPaths? LastPaths { get; private set; }

        public void UpdatePaths(ProfileChangeMonitorPaths paths)
        {
            LastPaths = paths;
        }

        public void Start(CancellationToken cancellationToken = default)
        {
            Started = true;
        }

        public void Emit(ProfileChangeSet changeSet)
        {
            Changed?.Invoke(this, changeSet);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class FakeStatusCacheService(FakeClock clock, CodexHomePaths paths) : IStatusCacheService
    {
        private int runningRefreshes;

        public Func<StatusRefreshReason, StatusRefreshScope, CancellationToken, Task<StatusSnapshot>>? RefreshHandler { get; set; }

        public List<RefreshInvocation> Invocations { get; } = [];

        public int MaxConcurrentRefreshes { get; private set; }

        public StatusSnapshot CurrentSnapshot { get; private set; } = new();

        public CodexHomePaths? CurrentPaths { get; private set; } = paths;

        public bool IsInitialized { get; private set; }

        public event EventHandler<StatusSnapshotChangedEventArgs>? SnapshotChanged;

        public Task<StatusSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
        {
            return IsInitialized
                ? Task.FromResult(CurrentSnapshot)
                : RefreshAsync(StatusRefreshReason.Startup, StatusRefreshScope.Full, cancellationToken);
        }

        public async Task<StatusSnapshot> RefreshAsync(StatusRefreshReason reason, StatusRefreshScope scope, CancellationToken cancellationToken = default)
        {
            var startedAtUtc = clock.UtcNow;
            var active = Interlocked.Increment(ref runningRefreshes);
            if (active > MaxConcurrentRefreshes)
            {
                MaxConcurrentRefreshes = active;
            }

            try
            {
                Invocations.Add(new RefreshInvocation(reason, scope, startedAtUtc));

                var previous = CurrentSnapshot;
                var snapshot = RefreshHandler is null
                    ? CreateSnapshot(clock.UtcNow, clock.UtcNow + TimeSpan.FromMinutes(5))
                    : await RefreshHandler(reason, scope, cancellationToken).ConfigureAwait(false);

                CurrentSnapshot = snapshot;
                IsInitialized = true;
                SnapshotChanged?.Invoke(this, new StatusSnapshotChangedEventArgs(previous, snapshot));
                return snapshot;
            }
            finally
            {
                Interlocked.Decrement(ref runningRefreshes);
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed record RefreshInvocation(
        StatusRefreshReason Reason,
        StatusRefreshScope Scope,
        DateTimeOffset StartedAtUtc);
}
