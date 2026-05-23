namespace CodexWidget.Core;

public enum SubscriptionTier
{
    Unknown = 0,
    Free = 1,
    Plus = 2,
    Pro = 3,
    ProLite = 4,
}

public enum ProfileAuthKind
{
    Unknown = 0,
    Login = 1,
    ApiKey = 2,
    CredentialsUnavailable = 3,
}

public enum ProfileUsageEligibility
{
    Unknown = 0,
    Eligible = 1,
    ApiKeyProfile = 2,
    MissingLoginName = 3,
    MissingAccessToken = 4,
    MissingAccountId = 5,
    MissingSubscriptionTier = 6,
    SourceUnavailable = 7,
    MalformedAuth = 8,
    CredentialsUnavailable = 9,
}

public sealed record ProfileDescriptor
{
    public string? ProfileId { get; init; }

    public string? DisplayName { get; init; }

    public string? LoginName { get; init; }

    public SubscriptionTier SubscriptionTier { get; init; } = SubscriptionTier.Unknown;

    public bool IsCurrent { get; init; }

    public ProfileAuthKind AuthKind { get; init; } = ProfileAuthKind.Unknown;

    public ProfileUsageEligibility UsageEligibility { get; init; } = ProfileUsageEligibility.Unknown;

    public SourceStatus SourceStatus { get; init; } = new();
}

public sealed record ProfileStatus
{
    public ProfileDescriptor Profile { get; init; } = new();

    public UsageBucketSnapshot? MainBucket { get; init; }

    public UsageBucketSnapshot? SparkBucket { get; init; }

    public IReadOnlyList<UsageBucketSnapshot> AllBuckets { get; init; } = Array.Empty<UsageBucketSnapshot>();

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();
}
