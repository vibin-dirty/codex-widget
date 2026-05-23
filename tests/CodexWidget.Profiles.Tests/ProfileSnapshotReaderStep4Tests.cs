using CodexWidget.Core;

namespace CodexWidget.Profiles.Tests;

public sealed class ProfileSnapshotReaderStep4Tests
{
    private readonly ProfileSnapshotReader _reader = new();

    [Theory]
    [InlineData("free", SubscriptionTier.Free)]
    [InlineData("chatgpt_plus", SubscriptionTier.Plus)]
    [InlineData("Pro", SubscriptionTier.Pro)]
    [InlineData("chatgpt-pro-lite", SubscriptionTier.ProLite)]
    public async Task ReadAsync_NormalizesSubscriptionTierFromJwt(string rawTier, SubscriptionTier expectedTier)
    {
        using var fixture = new SyntheticCodexHomeFixture();
        fixture.WriteCurrentAuthJson(CreateLoginAuthJson(
            "acct-tier",
            CreateJwt(
                email: "tier@example.invalid",
                plan: rawTier),
            accessToken: "access-tier"));

        var snapshot = await ReadSnapshotAsync(fixture);

        var profile = Assert.Single(snapshot.Profiles);
        Assert.Equal(expectedTier, profile.Profile.SubscriptionTier);
        Assert.Equal(ProfileUsageEligibility.Eligible, profile.Profile.UsageEligibility);
    }

    [Fact]
    public async Task ReadAsync_MarksMalformedJwtAsMalformedAuth_AndRedactsDiagnostics()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        var malformedJwt = "synthetic-header.bad!.synthetic-signature";
        fixture.WriteCurrentAuthJson(CreateLoginAuthJson(
            "acct-malformed",
            malformedJwt,
            accessToken: "access-malformed"));

        var snapshot = await ReadSnapshotAsync(fixture);

        var profile = Assert.Single(snapshot.Profiles);
        Assert.True(profile.Profile.IsCurrent);
        Assert.Equal(ProfileUsageEligibility.MalformedAuth, profile.Profile.UsageEligibility);
        Assert.Contains(profile.Diagnostics, diagnostic => diagnostic.Summary.Contains("JWT payload", StringComparison.Ordinal));
        Assert.All(profile.Diagnostics, diagnostic =>
        {
            Assert.DoesNotContain(malformedJwt, diagnostic.Detail ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(malformedJwt, string.Join(" ", diagnostic.Context.Values), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ReadAsync_ReportsNonObjectJwtPayloadAsMalformedAuth()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        var nonObjectJwt = SyntheticCodexHomeFixture.BuildSyntheticJwtFromPayloadJson("""["not","an","object"]""");
        fixture.WriteCurrentAuthJson(CreateLoginAuthJson(
            "acct-non-object",
            nonObjectJwt,
            accessToken: "access-non-object"));

        var snapshot = await ReadSnapshotAsync(fixture);

        var profile = Assert.Single(snapshot.Profiles);
        Assert.Equal(ProfileUsageEligibility.MalformedAuth, profile.Profile.UsageEligibility);
        Assert.Contains(profile.Diagnostics, diagnostic => diagnostic.Summary.Contains("JSON object", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_ReportsMissingJwtPayloadSegmentAsMalformedAuth()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        fixture.WriteCurrentAuthJson(CreateLoginAuthJson(
            "acct-missing-segment",
            "header-only",
            accessToken: "access-missing-segment"));

        var snapshot = await ReadSnapshotAsync(fixture);

        var profile = Assert.Single(snapshot.Profiles);
        Assert.Equal(ProfileUsageEligibility.MalformedAuth, profile.Profile.UsageEligibility);
        Assert.Contains(profile.Diagnostics, diagnostic => diagnostic.Summary.Contains("payload segment", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_IncludesUnsavedCurrentProfileFirst()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        fixture.WriteCurrentAuthJson(CreateLoginAuthJson(
            "acct-current",
            CreateJwt(
                email: "current@example.invalid",
                userId: "current-user",
                accountId: "workspace-current",
                plan: "chatgpt_plus"),
            accessToken: "access-current"));
        fixture.WriteSavedProfileJson("saved", CreateLoginAuthJson(
            "acct-saved",
            CreateJwt(
                email: "saved@example.invalid",
                userId: "saved-user",
                accountId: "workspace-saved",
                plan: "chatgpt_free"),
            accessToken: "access-saved"));

        var snapshot = await ReadSnapshotAsync(fixture);

        Assert.Null(snapshot.CurrentProfileId);
        Assert.Collection(snapshot.Profiles,
            current =>
            {
                Assert.True(current.Profile.IsCurrent);
                Assert.Null(current.Profile.ProfileId);
                Assert.Equal("current@example.invalid", current.Profile.LoginName);
            },
            saved =>
            {
                Assert.False(saved.Profile.IsCurrent);
                Assert.Equal("saved", saved.Profile.ProfileId);
            });
    }

    [Fact]
    public async Task ReadAsync_OmitsMatchingSavedProfileAndUsesMatchedSavedMetadata()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        var sharedJwt = CreateJwt(
            email: "match@example.invalid",
            userId: "match-user",
            accountId: "workspace-match",
            plan: "chatgpt_pro");

        fixture.WriteCurrentAuthJson(CreateLoginAuthJson("acct-match", sharedJwt, accessToken: "access-current"));
        fixture.WriteSavedProfileJson("beta", CreateLoginAuthJson("acct-match", sharedJwt, accessToken: "access-saved"));
        fixture.WriteSavedProfileJson("gamma", CreateLoginAuthJson(
            "acct-gamma",
            CreateJwt(
                email: "gamma@example.invalid",
                userId: "gamma-user",
                accountId: "workspace-gamma",
                plan: "chatgpt_free"),
            accessToken: "access-gamma"));
        fixture.WriteProfilesIndexJson("""
            {
              "profiles": {
                "beta": {
                  "label": "Team Beta",
                  "email": "match@example.invalid",
                  "plan": "pro",
                  "is_api_key": false
                },
                "gamma": {
                  "label": "Team Gamma",
                  "email": "gamma@example.invalid",
                  "plan": "free",
                  "is_api_key": false
                }
              }
            }
            """);

        var snapshot = await ReadSnapshotAsync(fixture);

        Assert.Equal("beta", snapshot.CurrentProfileId);
        Assert.Collection(snapshot.Profiles,
            current =>
            {
                Assert.True(current.Profile.IsCurrent);
                Assert.Equal("beta", current.Profile.ProfileId);
                Assert.Equal("Team Beta", current.Profile.DisplayName);
            },
            gamma =>
            {
                Assert.False(gamma.Profile.IsCurrent);
                Assert.Equal("gamma", gamma.Profile.ProfileId);
            });
        Assert.Equal(2, snapshot.UsageCredentialReferences.Count);
        Assert.Equal(fixture.CurrentAuthPath, snapshot.UsageCredentialReferences[0].SourcePath);
    }

    [Fact]
    public async Task ReadAsync_SelectsLexicographicallySmallestDuplicateSavedProfileId_AndKeepsOtherDuplicates()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        var sharedJwt = CreateJwt(
            email: "dup@example.invalid",
            userId: "dup-user",
            accountId: "workspace-dup",
            plan: "chatgpt_pro");

        fixture.WriteCurrentAuthJson(CreateLoginAuthJson("acct-dup", sharedJwt, accessToken: "access-current"));
        fixture.WriteSavedProfileJson("zzz", CreateLoginAuthJson("acct-dup", sharedJwt, accessToken: "access-zzz"));
        fixture.WriteSavedProfileJson("aaa", CreateLoginAuthJson("acct-dup", sharedJwt, accessToken: "access-aaa"));
        fixture.WriteSavedProfileJson("mmm", CreateLoginAuthJson(
            "acct-mmm",
            CreateJwt(
                email: "mmm@example.invalid",
                userId: "mmm-user",
                accountId: "workspace-mmm",
                plan: "chatgpt_free"),
            accessToken: "access-mmm"));

        var snapshot = await ReadSnapshotAsync(fixture);

        Assert.Equal("aaa", snapshot.CurrentProfileId);
        Assert.Collection(snapshot.Profiles,
            current =>
            {
                Assert.True(current.Profile.IsCurrent);
                Assert.Equal("aaa", current.Profile.ProfileId);
            },
            firstSaved => Assert.Equal("mmm", firstSaved.Profile.ProfileId),
            secondSaved => Assert.Equal("zzz", secondSaved.Profile.ProfileId));
    }

    [Fact]
    public async Task ReadAsync_ExcludesCurrentAuth_WhenCredentialStoreModeIsNotFile()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        fixture.WriteCurrentAuthJson(CreateLoginAuthJson(
            "acct-current",
            CreateJwt(
                email: "current@example.invalid",
                userId: "current-user",
                accountId: "workspace-current",
                plan: "chatgpt_plus"),
            accessToken: "access-current"));
        fixture.WriteSavedProfileJson("saved", CreateLoginAuthJson(
            "acct-saved",
            CreateJwt(
                email: "saved@example.invalid",
                userId: "saved-user",
                accountId: "workspace-saved",
                plan: "chatgpt_free"),
            accessToken: "access-saved"));
        fixture.WriteConfigToml("cli_auth_credentials_store_mode = \"keychain\"\n");

        var snapshot = await ReadSnapshotAsync(fixture);

        Assert.Null(snapshot.CurrentProfileId);
        var saved = Assert.Single(snapshot.Profiles);
        Assert.Equal("saved", saved.Profile.ProfileId);
        Assert.False(saved.Profile.IsCurrent);
        Assert.NotNull(snapshot.CurrentAuthProfile);
        Assert.Equal(ProfileSourceParseState.Unavailable, snapshot.CurrentAuthProfile!.ParseState);

        var currentAuthSource = Assert.Single(snapshot.Sources, source => source.Source == StatusSourceKind.CurrentAuth);
        Assert.Equal(SourceStatusState.Unavailable, currentAuthSource.State);
        Assert.Contains(currentAuthSource.Diagnostics, diagnostic => diagnostic.Summary.Contains("credential store mode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReadAsync_RecognizesApiKeyProfilesAsIneligible()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        fixture.WriteSavedProfileJson("api", SyntheticCodexHomeFixture.CreateSyntheticApiKeyAuthJson("sk-synthetic-api"));
        fixture.WriteProfilesIndexJson("""
            {
              "profiles": {
                "api": {
                  "label": "API Profile",
                  "plan": "plus",
                  "is_api_key": true
                }
              }
            }
            """);

        var snapshot = await ReadSnapshotAsync(fixture);

        var profile = Assert.Single(snapshot.Profiles);
        Assert.Equal("api", profile.Profile.ProfileId);
        Assert.Equal(ProfileAuthKind.ApiKey, profile.Profile.AuthKind);
        Assert.Equal(ProfileUsageEligibility.ApiKeyProfile, profile.Profile.UsageEligibility);
        Assert.Equal(SubscriptionTier.Plus, profile.Profile.SubscriptionTier);
        Assert.Empty(snapshot.UsageCredentialReferences);
    }

    [Fact]
    public async Task ReadAsync_ClassifiesMissingRequiredLoginFields()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        fixture.WriteSavedProfileJson("missing-access", CreateLoginAuthJson(
            "acct-access",
            CreateJwt(
                email: "access@example.invalid",
                userId: "access-user",
                accountId: "workspace-access",
                plan: "chatgpt_plus"),
            accessToken: ""));
        fixture.WriteSavedProfileJson("missing-account", CreateLoginAuthJson(
            "",
            CreateJwt(
                email: "account@example.invalid",
                userId: "account-user",
                accountId: "workspace-account",
                plan: "chatgpt_plus"),
            accessToken: "access-account"));
        fixture.WriteSavedProfileJson("missing-login", CreateLoginAuthJson(
            "acct-login",
            CreateJwt(
                email: null,
                userId: "login-user",
                accountId: "workspace-login",
                plan: "chatgpt_plus"),
            accessToken: "access-login"));
        fixture.WriteSavedProfileJson("missing-tier", CreateLoginAuthJson(
            "acct-tier",
            CreateJwt(
                email: "tier@example.invalid",
                userId: "tier-user",
                accountId: "workspace-tier",
                plan: null),
            accessToken: "access-tier"));

        var snapshot = await ReadSnapshotAsync(fixture);
        var profilesById = snapshot.Profiles.ToDictionary(profile => profile.Profile.ProfileId!, StringComparer.Ordinal);

        Assert.Equal(ProfileUsageEligibility.MissingAccessToken, profilesById["missing-access"].Profile.UsageEligibility);
        Assert.Equal(ProfileUsageEligibility.MissingAccountId, profilesById["missing-account"].Profile.UsageEligibility);
        Assert.Equal(ProfileUsageEligibility.MissingLoginName, profilesById["missing-login"].Profile.UsageEligibility);
        Assert.Equal(ProfileUsageEligibility.MissingSubscriptionTier, profilesById["missing-tier"].Profile.UsageEligibility);
    }

    [Fact]
    public async Task ReadAsync_PreservesIndexMetadataForMalformedSavedProfile()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        fixture.WriteSavedProfileJson("broken", """{"tokens":""");
        fixture.WriteProfilesIndexJson("""
            {
              "profiles": {
                "broken": {
                  "label": "Broken Profile",
                  "email": "broken@example.invalid",
                  "plan": "pro",
                  "is_api_key": false
                }
              }
            }
            """);

        var snapshot = await ReadSnapshotAsync(fixture);

        var profile = Assert.Single(snapshot.Profiles);
        Assert.Equal("broken", profile.Profile.ProfileId);
        Assert.Equal("Broken Profile", profile.Profile.DisplayName);
        Assert.Equal("broken@example.invalid", profile.Profile.LoginName);
        Assert.Equal(SubscriptionTier.Pro, profile.Profile.SubscriptionTier);
        Assert.Equal(ProfileUsageEligibility.MalformedAuth, profile.Profile.UsageEligibility);
    }

    private async Task<ProfileSnapshot> ReadSnapshotAsync(SyntheticCodexHomeFixture fixture)
    {
        return await _reader.ReadAsync(new ProfileSnapshotReadOptions
        {
            HomeResolution = fixture.CreateResolutionOptions(),
        });
    }

    private static string CreateLoginAuthJson(string accountId, string idToken, string accessToken, string refreshToken = "refresh-token")
    {
        return SyntheticCodexHomeFixture.CreateSyntheticLoginAuthJson(
            accountId: accountId,
            idToken: idToken,
            accessToken: accessToken,
            refreshToken: refreshToken);
    }

    private static string CreateJwt(string? email, string? userId = null, string? accountId = null, string? plan = null)
    {
        var claims = new Dictionary<string, object?>();
        if (email is not null)
        {
            claims["email"] = email;
        }

        var openAiAuthClaims = new Dictionary<string, object?>();
        if (userId is not null)
        {
            openAiAuthClaims["chatgpt_user_id"] = userId;
        }

        if (accountId is not null)
        {
            openAiAuthClaims["chatgpt_account_id"] = accountId;
        }

        if (plan is not null)
        {
            openAiAuthClaims["chatgpt_plan_type"] = plan;
        }

        if (openAiAuthClaims.Count > 0)
        {
            claims["https://api.openai.com/auth"] = openAiAuthClaims;
        }

        return SyntheticCodexHomeFixture.BuildSyntheticJwt(claims);
    }
}
