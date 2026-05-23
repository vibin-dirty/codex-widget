using CodexWidget.Core;
using CodexWidget.Presentation;
using CodexWidget.Runtime;
using CodexWidget.Status;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodexWidget.Web.Tests;

[CollectionDefinition("WebHostEnvironment", DisableParallelization = true)]
public sealed class WebHostEnvironmentCollection
{
    public const string Name = "WebHostEnvironment";
}

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> configurationOverrides;
    private readonly ICodexWidgetRuntimeFactory runtimeFactory;

    public TestWebApplicationFactory(
        ICodexWidgetRuntimeFactory runtimeFactory,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        this.runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
        this.configurationOverrides = configurationOverrides ?? new Dictionary<string, string?>(0, StringComparer.Ordinal);
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            if (configurationOverrides.Count > 0)
            {
                configurationBuilder.AddInMemoryCollection(configurationOverrides);
            }
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICodexWidgetRuntimeFactory>();
            services.AddSingleton(runtimeFactory);
        });
    }
}

public sealed class RecordingRuntimeFactory(Func<CodexWidgetRuntimeOptions, CodexWidgetRuntime>? runtimeCreator = null) : ICodexWidgetRuntimeFactory
{
    private readonly Func<CodexWidgetRuntimeOptions, CodexWidgetRuntime> runtimeCreator =
        runtimeCreator ?? (options => TestRuntimeFactory.CreateRuntime(options));

    public CodexWidgetRuntimeOptions? CapturedOptions { get; private set; }

    public CodexWidgetRuntime Create(CodexWidgetRuntimeOptions options)
    {
        CapturedOptions = options;
        return runtimeCreator(options);
    }
}

public static class TestRuntimeFactory
{
    public static CodexWidgetRuntime CreateRuntime(
        CodexWidgetRuntimeOptions options,
        IStatusCacheService? cacheService = null,
        IStatusRefreshScheduler? scheduler = null,
        IEnumerable<IDisposable>? ownedDisposables = null)
    {
        var nowUtc = new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero);
        var projectionService = new StatusProjectionService(new FixedClock(nowUtc));
        var presentationService = new WidgetPresentationService(projectionService, new FixedClock(nowUtc));
        var resolvedCacheService = cacheService ?? new RecordingStatusCacheService(CreateSnapshot(nowUtc));
        var resolvedScheduler = scheduler
            ?? (resolvedCacheService is RecordingStatusCacheService recordingCacheService
                ? new RecordingStatusRefreshScheduler(recordingCacheService)
                : new RecordingStatusRefreshScheduler());

        return new CodexWidgetRuntime(
            resolvedCacheService,
            resolvedScheduler,
            projectionService,
            presentationService,
            preferenceLoadResult: new PreferenceLoadResult(),
            preferenceFilePath: "/tmp/codex-widget-web-tests/settings.json",
            options: options,
            ownedDisposables: ownedDisposables);
    }

    private static StatusSnapshot CreateSnapshot(DateTimeOffset capturedAtUtc)
    {
        return new StatusSnapshot
        {
            CapturedAtUtc = capturedAtUtc,
            CurrentProfileId = "profile-a",
            RefreshState = new StatusRefreshState
            {
                Outcome = StatusRefreshOutcome.Idle,
                RequestedAtUtc = capturedAtUtc,
                CompletedAtUtc = capturedAtUtc,
            },
            NextScheduledRefreshAtUtc = capturedAtUtc.AddMinutes(15),
        };
    }
}

public sealed class RecordingStatusCacheService : IStatusCacheService
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

    public Exception? InitializeException { get; init; }

    public Exception? RefreshException { get; init; }

    public TaskCompletionSource? InitializeStartedSignal { get; init; }

    public TaskCompletionSource? AllowInitializeToCompleteSignal { get; init; }

    public Func<StatusRefreshReason, StatusRefreshScope, CancellationToken, Task<StatusSnapshot>>? RefreshHandler { get; init; }

    public StatusRefreshReason? LastRefreshReason { get; private set; }

    public StatusRefreshScope? LastRefreshScope { get; private set; }

    public bool LastRefreshTokenCanBeCanceled { get; private set; }

    public event EventHandler<StatusSnapshotChangedEventArgs>? SnapshotChanged;

    public async Task<StatusSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
    {
        InitializeCallCount++;
        InitializeStartedSignal?.TrySetResult();

        if (AllowInitializeToCompleteSignal is not null)
        {
            await AllowInitializeToCompleteSignal.Task.WaitAsync(cancellationToken);
        }

        if (InitializeException is not null)
        {
            throw InitializeException;
        }

        IsInitialized = true;
        return CurrentSnapshot;
    }

    public Task<StatusSnapshot> RefreshAsync(StatusRefreshReason reason, StatusRefreshScope scope, CancellationToken cancellationToken = default)
    {
        RefreshCallCount++;
        LastRefreshReason = reason;
        LastRefreshScope = scope;
        LastRefreshTokenCanBeCanceled = cancellationToken.CanBeCanceled;

        if (RefreshException is not null)
        {
            throw RefreshException;
        }

        if (RefreshHandler is not null)
        {
            return RefreshHandler(reason, scope, cancellationToken);
        }

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

public sealed class RecordingStatusRefreshScheduler(RecordingStatusCacheService? cacheService = null) : IStatusRefreshScheduler
{
    public int StartCallCount { get; private set; }

    public int StaleWidgetOpenRefreshCallCount { get; private set; }

    public int RefreshCallCount { get; private set; }

    public int DisposeCallCount { get; private set; }

    public Exception? StartException { get; init; }

    public TaskCompletionSource? StartCalledSignal { get; init; }

    public TaskCompletionSource? AllowStartToCompleteSignal { get; init; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCallCount++;
        StartCalledSignal?.TrySetResult();

        if (AllowStartToCompleteSignal is not null)
        {
            await AllowStartToCompleteSignal.Task.WaitAsync(cancellationToken);
        }

        if (StartException is not null)
        {
            throw StartException;
        }

        cacheService?.Publish(cacheService.CurrentSnapshot);
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

public sealed class FaultingStatusCacheService : IStatusCacheService
{
    private readonly StatusSnapshot snapshot;
    private readonly Exception currentSnapshotException;
    private readonly bool throwAfterInitialize;
    private bool initialized;

    public FaultingStatusCacheService(
        StatusSnapshot snapshot,
        Exception currentSnapshotException,
        bool throwAfterInitialize = true)
    {
        this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        this.currentSnapshotException = currentSnapshotException ?? throw new ArgumentNullException(nameof(currentSnapshotException));
        this.throwAfterInitialize = throwAfterInitialize;
    }

    public StatusSnapshot CurrentSnapshot => !throwAfterInitialize || initialized
        ? throw currentSnapshotException
        : snapshot;

    public CodexWidget.Profiles.CodexHomePaths? CurrentPaths => null;

    public bool IsInitialized => initialized;

    public event EventHandler<StatusSnapshotChangedEventArgs>? SnapshotChanged
    {
        add { }
        remove { }
    }

    public Task<StatusSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
    {
        initialized = true;
        return Task.FromResult(snapshot);
    }

    public Task<StatusSnapshot> RefreshAsync(
        StatusRefreshReason reason,
        StatusRefreshScope scope,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(snapshot);
    }

    public void Dispose()
    {
    }
}

public sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}

public sealed class RecordingOwnedResource : IDisposable
{
    public int DisposeCallCount { get; private set; }

    public void Dispose()
    {
        DisposeCallCount++;
    }
}

public sealed class TemporaryEnvironmentVariables : IDisposable
{
    private readonly IReadOnlyDictionary<string, string?> originalValues;

    public TemporaryEnvironmentVariables(IReadOnlyDictionary<string, string?> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        originalValues = overrides.Keys.ToDictionary(
            key => key,
            key => Environment.GetEnvironmentVariable(key),
            StringComparer.Ordinal);

        foreach (var pair in overrides)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    public void Dispose()
    {
        foreach (var pair in originalValues)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
