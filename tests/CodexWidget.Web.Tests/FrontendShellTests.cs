using System.Net;
using System.Text.Json;
using CodexWidget.TestSupport;

namespace CodexWidget.Web.Tests;

[Collection(WebHostEnvironmentCollection.Name)]
public sealed class FrontendShellTests
{
    [Fact]
    public async Task GetFrontendOptions_ReturnsSafeFrontendContract()
    {
        var runtimeFactory = new RecordingRuntimeFactory();
        using var environment = new TemporaryEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["CodexWidgetWeb__AllowLanBinding"] = "true",
                ["CodexWidgetWeb__CodexProfilesHome"] = SyntheticSecurityFixtures.SyntheticUnixCodexPath,
                ["CodexWidgetWeb__EnableCors"] = "true",
                ["CodexWidgetWeb__AllowedCorsOrigins__0"] = "https://widget-ui.local",
            });
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/status/frontend-options");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.Equal(15, root.GetProperty("pollingIntervalSeconds").GetInt32());
        var scopes = root.GetProperty("supportedManualRefreshScopes").EnumerateArray().Select(element => element.GetString()).ToArray();
        Assert.Equal(new[] { "full", "usageOnly", "profileOnly" }, scopes);
        Assert.Equal(
            ["pollingIntervalSeconds", "supportedManualRefreshScopes"],
            root.EnumerateObject().Select(property => property.Name).OrderBy(static name => name).ToArray());

        Assert.False(root.TryGetProperty("bindUrls", out _));
        Assert.False(root.TryGetProperty("codexProfilesHome", out _));
        Assert.False(root.TryGetProperty("allowLanBinding", out _));
        Assert.False(root.TryGetProperty("allowedCorsOrigins", out _));
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(payload);
    }

    [Fact]
    public async Task GetRoot_ReturnsStaticFrontendShell()
    {
        var runtimeFactory = new RecordingRuntimeFactory();
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<main id=\"main-content\"", payload, StringComparison.Ordinal);
        Assert.Contains("id=\"status-board\"", payload, StringComparison.Ordinal);
        Assert.Contains("id=\"theme-toggle\"", payload, StringComparison.Ordinal);
        Assert.Contains("role=\"switch\"", payload, StringComparison.Ordinal);
        Assert.Contains("Codex Usage", payload, StringComparison.Ordinal);
        Assert.Contains("id=\"live-announcements\"", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"compact-overview\"", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"full-details\"", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"manual-refresh-button\"", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Refresh Controls", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Full Details", payload, StringComparison.Ordinal);
    }
}
