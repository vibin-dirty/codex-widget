using System.Net;
using System.Text.Json;
using CodexWidget.TestSupport;

namespace CodexWidget.Web.Tests;

[Collection(WebHostEnvironmentCollection.Name)]
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task HealthEndpoint_ReturnsOkStatus()
    {
        var runtimeFactory = new RecordingRuntimeFactory();
        await using var factory = new TestWebApplicationFactory(runtimeFactory);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", payload.RootElement.GetProperty("status").GetString());
        Assert.False(payload.RootElement.TryGetProperty("runtimeInitialization", out _));
        Assert.Single(payload.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task HealthEndpoint_RemainsLiveWhenRuntimeStartupFailsWithoutLeakingSecrets()
    {
        var scheduler = new RecordingStatusRefreshScheduler
        {
            StartException = new InvalidOperationException(
                $"{SyntheticSecurityFixtures.SyntheticBearerHeader}; {SyntheticSecurityFixtures.SyntheticRawAuthJson}; {SyntheticSecurityFixtures.SyntheticUnixCodexPath}"),
        };
        var runtimeFactory = new RecordingRuntimeFactory(
            options => TestRuntimeFactory.CreateRuntime(options, scheduler: scheduler));
        await using var factory = new TestWebApplicationFactory(runtimeFactory);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(responseBody);
        Assert.Equal("ok", payload.RootElement.GetProperty("status").GetString());
        Assert.Single(payload.RootElement.EnumerateObject());
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(responseBody);
    }
}
