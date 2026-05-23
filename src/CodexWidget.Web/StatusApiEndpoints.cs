using CodexWidget.Runtime;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CodexWidget.Web;

public static class StatusApiEndpoints
{
    public static IEndpointRouteBuilder MapStatusApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var statusGroup = endpoints.MapGroup("/api/status");
        statusGroup.MapGet("/frontend-options", GetFrontendOptions);
        statusGroup.MapGet("/presentation", GetPresentation);
        statusGroup.MapGet("/snapshot", GetSnapshot);
        statusGroup.MapGet("/refresh", GetRefreshMetadata);
        statusGroup.MapPost("/refresh", PostRefreshAsync);
        return endpoints;
    }

    private static IResult GetFrontendOptions(ResolvedCodexWidgetWebOptions webOptions)
    {
        return Results.Ok(FrontendOptionsResponse.FromResolvedOptions(webOptions));
    }

    private static IResult GetPresentation(
        CodexWidgetRuntime runtime,
        WebRuntimeInitializationState runtimeInitializationState)
    {
        return ExecuteRuntimeRead(
            runtime,
            runtimeInitializationState,
            static currentRuntime =>
            {
                var presentation = currentRuntime.BuildPresentation();
                return Results.Ok(WidgetPresentationStateSanitizer.Sanitize(presentation));
            });
    }

    private static IResult GetSnapshot(
        CodexWidgetRuntime runtime,
        WebRuntimeInitializationState runtimeInitializationState)
    {
        return ExecuteRuntimeRead(
            runtime,
            runtimeInitializationState,
            static currentRuntime => Results.Ok(SafeStatusSnapshotResponse.FromSnapshot(currentRuntime.CurrentSnapshot)));
    }

    private static IResult GetRefreshMetadata(
        CodexWidgetRuntime runtime,
        WebRuntimeInitializationState runtimeInitializationState)
    {
        return ExecuteRuntimeRead(
            runtime,
            runtimeInitializationState,
            static currentRuntime => Results.Ok(currentRuntime.GetRefreshMetadata()));
    }

    private static async Task<IResult> PostRefreshAsync(
        HttpRequest request,
        CodexWidgetRuntime runtime,
        WebRuntimeInitializationState runtimeInitializationState,
        ManualRefreshRequestCoordinator refreshCoordinator,
        IOptions<JsonOptions> jsonOptions,
        CancellationToken cancellationToken)
    {
        if (!TryEnsureRuntimeReady(runtimeInitializationState, out var startupFailure))
        {
            return startupFailure;
        }

        var acquired = await refreshCoordinator.TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            string? failureSummary = null;
            try
            {
                failureSummary = runtime.GetRefreshMetadata().LatestSafeFailureSummary;
            }
            catch
            {
                // Keep conflict responses safe and deterministic even if runtime metadata is unavailable.
            }

            return Results.Conflict(WebApiErrors.RefreshConflict(failureSummary));
        }

        try
        {
            var refreshRequest = await TryReadRefreshRequestAsync(request, jsonOptions.Value.SerializerOptions, cancellationToken).ConfigureAwait(false);
            if (refreshRequest is null || !refreshRequest.TryResolveScope(out var resolvedScope))
            {
                return Results.BadRequest(WebApiErrors.InvalidRefreshScope(refreshRequest?.Scope));
            }

            await runtime.RefreshAsync(
                    CodexWidget.Core.StatusRefreshReason.Manual,
                    resolvedScope,
                    cancellationToken)
                .ConfigureAwait(false);

            var presentation = runtime.BuildPresentation();
            return Results.Ok(WidgetPresentationStateSanitizer.Sanitize(presentation));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return BuildRuntimeFailure(runtime, exception);
        }
        finally
        {
            refreshCoordinator.Exit();
        }
    }

    private static async Task<StatusRefreshRequest?> TryReadRefreshRequestAsync(
        HttpRequest request,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        if (!RequestContainsBody(request))
        {
            return new StatusRefreshRequest();
        }

        try
        {
            return await request.ReadFromJsonAsync<StatusRefreshRequest>(serializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool RequestContainsBody(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.ContentLength.GetValueOrDefault() > 0 || request.Headers.ContainsKey("Transfer-Encoding");
    }

    private static IResult ExecuteRuntimeRead(
        CodexWidgetRuntime runtime,
        WebRuntimeInitializationState runtimeInitializationState,
        Func<CodexWidgetRuntime, IResult> operation)
    {
        if (!TryEnsureRuntimeReady(runtimeInitializationState, out var startupFailure))
        {
            return startupFailure;
        }

        try
        {
            return operation(runtime);
        }
        catch (Exception exception)
        {
            return BuildRuntimeFailure(runtime, exception);
        }
    }

    private static bool TryEnsureRuntimeReady(
        WebRuntimeInitializationState runtimeInitializationState,
        out IResult failureResult)
    {
        var initializationSnapshot = runtimeInitializationState.Snapshot;
        if (initializationSnapshot.Status == WebRuntimeInitializationStatus.Ready)
        {
            failureResult = Results.Empty;
            return true;
        }

        failureResult = Results.Json(
            WebApiErrors.StartupFailed(initializationSnapshot),
            statusCode: StatusCodes.Status503ServiceUnavailable);
        return false;
    }

    private static IResult BuildRuntimeFailure(CodexWidgetRuntime runtime, Exception exception)
    {
        var fallbackSummary = BuildSafeExceptionSummary(exception);

        RefreshMetadata metadata;
        try
        {
            metadata = runtime.GetRefreshMetadata();
        }
        catch
        {
            metadata = new RefreshMetadata();
        }

        return Results.Json(
            WebApiErrors.RuntimeFailed(metadata, fallbackSummary),
            statusCode: StatusCodes.Status500InternalServerError);
    }

    private static string BuildSafeExceptionSummary(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var typeName = exception.GetType().Name;
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return "The runtime could not complete the request.";
        }

        var safeType = CodexWidget.Core.RedactionHelper.RedactDiagnosticValue("exceptionType", typeName);
        return $"Runtime request failed ({safeType}).";
    }
}

public sealed class ManualRefreshRequestCoordinator : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public Task<bool> TryEnterAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return gate.WaitAsync(0, cancellationToken);
    }

    public void Exit()
    {
        ThrowIfDisposed();
        gate.Release();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        gate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
