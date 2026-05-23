using CodexWidget.Core;
using System.Text.Json;

namespace CodexWidget.Status.Tests;

public sealed class StatusContractSerializationTests
{
    [Fact]
    public void StatusSnapshotSerialization_DoesNotEmitSensitiveValues()
    {
        const string accessToken = "synthetic-access-token-123456";
        const string refreshToken = "synthetic-refresh-token-123456";
        const string idToken = "synthetic-header.synthetic-payload.synthetic-signature";
        const string sourcePath = "/home/example/.codex/auth.json";
        const string lockPath = "/home/example/.codex/profiles/profiles.lock";

        var snapshot = CreateSnapshot(
            SourceDiagnostic.Create(
                SourceDiagnosticCode.Error,
                SourceDiagnosticSeverity.Warning,
                "Synthetic refresh failure",
                detail: $"Bearer {accessToken} from {sourcePath}",
                context: new[]
                {
                    new KeyValuePair<string, string?>("accessToken", accessToken),
                    new KeyValuePair<string, string?>("refreshToken", refreshToken),
                    new KeyValuePair<string, string?>("idToken", idToken),
                    new KeyValuePair<string, string?>("sourcePath", sourcePath),
                    new KeyValuePair<string, string?>("profilesLockPath", lockPath),
                },
                observedAtUtc: DateTimeOffset.UnixEpoch));

        var json = Serialize(snapshot);

        Assert.DoesNotContain(accessToken, json, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, json, StringComparison.Ordinal);
        Assert.DoesNotContain(idToken, json, StringComparison.Ordinal);
        Assert.DoesNotContain(sourcePath, json, StringComparison.Ordinal);
        Assert.DoesNotContain(lockPath, json, StringComparison.Ordinal);
        Assert.Contains("[redacted]", json, StringComparison.Ordinal);
        Assert.Contains("[redacted-path]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusSnapshotChangedEventArgsSerialization_DoesNotEmitSensitiveValues()
    {
        const string accessToken = "synthetic-access-token-abcdef";
        const string refreshToken = "synthetic-refresh-token-abcdef";
        const string sourcePath = "/home/example/.codex/profiles/work.json";

        var previousSnapshot = CreateSnapshot(
            SourceDiagnostic.Create(
                SourceDiagnosticCode.Error,
                SourceDiagnosticSeverity.Warning,
                "Synthetic previous failure",
                detail: $"Authorization: Bearer {accessToken}",
                context: new[]
                {
                    new KeyValuePair<string, string?>("sourcePath", sourcePath),
                    new KeyValuePair<string, string?>("refreshToken", refreshToken),
                },
                observedAtUtc: DateTimeOffset.UnixEpoch));
        var currentSnapshot = previousSnapshot with
        {
            CapturedAtUtc = DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(1),
        };
        var eventArgs = new StatusSnapshotChangedEventArgs(previousSnapshot, currentSnapshot);

        var json = Serialize(eventArgs);

        Assert.DoesNotContain(accessToken, json, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, json, StringComparison.Ordinal);
        Assert.DoesNotContain(sourcePath, json, StringComparison.Ordinal);
        Assert.Contains("[redacted]", json, StringComparison.Ordinal);
        Assert.Contains("previousSnapshot", json, StringComparison.Ordinal);
        Assert.Contains("currentSnapshot", json, StringComparison.Ordinal);
    }

    private static StatusSnapshot CreateSnapshot(SourceDiagnostic failureDiagnostic)
    {
        return new StatusSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UnixEpoch,
            CurrentProfileId = "work",
            RefreshState = new StatusRefreshState
            {
                Reason = StatusRefreshReason.Manual,
                Scope = StatusRefreshScope.Full,
                Outcome = StatusRefreshOutcome.Failed,
                RequestedAtUtc = DateTimeOffset.UnixEpoch,
                StartedAtUtc = DateTimeOffset.UnixEpoch,
                CompletedAtUtc = DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(1),
                Failure = failureDiagnostic,
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
                            ObservedAtUtc = DateTimeOffset.UnixEpoch,
                            Diagnostics =
                            [
                                SourceDiagnostic.Create(
                                    SourceDiagnosticCode.Error,
                                    SourceDiagnosticSeverity.Error,
                                    "Synthetic source diagnostic",
                                    detail: "Bearer synthetic-source-token-123456",
                                    context: new[]
                                    {
                                        new KeyValuePair<string, string?>("sourcePath", "/home/example/.codex/auth.json"),
                                    },
                                    observedAtUtc: DateTimeOffset.UnixEpoch),
                            ],
                        },
                    },
                    MainBucket = null,
                    SparkBucket = null,
                    AllBuckets = Array.Empty<UsageBucketSnapshot>(),
                    Diagnostics =
                    [
                        SourceDiagnostic.Create(
                            SourceDiagnosticCode.Error,
                            SourceDiagnosticSeverity.Warning,
                            "Synthetic profile diagnostic",
                            detail: "Authorization: Bearer synthetic-profile-token-789012",
                            context: new[]
                            {
                                new KeyValuePair<string, string?>("profilesLockPath", "/home/example/.codex/profiles/profiles.lock"),
                            },
                            observedAtUtc: DateTimeOffset.UnixEpoch),
                    ],
                },
            ],
            Sources =
            [
                new SourceStatus
                {
                    Source = StatusSourceKind.Cache,
                    State = SourceStatusState.Available,
                    Availability = StatusAvailability.Available(),
                    ObservedAtUtc = DateTimeOffset.UnixEpoch,
                },
            ],
        };
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }
}
