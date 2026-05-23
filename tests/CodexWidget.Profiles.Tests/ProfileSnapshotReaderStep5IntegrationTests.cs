using CodexWidget.Core;
using System.Diagnostics;
using System.Text.Json;

namespace CodexWidget.Profiles.Tests;

public sealed class ProfileSnapshotReaderStep5IntegrationTests
{
    private readonly ProfileSnapshotReader _reader = new();

    [Fact]
    public async Task ReadAsync_MissingCurrentAuth_KeepsSavedProfilesAndMarksCurrentAuthMissing()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        fixture.WriteSavedProfileJson("saved-a", CreateLoginAuthJson(
            "acct-saved-a",
            CreateJwt("saved-a@example.invalid", userId: "saved-a-user", accountId: "workspace-a", plan: "chatgpt_plus"),
            accessToken: "synthetic-access-token-saved-a"));
        fixture.WriteSavedProfileJson("saved-b", CreateLoginAuthJson(
            "acct-saved-b",
            CreateJwt("saved-b@example.invalid", userId: "saved-b-user", accountId: "workspace-b", plan: "chatgpt_pro"),
            accessToken: "synthetic-access-token-saved-b"));

        var snapshot = await ReadSnapshotAsync(fixture);

        Assert.Null(snapshot.CurrentProfileId);
        Assert.Equal(2, snapshot.Profiles.Count);
        Assert.All(snapshot.Profiles, profile => Assert.False(profile.Profile.IsCurrent));

        var currentAuthSource = Assert.Single(snapshot.Sources, source => source.Source == StatusSourceKind.CurrentAuth);
        Assert.Equal(SourceStatusState.Missing, currentAuthSource.State);
        Assert.Equal(StatusAvailabilityCode.Missing, currentAuthSource.Availability.Code);
    }

    [Fact]
    public async Task ReadAsync_UsesSavedProfiles_WhenProfilesIndexIsMissingOrMalformed()
    {
        using var fixtureMissing = new SyntheticCodexHomeFixture();
        fixtureMissing.WriteSavedProfileJson("saved", CreateLoginAuthJson(
            "acct-saved",
            CreateJwt("saved@example.invalid", userId: "saved-user", accountId: "workspace", plan: "chatgpt_plus"),
            accessToken: "synthetic-access-token-saved"));

        var missingIndexSnapshot = await ReadSnapshotAsync(fixtureMissing);
        var missingIndexProfile = Assert.Single(missingIndexSnapshot.Profiles);
        Assert.Equal("saved", missingIndexProfile.Profile.ProfileId);
        Assert.Equal("saved@example.invalid", missingIndexProfile.Profile.LoginName);
        Assert.Equal(SubscriptionTier.Plus, missingIndexProfile.Profile.SubscriptionTier);
        var missingIndexSource = Assert.Single(missingIndexSnapshot.Sources, source => source.Source == StatusSourceKind.ProfilesIndex);
        Assert.Equal(SourceStatusState.Missing, missingIndexSource.State);

        using var fixtureMalformed = new SyntheticCodexHomeFixture();
        fixtureMalformed.WriteSavedProfileJson("saved", CreateLoginAuthJson(
            "acct-saved",
            CreateJwt("saved@example.invalid", userId: "saved-user", accountId: "workspace", plan: "chatgpt_plus"),
            accessToken: "synthetic-access-token-saved"));
        fixtureMalformed.WriteProfilesIndexJson("""{"profiles":""");

        var malformedIndexSnapshot = await ReadSnapshotAsync(fixtureMalformed);
        var malformedIndexProfile = Assert.Single(malformedIndexSnapshot.Profiles);
        Assert.Equal("saved", malformedIndexProfile.Profile.ProfileId);
        Assert.Null(malformedIndexProfile.Profile.DisplayName);
        Assert.Equal("saved@example.invalid", malformedIndexProfile.Profile.LoginName);
        Assert.Equal(SubscriptionTier.Plus, malformedIndexProfile.Profile.SubscriptionTier);

        var malformedIndexSource = Assert.Single(malformedIndexSnapshot.Sources, source => source.Source == StatusSourceKind.ProfilesIndex);
        Assert.Equal(SourceStatusState.Malformed, malformedIndexSource.State);
        Assert.Equal(StatusAvailabilityCode.Malformed, malformedIndexSource.Availability.Code);
    }

    [Fact]
    public async Task ReadAsync_ReturnsUnavailableSnapshot_WhenCodexDirectoryIsMissing()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        Directory.Delete(fixture.CodexPath, recursive: true);

        var snapshot = await ReadSnapshotAsync(fixture);

        Assert.Empty(snapshot.Profiles);
        Assert.All(snapshot.Sources, source => Assert.Equal(SourceStatusState.Unavailable, source.State));
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Missing);
    }

    [Fact]
    public async Task ReadAsync_LockTimeoutUnderContention_IsBoundedAndRepeatable()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        using var blockingLock = new FileStream(
            fixture.ProfilesLockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            var snapshot = await _reader.ReadAsync(new ProfileSnapshotReadOptions
            {
                HomeResolution = fixture.CreateResolutionOptions(),
                LockAcquireTimeout = TimeSpan.FromMilliseconds(120),
                LockRetryInterval = TimeSpan.FromMilliseconds(10),
            });
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Lock timeout attempt {attempt + 1} exceeded bound: {stopwatch.Elapsed}.");
            Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Unavailable);
            Assert.All(snapshot.Sources, source => Assert.Equal(SourceStatusState.Unavailable, source.State));
        }
    }

    [Fact]
    public async Task ReadAsync_RedactsSensitiveDiagnostics_AndPublicSnapshotSerialization()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        const string accessToken = "synthetic-access-token-step5-1111";
        const string refreshToken = "synthetic-refresh-token-step5-2222";
        const string apiKey = "sk-synthetic-step5-secret-3333";
        var jwt = CreateJwt("step5@example.invalid", userId: "step5-user", accountId: "workspace-step5", plan: "chatgpt_pro");

        fixture.WriteCurrentAuthJson(CreateLoginAuthJson(
            "acct-step5-current",
            jwt,
            accessToken,
            refreshToken));
        fixture.WriteSavedProfileJson("bad", $"{{\"tokens\":\"{accessToken}\",\"OPENAI_API_KEY\":\"{apiKey}\",\"jwt\":\"{jwt}\"");
        fixture.WriteProfilesIndexJson("""
            {
              "profiles": {
                "bad": {
                  "label": "Bad",
                  "email": "bad@example.invalid",
                  "plan": "pro",
                  "is_api_key": false
                }
              }
            }
            """);

        var snapshot = await ReadSnapshotAsync(fixture);
        var rawSnapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Assert.NotEmpty(snapshot.Diagnostics);
        Assert.All(snapshot.Diagnostics, diagnostic =>
        {
            var flattened = FlattenDiagnostic(diagnostic);
            Assert.DoesNotContain(accessToken, flattened, StringComparison.Ordinal);
            Assert.DoesNotContain(refreshToken, flattened, StringComparison.Ordinal);
            Assert.DoesNotContain(apiKey, flattened, StringComparison.Ordinal);
            Assert.DoesNotContain(jwt, flattened, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.HomePath, flattened, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.CodexPath, flattened, StringComparison.Ordinal);
        });

        Assert.DoesNotContain(accessToken, rawSnapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, rawSnapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, rawSnapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain(jwt, rawSnapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.HomePath, rawSnapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.CodexPath, rawSnapshotJson, StringComparison.Ordinal);
        Assert.Contains(RedactionHelper.RedactedPathMarker, rawSnapshotJson, StringComparison.Ordinal);
    }

    private async Task<ProfileSnapshot> ReadSnapshotAsync(SyntheticCodexHomeFixture fixture)
    {
        return await _reader.ReadAsync(new ProfileSnapshotReadOptions
        {
            HomeResolution = fixture.CreateResolutionOptions(),
        });
    }

    private static string FlattenDiagnostic(SourceDiagnostic diagnostic)
    {
        return string.Join(" ",
            diagnostic.Summary,
            diagnostic.Detail ?? string.Empty,
            string.Join(" ", diagnostic.Context.Select(pair => pair.Value)));
    }

    private static string CreateLoginAuthJson(string accountId, string idToken, string accessToken, string refreshToken = "synthetic-refresh-token")
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
