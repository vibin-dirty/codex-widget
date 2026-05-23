using CodexWidget.Core;

namespace CodexWidget.Status;

public static class StatusRefreshScheduling
{
    public static DateTimeOffset? CalculateNextScheduledRefreshAtUtc(
        IEnumerable<ProfileStatus> profiles,
        DateTimeOffset nowUtc,
        WidgetPreferences? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var effectivePreferences = preferences ?? WidgetPreferenceDefaults.Create();
        var periodicDueAt = nowUtc + TimeSpan.FromSeconds(NormalizeRefreshPeriodSeconds(effectivePreferences.RefreshPeriodSeconds));
        var minimumDueAt = nowUtc + TimeSpan.FromSeconds(WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds);

        DateTimeOffset? earliestResetAtUtc = null;
        foreach (var resetAt in profiles
                     .SelectMany(profile => profile.AllBuckets)
                     .SelectMany(bucket => bucket.Windows)
                     .Where(window => window.Availability.IsAvailable && window.ResetAtUnixSeconds.HasValue && window.DurationSeconds is > 0)
                     .Select(window => DateTimeOffset.FromUnixTimeSeconds(window.ResetAtUnixSeconds!.Value)))
        {
            if (earliestResetAtUtc is null || resetAt < earliestResetAtUtc.Value)
            {
                earliestResetAtUtc = resetAt;
            }
        }

        if (earliestResetAtUtc is null)
        {
            return periodicDueAt;
        }

        var resetDueAt = earliestResetAtUtc.Value <= minimumDueAt
            ? minimumDueAt
            : earliestResetAtUtc.Value;

        return resetDueAt < periodicDueAt
            ? resetDueAt
            : periodicDueAt;
    }

    private static int NormalizeRefreshPeriodSeconds(int requestedSeconds)
    {
        return Math.Clamp(
            requestedSeconds,
            WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds,
            WidgetPreferenceDefaults.MaximumRefreshPeriodSeconds);
    }
}
