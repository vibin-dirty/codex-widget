using CodexWidget.Core;
using CodexWidget.Presentation;
using CodexWidget.Profiles;
using CodexWidget.Status;
using CodexWidget.Usage;
using System.Net.Http;

namespace CodexWidget.Runtime;

public sealed class CodexWidgetRuntime : IDisposable, IAsyncDisposable
{
    private static readonly string[] SensitiveSummaryFragments =
    [
        "token",
        "secret",
        "authorization",
        "password",
        "credential",
        "session",
        "cookie",
        "api_key",
        "apikey",
        "auth",
    ];

    private readonly IStatusCacheService cacheService;
    private readonly IStatusRefreshScheduler scheduler;
    private readonly WidgetPresentationService presentationService;
    private readonly IReadOnlyList<IDisposable> ownedDisposables;
    private readonly SemaphoreSlim schedulerStartGate = new(1, 1);
    private bool schedulerStarted;
    private bool disposed;

    public static CodexWidgetRuntime CreateProduction(CodexWidgetRuntimeOptions? options = null)
    {
        var resolvedOptions = options ?? new CodexWidgetRuntimeOptions();
        var pathProvider = new AppDataPreferencePathProvider();
        var preferenceStore = new PreferenceStore(pathProvider);
        var preferenceFilePath = pathProvider.GetPreferenceFilePath();
        var preferenceLoadResult = preferenceStore.Load();
        var preferences = resolvedOptions.ResolvePreferences(preferenceLoadResult);

        var profileSnapshotReader = new ProfileSnapshotReader();
        var httpClient = CreateHttpClient(resolvedOptions.HttpMessageHandler);
        var usageClient = new UsageClient(
            httpClient,
            new UsageEndpointResolver(),
            new UsageResponseMapper());
        var tokenRefreshService = new TokenRefreshService(httpClient);
        var authTokenFileUpdater = new AuthTokenFileUpdater();
        var refreshingUsageClient = new RefreshingUsageClient(
            usageClient,
            tokenRefreshService,
            authTokenFileUpdater);
        var cacheService = new StatusCacheService(
            profileSnapshotReader,
            refreshingUsageClient,
            clock: SystemClock.Instance,
            configTomlParser: new ConfigTomlParser(),
            options: resolvedOptions.CreateCacheOptions(preferences));
        var changeMonitor = new ProfileChangeMonitor();
        var scheduler = new StatusRefreshScheduler(
            cacheService,
            changeMonitor,
            clock: SystemClock.Instance,
            options: resolvedOptions.CreateSchedulerOptions(preferences));
        var projectionService = new StatusProjectionService(SystemClock.Instance);

        return new CodexWidgetRuntime(
            cacheService,
            scheduler,
            projectionService,
            preferenceLoadResult: preferenceLoadResult,
            preferenceFilePath: preferenceFilePath,
            preferenceStore: preferenceStore,
            options: resolvedOptions,
            ownedDisposables:
            [
                httpClient,
            ]);
    }

    public CodexWidgetRuntime(
        IStatusCacheService cacheService,
        IStatusRefreshScheduler scheduler,
        StatusProjectionService projectionService,
        WidgetPresentationService? presentationService = null,
        PreferenceLoadResult? preferenceLoadResult = null,
        string? preferenceFilePath = null,
        PreferenceStore? preferenceStore = null,
        CodexWidgetRuntimeOptions? options = null,
        IEnumerable<IDisposable>? ownedDisposables = null)
    {
        this.cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        ProjectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
        this.presentationService = presentationService ?? new WidgetPresentationService(ProjectionService);
        Options = options ?? new CodexWidgetRuntimeOptions();
        PreferenceLoadResult = preferenceLoadResult ?? new PreferenceLoadResult();
        PreferenceFilePath = preferenceFilePath ?? string.Empty;
        PreferenceStore = preferenceStore;
        Preferences = Options.ResolvePreferences(PreferenceLoadResult);
        this.ownedDisposables = DistinctOwnedDisposables(ownedDisposables);
    }

    public CodexWidgetRuntimeOptions Options { get; }

    public StatusProjectionService ProjectionService { get; }

    public string PreferenceFilePath { get; }

    public PreferenceStore? PreferenceStore { get; }

    public PreferenceLoadResult PreferenceLoadResult { get; }

    public WidgetPreferences Preferences { get; private set; }

    public IStatusCacheService CacheService => cacheService;

    public IStatusRefreshScheduler Scheduler => scheduler;

    public StatusSnapshot CurrentSnapshot => cacheService.CurrentSnapshot;

    public bool IsInitialized => cacheService.IsInitialized;

    public event EventHandler<StatusSnapshotChangedEventArgs>? SnapshotChanged
    {
        add => cacheService.SnapshotChanged += value;
        remove => cacheService.SnapshotChanged -= value;
    }

    public async Task<StatusSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (Options.StartSchedulerOnInitialize)
        {
            await StartSchedulerAsync(cancellationToken).ConfigureAwait(false);
            return CurrentSnapshot;
        }

        return await cacheService.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StartSchedulerAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (schedulerStarted)
        {
            return;
        }

        await schedulerStartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (schedulerStarted)
            {
                return;
            }

            await scheduler.StartAsync(cancellationToken).ConfigureAwait(false);
            schedulerStarted = true;
        }
        finally
        {
            schedulerStartGate.Release();
        }
    }

    public Task RequestStaleWidgetOpenRefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return scheduler.RequestStaleWidgetOpenRefreshAsync(cancellationToken);
    }

    public Task<StatusSnapshot> RefreshAsync(
        StatusRefreshReason reason,
        StatusRefreshScope scope,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return cacheService.RefreshAsync(reason, scope, cancellationToken);
    }

    public WidgetPresentationState BuildPresentation()
    {
        ThrowIfDisposed();
        return presentationService.Build(CurrentSnapshot, Preferences);
    }

    public RefreshMetadata GetRefreshMetadata()
    {
        ThrowIfDisposed();

        var snapshot = CurrentSnapshot;
        var presentation = presentationService.Build(snapshot, Preferences);
        var failure = snapshot.RefreshState.Failure?.WithRedactedContent();

        return new RefreshMetadata
        {
            CapturedAtUtc = snapshot.CapturedAtUtc,
            NextScheduledRefreshAtUtc = presentation.Refresh.NextScheduledRefreshAtUtc,
            SnapshotAge = presentation.Refresh.SnapshotAge,
            LatestOutcome = snapshot.RefreshState.Outcome,
            IsRefreshRunning = snapshot.RefreshState.Outcome == StatusRefreshOutcome.Running,
            LatestSafeFailureSummary = BuildSafeFailureSummary(failure),
        };
    }

    public void UpdatePreferences(WidgetPreferences preferences)
    {
        ThrowIfDisposed();
        Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        scheduler.Dispose();
        cacheService.Dispose();

        foreach (var ownedDisposable in ownedDisposables)
        {
            ownedDisposable.Dispose();
        }

        schedulerStartGate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private static IReadOnlyList<IDisposable> DistinctOwnedDisposables(IEnumerable<IDisposable>? candidates)
    {
        if (candidates is null)
        {
            return [];
        }

        var distinct = new HashSet<IDisposable>(ReferenceEqualityComparer.Instance);
        foreach (var candidate in candidates)
        {
            if (candidate is not null)
            {
                distinct.Add(candidate);
            }
        }

        return distinct.ToArray();
    }

    private static string? BuildSafeFailureSummary(SourceDiagnostic? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Summary))
        {
            return null;
        }

        var summary = RedactionHelper.RedactDiagnosticValue(null, failure.Summary);
        if (!ContainsSensitiveSummaryFragment(summary))
        {
            return summary;
        }

        return $"Refresh failure ({failure.Code}).";
    }

    private static bool ContainsSensitiveSummaryFragment(string value)
    {
        foreach (var fragment in SensitiveSummaryFragments)
        {
            if (value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler? optionalHandler)
    {
        return optionalHandler is null
            ? new HttpClient()
            : new HttpClient(optionalHandler, disposeHandler: true);
    }
}
