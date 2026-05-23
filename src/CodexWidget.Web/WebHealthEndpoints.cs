using CodexWidget.Core;
using CodexWidget.Runtime;

namespace CodexWidget.Web;

public static class WebHealthEndpoints
{
    public static IEndpointRouteBuilder MapWebHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health", static () => Results.Ok(new WebLivenessResponse()));
        endpoints.MapGet("/health/status", GetHealthStatus);
        return endpoints;
    }

    private static IResult GetHealthStatus(
        CodexWidgetRuntime runtime,
        WebRuntimeInitializationState runtimeInitializationState,
        ResolvedCodexWidgetWebOptions webOptions)
    {
        var initializationSnapshot = runtimeInitializationState.Snapshot;

        RefreshMetadata? refreshMetadata = null;
        string? runtimeFailure = initializationSnapshot.FailureSummary;

        if (initializationSnapshot.Status == WebRuntimeInitializationStatus.Ready)
        {
            try
            {
                refreshMetadata = runtime.GetRefreshMetadata();
            }
            catch (Exception exception)
            {
                runtimeFailure ??= BuildSafeFailureSummary(exception);
            }
        }

        return Results.Ok(
            WebHealthStatusResponse.FromRuntimeState(
                initializationSnapshot,
                webOptions,
                refreshMetadata,
                runtimeFailure));
    }

    private static string BuildSafeFailureSummary(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var typeName = exception.GetType().Name;
        var safeType = string.IsNullOrWhiteSpace(typeName)
            ? "unknown"
            : RedactionHelper.RedactDiagnosticValue("exceptionType", typeName);

        return $"Runtime request failed ({safeType}).";
    }
}
