using CodexWidget.Core;
using CodexWidget.Presentation;
using CodexWidget.Runtime;
using CodexWidget.TestSupport;
using System.Text.Json;

namespace CodexWidget.Web.Tests;

[Collection(WebHostEnvironmentCollection.Name)]
public sealed class ApiContractSerializationTests
{
    [Fact]
    public void WebSerializer_UsesReadableStringEnums()
    {
        var payload = SafeStatusSnapshotResponse.FromSnapshot(new StatusSnapshot
        {
            RefreshState = new StatusRefreshState
            {
                Reason = StatusRefreshReason.Manual,
                Scope = StatusRefreshScope.UsageOnly,
                Outcome = StatusRefreshOutcome.Failed,
            },
            Profiles =
            [
                new ProfileStatus
                {
                    Profile = new ProfileDescriptor
                    {
                        ProfileId = "profile-a",
                        SubscriptionTier = SubscriptionTier.Pro,
                        AuthKind = ProfileAuthKind.Login,
                        UsageEligibility = ProfileUsageEligibility.Eligible,
                    },
                },
            ],
            Sources =
            [
                new SourceStatus
                {
                    Source = StatusSourceKind.UsageEndpoint,
                    State = SourceStatusState.Stale,
                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Stale),
                },
            ],
        });

        var json = Serialize(payload);

        Assert.Contains("\"reason\":\"manual\"", json, StringComparison.Ordinal);
        Assert.Contains("\"scope\":\"usageOnly\"", json, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"failed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"subscriptionTier\":\"pro\"", json, StringComparison.Ordinal);
        Assert.Contains("\"authKind\":\"login\"", json, StringComparison.Ordinal);
        Assert.Contains("\"usageEligibility\":\"eligible\"", json, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"usageEndpoint\"", json, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"stale\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"stale\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthAndFrontendContracts_UseStableCamelCaseFieldNames()
    {
        var healthJson = Serialize(WebHealthStatusResponse.FromRuntimeState(
            new WebRuntimeInitializationSnapshot
            {
                Status = WebRuntimeInitializationStatus.Ready,
                AttemptedAtUtc = DateTimeOffset.UnixEpoch,
                ReadyAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1),
            },
            new ResolvedCodexWidgetWebOptions
            {
                EnableScheduler = true,
                PollingIntervalSeconds = 15,
            },
            new RefreshMetadata
            {
                CapturedAtUtc = DateTimeOffset.UnixEpoch,
                NextScheduledRefreshAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(15),
                SnapshotAge = TimeSpan.FromSeconds(12),
                LatestOutcome = StatusRefreshOutcome.Running,
                IsRefreshRunning = true,
            }));
        var frontendJson = Serialize(FrontendOptionsResponse.FromResolvedOptions(new ResolvedCodexWidgetWebOptions
        {
            PollingIntervalSeconds = 15,
        }));

        Assert.Contains("\"runtimeInitialization\":\"ready\"", healthJson, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"running\"", healthJson, StringComparison.Ordinal);
        Assert.Contains("\"staleness\":\"fresh\"", healthJson, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeInitialization", healthJson, StringComparison.Ordinal);
        Assert.DoesNotContain("PollingIntervalSeconds", frontendJson, StringComparison.Ordinal);

        using var healthDocument = JsonDocument.Parse(healthJson);
        Assert.Equal(
            ["refresh", "runtimeInitialization", "runtimeInitializationAttemptedAtUtc", "runtimeReadyAtUtc", "scheduler", "status"],
            healthDocument.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(static name => name).ToArray());

        using var frontendDocument = JsonDocument.Parse(frontendJson);
        Assert.Equal(
            ["pollingIntervalSeconds", "supportedManualRefreshScopes"],
            frontendDocument.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(static name => name).ToArray());
    }

    [Fact]
    public void SafeSnapshotSerialization_UsesStableCamelCaseFieldNames()
    {
        var json = Serialize(SafeStatusSnapshotResponse.FromSnapshot(new StatusSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UnixEpoch,
            CurrentProfileId = "profile-a",
            RefreshState = new StatusRefreshState
            {
                Reason = StatusRefreshReason.Manual,
                Scope = StatusRefreshScope.ProfileOnly,
                Outcome = StatusRefreshOutcome.Succeeded,
            },
            Profiles =
            [
                new ProfileStatus
                {
                    Profile = new ProfileDescriptor
                    {
                        ProfileId = "profile-a",
                        DisplayName = "Profile A",
                        SubscriptionTier = SubscriptionTier.Pro,
                        IsCurrent = true,
                        AuthKind = ProfileAuthKind.Login,
                        UsageEligibility = ProfileUsageEligibility.Eligible,
                    },
                },
            ],
        }));

        Assert.DoesNotContain("CapturedAtUtc", json, StringComparison.Ordinal);
        Assert.Contains("\"currentProfileId\":\"profile-a\"", json, StringComparison.Ordinal);
        Assert.Contains("\"refreshState\":", json, StringComparison.Ordinal);
        Assert.Contains("\"profiles\":", json, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            ["capturedAtUtc", "currentProfileId", "profiles", "refreshState", "sources"],
            document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(static name => name).ToArray());
    }

    [Fact]
    public void SafeSnapshotSerialization_RedactsSyntheticSecrets()
    {
        var accessToken = SyntheticSecurityFixtures.SyntheticAccessToken;
        var refreshToken = SyntheticSecurityFixtures.SyntheticRefreshToken;
        var idToken = SyntheticSecurityFixtures.SyntheticIdToken;
        var authorizationHeader = SyntheticSecurityFixtures.SyntheticBearerHeader;
        var rawAuthJson = SyntheticSecurityFixtures.SyntheticRawAuthJson;
        var apiKey = SyntheticSecurityFixtures.SyntheticApiKey;
        var codexPath = SyntheticSecurityFixtures.SyntheticUnixCodexPath;
        var rawCodexContent = SyntheticSecurityFixtures.SyntheticRawCodexContent;

        var snapshot = new StatusSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UnixEpoch,
            CurrentProfileId = "work",
            RefreshState = new StatusRefreshState
            {
                Reason = StatusRefreshReason.Manual,
                Scope = StatusRefreshScope.ProfileOnly,
                Outcome = StatusRefreshOutcome.Failed,
                RequestedAtUtc = DateTimeOffset.UnixEpoch,
                StartedAtUtc = DateTimeOffset.UnixEpoch,
                CompletedAtUtc = DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(1),
                Failure = new SourceDiagnostic
                {
                    Code = SourceDiagnosticCode.TokenRefreshFailed,
                    Severity = SourceDiagnosticSeverity.Error,
                    Summary = authorizationHeader,
                    Detail = rawAuthJson,
                    Context = new Dictionary<string, string>
                    {
                        ["accessToken"] = accessToken,
                        ["refreshToken"] = refreshToken,
                        ["idToken"] = idToken,
                        ["apiKey"] = apiKey,
                        ["authPath"] = codexPath,
                        ["rawCodexContent"] = rawCodexContent,
                    },
                    ObservedAtUtc = DateTimeOffset.UnixEpoch,
                },
            },
            Profiles =
            [
                new ProfileStatus
                {
                    Profile = new ProfileDescriptor
                    {
                        ProfileId = "work",
                        DisplayName = "Work",
                        LoginName = "person@example.invalid",
                        SubscriptionTier = SubscriptionTier.Pro,
                        IsCurrent = true,
                        AuthKind = ProfileAuthKind.Login,
                        UsageEligibility = ProfileUsageEligibility.Eligible,
                        SourceStatus = new SourceStatus
                        {
                            Source = StatusSourceKind.CurrentAuth,
                            State = SourceStatusState.Error,
                            Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Error, rawCodexContent),
                            Diagnostics =
                            [
                                new SourceDiagnostic
                                {
                                    Code = SourceDiagnosticCode.Error,
                                    Severity = SourceDiagnosticSeverity.Error,
                                    Summary = apiKey,
                                    Detail = authorizationHeader,
                                    Context = new Dictionary<string, string>
                                    {
                                        ["rawAuthJson"] = rawAuthJson,
                                        ["idToken"] = idToken,
                                    },
                                },
                            ],
                        },
                    },
                    MainBucket = new UsageBucketSnapshot
                    {
                        BucketId = "main",
                        BucketLabel = "Main",
                        BucketKind = UsageBucketKind.MainCodex,
                        FetchStatus = UsageBucketFetchStatus.Error,
                        Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.TokenRefreshFailed, rawAuthJson),
                        Windows =
                        [
                            new UsageWindowSnapshot
                            {
                                WindowKind = UsageWindowKind.FiveHour,
                                DurationSeconds = 18000,
                                ResetAtUnixSeconds = 1_700_000_000,
                                Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Error, authorizationHeader),
                            },
                        ],
                    },
                    Diagnostics =
                    [
                        new SourceDiagnostic
                        {
                            Code = SourceDiagnosticCode.Error,
                            Severity = SourceDiagnosticSeverity.Warning,
                            Summary = rawCodexContent,
                            Detail = rawAuthJson,
                            Context = new Dictionary<string, string>
                            {
                                ["authorization"] = authorizationHeader,
                            },
                        },
                    ],
                },
            ],
            Sources =
            [
                new SourceStatus
                {
                    Source = StatusSourceKind.ConfigToml,
                    State = SourceStatusState.Error,
                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Error, codexPath),
                    ObservedAtUtc = DateTimeOffset.UnixEpoch,
                    Diagnostics =
                    [
                        new SourceDiagnostic
                        {
                            Code = SourceDiagnosticCode.Malformed,
                            Severity = SourceDiagnosticSeverity.Warning,
                            Summary = rawAuthJson,
                            Detail = apiKey,
                            Context = new Dictionary<string, string>
                            {
                                ["rawCodexContent"] = rawCodexContent,
                            },
                        },
                    ],
                },
            ],
        };

        var json = Serialize(SafeStatusSnapshotResponse.FromSnapshot(snapshot));

        SecurityRedactionAssertions.AssertNoSyntheticSecrets(json, accessToken, refreshToken, idToken, authorizationHeader, rawAuthJson, apiKey, codexPath, rawCodexContent);
        Assert.Contains("[redacted]", json, StringComparison.Ordinal);
        Assert.Contains("[redacted-path]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("loginName", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorAndHealthContracts_RedactFailureSummaries()
    {
        var accessToken = SyntheticSecurityFixtures.SyntheticAccessToken;
        var refreshToken = SyntheticSecurityFixtures.SyntheticRefreshToken;
        var idToken = SyntheticSecurityFixtures.SyntheticIdToken;
        var authorizationHeader = SyntheticSecurityFixtures.SyntheticBearerHeader;
        var rawAuthJson = SyntheticSecurityFixtures.SyntheticRawAuthJson;
        var apiKey = SyntheticSecurityFixtures.SyntheticApiKey;
        var codexPath = SyntheticSecurityFixtures.SyntheticWindowsCodexPath;
        var rawCodexContent = SyntheticSecurityFixtures.SyntheticRawCodexContent;

        var errorJson = Serialize(WebApiErrors.StartupFailed(new WebRuntimeInitializationSnapshot
        {
            Status = WebRuntimeInitializationStatus.Failed,
            FailureSummary = $"{authorizationHeader}; {rawAuthJson}; {apiKey}; {rawCodexContent}",
        }));
        var healthJson = Serialize(WebHealthStatusResponse.FromRuntimeState(
            new WebRuntimeInitializationSnapshot
            {
                Status = WebRuntimeInitializationStatus.Failed,
                FailureSummary = $"{authorizationHeader}; {rawAuthJson}; {apiKey}; {codexPath}",
            },
            new ResolvedCodexWidgetWebOptions
            {
                EnableScheduler = true,
                PollingIntervalSeconds = 15,
            },
            refreshMetadata: null));
        var runtimeFailureJson = Serialize(WebApiErrors.RuntimeFailed(new RefreshMetadata
        {
            LatestSafeFailureSummary = $"{authorizationHeader}; {rawAuthJson}; {codexPath}",
        }));

        SecurityRedactionAssertions.AssertNoSyntheticSecrets(errorJson, accessToken, refreshToken, idToken, authorizationHeader, rawAuthJson, apiKey, codexPath, rawCodexContent);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(healthJson, accessToken, refreshToken, idToken, authorizationHeader, rawAuthJson, apiKey, codexPath);
        SecurityRedactionAssertions.AssertNoSyntheticSecrets(runtimeFailureJson, accessToken, refreshToken, idToken, authorizationHeader, rawAuthJson, apiKey, codexPath);
        Assert.Contains("\"runtimeInitialization\":\"failed\"", healthJson, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"degraded\"", healthJson, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationSerialization_RedactsDiagnosticData()
    {
        var accessToken = SyntheticSecurityFixtures.SyntheticAccessToken;
        var refreshToken = SyntheticSecurityFixtures.SyntheticRefreshToken;
        var idToken = SyntheticSecurityFixtures.SyntheticIdToken;
        var authorizationHeader = SyntheticSecurityFixtures.SyntheticBearerHeader;
        var rawAuthJson = SyntheticSecurityFixtures.SyntheticRawAuthJson;
        var apiKey = SyntheticSecurityFixtures.SyntheticApiKey;
        var codexPath = SyntheticSecurityFixtures.SyntheticUnixCodexPath;

        var nowUtc = new DateTimeOffset(2026, 05, 18, 12, 0, 0, TimeSpan.Zero);
        var presentation = WidgetPresentationStateSanitizer.Sanitize(
            new WidgetPresentationService(new StatusProjectionService(new FixedClock(nowUtc)), new FixedClock(nowUtc))
                .Build(
                new StatusSnapshot
                {
                    CapturedAtUtc = nowUtc,
                    CurrentProfileId = "work",
                    RefreshState = new StatusRefreshState
                    {
                        Outcome = StatusRefreshOutcome.Failed,
                        Failure = SourceDiagnostic.Create(
                            SourceDiagnosticCode.Error,
                            SourceDiagnosticSeverity.Error,
                            authorizationHeader,
                            detail: rawAuthJson,
                            context: new[]
                            {
                                new KeyValuePair<string, string?>("accessToken", accessToken),
                                new KeyValuePair<string, string?>("refreshToken", refreshToken),
                                new KeyValuePair<string, string?>("idToken", idToken),
                                new KeyValuePair<string, string?>("apiKey", apiKey),
                                new KeyValuePair<string, string?>("authPath", codexPath),
                            },
                            observedAtUtc: nowUtc),
                    },
                    Profiles =
                    [
                        new ProfileStatus
                        {
                            Profile = new ProfileDescriptor
                            {
                                ProfileId = "work",
                                DisplayName = "Work",
                                LoginName = "person@example.invalid",
                                SubscriptionTier = SubscriptionTier.Pro,
                                IsCurrent = true,
                                AuthKind = ProfileAuthKind.Login,
                                UsageEligibility = ProfileUsageEligibility.Eligible,
                                SourceStatus = new SourceStatus
                                {
                                    Source = StatusSourceKind.CurrentAuth,
                                    State = SourceStatusState.Error,
                                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Error),
                                },
                            },
                            Diagnostics =
                            [
                                SourceDiagnostic.Create(
                                    SourceDiagnosticCode.Error,
                                    SourceDiagnosticSeverity.Warning,
                                    "Synthetic profile diagnostic",
                                    detail: authorizationHeader,
                                    context: new[]
                                    {
                                        new KeyValuePair<string, string?>("rawAuthJson", rawAuthJson),
                                        new KeyValuePair<string, string?>("apiKey", apiKey),
                                        new KeyValuePair<string, string?>("authPath", codexPath),
                                    },
                                    observedAtUtc: nowUtc),
                            ],
                        },
                    ],
                    Sources =
                    [
                        new SourceStatus
                        {
                            Source = StatusSourceKind.ConfigToml,
                            State = SourceStatusState.Error,
                            Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Error),
                            Diagnostics =
                            [
                                SourceDiagnostic.Create(
                                    SourceDiagnosticCode.Error,
                                    SourceDiagnosticSeverity.Warning,
                                    "Synthetic source diagnostic",
                                    detail: authorizationHeader,
                                    context: new[]
                                    {
                                        new KeyValuePair<string, string?>("rawAuthJson", rawAuthJson),
                                    },
                                    observedAtUtc: nowUtc),
                            ],
                        },
                    ],
                },
                WidgetPreferenceDefaults.Create()));

        var json = Serialize(presentation);

        SecurityRedactionAssertions.AssertNoSyntheticSecrets(json, accessToken, refreshToken, idToken, authorizationHeader, rawAuthJson, apiKey, codexPath);
        Assert.Contains("[redacted]", json, StringComparison.Ordinal);
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, CodexWidgetWebJson.CreateSerializerOptions());
    }
}
