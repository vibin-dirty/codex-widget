using CodexWidget.Core;
using CodexWidget.TestSupport;

namespace CodexWidget.Web.Tests;

public sealed class StatusRefreshScopeParserTests
{
    [Theory]
    [InlineData("full", StatusRefreshScope.Full)]
    [InlineData("FULL", StatusRefreshScope.Full)]
    [InlineData("usageOnly", StatusRefreshScope.UsageOnly)]
    [InlineData("UsageOnly", StatusRefreshScope.UsageOnly)]
    [InlineData("profileOnly", StatusRefreshScope.ProfileOnly)]
    [InlineData("PROFILEONLY", StatusRefreshScope.ProfileOnly)]
    public void TryParse_AcceptsSupportedScopes(string scope, StatusRefreshScope expected)
    {
        var success = StatusRefreshScopeParser.TryParse(scope, out var parsedScope);

        Assert.True(success);
        Assert.Equal(expected, parsedScope);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("profile")]
    [InlineData("usage")]
    [InlineData("everything")]
    public void TryParse_RejectsUnsupportedScopes(string? scope)
    {
        var success = StatusRefreshScopeParser.TryParse(scope, out _);

        Assert.False(success);
    }

    [Theory]
    [InlineData(StatusRefreshScope.Full, "full")]
    [InlineData(StatusRefreshScope.UsageOnly, "usageOnly")]
    [InlineData(StatusRefreshScope.ProfileOnly, "profileOnly")]
    public void ToContractValue_UsesStableContractNames(StatusRefreshScope scope, string expected)
    {
        Assert.Equal(expected, StatusRefreshScopeParser.ToContractValue(scope));
    }

    [Fact]
    public void InvalidRefreshScopeError_UsesSafeContractShape()
    {
        var error = WebApiErrors.InvalidRefreshScope(SyntheticSecurityFixtures.SyntheticBearerHeader);

        Assert.Equal(WebApiErrorCodes.InvalidRequest, error.Error.Code);
        Assert.Equal(StatusRefreshScopeParser.SupportedScopes, error.Error.AllowedRefreshScopes);
        Assert.Contains("Use one of", error.Error.Message, StringComparison.Ordinal);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(error.Error.Message);
    }

    [Fact]
    public void RefreshRequest_DefaultsToFullScope()
    {
        var request = new StatusRefreshRequest();

        Assert.Equal(StatusRefreshScopeParser.Full, request.Scope);
        Assert.True(request.TryResolveScope(out var parsedScope));
        Assert.Equal(StatusRefreshScope.Full, parsedScope);
    }
}
