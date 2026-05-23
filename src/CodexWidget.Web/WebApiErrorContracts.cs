using CodexWidget.Core;
using CodexWidget.Runtime;
using System.Text.Json.Serialization;

namespace CodexWidget.Web;

public static class WebApiErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string RefreshConflict = "refresh_conflict";
    public const string RefreshRateLimited = "refresh_rate_limited";
    public const string StartupFailed = "startup_failed";
    public const string RuntimeFailed = "runtime_failed";
}

public sealed record WebApiError
{
    public string Code { get; init; } = WebApiErrorCodes.RuntimeFailed;

    public string Message { get; init; } = "The request could not be completed.";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureSummary { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AllowedRefreshScopes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RetryAfterSeconds { get; init; }
}

public sealed record WebApiErrorResponse
{
    public WebApiError Error { get; init; } = new();
}

public static class WebApiErrors
{
    public static WebApiErrorResponse InvalidRefreshScope(string? scope)
    {
        var receivedScope = string.IsNullOrWhiteSpace(scope)
            ? "A refresh scope is required."
            : "The supplied refresh scope is not supported.";

        return new WebApiErrorResponse
        {
            Error = new WebApiError
            {
                Code = WebApiErrorCodes.InvalidRequest,
                Message = $"{receivedScope} Use one of: {string.Join(", ", StatusRefreshScopeParser.SupportedScopes)}.",
                AllowedRefreshScopes = StatusRefreshScopeParser.SupportedScopes,
            },
        };
    }

    public static WebApiErrorResponse RefreshConflict(string? failureSummary = null)
    {
        return new WebApiErrorResponse
        {
            Error = new WebApiError
            {
                Code = WebApiErrorCodes.RefreshConflict,
                Message = "A refresh request is already in progress.",
                FailureSummary = WebApiRedaction.RedactOptionalText(failureSummary),
            },
        };
    }

    public static WebApiErrorResponse RefreshRateLimited(int retryAfterSeconds, string? failureSummary = null)
    {
        return new WebApiErrorResponse
        {
            Error = new WebApiError
            {
                Code = WebApiErrorCodes.RefreshRateLimited,
                Message = "Refresh requests are temporarily rate limited.",
                RetryAfterSeconds = retryAfterSeconds > 0 ? retryAfterSeconds : null,
                FailureSummary = WebApiRedaction.RedactOptionalText(failureSummary),
            },
        };
    }

    public static WebApiErrorResponse StartupFailed(WebRuntimeInitializationSnapshot initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);

        return new WebApiErrorResponse
        {
            Error = new WebApiError
            {
                Code = WebApiErrorCodes.StartupFailed,
                Message = "The runtime is not ready.",
                FailureSummary = WebApiRedaction.RedactOptionalText(initialization.FailureSummary),
            },
        };
    }

    public static WebApiErrorResponse RuntimeFailed(RefreshMetadata metadata, string? fallbackSummary = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var failureSummary = !string.IsNullOrWhiteSpace(metadata.LatestSafeFailureSummary)
            ? metadata.LatestSafeFailureSummary
            : fallbackSummary;

        return new WebApiErrorResponse
        {
            Error = new WebApiError
            {
                Code = WebApiErrorCodes.RuntimeFailed,
                Message = "The runtime could not complete the request.",
                FailureSummary = WebApiRedaction.RedactOptionalText(failureSummary),
            },
        };
    }

    public static WebApiErrorResponse RuntimeFailed(SourceDiagnostic? failure)
    {
        var safeSummary = failure is null ? null : SafeSourceDiagnosticResponse.FromDiagnostic(failure).Summary;

        return new WebApiErrorResponse
        {
            Error = new WebApiError
            {
                Code = WebApiErrorCodes.RuntimeFailed,
                Message = "The runtime could not complete the request.",
                FailureSummary = safeSummary,
            },
        };
    }
}
