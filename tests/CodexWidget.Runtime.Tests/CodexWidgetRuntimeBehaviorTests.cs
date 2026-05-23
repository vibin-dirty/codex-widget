using CodexWidget.Core;
using CodexWidget.Presentation;
using CodexWidget.Runtime;
using CodexWidget.Status;
using CodexWidget.TestSupport;

namespace CodexWidget.Runtime.Tests;

public sealed class CodexWidgetRuntimeBehaviorTests
{
    [Fact]
    public void Constructor_ExposesPreferenceLoadMetadataAndResolvedPreferenceOverride()
    {
        var loadResult = new PreferenceLoadResult
        {
            Preferences = WidgetPreferenceDefaults.Create() with
            {
                RefreshPeriodSeconds = 300,
            },
            UsedDefaults = true,
        };
        var preferenceFilePath = "/tmp/codex-widget/settings.json";
        var overridePreferences = loadResult.Preferences with
        {
            RefreshPeriodSeconds = 900,
            SelectedView = WidgetViewKind.Minimal,
        };
        var runtime = CreateRuntime(
            preferenceLoadResult: loadResult,
            preferenceOverride: overridePreferences);

        Assert.Same(loadResult, runtime.PreferenceLoadResult);
        Assert.Equal(preferenceFilePath, runtime.PreferenceFilePath);
        Assert.Same(overridePreferences, runtime.Preferences);
    }

    [Fact]
    public async Task InitializeAsync_WhenSchedulerStartupIsDisabled_InitializesCacheOnly()
    {
        var cacheService = new RecordingStatusCacheService(CreateSnapshot(new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero)));
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(
            cacheService,
            scheduler,
            options: new CodexWidgetRuntimeOptions
            {
                StartSchedulerOnInitialize = false,
            });

        var snapshot = await runtime.InitializeAsync();

        Assert.Equal(1, cacheService.InitializeCallCount);
        Assert.Equal(0, scheduler.StartCallCount);
        Assert.Same(cacheService.CurrentSnapshot, snapshot);
        Assert.True(runtime.IsInitialized);
    }

    [Fact]
    public async Task InitializeAsync_WhenSchedulerStartupIsEnabled_StartsSchedulerAndReturnsCurrentSnapshot()
    {
        var cacheService = new RecordingStatusCacheService(CreateSnapshot(new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero)));
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(
            cacheService,
            scheduler,
            options: new CodexWidgetRuntimeOptions
            {
                StartSchedulerOnInitialize = true,
            });

        var snapshot = await runtime.InitializeAsync();

        Assert.Equal(0, cacheService.InitializeCallCount);
        Assert.Equal(1, scheduler.StartCallCount);
        Assert.True(runtime.IsInitialized);
        Assert.Same(cacheService.CurrentSnapshot, snapshot);
    }

    [Fact]
    public async Task StartSchedulerAsync_IsIdempotent()
    {
        var cacheService = new RecordingStatusCacheService(CreateSnapshot(new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero)));
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(cacheService, scheduler);

        await runtime.StartSchedulerAsync();
        await runtime.StartSchedulerAsync();

        Assert.Equal(1, scheduler.StartCallCount);
    }

    [Fact]
    public async Task RequestStaleWidgetOpenRefreshAsync_PassesThroughToScheduler()
    {
        var cacheService = new RecordingStatusCacheService(CreateSnapshot(new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero)));
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(cacheService, scheduler);

        await runtime.RequestStaleWidgetOpenRefreshAsync();

        Assert.Equal(1, scheduler.StaleWidgetOpenRefreshCallCount);
        Assert.Equal(0, scheduler.RefreshCallCount);
    }

    [Fact]
    public async Task RefreshAsync_ForwardsReasonAndScopeAndReturnsCacheResult()
    {
        var cacheService = new RecordingStatusCacheService(CreateSnapshot(new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero)));
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(cacheService, scheduler);

        var snapshot = await runtime.RefreshAsync(StatusRefreshReason.Manual, StatusRefreshScope.Full);

        Assert.Equal(1, cacheService.RefreshCallCount);
        Assert.Equal(StatusRefreshReason.Manual, cacheService.LastRefreshReason);
        Assert.Equal(StatusRefreshScope.Full, cacheService.LastRefreshScope);
        Assert.Same(cacheService.CurrentSnapshot, snapshot);
    }

    [Theory]
    [InlineData(StatusRefreshReason.Manual, StatusRefreshScope.UsageOnly)]
    [InlineData(StatusRefreshReason.StaleWidgetOpen, StatusRefreshScope.ProfileOnly)]
    [InlineData(StatusRefreshReason.Scheduled, StatusRefreshScope.Full)]
    public async Task RefreshAsync_PassesThroughReasonAndScopeVariants(StatusRefreshReason reason, StatusRefreshScope scope)
    {
        var cacheService = new RecordingStatusCacheService(CreateSnapshot(new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero)));
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(cacheService, scheduler);

        await runtime.RefreshAsync(reason, scope);

        Assert.Equal(1, cacheService.RefreshCallCount);
        Assert.Equal(reason, cacheService.LastRefreshReason);
        Assert.Equal(scope, cacheService.LastRefreshScope);
    }

    [Fact]
    public void CurrentSnapshot_TracksCacheUpdates()
    {
        var initialSnapshot = CreateSnapshot(new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero));
        var updatedSnapshot = initialSnapshot with
        {
            CapturedAtUtc = initialSnapshot.CapturedAtUtc.AddMinutes(10),
        };
        var cacheService = new RecordingStatusCacheService(initialSnapshot);
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(cacheService, scheduler);

        Assert.Same(initialSnapshot, runtime.CurrentSnapshot);
        cacheService.Publish(updatedSnapshot);

        Assert.Same(updatedSnapshot, runtime.CurrentSnapshot);
    }

    [Fact]
    public void BuildPresentation_UsesCurrentSnapshotAndResolvedPreferences()
    {
        var capturedAtUtc = new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero);
        var snapshot = CreateSnapshot(capturedAtUtc);
        var preferences = WidgetPreferenceDefaults.Create() with
        {
            SelectedView = WidgetViewKind.Minimal,
        };
        var cacheService = new RecordingStatusCacheService(snapshot);
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(
            cacheService,
            scheduler,
            preferenceLoadResult: new PreferenceLoadResult
            {
                Preferences = preferences,
            });

        var presentation = runtime.BuildPresentation();

        Assert.Equal(WidgetViewKind.Minimal, presentation.SelectedView);
        Assert.Equal("Current profile Profile A.", presentation.Minimal.SummaryText);
        Assert.Equal("Profile A", presentation.Minimal.CurrentProfile?.ProfileDisplayName);
        Assert.Equal("Current profile Profile A.", presentation.SelectedViewSummaryText);
    }

    [Fact]
    public void BuildPresentation_UsesPreferenceOverrideFromRuntimeOptions()
    {
        var capturedAtUtc = new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero);
        var snapshot = CreateSnapshot(capturedAtUtc);
        var loadPreferences = WidgetPreferenceDefaults.Create() with
        {
            SelectedView = WidgetViewKind.Compact,
        };
        var overridePreferences = loadPreferences with
        {
            SelectedView = WidgetViewKind.Minimal,
        };
        var cacheService = new RecordingStatusCacheService(snapshot);
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(
            cacheService,
            scheduler,
            preferenceLoadResult: new PreferenceLoadResult
            {
                Preferences = loadPreferences,
            },
            options: new CodexWidgetRuntimeOptions
            {
                PreferenceOverride = overridePreferences,
            });

        var presentation = runtime.BuildPresentation();

        Assert.Same(overridePreferences, runtime.Preferences);
        Assert.Equal(WidgetViewKind.Minimal, presentation.SelectedView);
    }

    [Fact]
    public void GetRefreshMetadata_ExposesRefreshTimingAndRedactsSensitiveFailureSummaries()
    {
        var capturedAtUtc = new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero);
        var nowUtc = capturedAtUtc + TimeSpan.FromMinutes(20);
        var snapshot = CreateSnapshot(capturedAtUtc) with
        {
            NextScheduledRefreshAtUtc = capturedAtUtc + TimeSpan.FromMinutes(15),
            RefreshState = new StatusRefreshState
            {
                Outcome = StatusRefreshOutcome.Failed,
                Failure = new SourceDiagnostic
                {
                    Code = SourceDiagnosticCode.TokenRefreshFailed,
                    Severity = SourceDiagnosticSeverity.Error,
                    Summary = "Authorization token secret-token leaked from the bearer header.",
                    ObservedAtUtc = nowUtc,
                },
            },
        };
        var cacheService = new RecordingStatusCacheService(snapshot);
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(cacheService, scheduler, nowUtc: nowUtc);

        var metadata = runtime.GetRefreshMetadata();

        Assert.Equal(snapshot.CapturedAtUtc, metadata.CapturedAtUtc);
        Assert.Equal(snapshot.NextScheduledRefreshAtUtc, metadata.NextScheduledRefreshAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(20), metadata.SnapshotAge);
        Assert.Equal(StatusRefreshOutcome.Failed, metadata.LatestOutcome);
        Assert.False(metadata.IsRefreshRunning);
        Assert.Equal("Refresh failure (TokenRefreshFailed).", metadata.LatestSafeFailureSummary);
    }

    [Fact]
    public void GetRefreshMetadata_PreservesBenignFailureSummaries()
    {
        var capturedAtUtc = new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero);
        var nowUtc = capturedAtUtc + TimeSpan.FromMinutes(5);
        var snapshot = CreateSnapshot(capturedAtUtc) with
        {
            RefreshState = new StatusRefreshState
            {
                Outcome = StatusRefreshOutcome.Failed,
                Failure = new SourceDiagnostic
                {
                    Code = SourceDiagnosticCode.NetworkError,
                    Severity = SourceDiagnosticSeverity.Error,
                    Summary = "Usage endpoint returned 500.",
                    ObservedAtUtc = nowUtc,
                },
            },
        };
        var cacheService = new RecordingStatusCacheService(snapshot);
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(cacheService, scheduler, nowUtc: nowUtc);

        var metadata = runtime.GetRefreshMetadata();

        Assert.Equal("Usage endpoint returned 500.", metadata.LatestSafeFailureSummary);
    }

    [Fact]
    public void GetRefreshMetadata_BlankFailureSummary_ReturnsNullSummary()
    {
        var capturedAtUtc = new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero);
        var nowUtc = capturedAtUtc + TimeSpan.FromMinutes(3);
        var snapshot = CreateSnapshot(capturedAtUtc) with
        {
            RefreshState = new StatusRefreshState
            {
                Outcome = StatusRefreshOutcome.Failed,
                Failure = new SourceDiagnostic
                {
                    Code = SourceDiagnosticCode.Error,
                    Severity = SourceDiagnosticSeverity.Error,
                    Summary = "   ",
                    ObservedAtUtc = nowUtc,
                },
            },
        };
        var cacheService = new RecordingStatusCacheService(snapshot);
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(cacheService, scheduler, nowUtc: nowUtc);

        var metadata = runtime.GetRefreshMetadata();

        Assert.Null(metadata.LatestSafeFailureSummary);
    }

    [Fact]
    public void GetRefreshMetadata_RedactsSyntheticSecurityFixtureValues()
    {
        var capturedAtUtc = new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero);
        var nowUtc = capturedAtUtc + TimeSpan.FromMinutes(7);
        var snapshot = CreateSnapshot(capturedAtUtc) with
        {
            RefreshState = new StatusRefreshState
            {
                Outcome = StatusRefreshOutcome.Failed,
                Failure = new SourceDiagnostic
                {
                    Code = SourceDiagnosticCode.TokenRefreshFailed,
                    Severity = SourceDiagnosticSeverity.Error,
                    Summary = $"Refresh failed for {SyntheticSecurityFixtures.SyntheticBearerHeader}",
                    Detail = SyntheticSecurityFixtures.SyntheticRawAuthJson,
                    ObservedAtUtc = nowUtc,
                },
            },
        };
        var cacheService = new RecordingStatusCacheService(snapshot);
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(cacheService, scheduler, nowUtc: nowUtc);

        var metadata = runtime.GetRefreshMetadata();

        Assert.NotNull(metadata.LatestSafeFailureSummary);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(metadata.LatestSafeFailureSummary!);
        Assert.Equal("Refresh failure (TokenRefreshFailed).", metadata.LatestSafeFailureSummary);
    }

    [Fact]
    public async Task Dispose_IsIdempotentAndDisposesOwnedResourcesOnce()
    {
        var cacheService = new RecordingStatusCacheService(CreateSnapshot(new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero)));
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        var ownedOne = new RecordingDisposable();
        var ownedTwo = new RecordingDisposable();
        using var runtime = CreateRuntime(
            cacheService,
            scheduler,
            ownedDisposables:
            [
                ownedOne,
                ownedTwo,
                ownedOne,
            ]);

        runtime.Dispose();
        runtime.Dispose();
        await runtime.DisposeAsync();

        Assert.Equal(1, cacheService.DisposeCallCount);
        Assert.Equal(1, scheduler.DisposeCallCount);
        Assert.Equal(1, ownedOne.DisposeCallCount);
        Assert.Equal(1, ownedTwo.DisposeCallCount);
        Assert.Throws<ObjectDisposedException>(() => runtime.BuildPresentation());
    }

    private static CodexWidgetRuntime CreateRuntime(
        RecordingStatusCacheService? cacheService = null,
        RecordingStatusRefreshScheduler? scheduler = null,
        PreferenceLoadResult? preferenceLoadResult = null,
        WidgetPreferences? preferenceOverride = null,
        CodexWidgetRuntimeOptions? options = null,
        DateTimeOffset? nowUtc = null,
        IEnumerable<IDisposable>? ownedDisposables = null)
    {
        var clock = new FakeClock(nowUtc ?? new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero));
        var projectionService = new StatusProjectionService(clock);
        var presentationService = new WidgetPresentationService(projectionService, clock);
        var resolvedCacheService = cacheService ?? new RecordingStatusCacheService(CreateSnapshot(clock.UtcNow));
        var resolvedScheduler = scheduler ?? new RecordingStatusRefreshScheduler(resolvedCacheService);

        return new CodexWidgetRuntime(
            resolvedCacheService,
            resolvedScheduler,
            projectionService,
            presentationService,
            preferenceLoadResult,
            preferenceFilePath: "/tmp/codex-widget/settings.json",
            options: options ?? new CodexWidgetRuntimeOptions
            {
                PreferenceOverride = preferenceOverride,
            },
            ownedDisposables: ownedDisposables);
    }

    private static StatusSnapshot CreateSnapshot(DateTimeOffset capturedAtUtc)
    {
        return new StatusSnapshot
        {
            CapturedAtUtc = capturedAtUtc,
            CurrentProfileId = "profile-a",
            Profiles =
            [
                new ProfileStatus
                {
                    Profile = new ProfileDescriptor
                    {
                        ProfileId = "profile-a",
                        DisplayName = "Profile A",
                        LoginName = "profile-a@example.com",
                        SubscriptionTier = SubscriptionTier.Plus,
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
                    MainBucket = CreateBucket("main", UsageBucketKind.MainCodex, capturedAtUtc),
                    AllBuckets = [CreateBucket("main", UsageBucketKind.MainCodex, capturedAtUtc)],
                },
            ],
            RefreshState = new StatusRefreshState
            {
                Outcome = StatusRefreshOutcome.Idle,
                RequestedAtUtc = capturedAtUtc,
                CompletedAtUtc = capturedAtUtc,
            },
            NextScheduledRefreshAtUtc = capturedAtUtc.AddMinutes(15),
            Sources =
            [
                new SourceStatus
                {
                    Source = StatusSourceKind.CurrentAuth,
                    State = SourceStatusState.Available,
                    Availability = StatusAvailability.Available(),
                    ObservedAtUtc = capturedAtUtc,
                },
            ],
        };
    }

    private static UsageBucketSnapshot CreateBucket(string label, UsageBucketKind kind, DateTimeOffset capturedAtUtc)
    {
        return new UsageBucketSnapshot
        {
            BucketId = $"{label}-bucket",
            BucketLabel = label,
            BucketKind = kind,
            FetchStatus = UsageBucketFetchStatus.Succeeded,
            Availability = StatusAvailability.Available(),
            Windows =
            [
                new UsageWindowSnapshot
                {
                    WindowKind = UsageWindowKind.FiveHour,
                    DurationSeconds = 5 * 60 * 60,
                    ResetAtUnixSeconds = capturedAtUtc.AddHours(5).ToUnixTimeSeconds(),
                    UsedPercent = 25,
                    QuotaLeftPercent = 75,
                    TimeLeftPercent = 80,
                    Availability = StatusAvailability.Available(),
                },
                new UsageWindowSnapshot
                {
                    WindowKind = UsageWindowKind.Weekly,
                    DurationSeconds = 7 * 24 * 60 * 60,
                    ResetAtUnixSeconds = capturedAtUtc.AddDays(7).ToUnixTimeSeconds(),
                    UsedPercent = 40,
                    QuotaLeftPercent = 60,
                    TimeLeftPercent = 70,
                    Availability = StatusAvailability.Available(),
                },
            ],
        };
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RecordingDisposable : IDisposable
    {
        public int DisposeCallCount { get; private set; }

        public void Dispose()
        {
            DisposeCallCount++;
        }
    }

    private sealed class RecordingStatusCacheService : IStatusCacheService
    {
        public RecordingStatusCacheService(StatusSnapshot snapshot)
        {
            CurrentSnapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public StatusSnapshot CurrentSnapshot { get; private set; }

        public CodexWidget.Profiles.CodexHomePaths? CurrentPaths => null;

        public bool IsInitialized { get; private set; }

        public int InitializeCallCount { get; private set; }

        public int RefreshCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public StatusRefreshReason? LastRefreshReason { get; private set; }

        public StatusRefreshScope? LastRefreshScope { get; private set; }

        public event EventHandler<StatusSnapshotChangedEventArgs>? SnapshotChanged;

        public Task<StatusSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCallCount++;
            IsInitialized = true;
            return Task.FromResult(CurrentSnapshot);
        }

        public Task<StatusSnapshot> RefreshAsync(StatusRefreshReason reason, StatusRefreshScope scope, CancellationToken cancellationToken = default)
        {
            RefreshCallCount++;
            LastRefreshReason = reason;
            LastRefreshScope = scope;
            IsInitialized = true;
            return Task.FromResult(CurrentSnapshot);
        }

        public void Publish(StatusSnapshot snapshot)
        {
            var previousSnapshot = CurrentSnapshot;
            CurrentSnapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            IsInitialized = true;
            SnapshotChanged?.Invoke(this, new StatusSnapshotChangedEventArgs(previousSnapshot, snapshot));
        }

        public void Dispose()
        {
            DisposeCallCount++;
        }
    }

    private sealed class RecordingStatusRefreshScheduler : IStatusRefreshScheduler
    {
        private readonly RecordingStatusCacheService? cacheService;

        public RecordingStatusRefreshScheduler(RecordingStatusCacheService? cacheService = null)
        {
            this.cacheService = cacheService;
        }

        public int StartCallCount { get; private set; }

        public int StaleWidgetOpenRefreshCallCount { get; private set; }

        public int RefreshCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            cacheService?.Publish(cacheService.CurrentSnapshot);
            return Task.CompletedTask;
        }

        public Task RequestStaleWidgetOpenRefreshAsync(CancellationToken cancellationToken = default)
        {
            StaleWidgetOpenRefreshCallCount++;
            return Task.CompletedTask;
        }

        public Task RequestRefreshAsync(StatusRefreshReason reason, StatusRefreshScope scope, CancellationToken cancellationToken = default)
        {
            RefreshCallCount++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            DisposeCallCount++;
        }
    }
}
