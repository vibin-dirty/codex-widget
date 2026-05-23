using CodexWidget.Core;
using System.Text.Json;

namespace CodexWidget.Profiles.Tests;

public sealed class ProfileAccessContractTests
{
    [Fact]
    public void ProfileSnapshot_Defaults_AreUtcAndCollectionSafe()
    {
        var snapshot = new ProfileSnapshot();

        Assert.Equal(DateTimeOffset.UnixEpoch, snapshot.CapturedAtUtc);
        Assert.Equal(TimeSpan.Zero, snapshot.CapturedAtUtc.Offset);
        Assert.Empty(snapshot.Profiles);
        Assert.Empty(snapshot.Sources);
        Assert.Empty(snapshot.Diagnostics);
        Assert.Null(snapshot.CurrentProfileId);
        Assert.Empty(snapshot.UsageCredentialReferences);
        Assert.NotNull(snapshot.Paths);
    }

    [Fact]
    public void SnapshotSerialization_DoesNotEmitSensitiveCredentialData()
    {
        const string accessToken = "synthetic-access-token-123456";
        const string refreshToken = "synthetic-refresh-token-123456";
        const string idToken = "synthetic-header.synthetic-payload.synthetic-signature";
        const string apiKey = "sk-synthetic-private-key-123456";
        const string rawPath = "/home/example/.codex/auth.json";

        var snapshot = new ProfileSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UnixEpoch,
            CurrentProfileId = "work",
            Diagnostics =
            [
                SourceDiagnostic.Create(
                    SourceDiagnosticCode.Error,
                    SourceDiagnosticSeverity.Warning,
                    "synthetic warning",
                    detail: $"Authorization: Bearer {accessToken}",
                    context: new[]
                    {
                        new KeyValuePair<string, string?>("authFilePath", rawPath),
                        new KeyValuePair<string, string?>("refreshToken", refreshToken),
                    },
                    observedAtUtc: DateTimeOffset.UnixEpoch),
            ],
            UsageCredentialReferences =
            [
                new ProfileUsageCredentialReference
                {
                    ProfileId = "work",
                    AuthKind = ProfileAuthKind.Login,
                    UsageEligibility = ProfileUsageEligibility.Eligible,
                    LoginName = "person@example.invalid",
                    SubscriptionTier = SubscriptionTier.Pro,
                    SourcePath = rawPath,
                    AccountId = "synthetic-account",
                    IdToken = idToken,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    ApiKey = apiKey,
                },
            ],
            CurrentAuthProfile = new AuthProfile
            {
                SourceKind = AuthProfileSourceKind.CurrentAuth,
                ParseState = ProfileSourceParseState.Available,
                Tokens = new AuthTokens
                {
                    AccountId = "synthetic-account",
                    IdToken = idToken,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                },
                ApiKey = apiKey,
                SourcePath = rawPath,
            },
        };

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Assert.DoesNotContain(accessToken, json, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, json, StringComparison.Ordinal);
        Assert.DoesNotContain(idToken, json, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, json, StringComparison.Ordinal);
        Assert.DoesNotContain(rawPath, json, StringComparison.Ordinal);
        Assert.DoesNotContain("usageCredentialReferences", json, StringComparison.Ordinal);
        Assert.DoesNotContain("currentAuthProfile", json, StringComparison.Ordinal);
        Assert.Contains("[redacted]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityKey_UsesValueEquality()
    {
        var left = new IdentityKey
        {
            PrincipalId = "synthetic-user",
            WorkspaceOrOrgId = "synthetic-workspace",
            PlanType = "pro",
        };

        var right = left with { };

        Assert.Equal(left, right);
    }
}
