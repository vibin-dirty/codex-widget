using CodexWidget.Core;
using CodexWidget.Presentation;

namespace CodexWidget.Web;

public static class WidgetPresentationStateSanitizer
{
    public static WidgetPresentationState Sanitize(WidgetPresentationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state with
        {
            Refresh = SanitizeRefresh(state.Refresh),
            Minimal = SanitizeMinimal(state.Minimal),
            Compact = SanitizeCompact(state.Compact),
            Full = SanitizeFull(state.Full),
            SelectedViewSummaryText = RedactRequiredText(state.SelectedViewSummaryText, "Status data is unavailable."),
        };
    }

    private static WidgetRefreshPresentation SanitizeRefresh(WidgetRefreshPresentation refresh)
    {
        return refresh with
        {
            DetailText = RedactRequiredText(refresh.DetailText, "Status data is unavailable."),
            Sources = refresh.Sources.Select(SanitizeSource).ToArray(),
            Diagnostics = refresh.Diagnostics.Select(SanitizeDiagnostic).ToArray(),
        };
    }

    private static MinimalWidgetPresentation SanitizeMinimal(MinimalWidgetPresentation minimal)
    {
        return minimal with
        {
            CurrentProfile = minimal.CurrentProfile is null ? null : SanitizeProfile(minimal.CurrentProfile),
            SummaryText = RedactRequiredText(minimal.SummaryText, "Current profile is unavailable."),
        };
    }

    private static CompactWidgetPresentation SanitizeCompact(CompactWidgetPresentation compact)
    {
        return compact with
        {
            Profiles = compact.Profiles.Select(SanitizeProfile).ToArray(),
            SummaryText = RedactRequiredText(compact.SummaryText, "No profile data available."),
        };
    }

    private static FullWidgetPresentation SanitizeFull(FullWidgetPresentation full)
    {
        return full with
        {
            Profiles = full.Profiles.Select(SanitizeProfile).ToArray(),
            SummaryText = RedactRequiredText(full.SummaryText, "No profile data available."),
        };
    }

    private static WidgetProfilePresentation SanitizeProfile(WidgetProfilePresentation profile)
    {
        return profile with
        {
            MainBucket = profile.MainBucket is null ? null : SanitizeBucket(profile.MainBucket),
            SparkBucket = profile.SparkBucket is null ? null : SanitizeBucket(profile.SparkBucket),
            AdditionalBuckets = profile.AdditionalBuckets.Select(SanitizeBucket).ToArray(),
            Diagnostics = profile.Diagnostics.Select(SanitizeDiagnostic).ToArray(),
        };
    }

    private static WidgetBucketPresentation SanitizeBucket(WidgetBucketPresentation bucket)
    {
        return bucket with
        {
            Availability = SanitizeAvailability(bucket.Availability),
            FiveHourWindow = bucket.FiveHourWindow is null ? null : SanitizeWindow(bucket.FiveHourWindow),
            WeeklyWindow = bucket.WeeklyWindow is null ? null : SanitizeWindow(bucket.WeeklyWindow),
            Windows = bucket.Windows.Select(SanitizeWindow).ToArray(),
        };
    }

    private static WidgetWindowPresentation SanitizeWindow(WidgetWindowPresentation window)
    {
        return window with
        {
            Availability = SanitizeAvailability(window.Availability),
        };
    }

    private static WidgetSourcePresentation SanitizeSource(WidgetSourcePresentation source)
    {
        return source with
        {
            Diagnostics = source.Diagnostics.Select(SanitizeDiagnostic).ToArray(),
        };
    }

    private static WidgetDiagnosticPresentation SanitizeDiagnostic(WidgetDiagnosticPresentation diagnostic)
    {
        return diagnostic with
        {
            SummaryText = RedactRequiredText(diagnostic.SummaryText, "Diagnostic unavailable."),
            DetailText = WebApiRedaction.RedactOptionalText(diagnostic.DetailText),
            Context = WebApiRedaction.RedactContext(diagnostic.Context),
        };
    }

    private static StatusAvailability SanitizeAvailability(StatusAvailability availability)
    {
        return availability with
        {
            Detail = WebApiRedaction.RedactOptionalText(availability.Detail),
        };
    }

    private static string RedactRequiredText(string? value, string fallback)
    {
        var redacted = WebApiRedaction.RedactOptionalText(value);
        return string.IsNullOrWhiteSpace(redacted) ? fallback : redacted;
    }
}
