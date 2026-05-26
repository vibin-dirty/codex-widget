using CodexWidget.Core;

namespace CodexWidget.Presentation;

public sealed class WidgetPresentationService
{
    private readonly StatusProjectionService projectionService;
    private readonly IClock clock;

    public WidgetPresentationService(StatusProjectionService projectionService, IClock? clock = null)
    {
        this.projectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
        this.clock = clock ?? SystemClock.Instance;
    }

    public WidgetPresentationState Build(StatusSnapshot snapshot, WidgetPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(preferences);

        var minimalProjection = projectionService.ProjectMinimal(snapshot, preferences);
        var compactProjection = projectionService.ProjectCompact(snapshot, preferences);
        var fullProjection = projectionService.ProjectFull(snapshot, preferences);

        var minimal = BuildMinimal(minimalProjection);
        var compact = BuildCompact(compactProjection);
        var full = BuildFull(fullProjection);
        var selectedView = NormalizeVisibleSelectedView(preferences.SelectedView);
        var compactAccountLayout = NormalizeCompactAccountLayout(preferences.CompactAccountLayout);
        var widgetScalePercent = NormalizeWidgetScalePercent(preferences.WidgetScalePercent);

        var refresh = BuildRefresh(snapshot, preferences);

        return new WidgetPresentationState
        {
            SelectedView = selectedView,
            CompactAccountLayout = compactAccountLayout,
            WidgetScalePercent = widgetScalePercent,
            Refresh = refresh,
            Minimal = minimal,
            Compact = compact,
            Full = full,
            SelectedViewSummaryText = BuildSelectedViewSummary(selectedView, minimal, compact, full),
        };
    }

    private static WidgetViewKind NormalizeVisibleSelectedView(WidgetViewKind selectedView)
    {
        return selectedView switch
        {
            WidgetViewKind.Minimal => WidgetViewKind.Minimal,
            WidgetViewKind.Compact => WidgetViewKind.Compact,
            WidgetViewKind.Full => WidgetViewKind.Compact,
            _ => WidgetViewKind.Compact,
        };
    }

    private static CompactAccountLayout NormalizeCompactAccountLayout(CompactAccountLayout compactAccountLayout)
    {
        return Enum.IsDefined(compactAccountLayout)
            ? compactAccountLayout
            : WidgetPreferenceDefaults.DefaultCompactAccountLayout;
    }

    private static int NormalizeWidgetScalePercent(int percent)
    {
        return Math.Clamp(
            percent,
            WidgetPreferenceDefaults.MinimumWidgetScalePercent,
            WidgetPreferenceDefaults.MaximumWidgetScalePercent);
    }

    private static string BuildSelectedViewSummary(
        WidgetViewKind selectedView,
        MinimalWidgetPresentation minimal,
        CompactWidgetPresentation compact,
        FullWidgetPresentation full)
    {
        return selectedView switch
        {
            WidgetViewKind.Minimal => minimal.SummaryText,
            WidgetViewKind.Full => full.SummaryText,
            _ => compact.SummaryText,
        };
    }

    private static MinimalWidgetPresentation BuildMinimal(MinimalStatusViewProjection projection)
    {
        if (projection.CurrentProfile is null)
        {
            return new MinimalWidgetPresentation
            {
                SummaryText = "Current profile is unavailable.",
            };
        }

        var profile = BuildProfile(
            projection.CurrentProfile.StatusAll,
            projection.CurrentProfile.MainBucket,
            sparkBucket: null,
            additionalBuckets: null,
            diagnostics: null);

        return new MinimalWidgetPresentation
        {
            CurrentProfile = profile,
            SummaryText = $"Current profile {profile.ProfileDisplayName}.",
        };
    }

    private static CompactWidgetPresentation BuildCompact(CompactStatusViewProjection projection)
    {
        if (projection.Profiles.Count == 0)
        {
            return new CompactWidgetPresentation
            {
                SummaryText = "No profiles are available.",
            };
        }

        var profiles = projection.Profiles
            .Select(profile => BuildProfile(
                profile.StatusAll,
                profile.MainBucket,
                profile.SparkBucket,
                additionalBuckets: null,
                diagnostics: null))
            .ToArray();

        var currentCount = profiles.Count(profile => profile.IsCurrent);
        return new CompactWidgetPresentation
        {
            Profiles = profiles,
            SummaryText = currentCount > 0
                ? $"{profiles.Length} profiles loaded, {currentCount} active."
                : $"{profiles.Length} profiles loaded.",
        };
    }

    private static FullWidgetPresentation BuildFull(FullStatusViewProjection projection)
    {
        if (projection.Profiles.Count == 0)
        {
            return new FullWidgetPresentation
            {
                SummaryText = "No profiles are available.",
            };
        }

        var profiles = projection.Profiles
            .Select(profile => BuildProfile(
                profile.StatusAll,
                profile.MainBucket,
                profile.SparkBucket,
                profile.AdditionalBuckets,
                profile.Diagnostics))
            .ToArray();

        return new FullWidgetPresentation
        {
            Profiles = profiles,
            SummaryText = $"{profiles.Length} profiles with expanded usage details.",
        };
    }

    private static WidgetProfilePresentation BuildProfile(
        StatusAllProfileProjection profile,
        ProjectedUsageBucket? mainBucket,
        ProjectedUsageBucket? sparkBucket,
        IReadOnlyList<ProjectedUsageBucket>? additionalBuckets,
        IReadOnlyList<SourceDiagnostic>? diagnostics)
    {
        var displayName = WidgetPresentationFormatter.ResolveProfileDisplayName(profile.Name, profile.LoginName, profile.Id);
        var profileDiagnostics = diagnostics is null
            ? Array.Empty<WidgetDiagnosticPresentation>()
            : diagnostics.Select(BuildDiagnostic).ToArray();

        return new WidgetProfilePresentation
        {
            ProfileId = profile.Id,
            ProfileDisplayName = displayName,
            ProfileIdentityText = WidgetPresentationFormatter.FormatProfileIdentityText(profile.Name, profile.LoginName, profile.Id),
            IsCurrent = profile.IsCurrent,
            ActiveProfileText = WidgetPresentationFormatter.FormatActiveProfileText(profile.IsCurrent),
            SubscriptionTier = profile.SubscriptionTier,
            SubscriptionText = $"Subscription: {profile.SubscriptionTier}.",
            MainBucket = BuildBucket(mainBucket),
            SparkBucket = BuildBucket(sparkBucket),
            AdditionalBuckets = additionalBuckets is null
                ? Array.Empty<WidgetBucketPresentation>()
                : additionalBuckets.Select(BuildBucket).Where(bucket => bucket is not null).Cast<WidgetBucketPresentation>().ToArray(),
            Diagnostics = profileDiagnostics,
        };
    }

    private static WidgetBucketPresentation? BuildBucket(ProjectedUsageBucket? bucket)
    {
        if (bucket is null)
        {
            return null;
        }

        var availability = RedactAvailability(bucket.Availability);
        var windows = bucket.Windows.Select(BuildWindow).ToArray();
        var fiveHour = windows.FirstOrDefault(window => window.WindowKind == UsageWindowKind.FiveHour);
        var weekly = windows.FirstOrDefault(window => window.WindowKind == UsageWindowKind.Weekly);

        return new WidgetBucketPresentation
        {
            BucketId = bucket.BucketId,
            BucketLabel = bucket.BucketLabel,
            BucketIdentityText = WidgetPresentationFormatter.FormatBucketIdentityText(bucket.BucketLabel, bucket.BucketId),
            BucketKind = bucket.BucketKind,
            FetchStatus = bucket.FetchStatus,
            FetchStatusText = $"Fetch status: {bucket.FetchStatus}.",
            Availability = availability,
            AvailabilityText = WidgetPresentationFormatter.FormatAvailabilityText("Bucket", availability),
            FiveHourWindow = fiveHour,
            WeeklyWindow = weekly,
            Windows = windows,
        };
    }

    private static WidgetWindowPresentation BuildWindow(ProjectedUsageWindow window)
    {
        var availability = RedactAvailability(window.Availability);
        var isAvailable = availability.State == StatusAvailabilityState.Available;
        var quotaLeftPercent = isAvailable ? window.QuotaLeftPercent : null;
        var timeLeftPercent = isAvailable ? window.TimeLeftPercent : null;
        var endsAtUnixSeconds = isAvailable ? window.EndsAtUnixSeconds : null;
        var hasQuotaLeft = quotaLeftPercent is > 0;
        var hasTimeLeft = timeLeftPercent.HasValue;

        return new WidgetWindowPresentation
        {
            WindowKind = window.WindowKind,
            WindowIdentityText = WidgetPresentationFormatter.FormatWindowIdentityText(window.WindowKind),
            Availability = availability,
            IsAvailable = isAvailable,
            HasQuotaLeft = hasQuotaLeft,
            HasTimeLeft = hasTimeLeft,
            AvailabilityText = WidgetPresentationFormatter.FormatAvailabilityText("Window", availability),
            QuotaLeftPercent = quotaLeftPercent,
            QuotaText = WidgetPresentationFormatter.FormatPercentText("Quota left", quotaLeftPercent),
            TimeLeftPercent = timeLeftPercent,
            TimeText = WidgetPresentationFormatter.FormatPercentText("Time left", timeLeftPercent),
            EndsAtUnixSeconds = endsAtUnixSeconds,
            EndsAtText = WidgetPresentationFormatter.FormatLocalEndTimeText("Ends", endsAtUnixSeconds),
            EndsAtCompactText = WidgetPresentationFormatter.FormatLocalCompactEndTimeText(endsAtUnixSeconds),
        };
    }

    private static StatusAvailability RedactAvailability(StatusAvailability availability)
    {
        if (string.IsNullOrWhiteSpace(availability.Detail))
        {
            return availability;
        }

        return availability with
        {
            Detail = RedactionHelper.RedactDiagnosticValue("availabilityDetail", availability.Detail),
        };
    }

    private WidgetRefreshPresentation BuildRefresh(
        StatusSnapshot snapshot,
        WidgetPreferences preferences)
    {
        var nowUtc = clock.UtcNow;
        var age = nowUtc - snapshot.CapturedAtUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var refreshState = ResolveRefreshState(snapshot, preferences, nowUtc);
        var detailText = BuildRefreshDetail(snapshot, refreshState);
        var sourcePresentations = snapshot.Sources.Select(BuildSource).ToArray();
        var diagnostics = sourcePresentations
            .SelectMany(source => source.Diagnostics)
            .ToList();
        if (snapshot.RefreshState.Failure is not null)
        {
            diagnostics.Insert(0, BuildDiagnostic(snapshot.RefreshState.Failure));
        }

        return new WidgetRefreshPresentation
        {
            State = refreshState,
            StateText = WidgetPresentationFormatter.FormatRefreshStateText(refreshState),
            DetailText = detailText,
            CapturedAtUtc = snapshot.CapturedAtUtc,
            CapturedAtText = WidgetPresentationFormatter.FormatLocalInstantText("Captured", snapshot.CapturedAtUtc),
            SnapshotAge = age,
            SnapshotAgeText = WidgetPresentationFormatter.FormatSnapshotAgeText(age),
            NextScheduledRefreshAtUtc = snapshot.NextScheduledRefreshAtUtc,
            NextScheduledRefreshText = WidgetPresentationFormatter.FormatLocalInstantText("Next refresh", snapshot.NextScheduledRefreshAtUtc),
            Sources = sourcePresentations,
            Diagnostics = diagnostics,
        };
    }

    private static WidgetRefreshVisualState ResolveRefreshState(
        StatusSnapshot snapshot,
        WidgetPreferences preferences,
        DateTimeOffset nowUtc)
    {
        if (snapshot.RefreshState.Outcome == StatusRefreshOutcome.Running)
        {
            return WidgetRefreshVisualState.Refreshing;
        }

        if (snapshot.RefreshState.Outcome == StatusRefreshOutcome.Failed
            || snapshot.Sources.Any(source => source.State is SourceStatusState.Error or SourceStatusState.Malformed)
            || snapshot.Sources.SelectMany(source => source.Diagnostics).Any(diagnostic => diagnostic.Severity == SourceDiagnosticSeverity.Error))
        {
            return WidgetRefreshVisualState.Error;
        }

        var fallbackDueAtUtc = snapshot.CapturedAtUtc + TimeSpan.FromSeconds(Math.Clamp(
            preferences.RefreshPeriodSeconds,
            WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds,
            WidgetPreferenceDefaults.MaximumRefreshPeriodSeconds));
        var dueAtUtc = snapshot.NextScheduledRefreshAtUtc ?? fallbackDueAtUtc;
        var staleBySchedule = nowUtc >= dueAtUtc;
        var staleBySource = snapshot.Sources.Any(source => source.State == SourceStatusState.Stale);
        if (staleBySchedule || staleBySource)
        {
            return WidgetRefreshVisualState.Stale;
        }

        if (snapshot.Profiles.Count == 0
            || snapshot.Sources.Any(source => source.State is SourceStatusState.Missing or SourceStatusState.Unavailable))
        {
            return WidgetRefreshVisualState.Unavailable;
        }

        if (snapshot.RefreshState.Outcome == StatusRefreshOutcome.Cancelled
            || snapshot.Sources.SelectMany(source => source.Diagnostics).Any(diagnostic => diagnostic.Severity == SourceDiagnosticSeverity.Warning))
        {
            return WidgetRefreshVisualState.Warning;
        }

        return WidgetRefreshVisualState.Idle;
    }

    private static string BuildRefreshDetail(StatusSnapshot snapshot, WidgetRefreshVisualState state)
    {
        if (state == WidgetRefreshVisualState.Refreshing)
        {
            return "Refreshing profile and usage status.";
        }

        if (state == WidgetRefreshVisualState.Error)
        {
            if (snapshot.RefreshState.Failure is not null)
            {
                return $"Latest refresh failed: {BuildDiagnostic(snapshot.RefreshState.Failure).SummaryText}";
            }

            return "One or more status sources reported an error.";
        }

        if (state == WidgetRefreshVisualState.Stale)
        {
            return "Snapshot is stale and waiting for refresh.";
        }

        if (state == WidgetRefreshVisualState.Unavailable)
        {
            return "Status snapshot is unavailable.";
        }

        if (state == WidgetRefreshVisualState.Warning)
        {
            return "Snapshot has warning-level status conditions.";
        }

        return "Status snapshot is current.";
    }

    private static WidgetSourcePresentation BuildSource(SourceStatus source)
    {
        var diagnostics = source.Diagnostics.Select(BuildDiagnostic).ToArray();
        return new WidgetSourcePresentation
        {
            Source = source.Source,
            State = source.State,
            SourceText = $"Source: {source.Source}.",
            StateText = $"State: {source.State}.",
            AvailabilityText = WidgetPresentationFormatter.FormatAvailabilityText("Source", source.Availability),
            ObservedAtText = WidgetPresentationFormatter.FormatLocalInstantText("Observed", source.ObservedAtUtc),
            Diagnostics = diagnostics,
        };
    }

    private static WidgetDiagnosticPresentation BuildDiagnostic(SourceDiagnostic diagnostic)
    {
        var redacted = diagnostic.WithRedactedContent();
        var summary = RedactionHelper.RedactDiagnosticValue("summary", redacted.Summary);
        var detail = string.IsNullOrWhiteSpace(redacted.Detail)
            ? null
            : RedactionHelper.RedactDiagnosticValue("detail", redacted.Detail);
        var context = RedactionHelper.RedactDiagnosticContext(redacted.Context.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));

        return new WidgetDiagnosticPresentation
        {
            Code = redacted.Code,
            Severity = redacted.Severity,
            SummaryText = summary,
            DetailText = detail,
            Context = context,
            ObservedAtUtc = redacted.ObservedAtUtc,
            ObservedAtText = WidgetPresentationFormatter.FormatLocalInstantText("Observed", redacted.ObservedAtUtc),
        };
    }

}
