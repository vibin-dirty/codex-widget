using CodexWidget.Core;
using System.Text.Json;

namespace CodexWidget.Profiles;

public sealed class ProfilesIndexParser : IProfilesIndexParser
{
    public ProfilesIndexParseResult Parse(ProfilesIndexParseInput input, DateTimeOffset? observedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var diagnostics = new List<SourceDiagnostic>();
        if (string.IsNullOrWhiteSpace(input.JsonContent))
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.Missing,
                SourceDiagnosticSeverity.Info,
                "profiles.json is missing.",
                input,
                observedAtUtc));

            return new ProfilesIndexParseResult
            {
                ParseState = ProfileSourceParseState.Missing,
                Diagnostics = diagnostics,
            };
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
                "profiles.json is malformed.",
                input,
                observedAtUtc,
                detail: exception.Message));

            return new ProfilesIndexParseResult
            {
                ParseState = ProfileSourceParseState.Malformed,
                Diagnostics = diagnostics,
            };
        }

        using (jsonDocument)
        {
            if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.Malformed,
                    SourceDiagnosticSeverity.Warning,
                    "profiles.json root must be an object.",
                    input,
                    observedAtUtc));

                return new ProfilesIndexParseResult
                {
                    ParseState = ProfileSourceParseState.Malformed,
                    Diagnostics = diagnostics,
                };
            }

            var entries = new Dictionary<string, ProfileIndexEntry>(StringComparer.Ordinal);
            var root = jsonDocument.RootElement;
            if (!root.TryGetProperty("profiles", out var profilesElement))
            {
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.MissingRequiredField,
                    SourceDiagnosticSeverity.Warning,
                    "profiles.json is missing the profiles object.",
                    input,
                    observedAtUtc,
                    context: [new KeyValuePair<string, string?>("field", "profiles")]));

                return new ProfilesIndexParseResult
                {
                    ParseState = ProfileSourceParseState.Unavailable,
                    Diagnostics = diagnostics,
                };
            }

            if (profilesElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.Malformed,
                    SourceDiagnosticSeverity.Warning,
                    "profiles.json profiles field must be an object.",
                    input,
                    observedAtUtc));

                return new ProfilesIndexParseResult
                {
                    ParseState = ProfileSourceParseState.Malformed,
                    Diagnostics = diagnostics,
                };
            }

            foreach (var profileProperty in profilesElement.EnumerateObject())
            {
                var profileId = NormalizeNonEmpty(profileProperty.Name);
                if (string.IsNullOrWhiteSpace(profileId))
                {
                    diagnostics.Add(CreateDiagnostic(
                        SourceDiagnosticCode.Malformed,
                        SourceDiagnosticSeverity.Warning,
                        "profiles.json contains a malformed profile id entry.",
                        input,
                        observedAtUtc));
                    continue;
                }

                if (profileProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(CreateDiagnostic(
                        SourceDiagnosticCode.Malformed,
                        SourceDiagnosticSeverity.Warning,
                        "profiles.json profile entry is malformed.",
                        input,
                        observedAtUtc,
                        context: [new KeyValuePair<string, string?>("profileId", profileId)]));

                    entries[profileId] = new ProfileIndexEntry
                    {
                        ProfileId = profileId,
                        ParseState = ProfileSourceParseState.Malformed,
                    };
                    continue;
                }

                var entryObject = profileProperty.Value;
                var entryDiagnostics = new List<SourceDiagnostic>();

                var label = ReadNormalizedString(entryObject, "label");
                var email = ReadNormalizedString(entryObject, "email");
                var plan = ReadNormalizedString(entryObject, "plan");
                var isApiKey = ReadNullableBoolean(entryObject, "is_api_key", out var isApiKeyMalformed);

                if (isApiKeyMalformed)
                {
                    entryDiagnostics.Add(CreateDiagnostic(
                        SourceDiagnosticCode.Malformed,
                        SourceDiagnosticSeverity.Warning,
                        "profiles.json profile entry has malformed is_api_key.",
                        input,
                        observedAtUtc,
                        context: [new KeyValuePair<string, string?>("profileId", profileId)]));
                }

                entries[profileId] = new ProfileIndexEntry
                {
                    ProfileId = profileId,
                    Label = label,
                    Email = email,
                    Plan = plan,
                    IsApiKey = isApiKey,
                    ParseState = entryDiagnostics.Count == 0 ? ProfileSourceParseState.Available : ProfileSourceParseState.Malformed,
                };

                diagnostics.AddRange(entryDiagnostics);
            }

            return new ProfilesIndexParseResult
            {
                ParseState = ProfileSourceParseState.Available,
                Entries = entries,
                Diagnostics = diagnostics,
            };
        }
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

    private static bool? ReadNullableBoolean(JsonElement element, string propertyName, out bool malformed)
    {
        malformed = false;
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        if (propertyValue.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (propertyValue.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (propertyValue.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        malformed = true;
        return null;
    }

    private static SourceDiagnostic CreateDiagnostic(
        SourceDiagnosticCode code,
        SourceDiagnosticSeverity severity,
        string summary,
        ProfilesIndexParseInput input,
        DateTimeOffset? observedAtUtc,
        string? detail = null,
        IEnumerable<KeyValuePair<string, string?>>? context = null)
    {
        var mergedContext = new List<KeyValuePair<string, string?>>
        {
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
