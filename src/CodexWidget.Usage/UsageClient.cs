using CodexWidget.Core;
using System.Net;
using System.Net.Http.Headers;

namespace CodexWidget.Usage;

public interface IUsageClient
{
    Task<UsageFetchResult> FetchAsync(UsageProfileRequest request, CancellationToken cancellationToken = default);
}

public sealed record UsageClientOptions
{
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);

    public TimeSpan RequestTimeout { get; init; } = DefaultRequestTimeout;
}

public sealed class UsageClient : IUsageClient
{
    private const string AccountIdHeaderName = "ChatGPT-Account-Id";
    private const string UserAgentValue = "codex-profiles";

    private readonly HttpClient httpClient;
    private readonly IUsageEndpointResolver endpointResolver;
    private readonly IUsageResponseMapper responseMapper;
    private readonly TimeSpan requestTimeout;
    private readonly Func<DateTimeOffset> utcNowProvider;

    public UsageClient(
        HttpClient httpClient,
        IUsageEndpointResolver endpointResolver,
        IUsageResponseMapper responseMapper,
        UsageClientOptions? options = null,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpointResolver);
        ArgumentNullException.ThrowIfNull(responseMapper);

        var resolvedOptions = options ?? new UsageClientOptions();
        if (resolvedOptions.RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Request timeout must be greater than zero.");
        }

        this.httpClient = httpClient;
        this.endpointResolver = endpointResolver;
        this.responseMapper = responseMapper;
        requestTimeout = resolvedOptions.RequestTimeout;
        this.utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<UsageFetchResult> FetchAsync(UsageProfileRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var observedAt = utcNowProvider();
        if (!TryValidateRequest(request, observedAt, out var missingFieldResult))
        {
            return missingFieldResult;
        }

        var endpointResolution = endpointResolver.Resolve(request.ChatGptBaseUrl, observedAt);
        if (!endpointResolution.IsSuccess || endpointResolution.EndpointUri is null)
        {
            return new UsageFetchResult
            {
                ProfileId = request.ProfileId,
                Outcome = UsageFetchOutcome.EndpointRejected,
                Availability = endpointResolution.SourceStatus.Availability,
                EndpointResolution = endpointResolution,
                Diagnostics = endpointResolution.Diagnostics,
            };
        }

        using var httpRequest = BuildHttpRequest(request, endpointResolution.EndpointUri);
        using var timeoutCts = new CancellationTokenSource(requestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        HttpResponseMessage? httpResponse = null;
        try
        {
            httpResponse = await httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateFailureResult(
                request.ProfileId,
                UsageFetchOutcome.Canceled,
                StatusAvailabilityCode.Error,
                SourceDiagnosticCode.Error,
                "Usage request was canceled.",
                observedAt,
                endpointResolution,
                new[] { new KeyValuePair<string, string?>("reason", "operation-canceled") });
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return CreateFailureResult(
                request.ProfileId,
                UsageFetchOutcome.Timeout,
                StatusAvailabilityCode.NetworkError,
                SourceDiagnosticCode.NetworkError,
                "Usage request timed out.",
                observedAt,
                endpointResolution,
                new[]
                {
                    new KeyValuePair<string, string?>("reason", "timeout"),
                    new KeyValuePair<string, string?>("timeoutSeconds", requestTimeout.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
                });
        }
        catch (HttpRequestException exception)
        {
            return CreateFailureResult(
                request.ProfileId,
                UsageFetchOutcome.NetworkError,
                StatusAvailabilityCode.NetworkError,
                SourceDiagnosticCode.NetworkError,
                "Usage request failed due to a network error.",
                observedAt,
                endpointResolution,
                new[]
                {
                    new KeyValuePair<string, string?>("reason", "http-request-exception"),
                    new KeyValuePair<string, string?>("exceptionType", exception.GetType().Name),
                });
        }

        using (httpResponse)
        {
            if (httpResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                return CreateFailureResult(
                    request.ProfileId,
                    UsageFetchOutcome.Unauthorized,
                    StatusAvailabilityCode.Unauthorized,
                    SourceDiagnosticCode.Unauthorized,
                    "Usage request returned unauthorized.",
                    observedAt,
                    endpointResolution,
                    new[] { new KeyValuePair<string, string?>("statusCode", ((int)httpResponse.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)) });
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                return CreateFailureResult(
                    request.ProfileId,
                    UsageFetchOutcome.HttpError,
                    StatusAvailabilityCode.Error,
                    SourceDiagnosticCode.Error,
                    "Usage request returned a non-success status code.",
                    observedAt,
                    endpointResolution,
                    new[]
                    {
                        new KeyValuePair<string, string?>("statusCode", ((int)httpResponse.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new KeyValuePair<string, string?>("statusFamily", $"{(int)httpResponse.StatusCode / 100}xx"),
                    });
            }

            string responseBody;
            try
            {
                responseBody = await httpResponse.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CreateFailureResult(
                    request.ProfileId,
                    UsageFetchOutcome.Canceled,
                    StatusAvailabilityCode.Error,
                    SourceDiagnosticCode.Error,
                    "Usage response read was canceled.",
                    observedAt,
                    endpointResolution,
                    new[] { new KeyValuePair<string, string?>("reason", "operation-canceled") });
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                return CreateFailureResult(
                    request.ProfileId,
                    UsageFetchOutcome.Timeout,
                    StatusAvailabilityCode.NetworkError,
                    SourceDiagnosticCode.NetworkError,
                    "Usage response read timed out.",
                    observedAt,
                    endpointResolution,
                    new[]
                    {
                        new KeyValuePair<string, string?>("reason", "timeout"),
                        new KeyValuePair<string, string?>("timeoutSeconds", requestTimeout.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
                    });
            }

            UsageFetchResult mappedResult;
            try
            {
                mappedResult = responseMapper.Map(request, responseBody, observedAt);
            }
            catch (Exception exception)
            {
                return CreateFailureResult(
                    request.ProfileId,
                    UsageFetchOutcome.MalformedResponse,
                    StatusAvailabilityCode.Malformed,
                    SourceDiagnosticCode.Malformed,
                    "Usage response could not be mapped.",
                    observedAt,
                    endpointResolution,
                    new[]
                    {
                        new KeyValuePair<string, string?>("reason", "mapper-exception"),
                        new KeyValuePair<string, string?>("exceptionType", exception.GetType().Name),
                    });
            }

            if (mappedResult.Outcome == UsageFetchOutcome.Unknown)
            {
                return CreateFailureResult(
                    request.ProfileId,
                    UsageFetchOutcome.MalformedResponse,
                    StatusAvailabilityCode.Malformed,
                    SourceDiagnosticCode.Malformed,
                    "Usage response mapper returned an unknown outcome.",
                    observedAt,
                    endpointResolution,
                    new[] { new KeyValuePair<string, string?>("reason", "mapper-outcome-unknown") });
            }

            return mappedResult with
            {
                ProfileId = string.IsNullOrWhiteSpace(mappedResult.ProfileId) ? request.ProfileId : mappedResult.ProfileId,
                EndpointResolution = endpointResolution,
            };
        }
    }

    private static HttpRequestMessage BuildHttpRequest(UsageProfileRequest request, Uri endpointUri)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpointUri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.AccessToken);
        httpRequest.Headers.TryAddWithoutValidation(AccountIdHeaderName, request.AccountId);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgentValue);
        return httpRequest;
    }

    private static bool TryValidateRequest(
        UsageProfileRequest request,
        DateTimeOffset observedAt,
        out UsageFetchResult failureResult)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            failureResult = CreateMissingFieldResult(request.ProfileId, "accessToken", observedAt);
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.AccountId))
        {
            failureResult = CreateMissingFieldResult(request.ProfileId, "accountId", observedAt);
            return false;
        }

        failureResult = new UsageFetchResult();
        return true;
    }

    private static UsageFetchResult CreateMissingFieldResult(string? profileId, string fieldName, DateTimeOffset observedAt)
    {
        return CreateFailureResult(
            profileId,
            UsageFetchOutcome.MissingRequiredProfileFields,
            StatusAvailabilityCode.MissingRequiredField,
            SourceDiagnosticCode.MissingRequiredField,
            "Usage request is missing required profile data.",
            observedAt,
            new UsageEndpointResolutionResult(),
            new[] { new KeyValuePair<string, string?>("missingField", fieldName) });
    }

    private static UsageFetchResult CreateFailureResult(
        string? profileId,
        UsageFetchOutcome outcome,
        StatusAvailabilityCode availabilityCode,
        SourceDiagnosticCode diagnosticCode,
        string summary,
        DateTimeOffset observedAt,
        UsageEndpointResolutionResult endpointResolution,
        IEnumerable<KeyValuePair<string, string?>>? context = null,
        string? detail = null)
    {
        var diagnostic = SourceDiagnostic.Create(
            diagnosticCode,
            SourceDiagnosticSeverity.Warning,
            summary,
            detail: detail,
            context: context,
            observedAtUtc: observedAt);

        return new UsageFetchResult
        {
            ProfileId = profileId,
            Outcome = outcome,
            Availability = StatusAvailability.Unavailable(availabilityCode),
            EndpointResolution = endpointResolution,
            Diagnostics = new[] { diagnostic },
        };
    }
}
