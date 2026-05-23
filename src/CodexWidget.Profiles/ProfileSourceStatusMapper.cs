using CodexWidget.Core;

namespace CodexWidget.Profiles;

public static class ProfileSourceStatusMapper
{
    public static SourceStatus ToSourceStatus(
        StatusSourceKind source,
        ProfileSourceParseState parseState,
        IReadOnlyList<SourceDiagnostic>? diagnostics,
        DateTimeOffset? observedAtUtc = null)
    {
        var effectiveDiagnostics = diagnostics ?? Array.Empty<SourceDiagnostic>();

        return new SourceStatus
        {
            Source = source,
            State = ToSourceStatusState(parseState),
            Availability = ToAvailability(parseState, effectiveDiagnostics),
            ObservedAtUtc = observedAtUtc,
            Diagnostics = effectiveDiagnostics,
        };
    }

    public static SourceStatusState ToSourceStatusState(ProfileSourceParseState parseState)
    {
        return parseState switch
        {
            ProfileSourceParseState.Available => SourceStatusState.Available,
            ProfileSourceParseState.Missing => SourceStatusState.Missing,
            ProfileSourceParseState.Malformed => SourceStatusState.Malformed,
            ProfileSourceParseState.Unavailable => SourceStatusState.Unavailable,
            ProfileSourceParseState.Error => SourceStatusState.Error,
            _ => SourceStatusState.Unknown,
        };
    }

    public static StatusAvailability ToAvailability(
        ProfileSourceParseState parseState,
        IReadOnlyList<SourceDiagnostic> diagnostics)
    {
        return parseState switch
        {
            ProfileSourceParseState.Available => StatusAvailability.Available(),
            ProfileSourceParseState.Missing => StatusAvailability.Unavailable(StatusAvailabilityCode.Missing),
            ProfileSourceParseState.Malformed => StatusAvailability.Unavailable(StatusAvailabilityCode.Malformed),
            ProfileSourceParseState.Unavailable => StatusAvailability.Unavailable(MapDiagnosticCode(diagnostics, StatusAvailabilityCode.Unavailable)),
            ProfileSourceParseState.Error => StatusAvailability.Unavailable(StatusAvailabilityCode.Error),
            _ => new StatusAvailability(StatusAvailabilityState.Unknown),
        };
    }

    private static StatusAvailabilityCode MapDiagnosticCode(
        IReadOnlyList<SourceDiagnostic> diagnostics,
        StatusAvailabilityCode fallback)
    {
        foreach (var diagnostic in diagnostics)
        {
            var mapped = diagnostic.Code switch
            {
                SourceDiagnosticCode.Missing => StatusAvailabilityCode.Missing,
                SourceDiagnosticCode.Malformed => StatusAvailabilityCode.Malformed,
                SourceDiagnosticCode.Unavailable => StatusAvailabilityCode.Unavailable,
                SourceDiagnosticCode.Error => StatusAvailabilityCode.Error,
                SourceDiagnosticCode.ApiKeyProfile => StatusAvailabilityCode.ApiKeyProfile,
                SourceDiagnosticCode.MissingRequiredField => StatusAvailabilityCode.MissingRequiredField,
                _ => StatusAvailabilityCode.None,
            };

            if (mapped != StatusAvailabilityCode.None)
            {
                return mapped;
            }
        }

        return fallback;
    }
}
