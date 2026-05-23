using CodexWidget.Core;
using Tomlyn;
using Tomlyn.Model;

namespace CodexWidget.Profiles;

public sealed class ConfigTomlParser : IConfigTomlParser
{
    public CodexConfigProfile Parse(ConfigTomlParseInput input, DateTimeOffset? observedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var diagnostics = new List<SourceDiagnostic>();
        if (string.IsNullOrWhiteSpace(input.TomlContent))
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.Missing,
                SourceDiagnosticSeverity.Info,
                "config.toml is missing.",
                input,
                observedAtUtc));

            return new CodexConfigProfile
            {
                ParseState = ProfileSourceParseState.Missing,
                Diagnostics = diagnostics,
            };
        }

        TomlTable model;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(input.TomlContent) ?? new TomlTable();
        }
        catch (Exception exception)
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.Malformed,
                SourceDiagnosticSeverity.Warning,
                "config.toml is malformed.",
                input,
                observedAtUtc,
                detail: exception.Message));

            return new CodexConfigProfile
            {
                ParseState = ProfileSourceParseState.Malformed,
                Diagnostics = diagnostics,
            };
        }

        var credentialsStoreMode = ReadNormalizedString(model, "cli_auth_credentials_store_mode", out var credentialsStoreModeMalformed);
        var chatGptBaseUrl = ReadNormalizedString(model, "chatgpt_base_url", out var chatGptBaseUrlMalformed);

        if (credentialsStoreModeMalformed)
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.Malformed,
                SourceDiagnosticSeverity.Warning,
                "config.toml has malformed cli_auth_credentials_store_mode.",
                input,
                observedAtUtc,
                context: [new KeyValuePair<string, string?>("field", "cli_auth_credentials_store_mode")]));
        }

        if (chatGptBaseUrlMalformed)
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.Malformed,
                SourceDiagnosticSeverity.Warning,
                "config.toml has malformed chatgpt_base_url.",
                input,
                observedAtUtc,
                context: [new KeyValuePair<string, string?>("field", "chatgpt_base_url")]));
        }

        return new CodexConfigProfile
        {
            ParseState = diagnostics.Any(diagnostic => diagnostic.Code == SourceDiagnosticCode.Malformed)
                ? ProfileSourceParseState.Malformed
                : ProfileSourceParseState.Available,
            CredentialsStoreMode = credentialsStoreMode,
            ChatGptBaseUrl = chatGptBaseUrl,
            Diagnostics = diagnostics,
        };
    }

    private static string? ReadNormalizedString(TomlTable table, string key, out bool malformed)
    {
        malformed = false;
        if (!table.TryGetValue(key, out var rawValue))
        {
            return null;
        }

        if (rawValue is null)
        {
            return null;
        }

        if (rawValue is string stringValue)
        {
            return NormalizeNonEmpty(stringValue);
        }

        malformed = true;
        return null;
    }

    private static SourceDiagnostic CreateDiagnostic(
        SourceDiagnosticCode code,
        SourceDiagnosticSeverity severity,
        string summary,
        ConfigTomlParseInput input,
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
