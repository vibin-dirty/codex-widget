using CodexWidget.Core;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CodexWidget.Usage;

public interface ITokenRefreshService
{
    Task<UsageTokenRefreshResult> RefreshAsync(UsageProfileRequest request, CancellationToken cancellationToken = default);
}

public sealed record TokenRefreshServiceOptions
{
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);

    public TimeSpan RequestTimeout { get; init; } = DefaultRequestTimeout;
}

public sealed class TokenRefreshService : ITokenRefreshService
{
    private const string OAuthEndpoint = "https://auth.openai.com/oauth/token";
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    private const string Scope = "openid profile email";

    private readonly HttpClient httpClient;
    private readonly TimeSpan requestTimeout;
    private readonly Func<DateTimeOffset> utcNowProvider;

    public TokenRefreshService(
        HttpClient httpClient,
        TokenRefreshServiceOptions? options = null,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        var resolvedOptions = options ?? new TokenRefreshServiceOptions();
        if (resolvedOptions.RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Request timeout must be greater than zero.");
        }

        this.httpClient = httpClient;
        requestTimeout = resolvedOptions.RequestTimeout;
        this.utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<UsageTokenRefreshResult> RefreshAsync(UsageProfileRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var observedAt = utcNowProvider();
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return CreateFailureResult(
                UsageTokenRefreshOutcome.MissingRefreshToken,
                StatusAvailabilityCode.MissingRequiredField,
                SourceDiagnosticCode.MissingRequiredField,
                "Token refresh could not start because the profile is missing a refresh token.",
                observedAt,
                new[] { new KeyValuePair<string, string?>("missingField", "refreshToken") });
        }

        using var httpRequest = BuildHttpRequest(request.RefreshToken);
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
                UsageTokenRefreshOutcome.Canceled,
                StatusAvailabilityCode.Error,
                SourceDiagnosticCode.Error,
                "Token refresh was canceled.",
                observedAt,
                new[] { new KeyValuePair<string, string?>("reason", "operation-canceled") });
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return CreateFailureResult(
                UsageTokenRefreshOutcome.Timeout,
                StatusAvailabilityCode.NetworkError,
                SourceDiagnosticCode.NetworkError,
                "Token refresh timed out.",
                observedAt,
                new[]
                {
                    new KeyValuePair<string, string?>("reason", "timeout"),
                    new KeyValuePair<string, string?>("timeoutSeconds", requestTimeout.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
                });
        }
        catch (HttpRequestException exception)
        {
            return CreateFailureResult(
                UsageTokenRefreshOutcome.NetworkError,
                StatusAvailabilityCode.NetworkError,
                SourceDiagnosticCode.NetworkError,
                "Token refresh failed due to a network error.",
                observedAt,
                new[]
                {
                    new KeyValuePair<string, string?>("reason", "http-request-exception"),
                    new KeyValuePair<string, string?>("exceptionType", exception.GetType().Name),
                });
        }

        using (httpResponse)
        {
            if (!httpResponse.IsSuccessStatusCode)
            {
                return CreateFailureResult(
                    UsageTokenRefreshOutcome.HttpError,
                    httpResponse.StatusCode == HttpStatusCode.Unauthorized
                        ? StatusAvailabilityCode.Unauthorized
                        : StatusAvailabilityCode.Error,
                    httpResponse.StatusCode == HttpStatusCode.Unauthorized
                        ? SourceDiagnosticCode.Unauthorized
                        : SourceDiagnosticCode.Error,
                    "Token refresh returned a non-success status code.",
                    observedAt,
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
                    UsageTokenRefreshOutcome.Canceled,
                    StatusAvailabilityCode.Error,
                    SourceDiagnosticCode.Error,
                    "Token refresh response read was canceled.",
                    observedAt,
                    new[] { new KeyValuePair<string, string?>("reason", "operation-canceled") });
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                return CreateFailureResult(
                    UsageTokenRefreshOutcome.Timeout,
                    StatusAvailabilityCode.NetworkError,
                    SourceDiagnosticCode.NetworkError,
                    "Token refresh response read timed out.",
                    observedAt,
                    new[]
                    {
                        new KeyValuePair<string, string?>("reason", "timeout"),
                        new KeyValuePair<string, string?>("timeoutSeconds", requestTimeout.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
                    });
            }

            try
            {
                return ParseRefreshResponse(responseBody, observedAt);
            }
            catch (JsonException exception)
            {
                return CreateFailureResult(
                    UsageTokenRefreshOutcome.MalformedResponse,
                    StatusAvailabilityCode.Malformed,
                    SourceDiagnosticCode.Malformed,
                    "Token refresh response JSON is malformed.",
                    observedAt,
                    new[]
                    {
                        new KeyValuePair<string, string?>("reason", "json-exception"),
                        new KeyValuePair<string, string?>("exceptionType", exception.GetType().Name),
                    },
                    detail: exception.Message);
            }
        }
    }

    private static HttpRequestMessage BuildHttpRequest(string refreshToken)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope,
        });

        var contentBytes = Encoding.UTF8.GetBytes(payload);
        var content = new ByteArrayContent(contentBytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, OAuthEndpoint)
        {
            Content = content,
        };

        return request;
    }

    private static UsageTokenRefreshResult ParseRefreshResponse(string responseBody, DateTimeOffset observedAt)
    {
        using var jsonDocument = JsonDocument.Parse(responseBody);
        if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
        {
            return CreateFailureResult(
                UsageTokenRefreshOutcome.MalformedResponse,
                StatusAvailabilityCode.Malformed,
                SourceDiagnosticCode.Malformed,
                "Token refresh response root must be a JSON object.",
                observedAt);
        }

        var root = jsonDocument.RootElement;
        var idToken = ReadNormalizedString(root, "id_token");
        var accessToken = ReadNormalizedString(root, "access_token");
        var refreshToken = ReadNormalizedString(root, "refresh_token");
        if (idToken is null && accessToken is null && refreshToken is null)
        {
            return CreateFailureResult(
                UsageTokenRefreshOutcome.MalformedResponse,
                StatusAvailabilityCode.Malformed,
                SourceDiagnosticCode.Malformed,
                "Token refresh response did not include any token fields.",
                observedAt,
                new[] { new KeyValuePair<string, string?>("requiredFields", "id_token|access_token|refresh_token") });
        }

        var diagnostics = new List<SourceDiagnostic>();
        var accountId = JwtAccountIdResolver.ExtractAccountId(idToken, observedAt, out var diagnostic);
        if (diagnostic is not null)
        {
            diagnostics.Add(diagnostic);
        }

        return new UsageTokenRefreshResult
        {
            Outcome = UsageTokenRefreshOutcome.Succeeded,
            Availability = StatusAvailability.Available(),
            Diagnostics = diagnostics,
            AccountId = accountId,
            IdToken = idToken,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            RetryOutcome = UsageRetryOutcome.NotAttempted,
            TokenUpdate = new TokenUpdateResult
            {
                Outcome = TokenUpdateOutcome.NotAttempted,
            },
        };
    }

    private static UsageTokenRefreshResult CreateFailureResult(
        UsageTokenRefreshOutcome outcome,
        StatusAvailabilityCode availabilityCode,
        SourceDiagnosticCode diagnosticCode,
        string summary,
        DateTimeOffset observedAt,
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

        return new UsageTokenRefreshResult
        {
            Outcome = outcome,
            Availability = StatusAvailability.Unavailable(availabilityCode),
            Diagnostics = new[] { diagnostic },
            RetryOutcome = UsageRetryOutcome.NotAttempted,
            TokenUpdate = new TokenUpdateResult
            {
                Outcome = TokenUpdateOutcome.NotAttempted,
            },
        };
    }

    private static string? ReadNormalizedString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        return propertyValue.ValueKind switch
        {
            JsonValueKind.String => JwtAccountIdResolver.NormalizeNonEmpty(propertyValue.GetString()),
            JsonValueKind.Null => null,
            _ => null,
        };
    }
}

internal static class JwtAccountIdResolver
{
    private const string OpenAiAuthClaimName = "https://api.openai.com/auth";

    public static string? ExtractAccountId(string? idToken)
    {
        return ExtractAccountId(idToken, observedAtUtc: null, out _);
    }

    public static string? NormalizeNonEmpty(string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return null;
        }

        var trimmed = accountId.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static string? ExtractAccountId(string? idToken, DateTimeOffset? observedAtUtc, out SourceDiagnostic? diagnostic)
    {
        diagnostic = null;
        var normalizedToken = NormalizeNonEmpty(idToken);
        if (normalizedToken is null)
        {
            return null;
        }

        var segments = normalizedToken.Split('.');
        if (segments.Length < 2 || string.IsNullOrWhiteSpace(segments[1]))
        {
            diagnostic = CreateMalformedJwtDiagnostic("Refresh response id_token is missing a decodable JWT payload segment.", observedAtUtc);
            return null;
        }

        byte[] payloadBytes;
        try
        {
            payloadBytes = DecodeBase64Url(segments[1]);
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            diagnostic = CreateMalformedJwtDiagnostic("Refresh response id_token JWT payload is malformed.", observedAtUtc, exception.Message);
            return null;
        }

        try
        {
            using var payloadDocument = JsonDocument.Parse(payloadBytes);
            if (payloadDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                diagnostic = CreateMalformedJwtDiagnostic("Refresh response id_token payload must decode to a JSON object.", observedAtUtc);
                return null;
            }

            var root = payloadDocument.RootElement;
            if (root.TryGetProperty(OpenAiAuthClaimName, out var nestedAuthElement))
            {
                if (nestedAuthElement.ValueKind == JsonValueKind.Object)
                {
                    return ReadNormalizedString(nestedAuthElement, "chatgpt_account_id")
                        ?? ReadNormalizedString(nestedAuthElement, "account_id");
                }

                if (nestedAuthElement.ValueKind != JsonValueKind.Null)
                {
                    diagnostic = CreateMalformedJwtDiagnostic(
                        $"Refresh response id_token claim '{OpenAiAuthClaimName}' must be a JSON object when present.",
                        observedAtUtc);
                    return null;
                }
            }

            return ReadNormalizedString(root, "organization_id")
                ?? ReadNormalizedString(root, "project_id")
                ?? ReadNormalizedString(root, "account_id");
        }
        catch (JsonException exception)
        {
            diagnostic = CreateMalformedJwtDiagnostic("Refresh response id_token payload JSON is malformed.", observedAtUtc, exception.Message);
            return null;
        }
    }

    private static string? ReadNormalizedString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        return propertyValue.ValueKind == JsonValueKind.String
            ? NormalizeNonEmpty(propertyValue.GetString())
            : null;
    }

    private static byte[] DecodeBase64Url(string payloadSegment)
    {
        var normalizedSegment = payloadSegment.Trim()
            .Replace('-', '+')
            .Replace('_', '/');

        var padding = normalizedSegment.Length % 4;
        if (padding == 1)
        {
            throw new InvalidOperationException("JWT payload segment has an invalid base64url length.");
        }

        if (padding > 0)
        {
            normalizedSegment = normalizedSegment.PadRight(normalizedSegment.Length + (4 - padding), '=');
        }

        return Convert.FromBase64String(normalizedSegment);
    }

    private static SourceDiagnostic CreateMalformedJwtDiagnostic(string summary, DateTimeOffset? observedAtUtc, string? detail = null)
    {
        return SourceDiagnostic.Create(
            SourceDiagnosticCode.Malformed,
            SourceDiagnosticSeverity.Warning,
            summary,
            detail: detail,
            context: new[]
            {
                new KeyValuePair<string, string?>("field", "id_token"),
            },
            observedAtUtc: observedAtUtc);
    }
}
