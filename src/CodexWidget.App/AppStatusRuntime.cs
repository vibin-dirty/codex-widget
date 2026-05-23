using CodexWidget.Core;
using CodexWidget.Presentation;
using CodexWidget.Runtime;
using CodexWidget.Profiles;
using CodexWidget.Status;
using System.Net.Http;

namespace CodexWidget.App;

internal sealed class AppStatusRuntime : IDisposable
{
    private const string ValidationStateEnvironmentVariable = "CODEX_WIDGET_VALIDATION_STATE";

    private readonly CodexWidgetRuntime? _productionRuntime;
    private readonly WidgetPresentationService _presentationService;
    private readonly HttpClient? _httpClient;
    private bool _disposed;

    private AppStatusRuntime(
        PreferenceStore preferenceStore,
        string preferenceFilePath,
        PreferenceLoadResult preferenceLoadResult,
        WidgetPreferences preferences,
        StatusProjectionService projectionService,
        IStatusCacheService cacheService,
        IStatusRefreshScheduler scheduler,
        HttpClient? httpClient,
        CodexWidgetRuntime? productionRuntime = null)
    {
        PreferenceStore = preferenceStore;
        PreferenceFilePath = preferenceFilePath;
        PreferenceLoadResult = preferenceLoadResult;
        Preferences = preferences;
        ProjectionService = projectionService;
        CacheService = cacheService;
        Scheduler = scheduler;
        _productionRuntime = productionRuntime;
        _presentationService = new WidgetPresentationService(projectionService);
        _httpClient = httpClient;
    }

    public PreferenceStore PreferenceStore { get; }

    public string PreferenceFilePath { get; }

    public PreferenceLoadResult PreferenceLoadResult { get; }

    public WidgetPreferences Preferences { get; private set; }

    public StatusProjectionService ProjectionService { get; }

    public IStatusCacheService CacheService { get; }

    public IStatusRefreshScheduler Scheduler { get; }

    public StatusSnapshot CurrentSnapshot => _productionRuntime?.CurrentSnapshot ?? CacheService.CurrentSnapshot;

    public event EventHandler<StatusSnapshotChangedEventArgs>? SnapshotChanged
    {
        add
        {
            if (_productionRuntime is not null)
            {
                _productionRuntime.SnapshotChanged += value;
                return;
            }

            CacheService.SnapshotChanged += value;
        }
        remove
        {
            if (_productionRuntime is not null)
            {
                _productionRuntime.SnapshotChanged -= value;
                return;
            }

            CacheService.SnapshotChanged -= value;
        }
    }

    public static AppStatusRuntime Create(WidgetPreferences? preferenceOverride = null)
    {
        var validationState = ParseValidationRuntimeState(Environment.GetEnvironmentVariable(ValidationStateEnvironmentVariable));
        if (validationState is not null)
        {
            return ValidationRuntimeFactory.Create(validationState.Value, preferenceOverride);
        }

        return CreateProductionRuntime(preferenceOverride);
    }

    internal static AppStatusRuntime CreateProductionRuntime(WidgetPreferences? preferenceOverride = null)
    {
        var runtime = CodexWidgetRuntime.CreateProduction(new CodexWidgetRuntimeOptions
        {
            PreferenceOverride = preferenceOverride,
        });

        return new AppStatusRuntime(
            runtime.PreferenceStore ?? throw new InvalidOperationException("Production runtime did not provide a preference store."),
            runtime.PreferenceFilePath,
            runtime.PreferenceLoadResult,
            runtime.Preferences,
            runtime.ProjectionService,
            runtime.CacheService,
            runtime.Scheduler,
            httpClient: null,
            productionRuntime: runtime);
    }

    public void UpdatePreferences(WidgetPreferences preferences)
    {
        Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _productionRuntime?.UpdatePreferences(preferences);
    }

    public Task<StatusSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _productionRuntime?.InitializeAsync(cancellationToken)
            ?? CacheService.InitializeAsync(cancellationToken);
    }

    public Task StartSchedulerAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _productionRuntime?.StartSchedulerAsync(cancellationToken)
            ?? Scheduler.StartAsync(cancellationToken);
    }

    public Task RequestStaleWidgetOpenRefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _productionRuntime?.RequestStaleWidgetOpenRefreshAsync(cancellationToken)
            ?? Scheduler.RequestStaleWidgetOpenRefreshAsync(cancellationToken);
    }

    public Task<StatusSnapshot> RequestManualRefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_productionRuntime is not null)
        {
            return _productionRuntime.RefreshAsync(
                StatusRefreshReason.Manual,
                StatusRefreshScope.Full,
                cancellationToken);
        }

        return CacheService.RefreshAsync(
            StatusRefreshReason.Manual,
            StatusRefreshScope.Full,
            cancellationToken);
    }

    public WidgetPresentationState BuildPresentation(StatusSnapshot snapshot)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);

        if (_productionRuntime is not null && ReferenceEquals(snapshot, _productionRuntime.CurrentSnapshot))
        {
            return _productionRuntime.BuildPresentation();
        }

        return _presentationService.Build(snapshot, Preferences);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Shutdown order: scheduler/change monitor first, then cache/usage/http resources.
        if (_productionRuntime is not null)
        {
            _productionRuntime.Dispose();
            return;
        }

        Scheduler.Dispose();
        CacheService.Dispose();
        _httpClient?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    internal enum ValidationRuntimeState
    {
        Normal,
        Stale,
        Refreshing,
        Warning,
        Error,
        Unavailable,
        CompactDesign,
    }

    private static class ValidationRuntimeFactory
    {
        public static AppStatusRuntime Create(ValidationRuntimeState state, WidgetPreferences? preferenceOverride)
        {
            var pathProvider = new AppDataPreferencePathProvider();
            var preferenceStore = new PreferenceStore(pathProvider);
            var preferenceFilePath = pathProvider.GetPreferenceFilePath();
            var preferenceLoadResult = preferenceStore.Load();
            var preferences = preferenceOverride ?? preferenceLoadResult.Preferences;
            var nowUtc = DateTimeOffset.UtcNow;
            var snapshot = ValidationStatusSnapshotFactory.Create(state, nowUtc, preferences);
            var cacheService = new ValidationStatusCacheService(snapshot);
            var scheduler = new ValidationStatusRefreshScheduler();
            var projectionService = new StatusProjectionService(SystemClock.Instance);
            var httpClient = new HttpClient();

            return new AppStatusRuntime(
                preferenceStore,
                preferenceFilePath,
                preferenceLoadResult,
                preferences,
                projectionService,
                cacheService,
                scheduler,
                httpClient);
        }
    }

    private sealed class ValidationStatusCacheService : IStatusCacheService
    {
        private readonly StatusSnapshot _snapshot;

        public ValidationStatusCacheService(StatusSnapshot snapshot)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            IsInitialized = true;
        }

        public StatusSnapshot CurrentSnapshot => _snapshot;

        public CodexHomePaths? CurrentPaths => null;

        public bool IsInitialized { get; }

        public event EventHandler<StatusSnapshotChangedEventArgs>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task<StatusSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_snapshot);
        }

        public Task<StatusSnapshot> RefreshAsync(StatusRefreshReason reason, StatusRefreshScope scope, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_snapshot);
        }

        public void Dispose()
        {
        }
    }

    private sealed class ValidationStatusRefreshScheduler : IStatusRefreshScheduler
    {
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
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

    private static class ValidationStatusSnapshotFactory
    {
        private static readonly string[] AvailableDiagnosticsContext = ["validationOnly"];

        public static StatusSnapshot Create(ValidationRuntimeState state, DateTimeOffset nowUtc, WidgetPreferences preferences)
        {
            return state switch
            {
                ValidationRuntimeState.Refreshing => CreateRefreshing(nowUtc),
                ValidationRuntimeState.Warning => CreateWarning(nowUtc),
                ValidationRuntimeState.Error => CreateError(nowUtc),
                ValidationRuntimeState.Unavailable => CreateUnavailable(nowUtc),
                ValidationRuntimeState.CompactDesign => CreateCompactDesign(nowUtc),
                ValidationRuntimeState.Stale => CreateUsageSnapshot(nowUtc, preferences, quotaLeftPercent: 75, timeLeftPercent: 80, nextRefreshOffset: TimeSpan.FromMinutes(-5)),
                _ => CreateUsageSnapshot(nowUtc, preferences, quotaLeftPercent: 70, timeLeftPercent: 80, nextRefreshOffset: TimeSpan.FromMinutes(5)),
            };
        }

        private static StatusSnapshot CreateUsageSnapshot(
            DateTimeOffset nowUtc,
            WidgetPreferences preferences,
            int quotaLeftPercent,
            int timeLeftPercent,
            TimeSpan nextRefreshOffset)
        {
            var resetFiveHour = nowUtc.AddHours(5);
            var resetWeekly = nowUtc.AddDays(7);
            var mainBucket = CreateBucket(
                "codex",
                UsageBucketKind.MainCodex,
                quotaLeftPercent,
                timeLeftPercent,
                resetFiveHour,
                resetWeekly);
            var sparkBucket = CreateBucket(
                "spark",
                UsageBucketKind.Spark,
                Math.Max(0, quotaLeftPercent - 10),
                Math.Max(0, timeLeftPercent - 10),
                resetFiveHour,
                resetWeekly);

            return new StatusSnapshot
            {
                CapturedAtUtc = nowUtc,
                CurrentProfileId = "validation-work",
                Profiles =
                [
                    new ProfileStatus
                    {
                        Profile = new ProfileDescriptor
                        {
                            ProfileId = "validation-work",
                            DisplayName = "Validation Work",
                            LoginName = "validation-work@example.com",
                            SubscriptionTier = SubscriptionTier.Plus,
                            IsCurrent = true,
                            AuthKind = ProfileAuthKind.Login,
                            UsageEligibility = ProfileUsageEligibility.Eligible,
                            SourceStatus = new SourceStatus
                            {
                                Source = StatusSourceKind.CurrentAuth,
                                State = SourceStatusState.Available,
                                Availability = StatusAvailability.Available(),
                                ObservedAtUtc = nowUtc,
                            },
                        },
                        MainBucket = mainBucket,
                        SparkBucket = sparkBucket,
                        AllBuckets = [mainBucket, sparkBucket],
                        Diagnostics = Array.Empty<SourceDiagnostic>(),
                    },
                ],
                RefreshState = new StatusRefreshState
                {
                    Reason = StatusRefreshReason.Startup,
                    Scope = StatusRefreshScope.Full,
                    Outcome = StatusRefreshOutcome.Idle,
                    RequestedAtUtc = nowUtc,
                    CompletedAtUtc = nowUtc,
                },
                NextScheduledRefreshAtUtc = nowUtc.Add(nextRefreshOffset),
                Sources =
                [
                    CreateSource(StatusSourceKind.CurrentAuth, SourceStatusState.Available, nowUtc),
                    CreateSource(StatusSourceKind.ConfigToml, SourceStatusState.Available, nowUtc),
                    CreateSource(StatusSourceKind.ProfilesIndex, SourceStatusState.Available, nowUtc),
                    CreateSource(StatusSourceKind.SavedProfileAuth, SourceStatusState.Available, nowUtc),
                    CreateSource(StatusSourceKind.UsageBucket, SourceStatusState.Available, nowUtc),
                ],
            };
        }

        private static StatusSnapshot CreateCompactDesign(DateTimeOffset nowUtc)
        {
            var designOffset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 5, 17, 1, 40, 0));
            var altMain = CreateBucket(
                "codex",
                UsageBucketKind.MainCodex,
                quotaLeftPercent: 74,
                timeLeftPercent: 74,
                fiveHourReset: new DateTimeOffset(2026, 5, 17, 1, 40, 0, designOffset),
                weeklyReset: new DateTimeOffset(2026, 5, 19, 0, 0, 0, designOffset));
            var altSpark = CreateBucket(
                "spark",
                UsageBucketKind.Spark,
                quotaLeftPercent: 74,
                timeLeftPercent: 74,
                fiveHourReset: new DateTimeOffset(2026, 5, 17, 3, 20, 0, designOffset),
                weeklyReset: new DateTimeOffset(2026, 5, 20, 0, 0, 0, designOffset));
            var workMain = CreateBucket(
                "codex",
                UsageBucketKind.MainCodex,
                quotaLeftPercent: 35,
                timeLeftPercent: 35,
                fiveHourReset: new DateTimeOffset(2026, 5, 17, 2, 10, 0, designOffset),
                weeklyReset: new DateTimeOffset(2026, 5, 19, 0, 0, 0, designOffset));

            return new StatusSnapshot
            {
                CapturedAtUtc = nowUtc,
                CurrentProfileId = "validation-alt",
                Profiles =
                [
                    CreateProfile("validation-alt", "alt", isCurrent: true, nowUtc, altMain, altSpark),
                    CreateProfile("validation-work", "work", isCurrent: false, nowUtc, workMain, sparkBucket: null),
                ],
                RefreshState = new StatusRefreshState
                {
                    Reason = StatusRefreshReason.Startup,
                    Scope = StatusRefreshScope.Full,
                    Outcome = StatusRefreshOutcome.Idle,
                    RequestedAtUtc = nowUtc,
                    CompletedAtUtc = nowUtc,
                },
                NextScheduledRefreshAtUtc = nowUtc.AddMinutes(5),
                Sources =
                [
                    CreateSource(StatusSourceKind.CurrentAuth, SourceStatusState.Available, nowUtc),
                    CreateSource(StatusSourceKind.ConfigToml, SourceStatusState.Available, nowUtc),
                    CreateSource(StatusSourceKind.ProfilesIndex, SourceStatusState.Available, nowUtc),
                    CreateSource(StatusSourceKind.SavedProfileAuth, SourceStatusState.Available, nowUtc),
                    CreateSource(StatusSourceKind.UsageBucket, SourceStatusState.Available, nowUtc),
                ],
            };
        }

        private static ProfileStatus CreateProfile(
            string id,
            string displayName,
            bool isCurrent,
            DateTimeOffset nowUtc,
            UsageBucketSnapshot mainBucket,
            UsageBucketSnapshot? sparkBucket)
        {
            var allBuckets = sparkBucket is null
                ? [mainBucket]
                : new[] { mainBucket, sparkBucket };

            return new ProfileStatus
            {
                Profile = new ProfileDescriptor
                {
                    ProfileId = id,
                    DisplayName = displayName,
                    LoginName = $"{id}@example.com",
                    SubscriptionTier = SubscriptionTier.Plus,
                    IsCurrent = isCurrent,
                    AuthKind = ProfileAuthKind.Login,
                    UsageEligibility = ProfileUsageEligibility.Eligible,
                    SourceStatus = new SourceStatus
                    {
                        Source = StatusSourceKind.CurrentAuth,
                        State = SourceStatusState.Available,
                        Availability = StatusAvailability.Available(),
                        ObservedAtUtc = nowUtc,
                    },
                },
                MainBucket = mainBucket,
                SparkBucket = sparkBucket,
                AllBuckets = allBuckets,
                Diagnostics = Array.Empty<SourceDiagnostic>(),
            };
        }

        private static StatusSnapshot CreateRefreshing(DateTimeOffset nowUtc)
        {
            var snapshot = CreateUsageSnapshot(nowUtc, WidgetPreferenceDefaults.Create(), 70, 80, TimeSpan.FromMinutes(5));
            return snapshot with
            {
                RefreshState = snapshot.RefreshState with
                {
                    Outcome = StatusRefreshOutcome.Running,
                    StartedAtUtc = nowUtc,
                },
                NextScheduledRefreshAtUtc = null,
            };
        }

        private static StatusSnapshot CreateError(DateTimeOffset nowUtc)
        {
            var snapshot = CreateUsageSnapshot(nowUtc, WidgetPreferenceDefaults.Create(), 70, 80, TimeSpan.FromMinutes(5));
            return snapshot with
            {
                RefreshState = new StatusRefreshState
                {
                    Reason = StatusRefreshReason.Manual,
                    Scope = StatusRefreshScope.Full,
                    Outcome = StatusRefreshOutcome.Failed,
                    RequestedAtUtc = nowUtc,
                    StartedAtUtc = nowUtc,
                    CompletedAtUtc = nowUtc,
                    Failure = SourceDiagnostic.Create(
                        SourceDiagnosticCode.NetworkError,
                        SourceDiagnosticSeverity.Error,
                        "Validation-only refresh failed.",
                        detail: "Synthetic validation state produced a refresh failure.",
                        context: [new KeyValuePair<string, string?>("validationState", "error")],
                        observedAtUtc: nowUtc),
                },
                Sources = snapshot.Sources.Select(source => source with
                {
                    State = source.Source == StatusSourceKind.UsageBucket ? SourceStatusState.Error : source.State,
                    Diagnostics = source.Source == StatusSourceKind.UsageBucket
                        ? [SourceDiagnostic.Create(
                            SourceDiagnosticCode.NetworkError,
                            SourceDiagnosticSeverity.Error,
                            "Validation-only usage error.",
                            detail: "Synthetic validation state produced a usage error.",
                            context: [new KeyValuePair<string, string?>("validationState", "error")],
                            observedAtUtc: nowUtc)]
                        : source.Diagnostics,
                }).ToArray(),
            };
        }

        private static StatusSnapshot CreateWarning(DateTimeOffset nowUtc)
        {
            var snapshot = CreateUsageSnapshot(nowUtc, WidgetPreferenceDefaults.Create(), 70, 80, TimeSpan.FromMinutes(5));
            return snapshot with
            {
                Sources = snapshot.Sources.Select(source => source with
                {
                    Diagnostics = source.Source == StatusSourceKind.UsageBucket
                        ? [SourceDiagnostic.Create(
                            SourceDiagnosticCode.Stale,
                            SourceDiagnosticSeverity.Warning,
                            "Validation-only usage warning.",
                            detail: "Synthetic validation state produced a usage warning.",
                            context: [new KeyValuePair<string, string?>("validationState", "warning")],
                            observedAtUtc: nowUtc)]
                        : source.Diagnostics,
                }).ToArray(),
            };
        }

        private static StatusSnapshot CreateUnavailable(DateTimeOffset nowUtc)
        {
            return new StatusSnapshot
            {
                CapturedAtUtc = nowUtc,
                CurrentProfileId = null,
                Profiles = Array.Empty<ProfileStatus>(),
                RefreshState = new StatusRefreshState
                {
                    Reason = StatusRefreshReason.Startup,
                    Scope = StatusRefreshScope.Full,
                    Outcome = StatusRefreshOutcome.Idle,
                    RequestedAtUtc = nowUtc,
                    CompletedAtUtc = nowUtc,
                },
                NextScheduledRefreshAtUtc = null,
                Sources =
                [
                    CreateSource(StatusSourceKind.CurrentAuth, SourceStatusState.Missing, nowUtc),
                    CreateSource(StatusSourceKind.ConfigToml, SourceStatusState.Missing, nowUtc),
                ],
            };
        }

        private static UsageBucketSnapshot CreateBucket(string bucketId, UsageBucketKind bucketKind, int quotaLeftPercent, int timeLeftPercent, DateTimeOffset fiveHourReset, DateTimeOffset weeklyReset)
        {
            return new UsageBucketSnapshot
            {
                BucketId = bucketId,
                BucketLabel = bucketId,
                BucketKind = bucketKind,
                FetchStatus = UsageBucketFetchStatus.Succeeded,
                Availability = StatusAvailability.Available(),
                Windows =
                [
                    CreateWindow(UsageWindowKind.FiveHour, quotaLeftPercent, timeLeftPercent, fiveHourReset),
                    CreateWindow(UsageWindowKind.Weekly, Math.Min(100, quotaLeftPercent + 15), Math.Min(100, timeLeftPercent + 10), weeklyReset),
                ],
            };
        }

        private static UsageWindowSnapshot CreateWindow(UsageWindowKind kind, int quotaLeftPercent, int timeLeftPercent, DateTimeOffset resetAt)
        {
            return new UsageWindowSnapshot
            {
                WindowKind = kind,
                DurationSeconds = kind == UsageWindowKind.FiveHour ? 18000 : 604800,
                ResetAtUnixSeconds = resetAt.ToUnixTimeSeconds(),
                UsedPercent = 100 - quotaLeftPercent,
                QuotaLeftPercent = quotaLeftPercent,
                TimeLeftPercent = timeLeftPercent,
                Availability = StatusAvailability.Available(),
            };
        }

        private static SourceStatus CreateSource(StatusSourceKind sourceKind, SourceStatusState state, DateTimeOffset observedAtUtc)
        {
            return new SourceStatus
            {
                Source = sourceKind,
                State = state,
                Availability = state == SourceStatusState.Available
                    ? StatusAvailability.Available()
                    : StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable),
                ObservedAtUtc = observedAtUtc,
                Diagnostics = Array.Empty<SourceDiagnostic>(),
            };
        }
    }

    internal static ValidationRuntimeState? ParseValidationRuntimeState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<ValidationRuntimeState>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(typeof(ValidationRuntimeState), parsed)
            ? parsed
            : null;
    }
}
