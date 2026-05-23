using CodexWidget.Core;
using System.Text.Json.Serialization;

namespace CodexWidget.Usage;

public enum UsageFetchOutcome
{
    Unknown = 0,
    Succeeded = 1,
    MissingRequiredProfileFields = 2,
    EndpointRejected = 3,
    Unauthorized = 4,
    TokenRefreshFailed = 5,
    NetworkError = 6,
    Timeout = 7,
    MalformedResponse = 8,
    MissingBucket = 9,
    MissingWindow = 10,
    Canceled = 11,
    HttpError = 12,
}

public enum UsageEndpointResolutionOutcome
{
    Unknown = 0,
    Resolved = 1,
    Rejected = 2,
    Malformed = 3,
}

public enum UsageTokenRefreshOutcome
{
    Unknown = 0,
    NotAttempted = 1,
    Succeeded = 2,
    MissingRefreshToken = 3,
    MissingSourcePath = 4,
    Unauthorized = 5,
    NetworkError = 6,
    Timeout = 7,
    MalformedResponse = 8,
    TokenWriteFailed = 9,
    Canceled = 10,
    Error = 11,
    HttpError = 12,
}

public enum UsageRetryOutcome
{
    Unknown = 0,
    NotAttempted = 1,
    Succeeded = 2,
    Unauthorized = 3,
    NetworkError = 4,
    Timeout = 5,
    HttpError = 6,
    MalformedResponse = 7,
    Canceled = 8,
    Error = 9,
}

public enum TokenUpdateOutcome
{
    Unknown = 0,
    NotAttempted = 1,
    Succeeded = 2,
    MissingSourcePath = 3,
    MissingProfilesLockPath = 4,
    MissingTokenChanges = 5,
    SourceMissing = 6,
    MalformedSource = 7,
    LockUnavailable = 8,
    Canceled = 9,
    Error = 10,
}

public sealed record UsageProfileRequest
{
    public string? ProfileId { get; init; }

    public string? LoginName { get; init; }

    public SubscriptionTier SubscriptionTier { get; init; } = SubscriptionTier.Unknown;

    public string? ChatGptBaseUrl { get; init; }

    [JsonIgnore]
    public string? SourcePath { get; init; }

    [JsonIgnore]
    public string? ProfilesLockPath { get; init; }

    [JsonIgnore]
    public string? AccountId { get; init; }

    [JsonIgnore]
    public string? IdToken { get; init; }

    [JsonIgnore]
    public string? AccessToken { get; init; }

    [JsonIgnore]
    public string? RefreshToken { get; init; }

    [JsonIgnore]
    public string? AuthorizationHeaderValue { get; init; }
}

public sealed record UsageEndpointResolutionResult
{
    public UsageEndpointResolutionOutcome Outcome { get; init; } = UsageEndpointResolutionOutcome.Unknown;

    public Uri? BaseUri { get; init; }

    public Uri? EndpointUri { get; init; }

    public SourceStatus SourceStatus { get; init; } = new()
    {
        Source = StatusSourceKind.UsageEndpoint,
        State = SourceStatusState.Unknown,
        Availability = new StatusAvailability(StatusAvailabilityState.Unknown),
    };

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();

    public bool IsSuccess => Outcome == UsageEndpointResolutionOutcome.Resolved && EndpointUri is not null;
}

public sealed record UsageFetchResult
{
    public string? ProfileId { get; init; }

    public UsageFetchOutcome Outcome { get; init; } = UsageFetchOutcome.Unknown;

    public StatusAvailability Availability { get; init; } = new(StatusAvailabilityState.Unknown);

    public IReadOnlyList<UsageBucketSnapshot> Buckets { get; init; } = Array.Empty<UsageBucketSnapshot>();

    public UsageEndpointResolutionResult EndpointResolution { get; init; } = new();

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();

    public UsageTokenRefreshResult TokenRefresh { get; init; } = new()
    {
        Outcome = UsageTokenRefreshOutcome.NotAttempted,
        RetryOutcome = UsageRetryOutcome.NotAttempted,
        TokenUpdate = new TokenUpdateResult
        {
            Outcome = TokenUpdateOutcome.NotAttempted,
        },
    };
}

public sealed record UsageFetchBatchResult
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;

    public IReadOnlyList<UsageFetchResult> Results { get; init; } = Array.Empty<UsageFetchResult>();

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();
}

public sealed record UsageTokenRefreshResult
{
    public UsageTokenRefreshOutcome Outcome { get; init; } = UsageTokenRefreshOutcome.Unknown;

    public StatusAvailability Availability { get; init; } = new(StatusAvailabilityState.Unknown);

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();

    public UsageRetryOutcome RetryOutcome { get; init; } = UsageRetryOutcome.Unknown;

    public TokenUpdateResult TokenUpdate { get; init; } = new();

    [JsonIgnore]
    public string? AccountId { get; init; }

    [JsonIgnore]
    public string? IdToken { get; init; }

    [JsonIgnore]
    public string? AccessToken { get; init; }

    [JsonIgnore]
    public string? RefreshToken { get; init; }
}

public sealed record TokenUpdateRequest
{
    public string? ProfileId { get; init; }

    [JsonIgnore]
    public string? SourcePath { get; init; }

    [JsonIgnore]
    public string? ProfilesLockPath { get; init; }

    [JsonIgnore]
    public string? AccountId { get; init; }

    [JsonIgnore]
    public string? IdToken { get; init; }

    [JsonIgnore]
    public string? AccessToken { get; init; }

    [JsonIgnore]
    public string? RefreshToken { get; init; }
}

public sealed record TokenUpdateResult
{
    public TokenUpdateOutcome Outcome { get; init; } = TokenUpdateOutcome.Unknown;

    public StatusAvailability Availability { get; init; } = new(StatusAvailabilityState.Unknown);

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();
}
