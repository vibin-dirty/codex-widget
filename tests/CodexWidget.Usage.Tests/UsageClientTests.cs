using CodexWidget.Core;
using System.Net;
using System.Net.Http.Headers;

namespace CodexWidget.Usage.Tests;

public sealed class UsageClientTests
{
    [Fact]
    public async Task FetchAsync_SendsExpectedRequestAndUsesMapperResult()
    {
        const string accessToken = "synthetic-access-token-123456";
        const string accountId = "acct_123456";
        const string responseBody = "{\"rate_limit\":{}}";
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody),
        }));
        var mapper = new StubUsageResponseMapper((request, body, _) =>
        {
            Assert.Equal(responseBody, body);
            Assert.Equal("profile-work", request.ProfileId);
            return new UsageFetchResult
            {
                ProfileId = request.ProfileId,
                Outcome = UsageFetchOutcome.Succeeded,
                Availability = StatusAvailability.Available(),
                Diagnostics = Array.Empty<SourceDiagnostic>(),
            };
        });
        using var httpClient = new HttpClient(handler);
        var usageClient = new UsageClient(httpClient, new UsageEndpointResolver(), mapper);

        var result = await usageClient.FetchAsync(CreateRequest(accessToken: accessToken, accountId: accountId));

        Assert.Equal(UsageFetchOutcome.Succeeded, result.Outcome);
        Assert.Equal(UsageEndpointResolutionOutcome.Resolved, result.EndpointResolution.Outcome);
        Assert.Equal(new Uri("https://chatgpt.com/backend-api/wham/usage"), handler.LastRequestUri);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, mapper.CallCount);
        Assert.Equal("Bearer", handler.LastAuthorization?.Scheme);
        Assert.Equal(accessToken, handler.LastAuthorization?.Parameter);
        Assert.Equal(accountId, handler.GetSingleHeaderValue("ChatGPT-Account-Id"));
        Assert.Contains("codex-profiles", handler.LastUserAgentValues, StringComparer.Ordinal);
    }

    [Fact]
    public async Task FetchAsync_RejectedEndpoint_DoesNotCallHttpTransport()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP should not be called."));
        var mapper = new StubUsageResponseMapper((_, _, _) => throw new InvalidOperationException("Mapper should not be called."));
        using var httpClient = new HttpClient(handler);
        var usageClient = new UsageClient(httpClient, new UsageEndpointResolver(), mapper);
        var request = CreateRequest(chatGptBaseUrl: "https://malicious.example.invalid/backend-api");

        var result = await usageClient.FetchAsync(request);

        Assert.Equal(UsageFetchOutcome.EndpointRejected, result.Outcome);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, mapper.CallCount);
    }

    [Fact]
    public async Task FetchAsync_MissingAccessToken_DoesNotCallHttpTransport()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP should not be called."));
        var mapper = new StubUsageResponseMapper((_, _, _) => throw new InvalidOperationException("Mapper should not be called."));
        using var httpClient = new HttpClient(handler);
        var usageClient = new UsageClient(httpClient, new UsageEndpointResolver(), mapper);
        var request = CreateRequest(accessToken: null);

        var result = await usageClient.FetchAsync(request);

        Assert.Equal(UsageFetchOutcome.MissingRequiredProfileFields, result.Outcome);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, mapper.CallCount);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.MissingRequiredField);
        Assert.Contains(result.Diagnostics.SelectMany(diagnostic => diagnostic.Context), pair => pair.Key == "missingField" && pair.Value == "accessToken");
    }

    [Fact]
    public async Task FetchAsync_UnauthorizedStatus_ReturnsUnauthorizedWithoutMapper()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var mapper = new StubUsageResponseMapper((_, _, _) => throw new InvalidOperationException("Mapper should not be called."));
        using var httpClient = new HttpClient(handler);
        var usageClient = new UsageClient(httpClient, new UsageEndpointResolver(), mapper);

        var result = await usageClient.FetchAsync(CreateRequest());

        Assert.Equal(UsageFetchOutcome.Unauthorized, result.Outcome);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(0, mapper.CallCount);
    }

    [Fact]
    public async Task FetchAsync_NonSuccessStatus_ReturnsHttpErrorWithoutMapper()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var mapper = new StubUsageResponseMapper((_, _, _) => throw new InvalidOperationException("Mapper should not be called."));
        using var httpClient = new HttpClient(handler);
        var usageClient = new UsageClient(httpClient, new UsageEndpointResolver(), mapper);

        var result = await usageClient.FetchAsync(CreateRequest());

        Assert.Equal(UsageFetchOutcome.HttpError, result.Outcome);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(0, mapper.CallCount);
    }

    [Fact]
    public async Task FetchAsync_RequestTimeout_ReturnsTimeout()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var mapper = new StubUsageResponseMapper((_, _, _) => throw new InvalidOperationException("Mapper should not be called."));
        using var httpClient = new HttpClient(handler);
        var usageClient = new UsageClient(
            httpClient,
            new UsageEndpointResolver(),
            mapper,
            new UsageClientOptions { RequestTimeout = TimeSpan.FromMilliseconds(50) });

        var result = await usageClient.FetchAsync(CreateRequest());

        Assert.Equal(UsageFetchOutcome.Timeout, result.Outcome);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(0, mapper.CallCount);
    }

    [Fact]
    public async Task FetchAsync_CancellationTokenCancellation_ReturnsCanceled()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var mapper = new StubUsageResponseMapper((_, _, _) => throw new InvalidOperationException("Mapper should not be called."));
        using var httpClient = new HttpClient(handler);
        var usageClient = new UsageClient(
            httpClient,
            new UsageEndpointResolver(),
            mapper,
            new UsageClientOptions { RequestTimeout = TimeSpan.FromSeconds(5) });

        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var result = await usageClient.FetchAsync(CreateRequest(), cancellationTokenSource.Token);

        Assert.Equal(UsageFetchOutcome.Canceled, result.Outcome);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(0, mapper.CallCount);
    }

    [Fact]
    public async Task FetchAsync_NetworkException_ReturnsRedactedDiagnostics()
    {
        const string accessToken = "synthetic-access-token-abcdef";
        const string accountId = "acct_sensitive_987654";
        const string path = "/home/example/.codex/auth.json";

        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException(
            $"network error for token {accessToken} account {accountId} path {path}"));
        var mapper = new StubUsageResponseMapper((_, _, _) => throw new InvalidOperationException("Mapper should not be called."));
        using var httpClient = new HttpClient(handler);
        var usageClient = new UsageClient(httpClient, new UsageEndpointResolver(), mapper);

        var result = await usageClient.FetchAsync(CreateRequest(accessToken: accessToken, accountId: accountId));

        Assert.Equal(UsageFetchOutcome.NetworkError, result.Outcome);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(0, mapper.CallCount);
        var serializedDiagnostics = string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(diagnostic => $"{diagnostic.Summary}|{diagnostic.Detail}|{string.Join(",", diagnostic.Context.Select(pair => $"{pair.Key}:{pair.Value}"))}"));
        Assert.DoesNotContain(accessToken, serializedDiagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(accountId, serializedDiagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(path, serializedDiagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAsync_SuccessfulHttpWithUnknownMapperOutcome_ReturnsMalformedResponse()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        }));
        var mapper = new StubUsageResponseMapper((request, _, _) => new UsageFetchResult
        {
            ProfileId = request.ProfileId,
            Outcome = UsageFetchOutcome.Unknown,
            Availability = new StatusAvailability(StatusAvailabilityState.Unknown),
        });
        using var httpClient = new HttpClient(handler);
        var usageClient = new UsageClient(httpClient, new UsageEndpointResolver(), mapper);

        var result = await usageClient.FetchAsync(CreateRequest());

        Assert.Equal(UsageFetchOutcome.MalformedResponse, result.Outcome);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, mapper.CallCount);
    }

    private static UsageProfileRequest CreateRequest(
        string? chatGptBaseUrl = "https://chatgpt.com/backend-api",
        string? accessToken = "synthetic-access-token-123",
        string? accountId = "acct_123")
    {
        return new UsageProfileRequest
        {
            ProfileId = "profile-work",
            LoginName = "person@example.invalid",
            SubscriptionTier = SubscriptionTier.Pro,
            ChatGptBaseUrl = chatGptBaseUrl,
            AccessToken = accessToken,
            AccountId = accountId,
        };
    }

    private sealed class StubUsageResponseMapper(Func<UsageProfileRequest, string, DateTimeOffset, UsageFetchResult> map)
        : IUsageResponseMapper
    {
        private readonly Func<UsageProfileRequest, string, DateTimeOffset, UsageFetchResult> map = map;

        public int CallCount { get; private set; }

        public UsageFetchResult Map(UsageProfileRequest request, string responseBody, DateTimeOffset observedAtUtc)
        {
            CallCount++;
            return map(request, responseBody, observedAtUtc);
        }
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync = sendAsync;

        public int CallCount { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public AuthenticationHeaderValue? LastAuthorization { get; private set; }

        public IReadOnlyList<string> LastUserAgentValues { get; private set; } = Array.Empty<string>();

        public IReadOnlyDictionary<string, IReadOnlyList<string>> LastHeaders { get; private set; }
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        public string? GetSingleHeaderValue(string name)
        {
            if (!LastHeaders.TryGetValue(name, out var values) || values.Count == 0)
            {
                return null;
            }

            return values[0];
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;
            LastAuthorization = request.Headers.Authorization;
            LastUserAgentValues = request.Headers.UserAgent.Select(header => header.Product?.Name ?? header.Comment ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            LastHeaders = request.Headers
                .ToDictionary(
                    header => header.Key,
                    header => (IReadOnlyList<string>)header.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            return await sendAsync(request, cancellationToken);
        }
    }
}
