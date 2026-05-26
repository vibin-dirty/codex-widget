namespace CodexWidget.Core;

public sealed class StatusProjectionService
{
    private static readonly StringComparer IdentifierComparer = StringComparer.OrdinalIgnoreCase;
    private readonly IClock clock;

    public StatusProjectionService(IClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public StatusAllProfileProjection ProjectStatusAll(ProfileStatus profileStatus)
    {
        return ProjectStatusAll(profileStatus, WidgetPreferenceDefaults.Create());
    }

    public StatusAllProfileProjection ProjectStatusAll(ProfileStatus profileStatus, WidgetPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(profileStatus);
        ArgumentNullException.ThrowIfNull(preferences);

        var nowUtc = clock.UtcNow;
        var classified = ClassifyBuckets(profileStatus);
        var mainBucket = ProjectBucket(classified.MainBucket, nowUtc, preferences.WorkSchedule);
        var sparkBucket = ProjectBucket(classified.SparkBucket, nowUtc, preferences.WorkSchedule);

        return new StatusAllProfileProjection
        {
            Id = profileStatus.Profile.ProfileId,
            Name = profileStatus.Profile.DisplayName,
            LoginName = profileStatus.Profile.LoginName,
            IsCurrent = profileStatus.Profile.IsCurrent,
            SubscriptionTier = profileStatus.Profile.SubscriptionTier,
            Main5HourLeftPercent = mainBucket?.FiveHourWindow?.QuotaLeftPercent,
            MainWeeklyLeftPercent = mainBucket?.WeeklyWindow?.QuotaLeftPercent,
            Spark5HourLeftPercent = sparkBucket?.FiveHourWindow?.QuotaLeftPercent,
            SparkWeeklyLeftPercent = sparkBucket?.WeeklyWindow?.QuotaLeftPercent,
            Main5HourEndsAtUnixSeconds = mainBucket?.FiveHourWindow?.EndsAtUnixSeconds,
            MainWeeklyEndsAtUnixSeconds = mainBucket?.WeeklyWindow?.EndsAtUnixSeconds,
            Spark5HourEndsAtUnixSeconds = sparkBucket?.FiveHourWindow?.EndsAtUnixSeconds,
            SparkWeeklyEndsAtUnixSeconds = sparkBucket?.WeeklyWindow?.EndsAtUnixSeconds,
            Main5HourTimeLeftPercent = mainBucket?.FiveHourWindow?.TimeLeftPercent,
            MainWeeklyTimeLeftPercent = mainBucket?.WeeklyWindow?.TimeLeftPercent,
            Spark5HourTimeLeftPercent = sparkBucket?.FiveHourWindow?.TimeLeftPercent,
            SparkWeeklyTimeLeftPercent = sparkBucket?.WeeklyWindow?.TimeLeftPercent,
        };
    }

    public MinimalStatusViewProjection ProjectMinimal(StatusSnapshot snapshot)
    {
        return ProjectMinimal(snapshot, WidgetPreferenceDefaults.Create());
    }

    public MinimalStatusViewProjection ProjectMinimal(StatusSnapshot snapshot, WidgetPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(preferences);

        var currentProfile = SelectCurrentProfile(snapshot);
        if (currentProfile is null)
        {
            return new MinimalStatusViewProjection
            {
                CapturedAtUtc = snapshot.CapturedAtUtc,
            };
        }

        var nowUtc = clock.UtcNow;
        var classified = ClassifyBuckets(currentProfile);
        var mainBucket = ProjectBucket(classified.MainBucket, nowUtc, preferences.WorkSchedule);

        return new MinimalStatusViewProjection
        {
            CapturedAtUtc = snapshot.CapturedAtUtc,
            CurrentProfile = new MinimalProfileProjection
            {
                StatusAll = BuildStatusAll(currentProfile, mainBucket, sparkBucket: null),
                MainBucket = mainBucket,
            },
        };
    }

    public CompactStatusViewProjection ProjectCompact(StatusSnapshot snapshot)
    {
        return ProjectCompact(snapshot, WidgetPreferenceDefaults.Create());
    }

    public CompactStatusViewProjection ProjectCompact(StatusSnapshot snapshot, WidgetPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(preferences);

        var nowUtc = clock.UtcNow;
        var profiles = snapshot.Profiles
            .Select(profile => BuildCompactProfile(profile, nowUtc, preferences.WorkSchedule))
            .ToArray();

        return new CompactStatusViewProjection
        {
            CapturedAtUtc = snapshot.CapturedAtUtc,
            Profiles = profiles,
        };
    }

    public FullStatusViewProjection ProjectFull(StatusSnapshot snapshot)
    {
        return ProjectFull(snapshot, WidgetPreferenceDefaults.Create());
    }

    public FullStatusViewProjection ProjectFull(StatusSnapshot snapshot, WidgetPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(preferences);

        var nowUtc = clock.UtcNow;
        var profiles = snapshot.Profiles
            .Select(profile => BuildFullProfile(profile, nowUtc, preferences.WorkSchedule))
            .ToArray();

        return new FullStatusViewProjection
        {
            CapturedAtUtc = snapshot.CapturedAtUtc,
            Profiles = profiles,
        };
    }

    private static StatusAllProfileProjection BuildStatusAll(ProfileStatus profileStatus, ProjectedUsageBucket? mainBucket, ProjectedUsageBucket? sparkBucket)
    {
        return new StatusAllProfileProjection
        {
            Id = profileStatus.Profile.ProfileId,
            Name = profileStatus.Profile.DisplayName,
            LoginName = profileStatus.Profile.LoginName,
            IsCurrent = profileStatus.Profile.IsCurrent,
            SubscriptionTier = profileStatus.Profile.SubscriptionTier,
            Main5HourLeftPercent = mainBucket?.FiveHourWindow?.QuotaLeftPercent,
            MainWeeklyLeftPercent = mainBucket?.WeeklyWindow?.QuotaLeftPercent,
            Spark5HourLeftPercent = sparkBucket?.FiveHourWindow?.QuotaLeftPercent,
            SparkWeeklyLeftPercent = sparkBucket?.WeeklyWindow?.QuotaLeftPercent,
            Main5HourEndsAtUnixSeconds = mainBucket?.FiveHourWindow?.EndsAtUnixSeconds,
            MainWeeklyEndsAtUnixSeconds = mainBucket?.WeeklyWindow?.EndsAtUnixSeconds,
            Spark5HourEndsAtUnixSeconds = sparkBucket?.FiveHourWindow?.EndsAtUnixSeconds,
            SparkWeeklyEndsAtUnixSeconds = sparkBucket?.WeeklyWindow?.EndsAtUnixSeconds,
            Main5HourTimeLeftPercent = mainBucket?.FiveHourWindow?.TimeLeftPercent,
            MainWeeklyTimeLeftPercent = mainBucket?.WeeklyWindow?.TimeLeftPercent,
            Spark5HourTimeLeftPercent = sparkBucket?.FiveHourWindow?.TimeLeftPercent,
            SparkWeeklyTimeLeftPercent = sparkBucket?.WeeklyWindow?.TimeLeftPercent,
        };
    }

    private CompactProfileProjection BuildCompactProfile(
        ProfileStatus profileStatus,
        DateTimeOffset nowUtc,
        WeeklyWorkSchedule workSchedule)
    {
        var classified = ClassifyBuckets(profileStatus);
        var mainBucket = ProjectBucket(classified.MainBucket, nowUtc, workSchedule);
        var sparkBucket = ProjectBucket(classified.SparkBucket, nowUtc, workSchedule);

        return new CompactProfileProjection
        {
            StatusAll = BuildStatusAll(profileStatus, mainBucket, sparkBucket),
            MainBucket = mainBucket,
            SparkBucket = sparkBucket,
        };
    }

    private FullProfileProjection BuildFullProfile(
        ProfileStatus profileStatus,
        DateTimeOffset nowUtc,
        WeeklyWorkSchedule workSchedule)
    {
        var classified = ClassifyBuckets(profileStatus);
        var mainBucket = ProjectBucket(classified.MainBucket, nowUtc, workSchedule);
        var sparkBucket = ProjectBucket(classified.SparkBucket, nowUtc, workSchedule);
        var additionalBuckets = classified.AdditionalBuckets
            .Select(bucket => ProjectBucket(bucket, nowUtc, workSchedule))
            .Where(bucket => bucket is not null)
            .Cast<ProjectedUsageBucket>()
            .ToArray();

        return new FullProfileProjection
        {
            StatusAll = BuildStatusAll(profileStatus, mainBucket, sparkBucket),
            MainBucket = mainBucket,
            SparkBucket = sparkBucket,
            AdditionalBuckets = additionalBuckets,
            Diagnostics = profileStatus.Diagnostics,
        };
    }

    private static ProfileStatus? SelectCurrentProfile(StatusSnapshot snapshot)
    {
        var profileByFlag = snapshot.Profiles.FirstOrDefault(profile => profile.Profile.IsCurrent);
        if (profileByFlag is not null)
        {
            return profileByFlag;
        }

        if (string.IsNullOrWhiteSpace(snapshot.CurrentProfileId))
        {
            return null;
        }

        return snapshot.Profiles.FirstOrDefault(profile => IdentifierComparer.Equals(profile.Profile.ProfileId, snapshot.CurrentProfileId));
    }

    private static ProjectedUsageBucket? ProjectBucket(
        UsageBucketSnapshot? bucket,
        DateTimeOffset nowUtc,
        WeeklyWorkSchedule workSchedule)
    {
        if (bucket is null)
        {
            return null;
        }

        var windows = ProjectWindows(bucket.Windows, nowUtc, workSchedule);
        var fiveHour = windows.FirstOrDefault(window => window.WindowKind == UsageWindowKind.FiveHour);
        var weekly = windows.FirstOrDefault(window => window.WindowKind == UsageWindowKind.Weekly);

        return new ProjectedUsageBucket
        {
            BucketId = bucket.BucketId,
            BucketLabel = bucket.BucketLabel,
            BucketKind = bucket.BucketKind,
            FetchStatus = bucket.FetchStatus,
            Availability = bucket.Availability,
            FiveHourWindow = fiveHour,
            WeeklyWindow = weekly,
            Windows = windows,
        };
    }

    private static IReadOnlyList<ProjectedUsageWindow> ProjectWindows(
        IReadOnlyList<UsageWindowSnapshot> windows,
        DateTimeOffset nowUtc,
        WeeklyWorkSchedule workSchedule)
    {
        if (windows.Count == 0)
        {
            return Array.Empty<ProjectedUsageWindow>();
        }

        var withIndex = windows.Select((window, index) => new WindowIndex(window, index)).ToArray();
        var ranked = withIndex
            .Where(entry => entry.Window.DurationSeconds is > 0)
            .OrderBy(entry => entry.Window.DurationSeconds)
            .ThenBy(entry => entry.Index)
            .ToArray();

        var mappedKinds = new Dictionary<int, UsageWindowKind>();
        for (var i = 0; i < ranked.Length; i++)
        {
            mappedKinds[ranked[i].Index] = i switch
            {
                0 => UsageWindowKind.FiveHour,
                1 => UsageWindowKind.Weekly,
                _ => UsageWindowKind.Additional,
            };
        }

        var projected = new List<ProjectedUsageWindow>(windows.Count);
        for (var i = 0; i < withIndex.Length; i++)
        {
            var source = withIndex[i].Window;
            var mappedKind = mappedKinds.TryGetValue(i, out var kind)
                ? kind
                : UsageWindowKind.Additional;
            projected.Add(new ProjectedUsageWindow
            {
                WindowKind = mappedKind,
                DurationSeconds = source.DurationSeconds,
                EndsAtUnixSeconds = source.ResetAtUnixSeconds,
                UsedPercent = source.UsedPercent,
                QuotaLeftPercent = UsageCalculations.CalculateQuotaLeftPercent(source.UsedPercent),
                TimeLeftPercent = UsageCalculations.CalculateWindowTimeLeftPercent(
                    mappedKind,
                    source.ResetAtUnixSeconds,
                    source.DurationSeconds,
                    nowUtc,
                    workSchedule),
                Availability = ResolveWindowAvailability(source),
            });
        }

        return projected;
    }

    private static StatusAvailability ResolveWindowAvailability(UsageWindowSnapshot window)
    {
        if (window.Availability.State == StatusAvailabilityState.Unavailable)
        {
            return window.Availability;
        }

        if (window.DurationSeconds is null || window.DurationSeconds <= 0 || window.ResetAtUnixSeconds is null)
        {
            return StatusAvailability.Unavailable(StatusAvailabilityCode.MissingTimestampOrDuration);
        }

        if (!window.UsedPercent.HasValue)
        {
            return StatusAvailability.Unavailable(StatusAvailabilityCode.MissingRequiredField);
        }

        return StatusAvailability.Available();
    }

    private static BucketClassification ClassifyBuckets(ProfileStatus profileStatus)
    {
        var allBuckets = DeduplicateBuckets(profileStatus.AllBuckets);
        var mainBucket = profileStatus.MainBucket
            ?? allBuckets.FirstOrDefault(IsMainBucket);
        var sparkBucket = profileStatus.SparkBucket
            ?? allBuckets.FirstOrDefault(IsSparkBucket);
        var additionalBuckets = allBuckets
            .Where(bucket => mainBucket is null || !IsSameBucket(bucket, mainBucket))
            .Where(bucket => sparkBucket is null || !IsSameBucket(bucket, sparkBucket))
            .ToArray();

        return new BucketClassification(mainBucket, sparkBucket, additionalBuckets);
    }

    private static IReadOnlyList<UsageBucketSnapshot> DeduplicateBuckets(IReadOnlyList<UsageBucketSnapshot> buckets)
    {
        if (buckets.Count == 0)
        {
            return Array.Empty<UsageBucketSnapshot>();
        }

        var deduplicated = new List<UsageBucketSnapshot>(buckets.Count);
        foreach (var bucket in buckets)
        {
            if (deduplicated.Any(existing => IsSameBucket(existing, bucket)))
            {
                continue;
            }

            deduplicated.Add(bucket);
        }

        return deduplicated;
    }

    private static bool IsSameBucket(UsageBucketSnapshot left, UsageBucketSnapshot right)
    {
        return IdentifierComparer.Equals(left.BucketId, right.BucketId)
            && IdentifierComparer.Equals(left.BucketLabel, right.BucketLabel);
    }

    private static bool IsMainBucket(UsageBucketSnapshot bucket)
    {
        if (bucket.BucketKind == UsageBucketKind.MainCodex)
        {
            return true;
        }

        return IdentifierComparer.Equals(bucket.BucketId, "codex");
    }

    private static bool IsSparkBucket(UsageBucketSnapshot bucket)
    {
        if (bucket.BucketKind == UsageBucketKind.Spark)
        {
            return true;
        }

        if (bucket.BucketKind != UsageBucketKind.Additional)
        {
            return false;
        }

        return ContainsSparkIdentifier(bucket.BucketId) || ContainsSparkIdentifier(bucket.BucketLabel);
    }

    private static bool ContainsSparkIdentifier(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains("spark", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record WindowIndex(UsageWindowSnapshot Window, int Index);

    private sealed record BucketClassification(
        UsageBucketSnapshot? MainBucket,
        UsageBucketSnapshot? SparkBucket,
        IReadOnlyList<UsageBucketSnapshot> AdditionalBuckets);
}
