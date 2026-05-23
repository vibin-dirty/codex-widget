using CodexWidget.Core;
using System.Text.Json;

namespace CodexWidget.Profiles;

public sealed class ProfileIdentityMatcher : IProfileIdentityMatcher
{
    private const string OpenAiAuthClaimName = "https://api.openai.com/auth";
    private const string UnknownWorkspaceOrOrgId = "unknown";
    private const string ApiKeyPlanType = "key";

    public IdentityKey? BuildIdentityKey(AuthProfile authProfile)
    {
        ArgumentNullException.ThrowIfNull(authProfile);
        return Resolve(authProfile).Identity;
    }

    public bool Matches(IdentityKey? left, IdentityKey? right)
    {
        return left is not null
            && right is not null
            && left == right;
    }

    internal AuthProfileResolution Resolve(AuthProfile authProfile, DateTimeOffset? observedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(authProfile);

        if (authProfile.ParseState == ProfileSourceParseState.Malformed)
        {
            return new AuthProfileResolution
            {
                AuthKind = InferAuthKind(authProfile),
                UsageEligibility = ProfileUsageEligibility.MalformedAuth,
                LoginName = authProfile.IndexEntry?.Email,
                SubscriptionTier = NormalizeSubscriptionTier(authProfile.IndexEntry?.Plan),
            };
        }

        if (authProfile.ParseState == ProfileSourceParseState.Unavailable
            && authProfile.Diagnostics.Any(diagnostic => diagnostic.Summary.Contains("credential store mode", StringComparison.OrdinalIgnoreCase)))
        {
            return new AuthProfileResolution
            {
                AuthKind = ProfileAuthKind.CredentialsUnavailable,
                UsageEligibility = ProfileUsageEligibility.CredentialsUnavailable,
                LoginName = authProfile.IndexEntry?.Email,
                SubscriptionTier = NormalizeSubscriptionTier(authProfile.IndexEntry?.Plan),
            };
        }

        if (authProfile.ParseState is ProfileSourceParseState.Missing or ProfileSourceParseState.Unavailable or ProfileSourceParseState.Error)
        {
            return new AuthProfileResolution
            {
                AuthKind = InferAuthKind(authProfile),
                UsageEligibility = ProfileUsageEligibility.SourceUnavailable,
                LoginName = authProfile.IndexEntry?.Email,
                SubscriptionTier = NormalizeSubscriptionTier(authProfile.IndexEntry?.Plan),
            };
        }

        if (authProfile.IsApiKeyProfile)
        {
            return new AuthProfileResolution
            {
                AuthKind = ProfileAuthKind.ApiKey,
                UsageEligibility = ProfileUsageEligibility.ApiKeyProfile,
                SubscriptionTier = NormalizeSubscriptionTier(authProfile.IndexEntry?.Plan),
                Identity = BuildApiKeyIdentity(authProfile.Tokens.AccountId),
            };
        }

        var diagnostics = new List<SourceDiagnostic>();
        var jwtClaims = DecodeJwtClaims(authProfile, observedAtUtc, diagnostics);
        var loginName = GetJwtClaim(jwtClaims?.Root, "email");
        var normalizedTier = NormalizeSubscriptionTier(
            GetOpenAiAuthClaim(jwtClaims?.OpenAiAuthClaims, "chatgpt_plan_type")
            ?? authProfile.IndexEntry?.Plan);

        var identity = BuildLoginIdentity(authProfile, jwtClaims);
        var usageEligibility = jwtClaims is null
            && !string.IsNullOrWhiteSpace(authProfile.Tokens.IdToken)
            && diagnostics.Count > 0
                ? ProfileUsageEligibility.MalformedAuth
                : ClassifyLoginEligibility(authProfile, loginName, normalizedTier);

        return new AuthProfileResolution
        {
            AuthKind = ProfileAuthKind.Login,
            UsageEligibility = usageEligibility,
            Identity = identity,
            LoginName = loginName ?? authProfile.IndexEntry?.Email,
            HasDecodedLoginName = !string.IsNullOrWhiteSpace(loginName),
            SubscriptionTier = normalizedTier,
            Diagnostics = diagnostics,
        };
    }

    internal static SubscriptionTier NormalizeSubscriptionTier(string? rawTier)
    {
        if (string.IsNullOrWhiteSpace(rawTier))
        {
            return SubscriptionTier.Unknown;
        }

        var normalized = rawTier.Trim().ToLowerInvariant();
        if (normalized.StartsWith("chatgpt_", StringComparison.Ordinal))
        {
            normalized = normalized["chatgpt_".Length..];
        }
        else if (normalized.StartsWith("chatgpt-", StringComparison.Ordinal))
        {
            normalized = normalized["chatgpt-".Length..];
        }

        normalized = normalized.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        return normalized switch
        {
            "free" => SubscriptionTier.Free,
            "plus" => SubscriptionTier.Plus,
            "pro" => SubscriptionTier.Pro,
            "prolite" => SubscriptionTier.ProLite,
            _ => SubscriptionTier.Unknown,
        };
    }

    private static ProfileAuthKind InferAuthKind(AuthProfile authProfile)
    {
        if (authProfile.IsApiKeyProfile || authProfile.IndexEntry?.IsApiKey == true)
        {
            return ProfileAuthKind.ApiKey;
        }

        if (authProfile.ParseState == ProfileSourceParseState.Unavailable
            && authProfile.Diagnostics.Any(diagnostic => diagnostic.Summary.Contains("credential store mode", StringComparison.OrdinalIgnoreCase)))
        {
            return ProfileAuthKind.CredentialsUnavailable;
        }

        return authProfile.Tokens.IdToken is not null
            || authProfile.Tokens.AccessToken is not null
            || authProfile.Tokens.RefreshToken is not null
            || authProfile.Tokens.AccountId is not null
            ? ProfileAuthKind.Login
            : ProfileAuthKind.Unknown;
    }

    private static ProfileUsageEligibility ClassifyLoginEligibility(
        AuthProfile authProfile,
        string? decodedLoginName,
        SubscriptionTier subscriptionTier)
    {
        if (string.IsNullOrWhiteSpace(authProfile.Tokens.AccessToken))
        {
            return ProfileUsageEligibility.MissingAccessToken;
        }

        if (string.IsNullOrWhiteSpace(authProfile.Tokens.AccountId))
        {
            return ProfileUsageEligibility.MissingAccountId;
        }

        if (string.IsNullOrWhiteSpace(decodedLoginName))
        {
            return ProfileUsageEligibility.MissingLoginName;
        }

        if (subscriptionTier == SubscriptionTier.Unknown)
        {
            return ProfileUsageEligibility.MissingSubscriptionTier;
        }

        return ProfileUsageEligibility.Eligible;
    }

    private static IdentityKey? BuildApiKeyIdentity(string? accountId)
    {
        var normalizedAccountId = NormalizeNonEmpty(accountId);
        if (normalizedAccountId is null)
        {
            return null;
        }

        return new IdentityKey
        {
            PrincipalId = normalizedAccountId,
            WorkspaceOrOrgId = normalizedAccountId,
            PlanType = ApiKeyPlanType,
        };
    }

    private static IdentityKey? BuildLoginIdentity(AuthProfile authProfile, JwtClaimsSnapshot? jwtClaims)
    {
        var principalId = GetOpenAiAuthClaim(jwtClaims?.OpenAiAuthClaims, "chatgpt_user_id")
            ?? GetOpenAiAuthClaim(jwtClaims?.OpenAiAuthClaims, "user_id")
            ?? GetJwtClaim(jwtClaims?.Root, "sub")
            ?? NormalizeNonEmpty(authProfile.Tokens.AccountId);

        if (principalId is null)
        {
            return null;
        }

        return new IdentityKey
        {
            PrincipalId = principalId,
            WorkspaceOrOrgId = GetOpenAiAuthClaim(jwtClaims?.OpenAiAuthClaims, "chatgpt_account_id")
                ?? GetJwtClaim(jwtClaims?.Root, "organization_id")
                ?? GetJwtClaim(jwtClaims?.Root, "project_id")
                ?? NormalizeNonEmpty(authProfile.Tokens.AccountId)
                ?? UnknownWorkspaceOrOrgId,
            PlanType = GetOpenAiAuthClaim(jwtClaims?.OpenAiAuthClaims, "chatgpt_plan_type")?.ToLowerInvariant()
                ?? "unknown",
        };
    }

    private static JwtClaimsSnapshot? DecodeJwtClaims(
        AuthProfile authProfile,
        DateTimeOffset? observedAtUtc,
        ICollection<SourceDiagnostic> diagnostics)
    {
        var idToken = NormalizeNonEmpty(authProfile.Tokens.IdToken);
        if (idToken is null)
        {
            return null;
        }

        var segments = idToken.Split('.');
        if (segments.Length < 2 || string.IsNullOrWhiteSpace(segments[1]))
        {
            diagnostics.Add(CreateJwtDiagnostic(
                "Auth id_token is missing a decodable JWT payload segment.",
                authProfile,
                observedAtUtc));
            return null;
        }

        byte[] payloadBytes;
        try
        {
            payloadBytes = DecodeBase64Url(segments[1]);
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            diagnostics.Add(CreateJwtDiagnostic(
                "Auth id_token JWT payload is malformed.",
                authProfile,
                observedAtUtc,
                detail: exception.Message));
            return null;
        }

        JsonDocument payloadDocument;
        try
        {
            payloadDocument = JsonDocument.Parse(payloadBytes);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateJwtDiagnostic(
                "Auth id_token JWT payload JSON is malformed.",
                authProfile,
                observedAtUtc,
                detail: exception.Message));
            return null;
        }

        using (payloadDocument)
        {
            if (payloadDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(CreateJwtDiagnostic(
                    "Auth id_token JWT payload must decode to a JSON object.",
                    authProfile,
                    observedAtUtc));
                return null;
            }

            var rootClone = payloadDocument.RootElement.Clone();
            JsonElement? openAiAuthClaims = null;
            if (rootClone.TryGetProperty(OpenAiAuthClaimName, out var nestedAuthElement))
            {
                if (nestedAuthElement.ValueKind == JsonValueKind.Object)
                {
                    openAiAuthClaims = nestedAuthElement.Clone();
                }
                else if (nestedAuthElement.ValueKind != JsonValueKind.Null)
                {
                    diagnostics.Add(CreateJwtDiagnostic(
                        $"Auth id_token claim '{OpenAiAuthClaimName}' must be a JSON object when present.",
                        authProfile,
                        observedAtUtc));
                }
            }

            return new JwtClaimsSnapshot
            {
                Root = rootClone,
                OpenAiAuthClaims = openAiAuthClaims,
            };
        }
    }

    private static byte[] DecodeBase64Url(string payloadSegment)
    {
        var normalizedSegment = payloadSegment.Trim()
            .Replace('-', '+')
            .Replace('_', '/');

        var padding = normalizedSegment.Length % 4;
        if (padding == 1)
        {
            throw new InvalidOperationException("JWT payload segment has an invalid base64url length.");
        }

        if (padding > 0)
        {
            normalizedSegment = normalizedSegment.PadRight(normalizedSegment.Length + (4 - padding), '=');
        }

        return Convert.FromBase64String(normalizedSegment);
    }

    private static string? GetJwtClaim(JsonElement? element, string propertyName)
    {
        if (element is not JsonElement jsonElement || jsonElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!jsonElement.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        return propertyValue.ValueKind == JsonValueKind.String
            ? NormalizeNonEmpty(propertyValue.GetString())
            : null;
    }

    private static string? GetOpenAiAuthClaim(JsonElement? openAiAuthClaims, string propertyName)
    {
        return GetJwtClaim(openAiAuthClaims, propertyName);
    }

    private static SourceDiagnostic CreateJwtDiagnostic(
        string summary,
        AuthProfile authProfile,
        DateTimeOffset? observedAtUtc,
        string? detail = null)
    {
        return SourceDiagnostic.Create(
            SourceDiagnosticCode.Malformed,
            SourceDiagnosticSeverity.Warning,
            summary,
            detail: detail,
            context:
            [
                new KeyValuePair<string, string?>("sourceKind", authProfile.SourceKind.ToString()),
                new KeyValuePair<string, string?>("profileId", authProfile.ProfileId),
                new KeyValuePair<string, string?>("sourcePath", authProfile.SourcePath),
                new KeyValuePair<string, string?>("field", "tokens.id_token"),
            ],
            observedAtUtc: observedAtUtc);
    }

    private static string? NormalizeNonEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private sealed record JwtClaimsSnapshot
    {
        public JsonElement Root { get; init; }

        public JsonElement? OpenAiAuthClaims { get; init; }
    }
}

internal sealed record AuthProfileResolution
{
    public ProfileAuthKind AuthKind { get; init; } = ProfileAuthKind.Unknown;

    public ProfileUsageEligibility UsageEligibility { get; init; } = ProfileUsageEligibility.Unknown;

    public IdentityKey? Identity { get; init; }

    public string? LoginName { get; init; }

    public bool HasDecodedLoginName { get; init; }

    public SubscriptionTier SubscriptionTier { get; init; } = SubscriptionTier.Unknown;

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();
}
