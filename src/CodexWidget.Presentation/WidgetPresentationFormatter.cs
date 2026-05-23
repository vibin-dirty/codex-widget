using System.Globalization;
using CodexWidget.Core;

namespace CodexWidget.Presentation;

public static class WidgetPresentationFormatter
{
    private const string DateTimePattern = "yyyy-MM-dd HH:mm:ss zzz";
    private const string CompactDateTimePattern = "MM-dd HH:mm";

    public const string CompactUnavailableTimestampToken = "-- --:--";

    public static string FormatPercentText(string metricName, int? percent)
    {
        var label = string.IsNullOrWhiteSpace(metricName) ? "Value" : metricName.Trim();
        return percent.HasValue
            ? $"{label}: {percent.Value}%."
            : $"{label}: unavailable.";
    }

    public static string FormatLocalEndTimeText(string label, long? unixSeconds)
    {
        var prefix = string.IsNullOrWhiteSpace(label) ? "Ends" : label.Trim();
        if (!unixSeconds.HasValue)
        {
            return $"{prefix}: unavailable.";
        }

        var local = DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value).ToLocalTime();
        return $"{prefix}: {local.ToString(DateTimePattern, CultureInfo.InvariantCulture)}.";
    }

    public static string FormatLocalCompactEndTimeText(long? unixSeconds)
    {
        if (!unixSeconds.HasValue)
        {
            return CompactUnavailableTimestampToken;
        }

        var local = DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value).ToLocalTime();
        return local.ToString(CompactDateTimePattern, CultureInfo.InvariantCulture);
    }

    public static string FormatLocalInstantText(string label, DateTimeOffset? utcInstant)
    {
        var prefix = string.IsNullOrWhiteSpace(label) ? "Time" : label.Trim();
        if (!utcInstant.HasValue)
        {
            return $"{prefix}: unavailable.";
        }

        var local = utcInstant.Value.ToLocalTime();
        return $"{prefix}: {local.ToString(DateTimePattern, CultureInfo.InvariantCulture)}.";
    }

    public static string FormatSnapshotAgeText(TimeSpan age)
    {
        var safe = age < TimeSpan.Zero ? TimeSpan.Zero : age;
        return $"Snapshot age: {safe.TotalSeconds:0}s.";
    }

    public static string FormatAvailabilityText(string entityName, StatusAvailability availability)
    {
        var prefix = string.IsNullOrWhiteSpace(entityName) ? "Status" : entityName.Trim();
        if (availability.State == StatusAvailabilityState.Available)
        {
            return $"{prefix}: available.";
        }

        var codeText = availability.Code.ToString();
        if (string.IsNullOrWhiteSpace(availability.Detail))
        {
            return $"{prefix}: unavailable ({codeText}).";
        }

        var detail = RedactionHelper.RedactDiagnosticValue("availabilityDetail", availability.Detail);
        return $"{prefix}: unavailable ({codeText}, {detail}).";
    }

    public static string FormatActiveProfileText(bool isCurrent)
    {
        return isCurrent
            ? "Profile is active."
            : "Profile is not active.";
    }

    public static string FormatBucketIdentityText(string bucketLabel, string bucketId)
    {
        var safeLabel = string.IsNullOrWhiteSpace(bucketLabel) ? "unknown" : bucketLabel.Trim();
        var safeId = string.IsNullOrWhiteSpace(bucketId) ? "unknown" : bucketId.Trim();
        return $"Bucket {safeLabel} ({safeId}).";
    }

    public static string FormatWindowIdentityText(UsageWindowKind kind)
    {
        return kind switch
        {
            UsageWindowKind.FiveHour => "Window: 5-hour.",
            UsageWindowKind.Weekly => "Window: weekly.",
            UsageWindowKind.Additional => "Window: additional.",
            _ => "Window: unknown.",
        };
    }

    public static string FormatRefreshStateText(WidgetRefreshVisualState state)
    {
        return state switch
        {
            WidgetRefreshVisualState.Idle => "Status idle.",
            WidgetRefreshVisualState.Refreshing => "Status refreshing.",
            WidgetRefreshVisualState.Stale => "Status stale.",
            WidgetRefreshVisualState.Unavailable => "Status unavailable.",
            WidgetRefreshVisualState.Warning => "Status warning.",
            WidgetRefreshVisualState.Critical => "Status critical.",
            WidgetRefreshVisualState.Error => "Status error.",
            _ => "Status unavailable.",
        };
    }

    public static string FormatProfileIdentityText(string? displayName, string? loginName, string? profileId)
    {
        var resolvedName = ResolveProfileDisplayName(displayName, loginName, profileId);
        return $"Profile: {resolvedName}.";
    }

    public static string ResolveProfileDisplayName(string? displayName, string? loginName, string? profileId)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(loginName))
        {
            return loginName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(profileId))
        {
            return profileId.Trim();
        }

        return "Unknown profile";
    }
}
