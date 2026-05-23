using CodexWidget.Core;

namespace CodexWidget.Profiles.Tests;

public sealed class ProfileIdentityMatcherTests
{
    private readonly ProfileIdentityMatcher _matcher = new();

    [Fact]
    public void BuildIdentityKey_PrefersNestedOpenAiClaims()
    {
        var authProfile = new AuthProfile
        {
            SourceKind = AuthProfileSourceKind.SavedProfile,
            ParseState = ProfileSourceParseState.Available,
            Tokens = new AuthTokens
            {
                AccountId = "fallback-account",
                IdToken = SyntheticCodexHomeFixture.BuildSyntheticJwt(new Dictionary<string, object?>
                {
                    ["sub"] = "fallback-subject",
                    ["organization_id"] = "fallback-organization",
                    ["https://api.openai.com/auth"] = new Dictionary<string, object?>
                    {
                        ["chatgpt_user_id"] = "preferred-user",
                        ["user_id"] = "secondary-user",
                        ["chatgpt_account_id"] = "preferred-workspace",
                        ["chatgpt_plan_type"] = "ChatGPT_Pro",
                    },
                }),
            },
        };

        var identity = _matcher.BuildIdentityKey(authProfile);

        Assert.NotNull(identity);
        Assert.Equal("preferred-user", identity!.PrincipalId);
        Assert.Equal("preferred-workspace", identity.WorkspaceOrOrgId);
        Assert.Equal("chatgpt_pro", identity.PlanType);
    }

    [Fact]
    public void BuildIdentityKey_UsesDocumentedFallbackOrder()
    {
        var nestedFallback = _matcher.BuildIdentityKey(new AuthProfile
        {
            SourceKind = AuthProfileSourceKind.SavedProfile,
            ParseState = ProfileSourceParseState.Available,
            Tokens = new AuthTokens
            {
                AccountId = "account-nested-fallback",
                IdToken = SyntheticCodexHomeFixture.BuildSyntheticJwt(new Dictionary<string, object?>
                {
                    ["organization_id"] = "org-fallback",
                    ["https://api.openai.com/auth"] = new Dictionary<string, object?>
                    {
                        ["user_id"] = "nested-user-fallback",
                    },
                }),
            },
        });

        var tokenFallback = _matcher.BuildIdentityKey(new AuthProfile
        {
            SourceKind = AuthProfileSourceKind.SavedProfile,
            ParseState = ProfileSourceParseState.Available,
            Tokens = new AuthTokens
            {
                AccountId = "account-only",
            },
        });

        Assert.NotNull(nestedFallback);
        Assert.Equal("nested-user-fallback", nestedFallback!.PrincipalId);
        Assert.Equal("org-fallback", nestedFallback.WorkspaceOrOrgId);
        Assert.Equal("unknown", nestedFallback.PlanType);

        Assert.NotNull(tokenFallback);
        Assert.Equal("account-only", tokenFallback!.PrincipalId);
        Assert.Equal("account-only", tokenFallback.WorkspaceOrOrgId);
        Assert.Equal("unknown", tokenFallback.PlanType);
    }
}
