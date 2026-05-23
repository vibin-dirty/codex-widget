using CodexWidget.Core;
using CodexWidget.Runtime;
using Microsoft.Extensions.Hosting;

namespace CodexWidget.Web;

public sealed class CodexWidgetRuntimeStartupService : IHostedService
{
    private readonly CodexWidgetRuntime runtime;
    private readonly WebRuntimeInitializationState initializationState;
    private readonly ILogger<CodexWidgetRuntimeStartupService> logger;
    private readonly object startupGate = new();
    private CancellationTokenSource? startupCancellation;
    private Task? startupTask;

    public CodexWidgetRuntimeStartupService(
        CodexWidgetRuntime runtime,
        WebRuntimeInitializationState initializationState,
        ILogger<CodexWidgetRuntimeStartupService> logger)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.initializationState = initializationState ?? throw new ArgumentNullException(nameof(initializationState));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (startupGate)
        {
            if (startupTask is not null)
            {
                return;
            }

            startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupTask = InitializeRuntimeAsync(startupCancellation.Token);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? cancellationSource;
        Task? backgroundTask;

        lock (startupGate)
        {
            cancellationSource = startupCancellation;
            backgroundTask = startupTask;
            startupCancellation = null;
            startupTask = null;
        }

        if (cancellationSource is not null)
        {
            try
            {
                cancellationSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Another shutdown path may have already disposed the source.
            }
        }

        if (backgroundTask is null)
        {
            return;
        }

        try
        {
            await backgroundTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown cancellation should not surface as startup failure.
        }
        finally
        {
            if (cancellationSource is not null)
            {
                try
                {
                    cancellationSource.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Disposal can race with host teardown in integration tests.
                }
            }
        }
    }

    private async Task InitializeRuntimeAsync(CancellationToken cancellationToken)
    {
        var attemptedAtUtc = DateTimeOffset.UtcNow;
        initializationState.MarkAttempted(attemptedAtUtc);

        try
        {
            await runtime.InitializeAsync(cancellationToken).ConfigureAwait(false);
            initializationState.MarkReady(attemptedAtUtc, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown/abort cancellation should leave startup state unchanged.
        }
        catch (Exception exception)
        {
            var safeSummary = BuildSafeFailureSummary(exception);
            initializationState.MarkFailed(attemptedAtUtc, safeSummary);
            logger.LogError(
                "CodexWidget runtime initialization failed. ExceptionType={ExceptionType}; Summary={Summary}",
                exception.GetType().FullName,
                safeSummary);
        }
    }

    private static string BuildSafeFailureSummary(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var exceptionTypeName = exception.GetType().Name;
        var safeType = string.IsNullOrWhiteSpace(exceptionTypeName)
            ? "unknown"
            : RedactionHelper.RedactDiagnosticValue("exceptionType", exceptionTypeName);

        return $"Runtime initialization failed ({safeType}).";
    }
}
