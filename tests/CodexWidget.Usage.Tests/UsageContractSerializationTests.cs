using CodexWidget.Core;
using System.Text.Json;

namespace CodexWidget.Usage.Tests;

public sealed class UsageContractSerializationTests
{
    [Fact]
    public void UsageProfileRequestSerialization_DoesNotEmitSensitiveFields()
    {
        const string accessToken = "synthetic-access-token-123456";
        const string refreshToken = "synthetic-refresh-token-123456";
        const string idToken = "synthetic-header.synthetic-payload.synthetic-signature";
        const string sourcePath = "/home/example/.codex/auth.json";
        const string lockPath = "/home/example/.codex/profiles/profiles.lock";
        const string authorizationHeader = "Bearer synthetic-access-token-123456";

        var request = new UsageProfileRequest
        {
            ProfileId = "work",
            LoginName = "person@example.invalid",
            SubscriptionTier = SubscriptionTier.Pro,
            ChatGptBaseUrl = "https://chatgpt.com/backend-api",
            SourcePath = sourcePath,
            ProfilesLockPath = lockPath,
            AccountId = "acct_123",
            IdToken = idToken,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AuthorizationHeaderValue = authorizationHeader,
        };

        var json = Serialize(request);

        Assert.DoesNotContain(accessToken, json, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, json, StringComparison.Ordinal);
        Assert.DoesNotContain(idToken, json, StringComparison.Ordinal);
        Assert.DoesNotContain(sourcePath, json, StringComparison.Ordinal);
        Assert.DoesNotContain(lockPath, json, StringComparison.Ordinal);
        Assert.DoesNotContain(authorizationHeader, json, StringComparison.Ordinal);
        Assert.DoesNotContain("accountId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", json, StringComparison.Ordinal);
        Assert.DoesNotContain("refreshToken", json, StringComparison.Ordinal);
        Assert.DoesNotContain("authorizationHeaderValue", json, StringComparison.Ordinal);
        Assert.DoesNotContain("profilesLockPath", json, StringComparison.Ordinal);
        Assert.Contains("chatGptBaseUrl", json, StringComparison.Ordinal);
    }

    [Fact]
    public void UsageTokenRefreshAndTokenUpdateSerialization_DoesNotEmitSensitiveFields()
    {
        const string accessToken = "synthetic-access-token-123456";
        const string refreshToken = "synthetic-refresh-token-123456";
        const string idToken = "synthetic-header.synthetic-payload.synthetic-signature";
        const string sourcePath = "/home/example/.codex/auth.json";
        const string lockPath = "/home/example/.codex/profiles/profiles.lock";

        var refreshResult = new UsageTokenRefreshResult
        {
            Outcome = UsageTokenRefreshOutcome.Succeeded,
            Availability = StatusAvailability.Available(),
            AccountId = "acct_123",
            IdToken = idToken,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Diagnostics =
            [
                SourceDiagnostic.Create(
                    SourceDiagnosticCode.Error,
                    SourceDiagnosticSeverity.Warning,
                    "Synthetic refresh warning",
                    detail: $"Authorization: Bearer {accessToken}",
                    context: new[]
                    {
                        new KeyValuePair<string, string?>("sourcePath", sourcePath),
                        new KeyValuePair<string, string?>("refreshToken", refreshToken),
                    },
                    observedAtUtc: DateTimeOffset.UnixEpoch),
            ],
        };

        var updateRequest = new TokenUpdateRequest
        {
            ProfileId = "work",
            SourcePath = sourcePath,
            ProfilesLockPath = lockPath,
            AccountId = "acct_123",
            IdToken = idToken,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
        };

        var refreshJson = Serialize(refreshResult);
        var updateJson = Serialize(updateRequest);

        Assert.DoesNotContain(accessToken, refreshJson, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, refreshJson, StringComparison.Ordinal);
        Assert.DoesNotContain(idToken, refreshJson, StringComparison.Ordinal);
        Assert.DoesNotContain(sourcePath, refreshJson, StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, updateJson, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, updateJson, StringComparison.Ordinal);
        Assert.DoesNotContain(idToken, updateJson, StringComparison.Ordinal);
        Assert.DoesNotContain(sourcePath, updateJson, StringComparison.Ordinal);
        Assert.DoesNotContain(lockPath, updateJson, StringComparison.Ordinal);
        Assert.Contains("[redacted]", refreshJson, StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", updateJson, StringComparison.Ordinal);
        Assert.DoesNotContain("refreshToken", updateJson, StringComparison.Ordinal);
        Assert.DoesNotContain("idToken", updateJson, StringComparison.Ordinal);
        Assert.DoesNotContain("sourcePath", updateJson, StringComparison.Ordinal);
        Assert.DoesNotContain("profilesLockPath", updateJson, StringComparison.Ordinal);
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }
}
