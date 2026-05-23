using CodexWidget.Core;
using System.Net;

namespace CodexWidget.Usage;

public interface IUsageEndpointResolver
{
    UsageEndpointResolutionResult Resolve(string? chatGptBaseUrl, DateTimeOffset? observedAtUtc = null);
}

public sealed class UsageEndpointResolver : IUsageEndpointResolver
{
    public const string DefaultBaseUrl = "https://chatgpt.com/backend-api";

    private const string ChatGptHost = "chatgpt.com";
    private const string ChatOpenAiHost = "chat.openai.com";

    public UsageEndpointResolutionResult Resolve(string? chatGptBaseUrl, DateTimeOffset? observedAtUtc = null)
    {
        var observedAt = observedAtUtc ?? DateTimeOffset.UtcNow;
        var candidate = string.IsNullOrWhiteSpace(chatGptBaseUrl)
            ? DefaultBaseUrl
            : chatGptBaseUrl.Trim();

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsedCandidate))
        {
            return CreateMalformedResult(
                "Usage endpoint base URL is malformed.",
                observedAt,
                new[]
                {
                    new KeyValuePair<string, string?>("reason", "Input is not an absolute URL."),
                });
        }

        if (!string.IsNullOrWhiteSpace(parsedCandidate.Query) || !string.IsNullOrWhiteSpace(parsedCandidate.Fragment))
        {
            return CreateMalformedResult(
                "Usage endpoint base URL must not include query or fragment values.",
                observedAt,
                new[]
                {
                    new KeyValuePair<string, string?>("reason", "Query and fragment values are not supported."),
                    new KeyValuePair<string, string?>("scheme", parsedCandidate.Scheme),
                    new KeyValuePair<string, string?>("host", parsedCandidate.Host),
                });
        }

        var trimmedBase = parsedCandidate.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmedBase))
        {
            return CreateMalformedResult(
                "Usage endpoint base URL is malformed.",
                observedAt,
                new[]
                {
                    new KeyValuePair<string, string?>("reason", "Normalized URL is empty."),
                });
        }

        if (!Uri.TryCreate(trimmedBase, UriKind.Absolute, out var normalizedBaseUri))
        {
            return CreateMalformedResult(
                "Usage endpoint base URL is malformed after normalization.",
                observedAt,
                new[]
                {
                    new KeyValuePair<string, string?>("reason", "Normalized URL is not valid."),
                });
        }

        var isLoopback = IsLoopbackHost(normalizedBaseUri.Host);
        var isChatHost = IsApprovedChatHost(normalizedBaseUri.Host);
        var hasBackendApiPath = ContainsBackendApiSegment(normalizedBaseUri.AbsolutePath);

        if (isChatHost
            && normalizedBaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && IsRootPath(normalizedBaseUri.AbsolutePath)
            && !hasBackendApiPath)
        {
            var appendedBase = $"{trimmedBase}/backend-api";
            if (!Uri.TryCreate(appendedBase, UriKind.Absolute, out normalizedBaseUri))
            {
                return CreateMalformedResult(
                    "Usage endpoint base URL is malformed after backend-api normalization.",
                    observedAt,
                    new[]
                    {
                        new KeyValuePair<string, string?>("reason", "Could not append backend-api path."),
                    });
            }

            hasBackendApiPath = true;
        }

        if (isChatHost && !normalizedBaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return CreateRejectedResult(
                "Only HTTPS is allowed for ChatGPT usage endpoints.",
                observedAt,
                new[]
                {
                    new KeyValuePair<string, string?>("scheme", normalizedBaseUri.Scheme),
                    new KeyValuePair<string, string?>("host", normalizedBaseUri.Host),
                });
        }

        if (!normalizedBaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !normalizedBaseUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return CreateRejectedResult(
                "Usage endpoint base URL must use HTTP or HTTPS.",
                observedAt,
                new[]
                {
                    new KeyValuePair<string, string?>("scheme", normalizedBaseUri.Scheme),
                    new KeyValuePair<string, string?>("host", normalizedBaseUri.Host),
                });
        }

        if (!isChatHost && !isLoopback)
        {
            return CreateRejectedResult(
                "Usage endpoint host is not in the allowed host set.",
                observedAt,
                new[]
                {
                    new KeyValuePair<string, string?>("scheme", normalizedBaseUri.Scheme),
                    new KeyValuePair<string, string?>("host", normalizedBaseUri.Host),
                });
        }

        if (isChatHost && !hasBackendApiPath)
        {
            return CreateRejectedResult(
                "ChatGPT usage endpoint base URL must include /backend-api.",
                observedAt,
                new[]
                {
                    new KeyValuePair<string, string?>("host", normalizedBaseUri.Host),
                    new KeyValuePair<string, string?>("path", normalizedBaseUri.AbsolutePath),
                });
        }

        var endpointPath = hasBackendApiPath ? "wham/usage" : "api/codex/usage";
        var endpoint = new Uri($"{normalizedBaseUri.AbsoluteUri.TrimEnd('/')}/{endpointPath}", UriKind.Absolute);

        var sourceStatus = new SourceStatus
        {
            Source = StatusSourceKind.UsageEndpoint,
            State = SourceStatusState.Available,
            Availability = StatusAvailability.Available(),
            ObservedAtUtc = observedAt,
            Diagnostics = Array.Empty<SourceDiagnostic>(),
        };

        return new UsageEndpointResolutionResult
        {
            Outcome = UsageEndpointResolutionOutcome.Resolved,
            BaseUri = normalizedBaseUri,
            EndpointUri = endpoint,
            SourceStatus = sourceStatus,
            Diagnostics = Array.Empty<SourceDiagnostic>(),
        };
    }

    private static UsageEndpointResolutionResult CreateMalformedResult(
        string summary,
        DateTimeOffset observedAt,
        IEnumerable<KeyValuePair<string, string?>> context)
    {
        return CreateFailureResult(
            summary,
            SourceStatusState.Malformed,
            SourceDiagnosticCode.Malformed,
            UsageEndpointResolutionOutcome.Malformed,
            observedAt,
            context);
    }

    private static UsageEndpointResolutionResult CreateRejectedResult(
        string summary,
        DateTimeOffset observedAt,
        IEnumerable<KeyValuePair<string, string?>> context)
    {
        return CreateFailureResult(
            summary,
            SourceStatusState.Unavailable,
            SourceDiagnosticCode.Unavailable,
            UsageEndpointResolutionOutcome.Rejected,
            observedAt,
            context);
    }

    private static UsageEndpointResolutionResult CreateFailureResult(
        string summary,
        SourceStatusState statusState,
        SourceDiagnosticCode diagnosticCode,
        UsageEndpointResolutionOutcome outcome,
        DateTimeOffset observedAt,
        IEnumerable<KeyValuePair<string, string?>> context)
    {
        var diagnostic = SourceDiagnostic.Create(
            diagnosticCode,
            SourceDiagnosticSeverity.Warning,
            summary,
            context: context,
            observedAtUtc: observedAt);

        return new UsageEndpointResolutionResult
        {
            Outcome = outcome,
            BaseUri = null,
            EndpointUri = null,
            SourceStatus = new SourceStatus
            {
                Source = StatusSourceKind.UsageEndpoint,
                State = statusState,
                Availability = StatusAvailability.Unavailable(MapAvailabilityCode(diagnosticCode)),
                ObservedAtUtc = observedAt,
                Diagnostics = new[] { diagnostic },
            },
            Diagnostics = new[] { diagnostic },
        };
    }

    private static StatusAvailabilityCode MapAvailabilityCode(SourceDiagnosticCode code)
    {
        return code switch
        {
            SourceDiagnosticCode.Malformed => StatusAvailabilityCode.Malformed,
            SourceDiagnosticCode.Unavailable => StatusAvailabilityCode.Unavailable,
            _ => StatusAvailabilityCode.Error,
        };
    }

    private static bool IsApprovedChatHost(string host)
    {
        return host.Equals(ChatGptHost, StringComparison.OrdinalIgnoreCase)
            || host.Equals(ChatOpenAiHost, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRootPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) || path == "/";
    }

    private static bool ContainsBackendApiSegment(string path)
    {
        return path.Contains("/backend-api", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
