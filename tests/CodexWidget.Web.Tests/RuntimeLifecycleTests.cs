using Microsoft.Extensions.DependencyInjection;
using CodexWidget.TestSupport;
using System.Net;
using System.Text.Json;

namespace CodexWidget.Web.Tests;

[Collection(WebHostEnvironmentCollection.Name)]
public sealed class RuntimeLifecycleTests
{
    [Fact]
    public async Task HealthStatusEndpoint_ReportsPendingThenReadyInitializationState()
    {
        var schedulerStartCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSchedulerStartToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new RecordingStatusRefreshScheduler
        {
            StartCalledSignal = schedulerStartCalled,
            AllowStartToCompleteSignal = allowSchedulerStartToComplete,
        };
        var runtimeFactory = new RecordingRuntimeFactory(
            options => TestRuntimeFactory.CreateRuntime(options, scheduler: scheduler));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);

        using var client = factory.CreateClient();
        using (var livenessResponse = await client.GetAsync("/health"))
        {
            livenessResponse.EnsureSuccessStatusCode();
        }

        await schedulerStartCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var pendingResponse = await client.GetAsync("/health/status");
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        using (var pendingPayload = JsonDocument.Parse(await pendingResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("ok", pendingPayload.RootElement.GetProperty("status").GetString());
            Assert.Equal("pending", pendingPayload.RootElement.GetProperty("runtimeInitialization").GetString());
            Assert.Equal("starting", pendingPayload.RootElement.GetProperty("scheduler").GetProperty("state").GetString());
            Assert.Equal("unknown", pendingPayload.RootElement.GetProperty("refresh").GetProperty("staleness").GetString());
        }

        allowSchedulerStartToComplete.TrySetResult();
        using var readyPayload = await WaitForHealthStatusAsync(client, "ready");

        Assert.Equal("ok", readyPayload.RootElement.GetProperty("status").GetString());
        Assert.Equal("ready", readyPayload.RootElement.GetProperty("runtimeInitialization").GetString());
        Assert.Equal("running", readyPayload.RootElement.GetProperty("scheduler").GetProperty("state").GetString());
        Assert.NotEqual("unknown", readyPayload.RootElement.GetProperty("refresh").GetProperty("staleness").GetString());
        Assert.Equal(1, scheduler.StartCallCount);
    }

    [Fact]
    public async Task HealthStatusEndpoint_ReportsSafeRuntimeFailureWithoutLeakingSecrets()
    {
        var scheduler = new RecordingStatusRefreshScheduler
        {
            StartException = new InvalidOperationException(
                $"{SyntheticSecurityFixtures.SyntheticBearerHeader} from {SyntheticSecurityFixtures.SyntheticUnixCodexPath}; {SyntheticSecurityFixtures.SyntheticSessionId}"),
        };
        var runtimeFactory = new RecordingRuntimeFactory(
            options => TestRuntimeFactory.CreateRuntime(options, scheduler: scheduler));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"degraded\"", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("failed", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Runtime initialization failed", responseBody, StringComparison.Ordinal);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }

    [Fact]
    public async Task HealthStatusEndpoint_ReportsSchedulerRunningWhenEnabled()
    {
        var scheduler = new RecordingStatusRefreshScheduler();
        var runtimeFactory = new RecordingRuntimeFactory(
            options => TestRuntimeFactory.CreateRuntime(options, scheduler: scheduler));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ready", payload.RootElement.GetProperty("runtimeInitialization").GetString());
        Assert.Equal("running", payload.RootElement.GetProperty("scheduler").GetProperty("state").GetString());
        Assert.Equal(1, scheduler.StartCallCount);
    }

    [Fact]
    public async Task HealthStatusEndpoint_ReportsSchedulerDisabledWhenConfigured()
    {
        var runtimeFactory = new RecordingRuntimeFactory();
        using var environment = new TemporaryEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["CodexWidgetWeb__EnableScheduler"] = "false",
            });
        await using var factory = new TestWebApplicationFactory(runtimeFactory);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ready", payload.RootElement.GetProperty("runtimeInitialization").GetString());
        Assert.Equal("disabled", payload.RootElement.GetProperty("scheduler").GetProperty("state").GetString());
    }

    [Fact]
    public async Task HealthStatusEndpoint_ReportsDegradedSafeFailureWhenRuntimeReadFailsAfterReady()
    {
        var fault = new InvalidOperationException(
            $"{SyntheticSecurityFixtures.SyntheticBearerHeader}; {SyntheticSecurityFixtures.SyntheticRawAuthJson}; {SyntheticSecurityFixtures.SyntheticWindowsCodexPath}");
        var cacheService = new FaultingStatusCacheService(new CodexWidget.Core.StatusSnapshot(), fault);
        var runtimeFactory = new RecordingRuntimeFactory(options => TestRuntimeFactory.CreateRuntime(options, cacheService: cacheService));
        using var environment = new TemporaryEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["CodexWidgetWeb__EnableScheduler"] = "false",
            });
        await using var factory = new TestWebApplicationFactory(runtimeFactory);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/status");
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(responseBody);
        Assert.Equal("degraded", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal("ready", payload.RootElement.GetProperty("runtimeInitialization").GetString());
        Assert.Equal("disabled", payload.RootElement.GetProperty("scheduler").GetProperty("state").GetString());
        Assert.Equal("unknown", payload.RootElement.GetProperty("refresh").GetProperty("staleness").GetString());
        Assert.Contains(
            "Runtime request failed",
            payload.RootElement.GetProperty("runtimeFailure").GetString(),
            StringComparison.Ordinal);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }

    [Fact]
    public async Task HostShutdown_DisposesRuntimeOwnedResources()
    {
        var cacheService = new RecordingStatusCacheService(new CodexWidget.Core.StatusSnapshot());
        var scheduler = new RecordingStatusRefreshScheduler(cacheService);
        var ownedResource = new RecordingOwnedResource();
        var runtimeFactory = new RecordingRuntimeFactory(
            options => TestRuntimeFactory.CreateRuntime(
                options,
                cacheService,
                scheduler,
                ownedDisposables: [ownedResource]));
        var factory = new TestWebApplicationFactory(runtimeFactory);

        using (var client = factory.CreateClient())
        using (var response = await client.GetAsync("/health/status"))
        {
            response.EnsureSuccessStatusCode();
        }

        await factory.DisposeAsync();
        await WaitForDisposalAsync(cacheService, scheduler);

        Assert.Equal(1, cacheService.DisposeCallCount);
        Assert.Equal(1, scheduler.DisposeCallCount);
        Assert.Equal(1, ownedResource.DisposeCallCount);
    }

    private static async Task WaitForDisposalAsync(
        RecordingStatusCacheService cacheService,
        RecordingStatusRefreshScheduler scheduler)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (cacheService.DisposeCallCount > 0 && scheduler.DisposeCallCount > 0)
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    private static async Task<JsonDocument> WaitForHealthStatusAsync(HttpClient client, string expectedRuntimeInitialization)
    {
        var deadlineUtc = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow <= deadlineUtc)
        {
            using var response = await client.GetAsync("/health/status");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var payloadText = await response.Content.ReadAsStringAsync();
                var payload = JsonDocument.Parse(payloadText);
                var runtimeInitialization = payload.RootElement.GetProperty("runtimeInitialization").GetString();
                if (string.Equals(runtimeInitialization, expectedRuntimeInitialization, StringComparison.OrdinalIgnoreCase))
                {
                    return payload;
                }

                payload.Dispose();
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Timed out waiting for runtime initialization state '{expectedRuntimeInitialization}'.");
    }
}
