using System.Globalization;
using System.Text.Json;
using CodexWidget.Presentation;
using CodexWidget.Core;
using CodexWidget.TestSupport;

namespace CodexWidget.Presentation.Tests;

public sealed class WidgetPresentationServiceTests
{
    [Fact]
    public void Build_CurrentProfileSnapshot_ProducesMinimalCompactAndFullStates()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            id: "current-profile",
            isCurrent: true,
            mainBucket: CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
            [
                CreateAvailableWindow(UsageWindowKind.FiveHour, 18_000, now.ToUnixTimeSeconds() + 5_400, 60.0),
                CreateAvailableWindow(UsageWindowKind.Weekly, 604_800, now.ToUnixTimeSeconds() + 302_400, 10.0),
            ]));
        var snapshot = CreateSnapshot(now, [profile]);

        var state = service.Build(snapshot, WidgetPreferenceDefaults.Create());

        Assert.NotNull(state.Minimal.CurrentProfile);
        Assert.Equal("current-profile", state.Minimal.CurrentProfile.ProfileId);
        Assert.Contains("Current profile", state.Minimal.SummaryText, StringComparison.Ordinal);
        Assert.Single(state.Compact.Profiles);
        Assert.Single(state.Full.Profiles);
        Assert.Equal("Profile is active.", state.Minimal.CurrentProfile.ActiveProfileText);
        Assert.Equal(WidgetRefreshVisualState.Idle, state.Refresh.State);
    }

    [Fact]
    public void Build_NoProfiles_ProducesUnavailableState()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_010_000);
        var service = CreateService(now);
        var snapshot = CreateSnapshot(now, []);

        var state = service.Build(snapshot, WidgetPreferenceDefaults.Create());

        Assert.Null(state.Minimal.CurrentProfile);
        Assert.Empty(state.Compact.Profiles);
        Assert.Empty(state.Full.Profiles);
        Assert.Equal(WidgetRefreshVisualState.Unavailable, state.Refresh.State);
        Assert.Contains("unavailable", state.Minimal.SummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_AvailableWindow_FormatsCompactTimestampText()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_015_000);
        var endAt = now.ToUnixTimeSeconds() + 3_600;
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            id: "profile-compact",
            isCurrent: true,
            mainBucket: CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
            [
                CreateAvailableWindow(UsageWindowKind.FiveHour, 18_000, endAt, 50.0),
            ]));
        var snapshot = CreateSnapshot(now, [profile]);

        var state = service.Build(snapshot, WidgetPreferenceDefaults.Create());
        var window = state.Minimal.CurrentProfile!.MainBucket!.FiveHourWindow!;
        var expectedCompact = DateTimeOffset.FromUnixTimeSeconds(endAt).ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

        Assert.Equal(expectedCompact, window.EndsAtCompactText);
    }

    [Fact]
    public void Build_UnavailableWindow_UsesUnavailableTextsInsteadOfNumericPlaceholders()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_020_000);
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            id: "profile-a",
            isCurrent: true,
            mainBucket: CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
            [
                new UsageWindowSnapshot
                {
                    WindowKind = UsageWindowKind.FiveHour,
                    DurationSeconds = 18_000,
                    ResetAtUnixSeconds = now.ToUnixTimeSeconds() + 1_000,
                    UsedPercent = null,
                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.MissingRequiredField),
                },
            ]));
        var snapshot = CreateSnapshot(now, [profile]);

        var state = service.Build(snapshot, WidgetPreferenceDefaults.Create());
        Assert.NotNull(state.Minimal.CurrentProfile);
        Assert.NotNull(state.Minimal.CurrentProfile!.MainBucket);
        Assert.NotNull(state.Minimal.CurrentProfile.MainBucket!.FiveHourWindow);
        var window = state.Minimal.CurrentProfile.MainBucket.FiveHourWindow!;

        Assert.Null(window.QuotaLeftPercent);
        Assert.Equal("Quota left: unavailable.", window.QuotaText);
        Assert.Null(window.TimeLeftPercent);
        Assert.Equal("Time left: unavailable.", window.TimeText);
        Assert.Null(window.EndsAtUnixSeconds);
        Assert.Equal(WidgetPresentationFormatter.CompactUnavailableTimestampToken, window.EndsAtCompactText);
        Assert.False(window.IsAvailable);
        Assert.False(window.HasQuotaLeft);
        Assert.False(window.HasTimeLeft);
        Assert.Contains("MissingRequiredField", window.AvailabilityText, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ExplicitZeroValues_RemainZeroAndMissingValuesRemainUnavailable()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_022_000);
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            id: "profile-zero-vs-missing",
            isCurrent: true,
            mainBucket: CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
            [
                new UsageWindowSnapshot
                {
                    WindowKind = UsageWindowKind.FiveHour,
                    DurationSeconds = 18_000,
                    ResetAtUnixSeconds = now.ToUnixTimeSeconds(),
                    UsedPercent = 100,
                    Availability = StatusAvailability.Available(),
                },
                new UsageWindowSnapshot
                {
                    WindowKind = UsageWindowKind.Weekly,
                    DurationSeconds = 604_800,
                    ResetAtUnixSeconds = now.ToUnixTimeSeconds() + 1,
                    UsedPercent = null,
                    Availability = StatusAvailability.Available(),
                },
            ]));

        var state = service.Build(CreateSnapshot(now, [profile]), WidgetPreferenceDefaults.Create());
        var mainBucket = state.Minimal.CurrentProfile!.MainBucket!;
        var explicitZero = mainBucket.FiveHourWindow!;
        var missingValues = mainBucket.WeeklyWindow!;

        Assert.True(explicitZero.IsAvailable);
        Assert.Equal(0, explicitZero.QuotaLeftPercent);
        Assert.Equal("Quota left: 0%.", explicitZero.QuotaText);
        Assert.Equal(0, explicitZero.TimeLeftPercent);
        Assert.Equal("Time left: 0%.", explicitZero.TimeText);
        Assert.Equal(now.ToUnixTimeSeconds(), explicitZero.EndsAtUnixSeconds);
        Assert.NotEqual(WidgetPresentationFormatter.CompactUnavailableTimestampToken, explicitZero.EndsAtCompactText);
        Assert.True(explicitZero.HasTimeLeft);

        Assert.False(missingValues.IsAvailable);
        Assert.Null(missingValues.QuotaLeftPercent);
        Assert.Equal("Quota left: unavailable.", missingValues.QuotaText);
        Assert.Null(missingValues.TimeLeftPercent);
        Assert.Equal("Time left: unavailable.", missingValues.TimeText);
        Assert.Null(missingValues.EndsAtUnixSeconds);
        Assert.Equal("Ends: unavailable.", missingValues.EndsAtText);
        Assert.Equal(WidgetPresentationFormatter.CompactUnavailableTimestampToken, missingValues.EndsAtCompactText);
        Assert.NotEqual("Quota left: 0%.", missingValues.QuotaText);
        Assert.NotEqual("Time left: 0%.", missingValues.TimeText);
        Assert.False(string.IsNullOrWhiteSpace(state.Refresh.DetailText));
    }

    [Fact]
    public void Build_DepletedUsageMetrics_DoNotCreateSemanticWarningOrCriticalState()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_030_000);
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            id: "profile-b",
            isCurrent: true,
            mainBucket: CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
            [
                CreateWindowFromLeftValues(UsageWindowKind.FiveHour, 18_000, now.ToUnixTimeSeconds() + 3_600, quotaLeft: 25),
                CreateWindowFromLeftValues(UsageWindowKind.Weekly, 604_800, now.ToUnixTimeSeconds(), quotaLeft: 10),
            ]));
        var snapshot = CreateSnapshot(now, [profile]);

        var state = service.Build(snapshot, WidgetPreferenceDefaults.Create());
        Assert.NotNull(state.Minimal.CurrentProfile);
        Assert.NotNull(state.Minimal.CurrentProfile!.MainBucket);
        var bucket = state.Minimal.CurrentProfile.MainBucket!;

        Assert.Equal("Quota left: 25%.", bucket.FiveHourWindow!.QuotaText);
        Assert.Equal("Quota left: 10%.", bucket.WeeklyWindow!.QuotaText);
        Assert.Equal(WidgetRefreshVisualState.Idle, state.Refresh.State);
    }

    [Fact]
    public void Build_HealthyMetrics_RemainIdle()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_035_000);
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            id: "profile-normal",
            isCurrent: true,
            mainBucket: CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
            [
                CreateWindowFromLeftValues(UsageWindowKind.FiveHour, 18_000, now.ToUnixTimeSeconds() + 5_400, quotaLeft: 80),
                CreateWindowFromLeftValues(UsageWindowKind.Weekly, 604_800, now.ToUnixTimeSeconds() + 302_400, quotaLeft: 70),
            ]));
        var snapshot = CreateSnapshot(now, [profile]);
        var preferences = WidgetPreferenceDefaults.Create() with
        {
            SelectedView = WidgetViewKind.Full,
        };

        var state = service.Build(snapshot, preferences);
        Assert.NotNull(state.Minimal.CurrentProfile);
        var currentProfile = state.Minimal.CurrentProfile!;
        Assert.NotNull(currentProfile.MainBucket);

        Assert.Equal(WidgetRefreshVisualState.Idle, state.Refresh.State);
        Assert.Equal(WidgetViewKind.Compact, state.SelectedView);
        Assert.Equal(state.Compact.SummaryText, state.SelectedViewSummaryText);
    }

    [Fact]
    public void Build_InvalidCompactAccountLayout_UsesDefaultAndWidgetScaleClampsToBounds()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_035_250);
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            id: "profile-layout-bounds",
            isCurrent: true,
            mainBucket: CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
            [
                CreateWindowFromLeftValues(UsageWindowKind.FiveHour, 18_000, now.ToUnixTimeSeconds() + 5_400, quotaLeft: 80),
            ]));
        var snapshot = CreateSnapshot(now, [profile]);

        var lowState = service.Build(snapshot, WidgetPreferenceDefaults.Create() with
        {
            CompactAccountLayout = (CompactAccountLayout)999,
            WidgetScalePercent = WidgetPreferenceDefaults.MinimumWidgetScalePercent - 1,
        });

        Assert.Equal(WidgetPreferenceDefaults.DefaultCompactAccountLayout, lowState.CompactAccountLayout);
        Assert.Equal(WidgetPreferenceDefaults.MinimumWidgetScalePercent, lowState.WidgetScalePercent);

        var highState = service.Build(snapshot, WidgetPreferenceDefaults.Create() with
        {
            CompactAccountLayout = CompactAccountLayout.Horizontal,
            WidgetScalePercent = WidgetPreferenceDefaults.MaximumWidgetScalePercent + 1,
        });

        Assert.Equal(CompactAccountLayout.Horizontal, highState.CompactAccountLayout);
        Assert.Equal(WidgetPreferenceDefaults.MaximumWidgetScalePercent, highState.WidgetScalePercent);
    }

    [Fact]
    public void Build_FullSelectedView_NormalizesToCompactSummary()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_035_500);
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            id: "profile-full-summary",
            isCurrent: true,
            mainBucket: CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
            [
                CreateAvailableWindow(UsageWindowKind.FiveHour, 18_000, now.ToUnixTimeSeconds() + 5_400, 20.0),
            ]));
        var snapshot = CreateSnapshot(now, [profile]);
        var preferences = WidgetPreferenceDefaults.Create() with
        {
            SelectedView = WidgetViewKind.Full,
        };

        var state = service.Build(snapshot, preferences);

        Assert.Equal(WidgetViewKind.Compact, state.SelectedView);
        Assert.Equal(state.Compact.SummaryText, state.SelectedViewSummaryText);
    }

    [Fact]
    public void SerializeAndDeserialize_RepresentativeState_PreservesNumericEnumCompatibility()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_035_750);
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            id: "profile-serialization",
            isCurrent: true,
            mainBucket: CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
            [
                CreateWindowFromLeftValues(UsageWindowKind.FiveHour, 18_000, now.ToUnixTimeSeconds() + 5_400, quotaLeft: 20),
            ]));
        var snapshot = CreateSnapshot(now, [profile]);
        var preferences = WidgetPreferenceDefaults.Create() with
        {
            SelectedView = WidgetViewKind.Full,
            CompactAccountLayout = CompactAccountLayout.Horizontal,
            WidgetScalePercent = 121,
        };

        var state = service.Build(snapshot, preferences);
        var json = JsonSerializer.Serialize(state);
        var roundTripped = JsonSerializer.Deserialize<WidgetPresentationState>(json);

        Assert.Contains("\"SelectedView\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"CompactAccountLayout\":1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"QuotaSeverity\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"TimeSeverity\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Refresh\":{\"State\":0", json, StringComparison.Ordinal);

        Assert.NotNull(roundTripped);
        Assert.Equal(state.SelectedView, roundTripped!.SelectedView);
        Assert.Equal(state.CompactAccountLayout, roundTripped.CompactAccountLayout);
        Assert.Equal(state.WidgetScalePercent, roundTripped.WidgetScalePercent);
        Assert.Equal(state.SelectedViewSummaryText, roundTripped.SelectedViewSummaryText);
        Assert.Equal(state.Refresh.State, roundTripped.Refresh.State);
        Assert.Equal(state.Refresh.DetailText, roundTripped.Refresh.DetailText);
        Assert.Equal(state.Minimal.CurrentProfile!.ProfileId, roundTripped.Minimal.CurrentProfile!.ProfileId);
        Assert.Equal(state.Minimal.CurrentProfile!.MainBucket!.FiveHourWindow!.QuotaLeftPercent, roundTripped.Minimal.CurrentProfile!.MainBucket!.FiveHourWindow!.QuotaLeftPercent);
    }

    [Fact]
    public void Build_InvalidSelectedView_NormalizesToCompactSummary()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_035_100);
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            id: "profile-invalid-view",
            isCurrent: true,
            mainBucket: CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
            [
                CreateWindowFromLeftValues(UsageWindowKind.FiveHour, 18_000, now.ToUnixTimeSeconds() + 5_400, quotaLeft: 80),
            ]));
        var snapshot = CreateSnapshot(now, [profile]);
        var preferences = WidgetPreferenceDefaults.Create() with
        {
            SelectedView = (WidgetViewKind)999,
        };

        var state = service.Build(snapshot, preferences);

        Assert.Equal(WidgetViewKind.Compact, state.SelectedView);
        Assert.Equal(state.Compact.SummaryText, state.SelectedViewSummaryText);
    }

    [Fact]
    public void Build_RefreshingState_TakesPriority()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_040_000);
        var service = CreateService(now);
        var profile = CreateProfileStatus("profile-c", true, CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows: []));
        var snapshot = CreateSnapshot(now, [profile]) with
        {
            RefreshState = new StatusRefreshState
            {
                Outcome = StatusRefreshOutcome.Running,
                StartedAtUtc = now,
            },
        };

        var state = service.Build(snapshot, WidgetPreferenceDefaults.Create());

        Assert.Equal(WidgetRefreshVisualState.Refreshing, state.Refresh.State);
        Assert.Contains("refreshing", state.Refresh.StateText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_StaleState_DerivesFromScheduleAndSource()
    {
        var capturedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_050_000);
        var now = capturedAt + TimeSpan.FromMinutes(20);
        var service = CreateService(now);
        var profile = CreateProfileStatus("profile-d", true, CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows: []));
        var staleSource = new SourceStatus
        {
            Source = StatusSourceKind.Cache,
            State = SourceStatusState.Stale,
            Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Stale),
            ObservedAtUtc = capturedAt + TimeSpan.FromMinutes(1),
        };
        var snapshot = CreateSnapshot(capturedAt, [profile], [staleSource]) with
        {
            NextScheduledRefreshAtUtc = capturedAt + TimeSpan.FromMinutes(5),
        };

        var state = service.Build(snapshot, WidgetPreferenceDefaults.Create());

        Assert.Equal(WidgetRefreshVisualState.Stale, state.Refresh.State);
        Assert.Contains("stale", state.Refresh.DetailText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_ErrorState_ExposesRedactedFailure()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_060_000);
        var service = CreateService(now);
        var profile = CreateProfileStatus("profile-e", true, CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows: []));
        var failure = SourceDiagnostic.Create(
            SourceDiagnosticCode.Error,
            SourceDiagnosticSeverity.Error,
            "Refresh failed",
            detail: "Authorization: Bearer DemoTokenValue12345",
            context:
            [
                new KeyValuePair<string, string?>("profilePath", "/home/demo/.codex/auth.json"),
            ]);
        var snapshot = CreateSnapshot(now, [profile]) with
        {
            RefreshState = new StatusRefreshState
            {
                Outcome = StatusRefreshOutcome.Failed,
                Failure = failure,
            },
        };

        var state = service.Build(snapshot, WidgetPreferenceDefaults.Create());
        var firstDiagnostic = Assert.Single(state.Refresh.Diagnostics);

        Assert.Equal(WidgetRefreshVisualState.Error, state.Refresh.State);
        Assert.Contains("error", state.Refresh.StateText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DemoTokenValue12345", firstDiagnostic.DetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/demo", firstDiagnostic.Context["profilePath"], StringComparison.Ordinal);
    }

    [Fact]
    public void Build_BucketUnavailableState_UsesUnavailableBucketAndWindowText()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_065_000);
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            id: "profile-j",
            isCurrent: true,
            mainBucket: new UsageBucketSnapshot
            {
                BucketId = "codex",
                BucketLabel = "codex",
                BucketKind = UsageBucketKind.MainCodex,
                FetchStatus = UsageBucketFetchStatus.Unavailable,
                Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.MissingRequiredField, "bucket unavailable"),
                Windows =
                [
                    new UsageWindowSnapshot
                    {
                        WindowKind = UsageWindowKind.FiveHour,
                        DurationSeconds = 18_000,
                        ResetAtUnixSeconds = now.ToUnixTimeSeconds() + 1_000,
                        Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.MissingRequiredField, "window unavailable"),
                    },
                ],
            });

        var state = service.Build(CreateSnapshot(now, [profile]), WidgetPreferenceDefaults.Create());
        Assert.NotNull(state.Minimal.CurrentProfile);
        var currentProfile = state.Minimal.CurrentProfile!;
        Assert.NotNull(currentProfile.MainBucket);
        var bucket = currentProfile.MainBucket!;
        Assert.NotNull(bucket.FiveHourWindow);
        var window = bucket.FiveHourWindow!;

        Assert.Contains("unavailable", bucket.AvailabilityText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unavailable", window.AvailabilityText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Quota left: unavailable.", window.QuotaText);
        Assert.Equal("Time left: unavailable.", window.TimeText);
        Assert.Null(window.EndsAtUnixSeconds);
        Assert.Equal(WidgetPresentationFormatter.CompactUnavailableTimestampToken, window.EndsAtCompactText);
        Assert.False(window.IsAvailable);
        Assert.False(window.HasQuotaLeft);
        Assert.False(window.HasTimeLeft);
    }

    [Fact]
    public void Build_FormatsResetTimeUsingLocalTime()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_070_000);
        var endAt = now.ToUnixTimeSeconds() + 3_600;
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            id: "profile-f",
            isCurrent: true,
            mainBucket: CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
            [
                CreateAvailableWindow(UsageWindowKind.FiveHour, 18_000, endAt, 50.0),
            ]));
        var snapshot = CreateSnapshot(now, [profile]);

        var state = service.Build(snapshot, WidgetPreferenceDefaults.Create());
        Assert.NotNull(state.Minimal.CurrentProfile);
        Assert.NotNull(state.Minimal.CurrentProfile!.MainBucket);
        Assert.NotNull(state.Minimal.CurrentProfile.MainBucket!.FiveHourWindow);
        var window = state.Minimal.CurrentProfile.MainBucket.FiveHourWindow!;
        var expected = DateTimeOffset.FromUnixTimeSeconds(endAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        var expectedCompact = DateTimeOffset.FromUnixTimeSeconds(endAt).ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
        var expectedCaptured = now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        var expectedNextRefresh = (now + TimeSpan.FromMinutes(5)).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

        Assert.Equal($"Ends: {expected}.", window.EndsAtText);
        Assert.Equal(expectedCompact, window.EndsAtCompactText);
        Assert.True(window.IsAvailable);
        Assert.True(window.HasQuotaLeft);
        Assert.True(window.HasTimeLeft);
        Assert.Equal($"Captured: {expectedCaptured}.", state.Refresh.CapturedAtText);
        Assert.Equal($"Next refresh: {expectedNextRefresh}.", state.Refresh.NextScheduledRefreshText);
    }

    [Fact]
    public void Build_CompactShowsSparkOnlyWhenPresent_AndFullKeepsAdditionalBuckets()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_080_000);
        var service = CreateService(now);
        var main = CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
        [
            CreateAvailableWindow(UsageWindowKind.FiveHour, 18_000, now.ToUnixTimeSeconds() + 3_600, 20.0),
        ]);
        var spark = CreateBucket("metered_spark_compute", "Spark", UsageBucketKind.Additional, windows:
        [
            CreateAvailableWindow(UsageWindowKind.FiveHour, 18_000, now.ToUnixTimeSeconds() + 3_600, 40.0),
        ]);
        var additional = CreateBucket("extra_bucket", "Extra", UsageBucketKind.Additional, windows:
        [
            CreateAvailableWindow(UsageWindowKind.Additional, 86_400, now.ToUnixTimeSeconds() + 10_000, 30.0),
        ]);
        var withSpark = CreateProfileStatus(
            "profile-g",
            true,
            main,
            sparkBucket: null,
            allBuckets: [main, spark, additional]);
        var withoutSpark = CreateProfileStatus(
            "profile-h",
            false,
            main,
            sparkBucket: null,
            allBuckets: [main, additional]);

        var state = service.Build(CreateSnapshot(now, [withSpark, withoutSpark]), WidgetPreferenceDefaults.Create());

        var compactWithSpark = state.Compact.Profiles.Single(profile => profile.ProfileId == "profile-g");
        var compactWithoutSpark = state.Compact.Profiles.Single(profile => profile.ProfileId == "profile-h");
        var fullWithSpark = state.Full.Profiles.Single(profile => profile.ProfileId == "profile-g");

        Assert.NotNull(compactWithSpark.SparkBucket);
        Assert.Null(compactWithoutSpark.SparkBucket);
        Assert.Contains(fullWithSpark.AdditionalBuckets, bucket => bucket.BucketId == "extra_bucket");
    }

    [Fact]
    public void Build_RedactsSourceDiagnostics()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_090_000);
        var service = CreateService(now);
        var profile = CreateProfileStatus("profile-i", true, CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows: []));
        var source = new SourceStatus
        {
            Source = StatusSourceKind.UsageEndpoint,
            State = SourceStatusState.Error,
            Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Error),
            Diagnostics =
            [
                new SourceDiagnostic
                {
                    Code = SourceDiagnosticCode.Error,
                    Severity = SourceDiagnosticSeverity.Error,
                    Summary = "/home/demo/.codex/auth.json",
                    Detail = "Authorization: Bearer DemoTokenValue12345",
                    Context = new Dictionary<string, string>
                    {
                        ["authPath"] = "/home/demo/.codex/auth.json",
                        ["accessToken"] = "sk-demo-secret-token",
                    },
                },
            ],
        };
        var snapshot = CreateSnapshot(now, [profile], [source]);

        var state = service.Build(snapshot, WidgetPreferenceDefaults.Create());
        var diagnostic = Assert.Single(state.Refresh.Diagnostics);

        Assert.DoesNotContain("DemoTokenValue12345", diagnostic.DetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/demo", diagnostic.SummaryText, StringComparison.Ordinal);
        Assert.Equal("[redacted]…oken", diagnostic.Context["accessToken"]);
        Assert.StartsWith("[redacted-path]", diagnostic.Context["authPath"], StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RedactsSyntheticSecurityFixtureValuesInDiagnosticsAndAvailability()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_091_000);
        var service = CreateService(now);
        var profile = CreateProfileStatus(
            "profile-synthetic-redaction",
            true,
            new UsageBucketSnapshot
            {
                BucketId = "codex",
                BucketLabel = "codex",
                BucketKind = UsageBucketKind.MainCodex,
                FetchStatus = UsageBucketFetchStatus.Unavailable,
                Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Error, SyntheticSecurityFixtures.SyntheticRawAuthJson),
                Windows =
                [
                    new UsageWindowSnapshot
                    {
                        WindowKind = UsageWindowKind.FiveHour,
                        DurationSeconds = 18_000,
                        ResetAtUnixSeconds = now.ToUnixTimeSeconds() + 1_000,
                        UsedPercent = null,
                        Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.MissingRequiredField, SyntheticSecurityFixtures.SyntheticUnixCodexPath),
                    },
                ],
            });
        var source = new SourceStatus
        {
            Source = StatusSourceKind.UsageEndpoint,
            State = SourceStatusState.Error,
            Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Error, SyntheticSecurityFixtures.SyntheticWindowsCodexPath),
            Diagnostics =
            [
                new SourceDiagnostic
                {
                    Code = SourceDiagnosticCode.Error,
                    Severity = SourceDiagnosticSeverity.Error,
                    Summary = SyntheticSecurityFixtures.SyntheticRawCodexContent,
                    Detail = SyntheticSecurityFixtures.SyntheticBearerHeader,
                    Context = new Dictionary<string, string>
                    {
                        ["bearer"] = SyntheticSecurityFixtures.SyntheticBearerToken,
                        ["apiKey"] = SyntheticSecurityFixtures.SyntheticApiKey,
                        ["authPath"] = SyntheticSecurityFixtures.SyntheticUnixCodexPath,
                    },
                },
            ],
        };

        var state = service.Build(CreateSnapshot(now, [profile], [source]), WidgetPreferenceDefaults.Create());
        var serialized = JsonSerializer.Serialize(state);
        var diagnostic = Assert.Single(state.Refresh.Diagnostics);

        SecurityRedactionAssertions.AssertNoSyntheticSecrets(serialized);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(diagnostic.SummaryText);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(diagnostic.DetailText ?? string.Empty);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(string.Join("|", diagnostic.Context.Values));
        Assert.Contains("redacted", diagnostic.SummaryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redacted", diagnostic.DetailText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_FullViewKeepsAllAdditionalBuckets()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_095_000);
        var service = CreateService(now);
        var main = CreateBucket("codex", "codex", UsageBucketKind.MainCodex, windows:
        [
            CreateAvailableWindow(UsageWindowKind.FiveHour, 18_000, now.ToUnixTimeSeconds() + 3_600, 45.0),
        ]);
        var additionalOne = CreateBucket("extra_one", "Extra One", UsageBucketKind.Additional, windows:
        [
            CreateAvailableWindow(UsageWindowKind.Additional, 86_400, now.ToUnixTimeSeconds() + 10_000, 35.0),
        ]);
        var additionalTwo = CreateBucket("extra_two", "Extra Two", UsageBucketKind.Additional, windows:
        [
            CreateAvailableWindow(UsageWindowKind.Additional, 86_400, now.ToUnixTimeSeconds() + 12_000, 55.0),
        ]);
        var profile = CreateProfileStatus(
            "profile-full",
            true,
            main,
            sparkBucket: null,
            allBuckets: [main, additionalOne, additionalTwo]);

        var state = service.Build(CreateSnapshot(now, [profile]), WidgetPreferenceDefaults.Create() with
        {
            SelectedView = WidgetViewKind.Full,
        });
        var fullProfile = Assert.Single(state.Full.Profiles);

        Assert.Equal(2, fullProfile.AdditionalBuckets.Count);
        Assert.Contains(fullProfile.AdditionalBuckets, bucket =>
            bucket.BucketId == "extra_one"
            && bucket.BucketKind == UsageBucketKind.Additional
            && bucket.Windows.Count > 0);
        Assert.Contains(fullProfile.AdditionalBuckets, bucket => bucket.BucketId == "extra_two");
    }

    private static WidgetPresentationService CreateService(DateTimeOffset now)
    {
        var clock = new FixedClock(now);
        return new WidgetPresentationService(new StatusProjectionService(clock), clock);
    }

    private static StatusSnapshot CreateSnapshot(
        DateTimeOffset capturedAtUtc,
        IReadOnlyList<ProfileStatus> profiles,
        IReadOnlyList<SourceStatus>? sources = null)
    {
        return new StatusSnapshot
        {
            CapturedAtUtc = capturedAtUtc,
            Profiles = profiles,
            CurrentProfileId = profiles.FirstOrDefault(profile => profile.Profile.IsCurrent)?.Profile.ProfileId,
            NextScheduledRefreshAtUtc = capturedAtUtc + TimeSpan.FromMinutes(5),
            RefreshState = new StatusRefreshState
            {
                Outcome = StatusRefreshOutcome.Succeeded,
                CompletedAtUtc = capturedAtUtc,
            },
            Sources = sources ?? [CreateAvailableSource(capturedAtUtc)],
        };
    }

    private static SourceStatus CreateAvailableSource(DateTimeOffset observedAtUtc)
    {
        return new SourceStatus
        {
            Source = StatusSourceKind.Cache,
            State = SourceStatusState.Available,
            Availability = StatusAvailability.Available(),
            ObservedAtUtc = observedAtUtc,
        };
    }

    private static ProfileStatus CreateProfileStatus(
        string id,
        bool isCurrent,
        UsageBucketSnapshot? mainBucket,
        UsageBucketSnapshot? sparkBucket = null,
        IReadOnlyList<UsageBucketSnapshot>? allBuckets = null)
    {
        return new ProfileStatus
        {
            Profile = new ProfileDescriptor
            {
                ProfileId = id,
                DisplayName = $"Display {id}",
                LoginName = $"{id}@example.test",
                IsCurrent = isCurrent,
                SubscriptionTier = SubscriptionTier.Pro,
                UsageEligibility = ProfileUsageEligibility.Eligible,
                AuthKind = ProfileAuthKind.Login,
                SourceStatus = new SourceStatus
                {
                    Source = StatusSourceKind.SavedProfileAuth,
                    State = SourceStatusState.Available,
                    Availability = StatusAvailability.Available(),
                },
            },
            MainBucket = mainBucket,
            SparkBucket = sparkBucket,
            AllBuckets = allBuckets ?? (mainBucket is null ? [] : [mainBucket]),
            Diagnostics = [],
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

    private static UsageWindowSnapshot CreateAvailableWindow(
        UsageWindowKind kind,
        int durationSeconds,
        long resetAtUnixSeconds,
        double usedPercent)
    {
        return new UsageWindowSnapshot
        {
            WindowKind = kind,
            DurationSeconds = durationSeconds,
            ResetAtUnixSeconds = resetAtUnixSeconds,
            UsedPercent = usedPercent,
            Availability = StatusAvailability.Available(),
        };
    }

    private static UsageWindowSnapshot CreateWindowFromLeftValues(
        UsageWindowKind kind,
        int durationSeconds,
        long resetAtUnixSeconds,
        int quotaLeft)
    {
        return new UsageWindowSnapshot
        {
            WindowKind = kind,
            DurationSeconds = durationSeconds,
            ResetAtUnixSeconds = resetAtUnixSeconds,
            UsedPercent = 100 - quotaLeft,
            Availability = StatusAvailability.Available(),
        };
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
