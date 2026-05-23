using CodexWidget.Core;

namespace CodexWidget.Profiles;

public sealed record AuthProfileParseInput
{
    public string? ProfileId { get; init; }

    public AuthProfileSourceKind SourceKind { get; init; } = AuthProfileSourceKind.Unknown;

    public string? SourcePath { get; init; }

    public string? JsonContent { get; init; }
}

public sealed record ProfilesIndexParseInput
{
    public string? SourcePath { get; init; }

    public string? JsonContent { get; init; }
}

public sealed record ProfilesIndexParseResult
{
    public ProfileSourceParseState ParseState { get; init; } = ProfileSourceParseState.Unknown;

    public IReadOnlyDictionary<string, ProfileIndexEntry> Entries { get; init; } = new Dictionary<string, ProfileIndexEntry>(0, StringComparer.Ordinal);

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();
}

public sealed record ConfigTomlParseInput
{
    public string? SourcePath { get; init; }

    public string? TomlContent { get; init; }
}

public sealed record CodexConfigProfile
{
    public ProfileSourceParseState ParseState { get; init; } = ProfileSourceParseState.Unknown;

    public string? CredentialsStoreMode { get; init; }

    public string? ChatGptBaseUrl { get; init; }

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();
}

public interface IAuthProfileParser
{
    AuthProfile Parse(AuthProfileParseInput input, DateTimeOffset? observedAtUtc = null);
}

public interface IProfilesIndexParser
{
    ProfilesIndexParseResult Parse(ProfilesIndexParseInput input, DateTimeOffset? observedAtUtc = null);
}

public interface IConfigTomlParser
{
    CodexConfigProfile Parse(ConfigTomlParseInput input, DateTimeOffset? observedAtUtc = null);
}
