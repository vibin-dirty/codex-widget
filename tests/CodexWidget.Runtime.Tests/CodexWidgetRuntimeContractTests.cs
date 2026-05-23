using CodexWidget.Core;
using CodexWidget.Presentation;
using CodexWidget.Runtime;
using CodexWidget.Status;

namespace CodexWidget.Runtime.Tests;

public sealed class CodexWidgetRuntimeContractTests
{
    [Fact]
    public void Options_CreateEffectiveOptions_ApplyOverridePreferencesAndSnapshotReadOptions()
    {
        var loadResult = new PreferenceLoadResult
        {
            Preferences = WidgetPreferenceDefaults.Create() with
            {
                RefreshPeriodSeconds = 300,
            },
        };
        var overridePreferences = loadResult.Preferences with
        {
            RefreshPeriodSeconds = 900,
        };
        var profileSnapshotReadOptions = new CodexWidget.Profiles.ProfileSnapshotReadOptions
        {
            LockAcquireTimeout = TimeSpan.FromSeconds(2),
        };
        var options = new CodexWidgetRuntimeOptions
        {
            PreferenceOverride = overridePreferences,
            ProfileSnapshotReadOptions = profileSnapshotReadOptions,
            CacheOptions = new StatusCacheServiceOptions
            {
                MaxConcurrentUsageFetches = 5,
            },
            SchedulerOptions = new StatusRefreshSchedulerOptions
            {
                Preferences = WidgetPreferenceDefaults.Create() with
                {
                    RefreshPeriodSeconds = 60,
                },
            },
        };

        var resolvedPreferences = options.ResolvePreferences(loadResult);
        var cacheOptions = options.CreateCacheOptions(loadResult);
        var schedulerOptions = options.CreateSchedulerOptions(loadResult);

        Assert.Same(overridePreferences, resolvedPreferences);
        Assert.Same(overridePreferences, cacheOptions.Preferences);
        Assert.Same(overridePreferences, schedulerOptions.Preferences);
        Assert.Same(profileSnapshotReadOptions, cacheOptions.ProfileSnapshotReadOptions);
        Assert.Equal(5, cacheOptions.MaxConcurrentUsageFetches);
    }

    [Fact]
    public async Task InitializeAsync_WithAutomaticSchedulerStartup_StartsSchedulerWithoutDirectCacheInitialization()
    {
        var cacheService = new FakeStatusCacheService();
        var scheduler = new FakeStatusRefreshScheduler(cacheService);
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
        var cacheService = new FakeStatusCacheService();
        var scheduler = new FakeStatusRefreshScheduler(cacheService);
        using var runtime = CreateRuntime(cacheService, scheduler);

        await runtime.StartSchedulerAsync();
        await runtime.StartSchedulerAsync();

        Assert.Equal(1, scheduler.StartCallCount);
    }

    [Fact]
    public void BuildPresentationAndGetRefreshMetadata_UseCurrentSnapshotAndRedactedFailureSummary()
    {
        var capturedAtUtc = new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero);
        var nowUtc = capturedAtUtc + TimeSpan.FromMinutes(10);
        var snapshot = new StatusSnapshot
        {
            CapturedAtUtc = capturedAtUtc,
            NextScheduledRefreshAtUtc = capturedAtUtc + TimeSpan.FromMinutes(5),
            RefreshState = new StatusRefreshState
            {
                Outcome = StatusRefreshOutcome.Failed,
                Failure = SourceDiagnostic.Create(
                    SourceDiagnosticCode.Error,
                    SourceDiagnosticSeverity.Error,
                    "Request failed for bearer secret-token.",
                    detail: "token=secret-token",
                    observedAtUtc: nowUtc),
            },
        };
        var cacheService = new FakeStatusCacheService(snapshot);
        var scheduler = new FakeStatusRefreshScheduler();
        using var runtime = CreateRuntime(cacheService, scheduler, nowUtc: nowUtc);

        var presentation = runtime.BuildPresentation();
        var metadata = runtime.GetRefreshMetadata();

        Assert.Equal(WidgetRefreshVisualState.Error, presentation.Refresh.State);
        Assert.Equal(TimeSpan.FromMinutes(10), metadata.SnapshotAge);
        Assert.Equal(snapshot.CapturedAtUtc, metadata.CapturedAtUtc);
        Assert.Equal(snapshot.NextScheduledRefreshAtUtc, metadata.NextScheduledRefreshAtUtc);
        Assert.Equal(StatusRefreshOutcome.Failed, metadata.LatestOutcome);
        Assert.False(metadata.IsRefreshRunning);
        Assert.DoesNotContain("secret-token", metadata.LatestSafeFailureSummary, StringComparison.Ordinal);
        Assert.NotNull(metadata.LatestSafeFailureSummary);
    }

    [Fact]
    public void SnapshotChanged_PassesThroughCacheEvent_AndUpdatePreferencesChangesBuildInputs()
    {
        var initialPreferences = WidgetPreferenceDefaults.Create();
        var updatedPreferences = initialPreferences with
        {
            SelectedView = WidgetViewKind.Minimal,
        };
        var cacheService = new FakeStatusCacheService();
        var scheduler = new FakeStatusRefreshScheduler();
        using var runtime = CreateRuntime(
            cacheService,
            scheduler,
            preferenceLoadResult: new PreferenceLoadResult
            {
                Preferences = initialPreferences,
            });
        StatusSnapshotChangedEventArgs? observedEvent = null;
        runtime.SnapshotChanged += (_, args) => observedEvent = args;

        runtime.UpdatePreferences(updatedPreferences);
        var nextSnapshot = cacheService.CurrentSnapshot with
        {
            CapturedAtUtc = cacheService.CurrentSnapshot.CapturedAtUtc + TimeSpan.FromMinutes(1),
        };
        cacheService.Publish(nextSnapshot);
        var presentation = runtime.BuildPresentation();

        Assert.Same(updatedPreferences, runtime.Preferences);
        Assert.Same(nextSnapshot, runtime.CurrentSnapshot);
        Assert.NotNull(observedEvent);
        Assert.Same(nextSnapshot, observedEvent!.CurrentSnapshot);
        Assert.Equal(WidgetViewKind.Minimal, presentation.SelectedView);
    }

    private static CodexWidgetRuntime CreateRuntime(
        FakeStatusCacheService cacheService,
        FakeStatusRefreshScheduler scheduler,
        PreferenceLoadResult? preferenceLoadResult = null,
        CodexWidgetRuntimeOptions? options = null,
        DateTimeOffset? nowUtc = null)
    {
        var clock = new FakeClock(nowUtc ?? new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero));
        var projectionService = new StatusProjectionService(clock);
        var presentationService = new WidgetPresentationService(projectionService, clock);

        return new CodexWidgetRuntime(
            cacheService,
            scheduler,
            projectionService,
            presentationService,
            preferenceLoadResult,
            preferenceFilePath: "/tmp/settings.json",
            options: options);
    }

    private sealed class FakeClock(DateTimeOffset nowUtc) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = nowUtc;
    }

    private sealed class FakeStatusCacheService : IStatusCacheService
    {
        public FakeStatusCacheService(StatusSnapshot? snapshot = null)
        {
            CurrentSnapshot = snapshot ?? new StatusSnapshot
            {
                CapturedAtUtc = new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero),
            };
        }

        public StatusSnapshot CurrentSnapshot { get; private set; }

        public CodexWidget.Profiles.CodexHomePaths? CurrentPaths => null;

        public bool IsInitialized { get; private set; }

        public int InitializeCallCount { get; private set; }

        public int RefreshCallCount { get; private set; }

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
            IsInitialized = true;
            return Task.FromResult(CurrentSnapshot);
        }

        public void Publish(StatusSnapshot snapshot)
        {
            var previous = CurrentSnapshot;
            CurrentSnapshot = snapshot;
            IsInitialized = true;
            SnapshotChanged?.Invoke(this, new StatusSnapshotChangedEventArgs(previous, snapshot));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeStatusRefreshScheduler : IStatusRefreshScheduler
    {
        private readonly FakeStatusCacheService? cacheService;

        public FakeStatusRefreshScheduler(FakeStatusCacheService? cacheService = null)
        {
            this.cacheService = cacheService;
        }

        public int StartCallCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            if (cacheService is not null)
            {
                cacheService.Publish(cacheService.CurrentSnapshot);
            }

            return Task.CompletedTask;
        }

        public Task RequestStaleWidgetOpenRefreshAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RequestRefreshAsync(StatusRefreshReason reason, StatusRefreshScope scope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
