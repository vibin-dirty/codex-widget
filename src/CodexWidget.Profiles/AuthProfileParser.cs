using CodexWidget.Core;
using System.Text.Json;

namespace CodexWidget.Profiles;

public sealed class AuthProfileParser : IAuthProfileParser
{
    public AuthProfile Parse(AuthProfileParseInput input, DateTimeOffset? observedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var diagnostics = new List<SourceDiagnostic>();
        if (string.IsNullOrWhiteSpace(input.JsonContent))
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.Missing,
                SourceDiagnosticSeverity.Warning,
                "Auth source is missing.",
                input,
                observedAtUtc));

            return CreateResult(input, ProfileSourceParseState.Missing, diagnostics);
        }

        JsonDocument jsonDocument;
        try
        {
            jsonDocument = JsonDocument.Parse(input.JsonContent);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.Malformed,
                SourceDiagnosticSeverity.Warning,
                "Auth source JSON is malformed.",
                input,
                observedAtUtc,
                detail: exception.Message));

            return CreateResult(input, ProfileSourceParseState.Malformed, diagnostics);
        }

        using (jsonDocument)
        {
            if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.Malformed,
                    SourceDiagnosticSeverity.Warning,
                    "Auth source JSON root must be an object.",
                    input,
                    observedAtUtc));

                return CreateResult(input, ProfileSourceParseState.Malformed, diagnostics);
            }

            var root = jsonDocument.RootElement;
            var hasTokens = root.TryGetProperty("tokens", out var tokensElement);
            if (hasTokens)
            {
                if (tokensElement.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(CreateDiagnostic(
                        SourceDiagnosticCode.Malformed,
                        SourceDiagnosticSeverity.Warning,
                        "Auth source tokens field must be an object when present.",
                        input,
                        observedAtUtc));

                    return CreateResult(input, ProfileSourceParseState.Malformed, diagnostics);
                }

                var tokens = new AuthTokens
                {
                    AccountId = ReadNormalizedString(tokensElement, "account_id"),
                    IdToken = ReadNormalizedString(tokensElement, "id_token"),
                    AccessToken = ReadNormalizedString(tokensElement, "access_token"),
                    RefreshToken = ReadNormalizedString(tokensElement, "refresh_token"),
                };

                AddMissingFieldDiagnostics(input, observedAtUtc, diagnostics, tokens);

                return CreateResult(
                    input,
                    ProfileSourceParseState.Available,
                    diagnostics,
                    tokens: tokens);
            }

            var apiKey = ReadNormalizedString(root, "OPENAI_API_KEY");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.ApiKeyProfile,
                    SourceDiagnosticSeverity.Info,
                    "Auth source is an API-key profile.",
                    input,
                    observedAtUtc));

                return CreateResult(
                    input,
                    ProfileSourceParseState.Available,
                    diagnostics,
                    isApiKeyProfile: true,
                    apiKey: apiKey);
            }

            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.MissingRequiredField,
                SourceDiagnosticSeverity.Warning,
                "Auth source is missing both tokens and OPENAI_API_KEY.",
                input,
                observedAtUtc));

            return CreateResult(input, ProfileSourceParseState.Unavailable, diagnostics);
        }
    }

    private static AuthProfile CreateResult(
        AuthProfileParseInput input,
        ProfileSourceParseState parseState,
        IReadOnlyList<SourceDiagnostic> diagnostics,
        bool isApiKeyProfile = false,
        AuthTokens? tokens = null,
        string? apiKey = null)
    {
        return new AuthProfile
        {
            ProfileId = NormalizeNonEmpty(input.ProfileId),
            SourceKind = input.SourceKind,
            SourcePath = NormalizeNonEmpty(input.SourcePath),
            ParseState = parseState,
            IsApiKeyProfile = isApiKeyProfile,
            Tokens = tokens ?? new AuthTokens(),
            ApiKey = NormalizeNonEmpty(apiKey),
            Diagnostics = diagnostics,
        };
    }

    private static void AddMissingFieldDiagnostics(
        AuthProfileParseInput input,
        DateTimeOffset? observedAtUtc,
        ICollection<SourceDiagnostic> diagnostics,
        AuthTokens tokens)
    {
        AddMissingFieldDiagnosticIfNeeded("account_id", tokens.AccountId);
        AddMissingFieldDiagnosticIfNeeded("id_token", tokens.IdToken);
        AddMissingFieldDiagnosticIfNeeded("access_token", tokens.AccessToken);
        AddMissingFieldDiagnosticIfNeeded("refresh_token", tokens.RefreshToken);

        void AddMissingFieldDiagnosticIfNeeded(string fieldName, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.MissingRequiredField,
                SourceDiagnosticSeverity.Warning,
                $"Auth source is missing tokens.{fieldName}.",
                input,
                observedAtUtc,
                context:
                [
                    new KeyValuePair<string, string?>("field", $"tokens.{fieldName}"),
                ]));
        }
    }

    private static SourceDiagnostic CreateDiagnostic(
        SourceDiagnosticCode code,
        SourceDiagnosticSeverity severity,
        string summary,
        AuthProfileParseInput input,
        DateTimeOffset? observedAtUtc,
        string? detail = null,
        IEnumerable<KeyValuePair<string, string?>>? context = null)
    {
        var mergedContext = new List<KeyValuePair<string, string?>>
        {
            new("sourceKind", input.SourceKind.ToString()),
            new("profileId", input.ProfileId),
            new("sourcePath", input.SourcePath),
        };

        if (context is not null)
        {
            mergedContext.AddRange(context);
        }

        return SourceDiagnostic.Create(
            code,
            severity,
            summary,
            detail: detail,
            context: mergedContext,
            observedAtUtc: observedAtUtc);
    }

    private static string? ReadNormalizedString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        return propertyValue.ValueKind switch
        {
            JsonValueKind.String => NormalizeNonEmpty(propertyValue.GetString()),
            JsonValueKind.Null => null,
            _ => null,
        };
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
}
