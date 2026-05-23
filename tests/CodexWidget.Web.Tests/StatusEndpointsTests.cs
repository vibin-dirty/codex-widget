using CodexWidget.Core;
using CodexWidget.TestSupport;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace CodexWidget.Web.Tests;

[Collection(WebHostEnvironmentCollection.Name)]
public sealed class StatusEndpointsTests
{
    [Fact]
    public async Task GetPresentation_ReturnsSanitizedPresentationState()
    {
        var cacheService = new RecordingStatusCacheService(CreateSensitiveSnapshot());
        var runtimeFactory = new RecordingRuntimeFactory(options => TestRuntimeFactory.CreateRuntime(options, cacheService: cacheService));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/status/presentation");
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("selectedView", out _));
        Assert.True(root.TryGetProperty("selectedViewSummaryText", out _));
        Assert.True(root.TryGetProperty("compact", out _));
        Assert.True(root.TryGetProperty("full", out _));
        Assert.True(root.TryGetProperty("refresh", out _));
        Assert.Contains("[redacted]", responseBody, StringComparison.Ordinal);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }

    [Fact]
    public async Task GetSnapshot_ReturnsSafeSnapshotContract()
    {
        var cacheService = new RecordingStatusCacheService(CreateSensitiveSnapshot());
        var runtimeFactory = new RecordingRuntimeFactory(options => TestRuntimeFactory.CreateRuntime(options, cacheService: cacheService));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/status/snapshot");
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("capturedAtUtc", out _));
        Assert.True(root.TryGetProperty("currentProfileId", out _));
        Assert.True(root.TryGetProperty("refreshState", out _));
        Assert.True(root.TryGetProperty("profiles", out var profiles));
        Assert.True(root.TryGetProperty("sources", out _));
        Assert.Single(profiles.EnumerateArray());
        Assert.DoesNotContain("loginName", responseBody, StringComparison.Ordinal);
        Assert.Contains("[redacted-path]", responseBody, StringComparison.Ordinal);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }

    [Fact]
    public async Task GetRefresh_ReturnsSafeRefreshMetadata()
    {
        var cacheService = new RecordingStatusCacheService(CreateSensitiveSnapshot());
        var runtimeFactory = new RecordingRuntimeFactory(options => TestRuntimeFactory.CreateRuntime(options, cacheService: cacheService));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/status/refresh");
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("capturedAtUtc", out _));
        Assert.True(root.TryGetProperty("snapshotAge", out _));
        Assert.Equal("failed", root.GetProperty("latestOutcome").GetString());
        Assert.True(root.TryGetProperty("latestSafeFailureSummary", out var failureSummary));
        Assert.Contains("Refresh failure", failureSummary.GetString(), StringComparison.Ordinal);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }

    [Theory]
    [InlineData(StatusRefreshScopeParser.Full, StatusRefreshScope.Full)]
    [InlineData(StatusRefreshScopeParser.UsageOnly, StatusRefreshScope.UsageOnly)]
    [InlineData(StatusRefreshScopeParser.ProfileOnly, StatusRefreshScope.ProfileOnly)]
    public async Task PostRefresh_WithSupportedScope_RefreshesRuntimeAndReturnsSanitizedPresentation(
        string scope,
        StatusRefreshScope expectedScope)
    {
        var cacheService = new RecordingStatusCacheService(CreateSensitiveSnapshot());
        var runtimeFactory = new RecordingRuntimeFactory(options => TestRuntimeFactory.CreateRuntime(options, cacheService: cacheService));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/status/refresh", new { scope });
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, cacheService.RefreshCallCount);
        Assert.Equal(StatusRefreshReason.Manual, cacheService.LastRefreshReason);
        Assert.Equal(expectedScope, cacheService.LastRefreshScope);
        Assert.True(cacheService.LastRefreshTokenCanBeCanceled);
        Assert.Contains("\"selectedView\"", responseBody, StringComparison.Ordinal);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }

    [Fact]
    public async Task PostRefresh_WithoutRequestBody_DefaultsToFullScope()
    {
        var cacheService = new RecordingStatusCacheService(CreateSensitiveSnapshot());
        var runtimeFactory = new RecordingRuntimeFactory(options => TestRuntimeFactory.CreateRuntime(options, cacheService: cacheService));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/status/refresh");

        using var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(StatusRefreshScope.Full, cacheService.LastRefreshScope);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }

    [Fact]
    public async Task PostRefresh_WithInvalidScope_ReturnsBadRequestWithSafeErrorBody()
    {
        var cacheService = new RecordingStatusCacheService(CreateSensitiveSnapshot());
        var runtimeFactory = new RecordingRuntimeFactory(options => TestRuntimeFactory.CreateRuntime(options, cacheService: cacheService));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/status/refresh", new { scope = SyntheticSecurityFixtures.SyntheticBearerHeader });
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(responseBody);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(WebApiErrorCodes.InvalidRequest, error.GetProperty("code").GetString());
        Assert.Equal(3, error.GetProperty("allowedRefreshScopes").GetArrayLength());
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }

    [Fact]
    public async Task PostRefresh_WithMalformedJson_ReturnsBadRequestWithSafeErrorBody()
    {
        var cacheService = new RecordingStatusCacheService(CreateSensitiveSnapshot());
        var runtimeFactory = new RecordingRuntimeFactory(options => TestRuntimeFactory.CreateRuntime(options, cacheService: cacheService));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();
        using var content = new StringContent(
            "{\"scope\":\"full\",\"auth\":\"" + SyntheticSecurityFixtures.SyntheticRawAuthJson,
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync("/api/status/refresh", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(responseBody);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(WebApiErrorCodes.InvalidRequest, error.GetProperty("code").GetString());
        Assert.Contains("Use one of", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }

    [Fact]
    public async Task PostRefresh_WhenAnotherManualRefreshIsRunning_ReturnsConflict()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRefreshToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var cacheService = new RecordingStatusCacheService(CreateSensitiveSnapshot())
        {
            RefreshHandler = async (_, _, cancellationToken) =>
            {
                refreshStarted.TrySetResult();
                await allowRefreshToFinish.Task.WaitAsync(cancellationToken);
                return CreateSensitiveSnapshot();
            },
        };

        var runtimeFactory = new RecordingRuntimeFactory(options => TestRuntimeFactory.CreateRuntime(options, cacheService: cacheService));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();

        var firstRefreshTask = client.PostAsJsonAsync("/api/status/refresh", new { scope = StatusRefreshScopeParser.Full });
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var conflictResponse = await client.PostAsJsonAsync("/api/status/refresh", new { scope = StatusRefreshScopeParser.UsageOnly });
        var conflictBody = await conflictResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        using (var conflictJson = JsonDocument.Parse(conflictBody))
        {
            var error = conflictJson.RootElement.GetProperty("error");
            Assert.Equal(WebApiErrorCodes.RefreshConflict, error.GetProperty("code").GetString());
            Assert.Contains("Refresh failure", error.GetProperty("failureSummary").GetString(), StringComparison.Ordinal);
        }
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(conflictBody);

        allowRefreshToFinish.TrySetResult();

        using var firstRefreshResponse = await firstRefreshTask;
        Assert.Equal(HttpStatusCode.OK, firstRefreshResponse.StatusCode);
    }

    [Fact]
    public async Task PostRefresh_WhenRuntimeCancelsWork_ReturnsSafeFailureAndReleasesCoordinator()
    {
        var refreshAttempt = 0;

        var cacheService = new RecordingStatusCacheService(CreateSensitiveSnapshot())
        {
            RefreshHandler = (_, _, cancellationToken) =>
            {
                refreshAttempt++;
                if (refreshAttempt == 1)
                {
                    throw new OperationCanceledException(
                        $"{SyntheticSecurityFixtures.SyntheticBearerHeader}; {SyntheticSecurityFixtures.SyntheticRawAuthJson}; {SyntheticSecurityFixtures.SyntheticUnixCodexPath}",
                        cancellationToken);
                }

                return Task.FromResult(CreateSensitiveSnapshot());
            },
        };

        var runtimeFactory = new RecordingRuntimeFactory(options => TestRuntimeFactory.CreateRuntime(options, cacheService: cacheService));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();
        using var canceledResponse = await client.PostAsJsonAsync("/api/status/refresh", new { scope = StatusRefreshScopeParser.Full });
        var canceledBody = await canceledResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, canceledResponse.StatusCode);
        using (var canceledDocument = JsonDocument.Parse(canceledBody))
        {
            var error = canceledDocument.RootElement.GetProperty("error");
            Assert.Equal(WebApiErrorCodes.RuntimeFailed, error.GetProperty("code").GetString());
        }
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(canceledBody);

        using var followupResponse = await client.PostAsJsonAsync("/api/status/refresh", new { scope = StatusRefreshScopeParser.UsageOnly });
        Assert.Equal(HttpStatusCode.OK, followupResponse.StatusCode);
        Assert.Equal(2, cacheService.RefreshCallCount);
        Assert.Equal(StatusRefreshScope.UsageOnly, cacheService.LastRefreshScope);
    }

    [Fact]
    public async Task PostRefresh_WhenRuntimeThrows_ReturnsSafeRuntimeFailureBody()
    {
        var cacheService = new RecordingStatusCacheService(CreateSensitiveSnapshot())
        {
            RefreshException = new InvalidOperationException(
                $"{SyntheticSecurityFixtures.SyntheticBearerHeader}; {SyntheticSecurityFixtures.SyntheticRawAuthJson}; {SyntheticSecurityFixtures.SyntheticUnixCodexPath}; {SyntheticSecurityFixtures.SyntheticCookieHeader}"),
        };

        var runtimeFactory = new RecordingRuntimeFactory(options => TestRuntimeFactory.CreateRuntime(options, cacheService: cacheService));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/status/refresh", new { scope = StatusRefreshScopeParser.Full });
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using (var document = JsonDocument.Parse(responseBody))
        {
            var error = document.RootElement.GetProperty("error");
            Assert.Equal(WebApiErrorCodes.RuntimeFailed, error.GetProperty("code").GetString());
        }

        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }

    [Theory]
    [InlineData("GET", "/api/status/presentation")]
    [InlineData("GET", "/api/status/snapshot")]
    [InlineData("GET", "/api/status/refresh")]
    [InlineData("POST", "/api/status/refresh")]
    public async Task StatusEndpoints_WhenRuntimeStartupFails_ReturnStartupFailedResponse(
        string method,
        string path)
    {
        var scheduler = new RecordingStatusRefreshScheduler
        {
            StartException = new InvalidOperationException(
                $"{SyntheticSecurityFixtures.SyntheticBearerHeader}; {SyntheticSecurityFixtures.SyntheticRawAuthJson}; {SyntheticSecurityFixtures.SyntheticWindowsCodexPath}; {SyntheticSecurityFixtures.SyntheticSessionId}"),
        };
        var runtimeFactory = new RecordingRuntimeFactory(
            options => TestRuntimeFactory.CreateRuntime(options, scheduler: scheduler));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using (var document = JsonDocument.Parse(responseBody))
        {
            var error = document.RootElement.GetProperty("error");
            Assert.Equal(WebApiErrorCodes.StartupFailed, error.GetProperty("code").GetString());
        }

        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }

    [Theory]
    [InlineData("/api/status/presentation")]
    [InlineData("/api/status/snapshot")]
    [InlineData("/api/status/refresh")]
    public async Task StatusReadEndpoints_WhenRuntimeReadFailsAfterStartup_ReturnSafeRuntimeFailureBody(string path)
    {
        var fault = new InvalidOperationException(
            $"{SyntheticSecurityFixtures.SyntheticBearerHeader}; {SyntheticSecurityFixtures.SyntheticRawAuthJson}; {SyntheticSecurityFixtures.SyntheticUnixCodexPath}");
        var cacheService = new FaultingStatusCacheService(CreateSensitiveSnapshot(), fault);
        var runtimeFactory = new RecordingRuntimeFactory(options => TestRuntimeFactory.CreateRuntime(options, cacheService: cacheService));
        using var environment = new TemporaryEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["CodexWidgetWeb__EnableScheduler"] = "false",
            });
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var document = JsonDocument.Parse(responseBody);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(WebApiErrorCodes.RuntimeFailed, error.GetProperty("code").GetString());
        Assert.Equal("The runtime could not complete the request.", error.GetProperty("message").GetString());
        Assert.Contains("Runtime request failed", error.GetProperty("failureSummary").GetString(), StringComparison.Ordinal);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }

    private static StatusSnapshot CreateSensitiveSnapshot()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero);
        return new StatusSnapshot
        {
            CapturedAtUtc = nowUtc,
            CurrentProfileId = "work",
            RefreshState = new StatusRefreshState
            {
                Reason = StatusRefreshReason.Manual,
                Scope = StatusRefreshScope.Full,
                Outcome = StatusRefreshOutcome.Failed,
                Failure = SourceDiagnostic.Create(
                    SourceDiagnosticCode.TokenRefreshFailed,
                    SourceDiagnosticSeverity.Error,
                    SyntheticSecurityFixtures.SyntheticBearerHeader,
                    detail: SyntheticSecurityFixtures.SyntheticRawAuthJson,
                    context:
                    [
                        new KeyValuePair<string, string?>("accessToken", SyntheticSecurityFixtures.SyntheticAccessToken),
                        new KeyValuePair<string, string?>("refreshToken", SyntheticSecurityFixtures.SyntheticRefreshToken),
                        new KeyValuePair<string, string?>("idToken", SyntheticSecurityFixtures.SyntheticIdToken),
                        new KeyValuePair<string, string?>("authPath", SyntheticSecurityFixtures.SyntheticUnixCodexPath),
                        new KeyValuePair<string, string?>("cookieHeader", SyntheticSecurityFixtures.SyntheticCookieHeader),
                    ],
                    observedAtUtc: nowUtc),
            },
            Profiles =
            [
                new ProfileStatus
                {
                    Profile = new ProfileDescriptor
                    {
                        ProfileId = "work",
                        DisplayName = "Work",
                        LoginName = "person@example.invalid",
                        IsCurrent = true,
                        SubscriptionTier = SubscriptionTier.Pro,
                        AuthKind = ProfileAuthKind.Login,
                        UsageEligibility = ProfileUsageEligibility.Eligible,
                        SourceStatus = new SourceStatus
                        {
                            Source = StatusSourceKind.CurrentAuth,
                            State = SourceStatusState.Error,
                            Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Error, SyntheticSecurityFixtures.SyntheticRawCodexContent),
                        },
                    },
                    Diagnostics =
                    [
                        SourceDiagnostic.Create(
                            SourceDiagnosticCode.Error,
                            SourceDiagnosticSeverity.Warning,
                            SyntheticSecurityFixtures.SyntheticRawAuthJson,
                            detail: SyntheticSecurityFixtures.SyntheticBearerHeader,
                            context:
                            [
                                new KeyValuePair<string, string?>("apiKey", SyntheticSecurityFixtures.SyntheticApiKey),
                                new KeyValuePair<string, string?>("rawCodexContent", SyntheticSecurityFixtures.SyntheticRawCodexContent),
                                new KeyValuePair<string, string?>("sessionId", SyntheticSecurityFixtures.SyntheticSessionId),
                            ],
                            observedAtUtc: nowUtc),
                    ],
                },
            ],
            Sources =
            [
                new SourceStatus
                {
                    Source = StatusSourceKind.ConfigToml,
                    State = SourceStatusState.Error,
                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Error, SyntheticSecurityFixtures.SyntheticWindowsCodexPath),
                    Diagnostics =
                    [
                        SourceDiagnostic.Create(
                            SourceDiagnosticCode.Error,
                            SourceDiagnosticSeverity.Error,
                            SyntheticSecurityFixtures.SyntheticBearerHeader,
                            detail: SyntheticSecurityFixtures.SyntheticRawAuthJson,
                            context:
                            [
                                new KeyValuePair<string, string?>("token", SyntheticSecurityFixtures.SyntheticBearerToken),
                            ],
                            observedAtUtc: nowUtc),
                    ],
                },
            ],
        };
    }
}
