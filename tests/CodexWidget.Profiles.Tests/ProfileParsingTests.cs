using CodexWidget.Core;

namespace CodexWidget.Profiles.Tests;

public sealed class ProfileParsingTests
{
    private readonly AuthProfileParser _authParser = new();
    private readonly ProfilesIndexParser _indexParser = new();
    private readonly ConfigTomlParser _configParser = new();

    [Fact]
    public void AuthParser_ParsesNormalLoginAuth()
    {
        var input = new AuthProfileParseInput
        {
            ProfileId = "work",
            SourceKind = AuthProfileSourceKind.SavedProfile,
            SourcePath = "/tmp/synthetic/.codex/profiles/work.json",
            JsonContent = SyntheticCodexHomeFixture.CreateSyntheticLoginAuthJson(
                accountId: "synthetic-account-work",
                idToken: "synthetic.header.signature",
                accessToken: "synthetic-access-token-work",
                refreshToken: "synthetic-refresh-token-work"),
        };

        var result = _authParser.Parse(input);

        Assert.Equal(ProfileSourceParseState.Available, result.ParseState);
        Assert.False(result.IsApiKeyProfile);
        Assert.Equal("synthetic-account-work", result.Tokens.AccountId);
        Assert.Equal("synthetic.header.signature", result.Tokens.IdToken);
        Assert.Equal("synthetic-access-token-work", result.Tokens.AccessToken);
        Assert.Equal("synthetic-refresh-token-work", result.Tokens.RefreshToken);
        Assert.Null(result.ApiKey);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Malformed);
    }

    [Fact]
    public void AuthParser_ParsesApiKeyProfile_WhenTokensAreAbsent()
    {
        var input = new AuthProfileParseInput
        {
            ProfileId = "api",
            SourceKind = AuthProfileSourceKind.SavedProfile,
            SourcePath = "/tmp/synthetic/.codex/profiles/api.json",
            JsonContent = SyntheticCodexHomeFixture.CreateSyntheticApiKeyAuthJson("sk-synthetic-api-key-profile"),
        };

        var result = _authParser.Parse(input);

        Assert.Equal(ProfileSourceParseState.Available, result.ParseState);
        Assert.True(result.IsApiKeyProfile);
        Assert.Equal("sk-synthetic-api-key-profile", result.ApiKey);
        Assert.Null(result.Tokens.AccountId);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.ApiKeyProfile);
    }

    [Fact]
    public void AuthParser_MarksUnavailable_WhenTokensAndApiKeyAreMissing()
    {
        var result = _authParser.Parse(new AuthProfileParseInput
        {
            SourceKind = AuthProfileSourceKind.CurrentAuth,
            SourcePath = "/tmp/synthetic/.codex/auth.json",
            JsonContent = """{"profile":"empty"}""",
        });

        Assert.Equal(ProfileSourceParseState.Unavailable, result.ParseState);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.MissingRequiredField);
    }

    [Fact]
    public void AuthParser_MarksMalformed_ForMalformedJson_AndMalformedTokensShape()
    {
        var malformedJson = _authParser.Parse(new AuthProfileParseInput
        {
            SourceKind = AuthProfileSourceKind.CurrentAuth,
            SourcePath = "/tmp/synthetic/.codex/auth.json",
            JsonContent = """{"tokens":""",
        });

        var malformedTokens = _authParser.Parse(new AuthProfileParseInput
        {
            SourceKind = AuthProfileSourceKind.CurrentAuth,
            SourcePath = "/tmp/synthetic/.codex/auth.json",
            JsonContent = """{"tokens":"bad-shape"}""",
        });

        Assert.Equal(ProfileSourceParseState.Malformed, malformedJson.ParseState);
        Assert.Contains(malformedJson.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Malformed);
        Assert.Equal(ProfileSourceParseState.Malformed, malformedTokens.ParseState);
        Assert.Contains(malformedTokens.Diagnostics, diagnostic => diagnostic.Summary.Contains("tokens field must be an object", StringComparison.Ordinal));
    }

    [Fact]
    public void IndexParser_ReportsMalformedEntries_ButPreservesValidMetadata()
    {
        const string json = """
            {
              "profiles": {
                "alpha": "malformed",
                "beta": {
                  "label": "Team Beta",
                  "email": "beta@example.invalid",
                  "plan": "plus",
                  "is_api_key": false
                }
              }
            }
            """;

        var result = _indexParser.Parse(new ProfilesIndexParseInput
        {
            SourcePath = "/tmp/synthetic/.codex/profiles/profiles.json",
            JsonContent = json,
        });

        Assert.Equal(ProfileSourceParseState.Available, result.ParseState);
        Assert.True(result.Entries.TryGetValue("alpha", out var alpha));
        Assert.Equal(ProfileSourceParseState.Malformed, alpha!.ParseState);
        Assert.True(result.Entries.TryGetValue("beta", out var beta));
        Assert.Equal("Team Beta", beta!.Label);
        Assert.Equal("beta@example.invalid", beta.Email);
        Assert.Equal("plus", beta.Plan);
        Assert.False(beta.IsApiKey);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Malformed);
    }

    [Fact]
    public void IndexParser_PreservesPartialMetadata_WhenFieldsAreMissing()
    {
        const string json = """
            {
              "profiles": {
                "partial": {
                  "label": "Partial Label"
                }
              }
            }
            """;

        var result = _indexParser.Parse(new ProfilesIndexParseInput
        {
            SourcePath = "/tmp/synthetic/.codex/profiles/profiles.json",
            JsonContent = json,
        });

        var partial = Assert.Single(result.Entries.Values);
        Assert.Equal("partial", partial.ProfileId);
        Assert.Equal("Partial Label", partial.Label);
        Assert.Null(partial.Email);
        Assert.Null(partial.Plan);
        Assert.Null(partial.IsApiKey);
    }

    [Fact]
    public void ConfigParser_HandlesAbsentModeAndMalformedToml()
    {
        var missing = _configParser.Parse(new ConfigTomlParseInput
        {
            SourcePath = "/tmp/synthetic/.codex/config.toml",
            TomlContent = null,
        });

        var malformed = _configParser.Parse(new ConfigTomlParseInput
        {
            SourcePath = "/tmp/synthetic/.codex/config.toml",
            TomlContent = "cli_auth_credentials_store_mode = [",
        });

        Assert.Equal(ProfileSourceParseState.Missing, missing.ParseState);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Missing);
        Assert.Equal(ProfileSourceParseState.Malformed, malformed.ParseState);
        Assert.Contains(malformed.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Malformed);
    }

    [Fact]
    public void ConfigParser_ParsesCredentialStoreMode_AndOptionalChatGptBaseUrl()
    {
        const string toml = """
            cli_auth_credentials_store_mode = "keychain"
            chatgpt_base_url = "https://chatgpt.example.invalid/backend-api"
            """;

        var result = _configParser.Parse(new ConfigTomlParseInput
        {
            SourcePath = "/tmp/synthetic/.codex/config.toml",
            TomlContent = toml,
        });

        Assert.Equal(ProfileSourceParseState.Available, result.ParseState);
        Assert.Equal("keychain", result.CredentialsStoreMode);
        Assert.Equal("https://chatgpt.example.invalid/backend-api", result.ChatGptBaseUrl);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ParserDiagnostics_AreRedacted_AndSourceStatusMappingReflectsParseState()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        var sensitivePath = Path.Combine(fixture.ProfilesDirectoryPath, "sk-super-secret", "profile.json");
        var accessToken = "synthetic-access-token-value-9999";

        var result = _authParser.Parse(new AuthProfileParseInput
        {
            ProfileId = "work",
            SourceKind = AuthProfileSourceKind.SavedProfile,
            SourcePath = sensitivePath,
            JsonContent = "{\"tokens\":{\"account_id\":\"synthetic-account\",\"id_token\":\"synthetic.id.token\",\"access_token\":\""
                + accessToken
                + "\",\"refresh_token\":\"\"}}",
        });

        var status = ProfileSourceStatusMapper.ToSourceStatus(
            StatusSourceKind.SavedProfileAuth,
            result.ParseState,
            result.Diagnostics);

        Assert.Equal(SourceStatusState.Available, status.State);
        Assert.Equal(StatusAvailabilityState.Available, status.Availability.State);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.MissingRequiredField);
        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.DoesNotContain(accessToken, diagnostic.Detail ?? string.Empty, StringComparison.Ordinal);
            var contextValues = string.Join(" ", diagnostic.Context.Values);
            Assert.DoesNotContain(sensitivePath, contextValues, StringComparison.Ordinal);
            Assert.Contains(RedactionHelper.RedactedPathMarker, contextValues, StringComparison.Ordinal);
        });
    }
}
