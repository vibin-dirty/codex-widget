using System.Reflection;

namespace CodexWidget.Core.Tests;

public sealed class StatusProjectionServiceTests
{
    [Theory]
    [InlineData(0.0, 100)]
    [InlineData(12.2, 88)]
    [InlineData(49.5, 51)]
    [InlineData(99.6, 0)]
    [InlineData(-20.0, 100)]
    [InlineData(140.0, 0)]
    public void CalculateQuotaLeftPercent_ClampsAndRounds(double usedPercent, int expected)
    {
        Assert.Equal(expected, UsageCalculations.CalculateQuotaLeftPercent(usedPercent));
    }

    [Fact]
    public void CalculateQuotaLeftPercent_ReturnsNullWhenUsageMissing()
    {
        Assert.Null(UsageCalculations.CalculateQuotaLeftPercent(null));
    }

    [Fact]
    public void CalculateTimeLeftPercent_ClampsRoundsAndHandlesExpiredWindows()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000);

        Assert.Equal(50, UsageCalculations.CalculateTimeLeftPercent(1_900, 1_800, now));
        Assert.Equal(0, UsageCalculations.CalculateTimeLeftPercent(900, 1_800, now));
        Assert.Equal(0, UsageCalculations.CalculateTimeLeftPercent(1_000, 1_800, now));
        Assert.Equal(100, UsageCalculations.CalculateTimeLeftPercent(4_000, 1_800, now));
    }

    [Fact]
    public void CalculateTimeLeftPercent_ReturnsNullWhenResetOrDurationMissingOrInvalid()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000);

        Assert.Null(UsageCalculations.CalculateTimeLeftPercent(null, 1_800, now));
        Assert.Null(UsageCalculations.CalculateTimeLeftPercent(1_900, null, now));
        Assert.Null(UsageCalculations.CalculateTimeLeftPercent(1_900, 0, now));
        Assert.Null(UsageCalculations.CalculateTimeLeftPercent(1_900, -5, now));
    }

    [Fact]
    public void CalculateWindowTimeLeftPercent_FiveHourUsesDurationBasedPercentage()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000);

        Assert.Equal(50, UsageCalculations.CalculateWindowTimeLeftPercent(UsageWindowKind.FiveHour, 1_900, 1_800, now));
    }

    [Theory]
    [InlineData(2026, 1, 5, 7, 0, 2026, 1, 12, 7, 0, 1, 1, 100)]
    [InlineData(2026, 1, 5, 12, 0, 2026, 1, 6, 12, 0, 1, 1, 18)]
    [InlineData(2026, 1, 5, 18, 0, 2026, 1, 6, 7, 0, 1, 1, 0)]
    [InlineData(2026, 1, 10, 19, 0, 2026, 1, 11, 21, 30, 1, 1, 8)]
    [InlineData(2026, 6, 1, 7, 0, 2026, 6, 8, 7, 0, 2, 2, 100)]
    [InlineData(2026, 1, 5, 7, 0, 2026, 1, 13, 17, 0, 1, 1, 100)]
    public void CalculateWindowTimeLeftPercent_WeeklyUsesWarsawWorkTime(
        int startYear,
        int startMonth,
        int startDay,
        int startHour,
        int startMinute,
        int resetYear,
        int resetMonth,
        int resetDay,
        int resetHour,
        int resetMinute,
        int startOffsetHours,
        int resetOffsetHours,
        int expected)
    {
        var now = new DateTimeOffset(startYear, startMonth, startDay, startHour, startMinute, 0, TimeSpan.FromHours(startOffsetHours));
        var resetAt = new DateTimeOffset(resetYear, resetMonth, resetDay, resetHour, resetMinute, 0, TimeSpan.FromHours(resetOffsetHours));

        Assert.Equal(expected, UsageCalculations.CalculateWindowTimeLeftPercent(UsageWindowKind.Weekly, resetAt.ToUnixTimeSeconds(), 604_800, now));
    }

    [Fact]
    public void CalculateWindowTimeLeftPercent_WeeklyUsesConfiguredSchedule()
    {
        var now = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.FromHours(1));
        var resetAt = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.FromHours(1));
        var schedule = new WeeklyWorkSchedule
        {
            Monday = new DayWorkSchedule
            {
                Windows =
                [
                    new WorkWindow { Start = new TimeOnly(7, 0), End = new TimeOnly(9, 0) },
                ],
            },
        };

        Assert.Equal(
            50,
            UsageCalculations.CalculateWindowTimeLeftPercent(
                UsageWindowKind.Weekly,
                resetAt.ToUnixTimeSeconds(),
                604_800,
                now,
                schedule));
    }

    [Fact]
    public void CalculateWindowTimeLeftPercent_WeeklyReturnsNullForZeroTotalConfiguredWeek()
    {
        var now = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.FromHours(1));
        var resetAt = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.FromHours(1));

        Assert.Null(
            UsageCalculations.CalculateWindowTimeLeftPercent(
                UsageWindowKind.Weekly,
                resetAt.ToUnixTimeSeconds(),
                604_800,
                now,
                new WeeklyWorkSchedule()));
    }

    [Fact]
    public void ProjectStatusAll_MapsSingleWindowToFiveHourAndLeavesWeeklyUnavailable()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(10_000);
        var service = new StatusProjectionService(new FixedClock(now));
        var profileStatus = CreateProfileStatus(
            isCurrent: true,
            mainBucket: CreateBucket(
                id: "codex",
                label: "codex",
                kind: UsageBucketKind.MainCodex,
                windows:
                [
                    new UsageWindowSnapshot
                    {
                        DurationSeconds = 18_000,
                        ResetAtUnixSeconds = now.ToUnixTimeSeconds() + 9_000,
                        UsedPercent = 40.0,
                        Availability = StatusAvailability.Available(),
                    },
                ]));

        var projection = service.ProjectStatusAll(profileStatus);

        Assert.Equal(60, projection.Main5HourLeftPercent);
        Assert.Equal(50, projection.Main5HourTimeLeftPercent);
        Assert.Equal(now.ToUnixTimeSeconds() + 9_000, projection.Main5HourEndsAtUnixSeconds);
        Assert.Null(projection.MainWeeklyLeftPercent);
        Assert.Null(projection.MainWeeklyTimeLeftPercent);
        Assert.Null(projection.MainWeeklyEndsAtUnixSeconds);
    }

    [Fact]
    public void ProjectStatusAll_MapsTwoWindowsByDurationAscending()
    {
        var now = new DateTimeOffset(2026, 1, 5, 7, 0, 0, TimeSpan.FromHours(1));
        var service = new StatusProjectionService(new FixedClock(now));
        var profileStatus = CreateProfileStatus(
            isCurrent: true,
            mainBucket: CreateBucket(
                id: "codex",
                label: "codex",
                kind: UsageBucketKind.MainCodex,
                windows:
                [
                    new UsageWindowSnapshot
                    {
                        DurationSeconds = 604_800,
                        ResetAtUnixSeconds = now.AddDays(7).ToUnixTimeSeconds(),
                        UsedPercent = 20.0,
                        Availability = StatusAvailability.Available(),
                    },
                    new UsageWindowSnapshot
                    {
                        DurationSeconds = 18_000,
                        ResetAtUnixSeconds = now.ToUnixTimeSeconds() + 9_000,
                        UsedPercent = 75.0,
                        Availability = StatusAvailability.Available(),
                    },
                ]));

        var projection = service.ProjectStatusAll(profileStatus);

        Assert.Equal(25, projection.Main5HourLeftPercent);
        Assert.Equal(80, projection.MainWeeklyLeftPercent);
        Assert.Equal(50, projection.Main5HourTimeLeftPercent);
        Assert.Equal(100, projection.MainWeeklyTimeLeftPercent);
    }

    [Fact]
    public void ProjectStatusAll_UsesConfiguredWeeklyScheduleForTimeLeftProjection()
    {
        var now = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.FromHours(1));
        var service = new StatusProjectionService(new FixedClock(now));
        var profileStatus = CreateProfileStatus(
            isCurrent: true,
            mainBucket: CreateBucket(
                id: "codex",
                label: "codex",
                kind: UsageBucketKind.MainCodex,
                windows:
                [
                    new UsageWindowSnapshot
                    {
                        DurationSeconds = 18_000,
                        ResetAtUnixSeconds = now.AddHours(1).ToUnixTimeSeconds(),
                        UsedPercent = 40.0,
                        Availability = StatusAvailability.Available(),
                    },
                    new UsageWindowSnapshot
                    {
                        DurationSeconds = 604_800,
                        ResetAtUnixSeconds = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.FromHours(1)).ToUnixTimeSeconds(),
                        UsedPercent = 20.0,
                        Availability = StatusAvailability.Available(),
                    },
                ]));
        var preferences = WidgetPreferenceDefaults.Create() with
        {
            WorkSchedule = new WeeklyWorkSchedule
            {
                Monday = new DayWorkSchedule
                {
                    Windows =
                    [
                        new WorkWindow { Start = new TimeOnly(7, 0), End = new TimeOnly(9, 0) },
                    ],
                },
            },
        };

        var projection = service.ProjectStatusAll(profileStatus, preferences);

        Assert.Equal(50, projection.MainWeeklyTimeLeftPercent);
    }

    [Fact]
    public void ProjectStatusAll_LeavesFieldsNullWhenMainBucketOrWindowsAreMissing()
    {
        var service = new StatusProjectionService(new FixedClock(DateTimeOffset.FromUnixTimeSeconds(30_000)));
        var profileStatus = CreateProfileStatus(isCurrent: true, mainBucket: null);

        var projection = service.ProjectStatusAll(profileStatus);

        Assert.Null(projection.Main5HourLeftPercent);
        Assert.Null(projection.MainWeeklyLeftPercent);
        Assert.Null(projection.Main5HourTimeLeftPercent);
        Assert.Null(projection.MainWeeklyTimeLeftPercent);
        Assert.Null(projection.Main5HourEndsAtUnixSeconds);
        Assert.Null(projection.MainWeeklyEndsAtUnixSeconds);
    }

    [Fact]
    public void ProjectStatusAll_LeavesTimeLeftUnavailableWhenResetOrDurationMissing()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(40_000);
        var service = new StatusProjectionService(new FixedClock(now));
        var profileStatus = CreateProfileStatus(
            isCurrent: true,
            mainBucket: CreateBucket(
                id: "codex",
                label: "codex",
                kind: UsageBucketKind.MainCodex,
                windows:
                [
                    new UsageWindowSnapshot
                    {
                        DurationSeconds = null,
                        ResetAtUnixSeconds = now.ToUnixTimeSeconds() + 100,
                        UsedPercent = 10.0,
                        Availability = StatusAvailability.Available(),
                    },
                ]));

        var projection = service.ProjectStatusAll(profileStatus);

        Assert.Null(projection.Main5HourTimeLeftPercent);
        Assert.Null(projection.Main5HourEndsAtUnixSeconds);
        Assert.Null(projection.Main5HourLeftPercent);
    }

    [Fact]
    public void ProjectCompact_IdentifiesSparkSummaryFromAdditionalBucket()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(50_000);
        var service = new StatusProjectionService(new FixedClock(now));
        var mainBucket = CreateBucket(
            id: "codex",
            label: "codex",
            kind: UsageBucketKind.MainCodex,
            windows:
            [
                CreateWindow(18_000, now.ToUnixTimeSeconds() + 9_000, 30.0),
                CreateWindow(604_800, now.ToUnixTimeSeconds() + 302_400, 45.0),
            ]);
        var sparkBucket = CreateBucket(
            id: "metered_spark_compute",
            label: "Spark",
            kind: UsageBucketKind.Additional,
            windows:
            [
                CreateWindow(18_000, now.ToUnixTimeSeconds() + 9_000, 60.0),
                CreateWindow(604_800, now.ToUnixTimeSeconds() + 302_400, 70.0),
            ]);
        var otherBucket = CreateBucket(
            id: "extra_bucket",
            label: "Extra",
            kind: UsageBucketKind.Additional,
            windows:
            [
                CreateWindow(18_000, now.ToUnixTimeSeconds() + 9_000, 50.0),
            ]);
        var snapshot = new StatusSnapshot
        {
            CapturedAtUtc = now,
            Profiles =
            [
                CreateProfileStatus(
                    isCurrent: true,
                    mainBucket: mainBucket,
                    allBuckets:
                    [
                        mainBucket,
                        sparkBucket,
                        otherBucket,
                    ]),
            ],
        };

        var projection = service.ProjectCompact(snapshot);
        var profile = Assert.Single(projection.Profiles);

        Assert.NotNull(profile.SparkBucket);
        Assert.Equal(40, profile.StatusAll.Spark5HourLeftPercent);
        Assert.Equal(30, profile.StatusAll.SparkWeeklyLeftPercent);
    }

    [Fact]
    public void ProjectCompact_LeavesSparkFieldsNullWhenNoSparkBucketExists()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(60_000);
        var service = new StatusProjectionService(new FixedClock(now));
        var mainBucket = CreateBucket(
            id: "codex",
            label: "codex",
            kind: UsageBucketKind.MainCodex,
            windows:
            [
                CreateWindow(18_000, now.ToUnixTimeSeconds() + 9_000, 30.0),
            ]);
        var snapshot = new StatusSnapshot
        {
            CapturedAtUtc = now,
            Profiles =
            [
                CreateProfileStatus(
                    isCurrent: true,
                    mainBucket: mainBucket,
                    allBuckets:
                    [
                        mainBucket,
                        CreateBucket(
                            id: "other",
                            label: "Other",
                            kind: UsageBucketKind.Additional,
                            windows:
                            [
                                CreateWindow(18_000, now.ToUnixTimeSeconds() + 9_000, 40.0),
                            ]),
                    ]),
            ],
        };

        var projection = service.ProjectCompact(snapshot);
        var profile = Assert.Single(projection.Profiles);

        Assert.Null(profile.SparkBucket);
        Assert.Null(profile.StatusAll.Spark5HourLeftPercent);
        Assert.Null(profile.StatusAll.SparkWeeklyLeftPercent);
    }

    [Fact]
    public void ProjectMinimal_UsesCurrentProfileAndMainBucketOnly()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(70_000);
        var service = new StatusProjectionService(new FixedClock(now));
        var currentMainBucket = CreateBucket(
            id: "codex",
            label: "codex",
            kind: UsageBucketKind.MainCodex,
            windows:
            [
                CreateWindow(18_000, now.ToUnixTimeSeconds() + 9_000, 20.0),
            ]);
        var notCurrentMainBucket = CreateBucket(
            id: "codex",
            label: "codex",
            kind: UsageBucketKind.MainCodex,
            windows:
            [
                CreateWindow(18_000, now.ToUnixTimeSeconds() + 9_000, 80.0),
            ]);
        var snapshot = new StatusSnapshot
        {
            CapturedAtUtc = now,
            Profiles =
            [
                CreateProfileStatus(
                    id: "a",
                    name: "A",
                    isCurrent: false,
                    mainBucket: notCurrentMainBucket),
                CreateProfileStatus(
                    id: "b",
                    name: "B",
                    isCurrent: true,
                    mainBucket: currentMainBucket),
            ],
        };

        var projection = service.ProjectMinimal(snapshot);

        Assert.NotNull(projection.CurrentProfile);
        Assert.Equal("b", projection.CurrentProfile!.StatusAll.Id);
        Assert.Equal(80, projection.CurrentProfile.StatusAll.Main5HourLeftPercent);
        Assert.Null(projection.CurrentProfile.StatusAll.Spark5HourLeftPercent);
    }

    [Fact]
    public void ProjectFull_IncludesAdditionalBucketsBeyondMainAndSpark()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(80_000);
        var service = new StatusProjectionService(new FixedClock(now));
        var mainBucket = CreateBucket(
            id: "codex",
            label: "codex",
            kind: UsageBucketKind.MainCodex,
            windows:
            [
                CreateWindow(18_000, now.ToUnixTimeSeconds() + 9_000, 10.0),
            ]);
        var sparkBucket = CreateBucket(
            id: "spark_feature",
            label: "Spark",
            kind: UsageBucketKind.Additional,
            windows:
            [
                CreateWindow(18_000, now.ToUnixTimeSeconds() + 9_000, 20.0),
            ]);
        var extraBucket = CreateBucket(
            id: "other_feature",
            label: "Other",
            kind: UsageBucketKind.Additional,
            windows:
            [
                CreateWindow(18_000, now.ToUnixTimeSeconds() + 9_000, 30.0),
            ]);
        var profile = CreateProfileStatus(
            isCurrent: true,
            mainBucket: mainBucket,
            allBuckets:
            [
                mainBucket,
                sparkBucket,
                extraBucket,
            ]);
        var snapshot = new StatusSnapshot
        {
            CapturedAtUtc = now,
            Profiles = [profile],
        };

        var projection = service.ProjectFull(snapshot);
        var fullProfile = Assert.Single(projection.Profiles);
        var additional = Assert.Single(fullProfile.AdditionalBuckets);

        Assert.Equal("other_feature", additional.BucketId);
        Assert.NotNull(fullProfile.MainBucket);
        Assert.NotNull(fullProfile.SparkBucket);
    }

    [Fact]
    public void ProjectionContracts_KeepResetFieldsAsUnixSeconds()
    {
        Assert.Equal(typeof(long?), typeof(StatusAllProfileProjection).GetProperty(nameof(StatusAllProfileProjection.Main5HourEndsAtUnixSeconds), BindingFlags.Public | BindingFlags.Instance)!.PropertyType);
        Assert.Equal(typeof(long?), typeof(ProjectedUsageWindow).GetProperty(nameof(ProjectedUsageWindow.EndsAtUnixSeconds), BindingFlags.Public | BindingFlags.Instance)!.PropertyType);
    }

    private static ProfileStatus CreateProfileStatus(
        bool isCurrent,
        UsageBucketSnapshot? mainBucket,
        IReadOnlyList<UsageBucketSnapshot>? allBuckets = null,
        string? id = "profile-1",
        string? name = "Profile 1")
    {
        return new ProfileStatus
        {
            Profile = new ProfileDescriptor
            {
                ProfileId = id,
                DisplayName = name,
                LoginName = "user@example.com",
                SubscriptionTier = SubscriptionTier.Pro,
                IsCurrent = isCurrent,
            },
            MainBucket = mainBucket,
            AllBuckets = allBuckets ?? (mainBucket is null ? [] : [mainBucket]),
        };
    }

    private static UsageBucketSnapshot CreateBucket(
        string id,
        string label,
        UsageBucketKind kind,
        IReadOnlyList<UsageWindowSnapshot> windows)
    {
        return new UsageBucketSnapshot
        {
            BucketId = id,
            BucketLabel = label,
            BucketKind = kind,
            Windows = windows,
            FetchStatus = UsageBucketFetchStatus.Succeeded,
            Availability = StatusAvailability.Available(),
        };
    }

    private static UsageWindowSnapshot CreateWindow(int durationSeconds, long resetAtUnixSeconds, double usedPercent)
    {
        return new UsageWindowSnapshot
        {
            DurationSeconds = durationSeconds,
            ResetAtUnixSeconds = resetAtUnixSeconds,
            UsedPercent = usedPercent,
            Availability = StatusAvailability.Available(),
        };
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
