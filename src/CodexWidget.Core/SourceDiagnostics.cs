namespace CodexWidget.Core;

public enum StatusSourceKind
{
    Unknown = 0,
    CurrentAuth = 1,
    SavedProfileAuth = 2,
    ProfilesIndex = 3,
    ConfigToml = 4,
    UsageEndpoint = 5,
    UsageBucket = 6,
    Cache = 7,
}

public enum SourceStatusState
{
    Unknown = 0,
    Available = 1,
    Missing = 2,
    Malformed = 3,
    Stale = 4,
    Unavailable = 5,
    Error = 6,
}

public enum SourceDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public enum SourceDiagnosticCode
{
    Unknown = 0,
    Missing = 1,
    Malformed = 2,
    Stale = 3,
    Unavailable = 4,
    Error = 5,
    Unauthorized = 6,
    NetworkError = 7,
    ApiKeyProfile = 8,
    MissingProfile = 9,
    MissingBucket = 10,
    MissingWindow = 11,
    MissingTimestampOrDuration = 12,
    MissingRequiredField = 13,
    TokenRefreshFailed = 14,
}

public sealed record SourceDiagnostic
{
    public SourceDiagnosticCode Code { get; init; } = SourceDiagnosticCode.Unknown;

    public SourceDiagnosticSeverity Severity { get; init; } = SourceDiagnosticSeverity.Info;

    public string Summary { get; init; } = string.Empty;

    public string? Detail { get; init; }

    /// <summary>
    /// Optional troubleshooting fields with sensitive values redacted.
    /// </summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>(0, StringComparer.Ordinal);

    public DateTimeOffset? ObservedAtUtc { get; init; }

    public static SourceDiagnostic Create(
        SourceDiagnosticCode code,
        SourceDiagnosticSeverity severity,
        string summary,
        string? detail = null,
        IEnumerable<KeyValuePair<string, string?>>? context = null,
        DateTimeOffset? observedAtUtc = null)
    {
        return new SourceDiagnostic
        {
            Code = code,
            Severity = severity,
            Summary = summary,
            Detail = string.IsNullOrWhiteSpace(detail) ? null : RedactionHelper.RedactDiagnosticValue(nameof(Detail), detail),
            Context = RedactionHelper.RedactDiagnosticContext(context),
            ObservedAtUtc = observedAtUtc,
        };
    }

    public SourceDiagnostic WithRedactedContent()
    {
        return this with
        {
            Detail = string.IsNullOrWhiteSpace(Detail) ? null : RedactionHelper.RedactDiagnosticValue(nameof(Detail), Detail),
            Context = RedactionHelper.RedactDiagnosticContext(Context.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))),
        };
    }
}

public sealed record SourceStatus
{
    public StatusSourceKind Source { get; init; } = StatusSourceKind.Unknown;

    public SourceStatusState State { get; init; } = SourceStatusState.Unknown;

    public StatusAvailability Availability { get; init; } = new(StatusAvailabilityState.Unknown);

    public DateTimeOffset? ObservedAtUtc { get; init; }

    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; init; } = Array.Empty<SourceDiagnostic>();
}
