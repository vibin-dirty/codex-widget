using CodexWidget.Core;
using System.Text.Json;

namespace CodexWidget.Usage.Tests;

public sealed class UsageEndpointResolverTests
{
    private readonly UsageEndpointResolver resolver = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_UsesDefaultBaseUrlWhenInputMissing(string? configuredBaseUrl)
    {
        var result = resolver.Resolve(configuredBaseUrl, DateTimeOffset.UnixEpoch);

        Assert.Equal(UsageEndpointResolutionOutcome.Resolved, result.Outcome);
        Assert.Equal("https://chatgpt.com/backend-api", result.BaseUri?.AbsoluteUri.TrimEnd('/'));
        Assert.Equal("https://chatgpt.com/backend-api/wham/usage", result.EndpointUri?.AbsoluteUri);
    }

    [Fact]
    public void Resolve_TrimTrailingSlash()
    {
        var result = resolver.Resolve("https://chatgpt.com/backend-api/", DateTimeOffset.UnixEpoch);

        Assert.Equal(UsageEndpointResolutionOutcome.Resolved, result.Outcome);
        Assert.Equal("https://chatgpt.com/backend-api", result.BaseUri?.AbsoluteUri.TrimEnd('/'));
        Assert.Equal("https://chatgpt.com/backend-api/wham/usage", result.EndpointUri?.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://chatgpt.com", "https://chatgpt.com/backend-api", "https://chatgpt.com/backend-api/wham/usage")]
    [InlineData("https://chat.openai.com", "https://chat.openai.com/backend-api", "https://chat.openai.com/backend-api/wham/usage")]
    public void Resolve_AppendsBackendApiForRootChatHosts(string configuredBaseUrl, string expectedBase, string expectedEndpoint)
    {
        var result = resolver.Resolve(configuredBaseUrl, DateTimeOffset.UnixEpoch);

        Assert.Equal(UsageEndpointResolutionOutcome.Resolved, result.Outcome);
        Assert.Equal(expectedBase, result.BaseUri?.AbsoluteUri.TrimEnd('/'));
        Assert.Equal(expectedEndpoint, result.EndpointUri?.AbsoluteUri);
    }

    [Fact]
    public void Resolve_AllowsNestedBackendApiPath()
    {
        var result = resolver.Resolve("https://chatgpt.com/custom/backend-api/v2/", DateTimeOffset.UnixEpoch);

        Assert.Equal(UsageEndpointResolutionOutcome.Resolved, result.Outcome);
        Assert.Equal("https://chatgpt.com/custom/backend-api/v2", result.BaseUri?.AbsoluteUri.TrimEnd('/'));
        Assert.Equal("https://chatgpt.com/custom/backend-api/v2/wham/usage", result.EndpointUri?.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://localhost:8765", "http://localhost:8765/api/codex/usage")]
    [InlineData("https://127.0.0.1:44300/", "https://127.0.0.1:44300/api/codex/usage")]
    [InlineData("http://[::1]:8080", "http://[::1]:8080/api/codex/usage")]
    public void Resolve_AllowsLoopbackHostsOverHttpOrHttps(string configuredBaseUrl, string expectedEndpoint)
    {
        var result = resolver.Resolve(configuredBaseUrl, DateTimeOffset.UnixEpoch);

        Assert.Equal(UsageEndpointResolutionOutcome.Resolved, result.Outcome);
        Assert.Equal(expectedEndpoint, result.EndpointUri?.AbsoluteUri);
    }

    [Fact]
    public void Resolve_RejectsNonLoopbackHttp()
    {
        var result = resolver.Resolve("http://example.com/backend-api", DateTimeOffset.UnixEpoch);

        Assert.Equal(UsageEndpointResolutionOutcome.Rejected, result.Outcome);
        Assert.Null(result.EndpointUri);
        Assert.Equal(SourceStatusState.Unavailable, result.SourceStatus.State);
        Assert.Equal(StatusAvailabilityCode.Unavailable, result.SourceStatus.Availability.Code);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Unavailable);
    }

    [Fact]
    public void Resolve_RejectsArbitraryHttpsHosts()
    {
        var result = resolver.Resolve("https://example.com/backend-api", DateTimeOffset.UnixEpoch);

        Assert.Equal(UsageEndpointResolutionOutcome.Rejected, result.Outcome);
        Assert.Null(result.EndpointUri);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Unavailable);
    }

    [Theory]
    [InlineData("not-a-valid-url")]
    [InlineData("://missing-scheme.example")]
    [InlineData("https://chatgpt.com/backend-api?token=secret")]
    public void Resolve_RejectsMalformedUrls(string configuredBaseUrl)
    {
        var result = resolver.Resolve(configuredBaseUrl, DateTimeOffset.UnixEpoch);

        Assert.Equal(UsageEndpointResolutionOutcome.Malformed, result.Outcome);
        Assert.Null(result.EndpointUri);
        Assert.Equal(SourceStatusState.Malformed, result.SourceStatus.State);
        Assert.Equal(StatusAvailabilityCode.Malformed, result.SourceStatus.Availability.Code);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Malformed);
    }

    [Fact]
    public void Resolve_RejectionDiagnosticsDoNotEchoRawInputOrSecrets()
    {
        const string raw = "https://chatgpt.com/private/team/environment";
        var result = resolver.Resolve(raw, DateTimeOffset.UnixEpoch);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Assert.Equal(UsageEndpointResolutionOutcome.Rejected, result.Outcome);
        Assert.DoesNotContain(raw, json, StringComparison.Ordinal);
        Assert.Contains("[redacted-path]", json, StringComparison.Ordinal);
    }
}
