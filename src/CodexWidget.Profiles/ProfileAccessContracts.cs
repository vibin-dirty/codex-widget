using CodexWidget.Core;
using System.Text.Json.Serialization;

namespace CodexWidget.Profiles;

public enum AuthProfileSourceKind
{
    Unknown = 0,
    CurrentAuth = 1,
    SavedProfile = 2,
}

public enum ProfileSourceParseState
{
    Unknown = 0,
    Available = 1,
    Missing = 2,
    Malformed = 3,
    Unavailable = 4,
    Error = 5,
}

public sealed record AuthTokens
{
    [JsonIgnore]
    public string? AccountId { get; init; }

    [JsonIgnore]
    public string? IdToken { get; init; }

    [JsonIgnore]
    public string? AccessToken { get; init; }

    [JsonIgnore]
    public string? RefreshToken { get; init; }
}

public sealed record ProfileIndexEntry
{
    public string ProfileId { get; init; } = string.Empty;

    public string? Label { get; init; }

    public string? Email { get; init; }

    public string? Plan { get; init; }

    public bool? IsApiKey { get; init; }

    public ProfileSourceParseState ParseState { get; init; } = ProfileSourceParseState.Unknown;
}

public sealed record IdentityKey
{
    public string PrincipalId { get; init; } = string.Empty;

    public string WorkspaceOrOrgId { get; init; } = string.Empty;

    public string PlanType { get; init; } = string.Empty;
}

public sealed record AuthProfile
{
    public string? ProfileId { get; init; }

    public AuthProfileSourceKind SourceKind { get; init; } = AuthProfileSourceKind.Unknown;

    public ProfileSourceParseState ParseState { get; init; } = ProfileSourceParseState.Unknown;

    public bool IsApiKeyProfile { get; init; }

    [JsonIgnore]
    public string? SourcePath { get; init; }

    [JsonIgnore]
    public AuthTokens Tokens { get; init; } = new();

    [JsonIgnore]
    public string? ApiKey { get; init; }

    public ProfileIndexEntry? IndexEntry { get; init; }

    public IdentityKey? Identity { get; init; }

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();
}

public sealed record ProfileUsageCredentialReference
{
    public string? ProfileId { get; init; }

    public ProfileAuthKind AuthKind { get; init; } = ProfileAuthKind.Unknown;

    public ProfileUsageEligibility UsageEligibility { get; init; } = ProfileUsageEligibility.Unknown;

    public string? LoginName { get; init; }

    public SubscriptionTier SubscriptionTier { get; init; } = SubscriptionTier.Unknown;

    [JsonIgnore]
    public string? SourcePath { get; init; }

    [JsonIgnore]
    public string? AccountId { get; init; }

    [JsonIgnore]
    public string? IdToken { get; init; }

    [JsonIgnore]
    public string? AccessToken { get; init; }

    [JsonIgnore]
    public string? RefreshToken { get; init; }

    [JsonIgnore]
    public string? ApiKey { get; init; }
}

public sealed record ProfileSnapshot
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;

    [JsonIgnore]
    public CodexHomePaths Paths { get; init; } = new();

    public IReadOnlyList<ProfileStatus> Profiles { get; init; } = Array.Empty<ProfileStatus>();

    public string? CurrentProfileId { get; init; }

    public IReadOnlyList<SourceStatus> Sources { get; init; } = Array.Empty<SourceStatus>();

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();

    [JsonIgnore]
    public AuthProfile? CurrentAuthProfile { get; init; }

    [JsonIgnore]
    public IReadOnlyList<AuthProfile> SavedProfiles { get; init; } = Array.Empty<AuthProfile>();

    [JsonIgnore]
    public IReadOnlyDictionary<string, ProfileIndexEntry> SavedProfileIndex { get; init; } = new Dictionary<string, ProfileIndexEntry>(0, StringComparer.Ordinal);

    [JsonIgnore]
    public IReadOnlyList<ProfileUsageCredentialReference> UsageCredentialReferences { get; init; } = Array.Empty<ProfileUsageCredentialReference>();

    [JsonIgnore]
    public RawProfileSnapshot RawSources { get; init; } = new();
}

public sealed record ProfileSnapshotReadOptions
{
    public CodexHomeResolutionOptions HomeResolution { get; init; } = new();

    public DateTimeOffset? CapturedAtUtcOverride { get; init; }

    public TimeSpan LockAcquireTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan LockRetryInterval { get; init; } = TimeSpan.FromMilliseconds(25);
}

public interface IProfileSnapshotReader
{
    Task<ProfileSnapshot> ReadAsync(ProfileSnapshotReadOptions? options = null, CancellationToken cancellationToken = default);
}

public interface IProfileIdentityMatcher
{
    IdentityKey? BuildIdentityKey(AuthProfile authProfile);

    bool Matches(IdentityKey? left, IdentityKey? right);
}

public sealed record RawProfileSourceFile
{
    public StatusSourceKind SourceKind { get; init; } = StatusSourceKind.Unknown;

    public string? ProfileId { get; init; }

    [JsonIgnore]
    public string Path { get; init; } = string.Empty;

    public ProfileSourceParseState ReadState { get; init; } = ProfileSourceParseState.Unknown;

    [JsonIgnore]
    public string? Content { get; init; }

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();
}

public sealed record RawProfileSnapshot
{
    public RawProfileSourceFile? CurrentAuthFile { get; init; }

    public RawProfileSourceFile? ConfigTomlFile { get; init; }

    public RawProfileSourceFile? ProfilesIndexFile { get; init; }

    public IReadOnlyList<RawProfileSourceFile> SavedProfileFiles { get; init; } = Array.Empty<RawProfileSourceFile>();
}
