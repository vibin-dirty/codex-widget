using CodexWidget.Core;
using CodexWidget.Profiles;
using CodexWidget.Status;
using CodexWidget.Usage;

namespace CodexWidget.Status.Tests;

public sealed class StatusCacheServiceTests
{
    [Fact]
    public async Task InitializeAsync_PerformsStartupFullRefresh()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var snapshot = CreateProfileSnapshot(
            nowUtc,
            [CreateEligibleProfile("work", isCurrent: true, sourcePath: "/codex/profiles/work.json", accountId: "acct-work")]);
        var reader = new FakeProfileSnapshotReader(snapshot);
        var usageClient = new FakeUsageClient
        {
            Handler = request => Task.FromResult(CreateSuccessResult(request.ProfileId, nowUtc + TimeSpan.FromMinutes(2))),
        };
        using var service = CreateService(reader, usageClient, clock);

        var result = await service.InitializeAsync();

        Assert.True(service.IsInitialized);
        Assert.Equal(StatusRefreshReason.Startup, result.RefreshState.Reason);
        Assert.Equal(StatusRefreshScope.Full, result.RefreshState.Scope);
        Assert.Equal(StatusRefreshOutcome.Succeeded, result.RefreshState.Outcome);
        Assert.Single(result.Profiles);
        Assert.Equal("work", result.CurrentProfileId);
        Assert.Equal(1, usageClient.CallCount);
        Assert.NotNull(result.Profiles[0].MainBucket);
        Assert.Equal(nowUtc + TimeSpan.FromMinutes(2), result.NextScheduledRefreshAtUtc);
    }

    [Fact]
    public async Task RefreshAsync_ProfileOnly_PreservesExistingUsageWithoutCallingUsageClient()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var initialSnapshot = CreateProfileSnapshot(
            nowUtc,
            [CreateEligibleProfile("work", isCurrent: true, sourcePath: "/codex/profiles/work.json", accountId: "acct-work", displayName: "Work")]);
        var updatedSnapshot = CreateProfileSnapshot(
            nowUtc + TimeSpan.FromMinutes(1),
            [CreateEligibleProfile("work", isCurrent: true, sourcePath: "/codex/profiles/work.json", accountId: "acct-work", displayName: "Renamed Work")]);
        var reader = new FakeProfileSnapshotReader(initialSnapshot, updatedSnapshot);
        var usageClient = new FakeUsageClient
        {
            Handler = request => Task.FromResult(CreateSuccessResult(request.ProfileId, nowUtc + TimeSpan.FromMinutes(5))),
        };
        using var service = CreateService(reader, usageClient, clock);

        await service.InitializeAsync();
        var result = await service.RefreshAsync(StatusRefreshReason.ProfileChanged, StatusRefreshScope.ProfileOnly);

        Assert.Equal(1, usageClient.CallCount);
        Assert.Equal("Renamed Work", result.Profiles[0].Profile.DisplayName);
        Assert.NotNull(result.Profiles[0].MainBucket);
        Assert.Equal(UsageBucketFetchStatus.Succeeded, result.Profiles[0].MainBucket!.FetchStatus);
        Assert.Equal(StatusRefreshOutcome.Succeeded, result.RefreshState.Outcome);
    }

    [Fact]
    public async Task RefreshAsync_UsageOnly_ReusesCachedProfileSnapshot()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var snapshot = CreateProfileSnapshot(
            nowUtc,
            [CreateEligibleProfile("work", isCurrent: true, sourcePath: "/codex/profiles/work.json", accountId: "acct-work")]);
        var reader = new FakeProfileSnapshotReader(snapshot);
        var usageClient = new FakeUsageClient
        {
            Handler = request => Task.FromResult(CreateSuccessResult(request.ProfileId, nowUtc + TimeSpan.FromMinutes(5))),
        };
        using var service = CreateService(reader, usageClient, clock);

        await service.InitializeAsync();
        var result = await service.RefreshAsync(StatusRefreshReason.Periodic, StatusRefreshScope.UsageOnly);

        Assert.Equal(1, reader.CallCount);
        Assert.Equal(2, usageClient.CallCount);
        Assert.Equal(StatusRefreshScope.UsageOnly, result.RefreshState.Scope);
        Assert.Equal(StatusRefreshOutcome.Succeeded, result.RefreshState.Outcome);
    }

    [Fact]
    public async Task RefreshAsync_PartialUsageFailure_PublishesExplicitUnavailableState()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var snapshot = CreateProfileSnapshot(
            nowUtc,
            [
                CreateEligibleProfile("work", isCurrent: true, sourcePath: "/codex/profiles/work.json", accountId: "acct-work"),
                CreateEligibleProfile("home", isCurrent: false, sourcePath: "/codex/profiles/home.json", accountId: "acct-home"),
            ]);
        var reader = new FakeProfileSnapshotReader(snapshot);
        var usageClient = new FakeUsageClient
        {
            Handler = request =>
            {
                if (request.ProfileId == "home")
                {
                    return Task.FromResult(new UsageFetchResult
                    {
                        ProfileId = request.ProfileId,
                        Outcome = UsageFetchOutcome.MalformedResponse,
                        Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Malformed),
                        Diagnostics =
                        [
                            SourceDiagnostic.Create(
                                SourceDiagnosticCode.Malformed,
                                SourceDiagnosticSeverity.Error,
                                "Synthetic malformed usage payload.",
                                observedAtUtc: nowUtc),
                        ],
                    });
                }

                return Task.FromResult(CreateSuccessResult(request.ProfileId, nowUtc + TimeSpan.FromMinutes(5)));
            },
        };
        using var service = CreateService(reader, usageClient, clock);

        var result = await service.InitializeAsync();

        var homeProfile = Assert.Single(result.Profiles, profile => profile.Profile.ProfileId == "home");
        Assert.Equal(StatusRefreshOutcome.Failed, result.RefreshState.Outcome);
        Assert.Equal(StatusAvailabilityCode.Malformed, homeProfile.MainBucket!.Availability.Code);
        Assert.Contains(homeProfile.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Malformed);
    }

    [Fact]
    public async Task RefreshAsync_TransientUsageFailure_PreservesStaleUsageForMatchingIdentity()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var snapshot = CreateProfileSnapshot(
            nowUtc,
            [CreateEligibleProfile("work", isCurrent: true, sourcePath: "/codex/profiles/work.json", accountId: "acct-work")]);
        var reader = new FakeProfileSnapshotReader(snapshot);
        var usageClient = new FakeUsageClient();
        usageClient.QueueResult(CreateSuccessResult("work", nowUtc + TimeSpan.FromMinutes(5)));
        usageClient.QueueResult(new UsageFetchResult
        {
            ProfileId = "work",
            Outcome = UsageFetchOutcome.NetworkError,
            Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.NetworkError),
            Diagnostics =
            [
                SourceDiagnostic.Create(
                    SourceDiagnosticCode.NetworkError,
                    SourceDiagnosticSeverity.Warning,
                    "Synthetic network failure.",
                    observedAtUtc: nowUtc + TimeSpan.FromMinutes(1)),
            ],
        });
        using var service = CreateService(reader, usageClient, clock);

        await service.InitializeAsync();
        var result = await service.RefreshAsync(StatusRefreshReason.Periodic, StatusRefreshScope.UsageOnly);

        var profile = Assert.Single(result.Profiles);
        Assert.Equal(StatusRefreshOutcome.Failed, result.RefreshState.Outcome);
        Assert.Equal(UsageBucketFetchStatus.Succeeded, profile.MainBucket!.FetchStatus);
        Assert.Contains(profile.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Stale);
    }

    [Fact]
    public async Task RefreshAsync_IdentityChange_DropsStaleUsageInsteadOfReusingIt()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var initialSnapshot = CreateProfileSnapshot(
            nowUtc,
            [CreateEligibleProfile("work", isCurrent: true, sourcePath: "/codex/profiles/work.json", accountId: "acct-work")]);
        var changedSnapshot = CreateProfileSnapshot(
            nowUtc + TimeSpan.FromMinutes(1),
            [CreateEligibleProfile("work", isCurrent: true, sourcePath: "/codex/profiles/work-renamed.json", accountId: "acct-work")]);
        var reader = new FakeProfileSnapshotReader(initialSnapshot, changedSnapshot);
        var usageClient = new FakeUsageClient();
        usageClient.QueueResult(CreateSuccessResult("work", nowUtc + TimeSpan.FromMinutes(5)));
        usageClient.QueueResult(new UsageFetchResult
        {
            ProfileId = "work",
            Outcome = UsageFetchOutcome.NetworkError,
            Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.NetworkError),
            Diagnostics =
            [
                SourceDiagnostic.Create(
                    SourceDiagnosticCode.NetworkError,
                    SourceDiagnosticSeverity.Warning,
                    "Synthetic network failure.",
                    observedAtUtc: nowUtc + TimeSpan.FromMinutes(1)),
            ],
        });
        using var service = CreateService(reader, usageClient, clock);

        await service.InitializeAsync();
        var result = await service.RefreshAsync(StatusRefreshReason.ProfileChanged, StatusRefreshScope.Full);

        var profile = Assert.Single(result.Profiles);
        Assert.Equal(StatusAvailabilityCode.NetworkError, profile.MainBucket!.Availability.Code);
        Assert.DoesNotContain(profile.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Stale);
    }

    [Fact]
    public async Task RefreshAsync_NoEligibleProfiles_DoesNotCallUsageClient()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var snapshot = CreateProfileSnapshot(
            nowUtc,
            [
                CreateApiKeyProfile("api", isCurrent: true, sourcePath: "/codex/profiles/api.json"),
                CreateIneligibleLoginProfile("missing-access", ProfileUsageEligibility.MissingAccessToken, sourcePath: "/codex/profiles/missing.json"),
            ]);
        var reader = new FakeProfileSnapshotReader(snapshot);
        var usageClient = new FakeUsageClient
        {
            Handler = _ => throw new InvalidOperationException("Usage client should not be called."),
        };
        using var service = CreateService(reader, usageClient, clock);

        var result = await service.InitializeAsync();

        Assert.Equal(0, usageClient.CallCount);
        Assert.Equal(StatusRefreshOutcome.Succeeded, result.RefreshState.Outcome);
        Assert.All(result.Profiles, profile => Assert.NotNull(profile.MainBucket));
        Assert.Contains(result.Profiles, profile => profile.MainBucket!.Availability.Code == StatusAvailabilityCode.ApiKeyProfile);
        Assert.Contains(result.Profiles, profile => profile.MainBucket!.Availability.Code == StatusAvailabilityCode.MissingRequiredField);
    }

    [Fact]
    public async Task RefreshAsync_LimitsUsageFetchConcurrencyToThreeProfiles()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var snapshot = CreateProfileSnapshot(
            nowUtc,
            Enumerable.Range(1, 6)
                .Select(index => CreateEligibleProfile(
                    $"profile-{index}",
                    isCurrent: index == 1,
                    sourcePath: $"/codex/profiles/profile-{index}.json",
                    accountId: $"acct-{index}"))
                .ToArray());
        var reader = new FakeProfileSnapshotReader(snapshot);
        var usageClient = new FakeUsageClient();
        usageClient.Handler = async request =>
        {
            usageClient.EnterConcurrentCall();
            try
            {
                await Task.Delay(50);
                return CreateSuccessResult(request.ProfileId, nowUtc + TimeSpan.FromMinutes(5));
            }
            finally
            {
                usageClient.ExitConcurrentCall();
            }
        };
        using var service = CreateService(reader, usageClient, clock);

        var result = await service.InitializeAsync();

        Assert.Equal(StatusRefreshOutcome.Succeeded, result.RefreshState.Outcome);
        Assert.True(usageClient.MaxConcurrentCalls <= 3, $"Expected max concurrency <= 3 but was {usageClient.MaxConcurrentCalls}.");
        Assert.Equal(6, usageClient.CallCount);
    }

    [Fact]
    public async Task RefreshAsync_Cancellation_PublishesCancelledRefreshState()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var snapshot = CreateProfileSnapshot(
            nowUtc,
            [CreateEligibleProfile("work", isCurrent: true, sourcePath: "/codex/profiles/work.json", accountId: "acct-work")]);
        var reader = new FakeProfileSnapshotReader(snapshot);
        var usageClient = new FakeUsageClient();
        usageClient.Handler = async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), usageClient.CurrentCancellationToken);
            return CreateSuccessResult("work", nowUtc + TimeSpan.FromMinutes(5));
        };
        using var service = CreateService(reader, usageClient, clock);

        await service.InitializeAsync();
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var result = await service.RefreshAsync(StatusRefreshReason.Manual, StatusRefreshScope.UsageOnly, cancellationTokenSource.Token);

        Assert.Equal(StatusRefreshOutcome.Cancelled, result.RefreshState.Outcome);
        Assert.Equal(StatusRefreshReason.Manual, result.RefreshState.Reason);
    }

    [Fact]
    public async Task RefreshAsync_PublishesRunningAndCompletedStateTransitions()
    {
        var nowUtc = new DateTimeOffset(2026, 05, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(nowUtc);
        var snapshot = CreateProfileSnapshot(
            nowUtc,
            [CreateEligibleProfile("work", isCurrent: true, sourcePath: "/codex/profiles/work.json", accountId: "acct-work")]);
        var reader = new FakeProfileSnapshotReader(snapshot);
        var usageClient = new FakeUsageClient
        {
            Handler = request => Task.FromResult(CreateSuccessResult(request.ProfileId, nowUtc + TimeSpan.FromMinutes(5))),
        };
        using var service = CreateService(reader, usageClient, clock);
        var observedOutcomes = new List<StatusRefreshOutcome>();

        service.SnapshotChanged += (_, args) => observedOutcomes.Add(args.CurrentSnapshot.RefreshState.Outcome);

        var result = await service.RefreshAsync(StatusRefreshReason.Manual, StatusRefreshScope.Full);

        Assert.Equal([StatusRefreshOutcome.Running, StatusRefreshOutcome.Succeeded], observedOutcomes);
        Assert.NotNull(result.RefreshState.RequestedAtUtc);
        Assert.NotNull(result.RefreshState.StartedAtUtc);
        Assert.NotNull(result.RefreshState.CompletedAtUtc);
    }

    private static StatusCacheService CreateService(FakeProfileSnapshotReader reader, FakeUsageClient usageClient, FakeClock clock)
    {
        return new StatusCacheService(
            reader,
            usageClient,
            clock,
            options: new StatusCacheServiceOptions
            {
                Preferences = new WidgetPreferences
                {
                    RefreshPeriodSeconds = 300,
                },
            });
    }

    private static UsageFetchResult CreateSuccessResult(string? profileId, DateTimeOffset resetAtUtc)
    {
        return new UsageFetchResult
        {
            ProfileId = profileId,
            Outcome = UsageFetchOutcome.Succeeded,
            Availability = StatusAvailability.Available(),
            Buckets =
            [
                new UsageBucketSnapshot
                {
                    BucketId = "codex",
                    BucketLabel = "codex",
                    BucketKind = UsageBucketKind.MainCodex,
                    FetchStatus = UsageBucketFetchStatus.Succeeded,
                    Availability = StatusAvailability.Available(),
                    Windows =
                    [
                        new UsageWindowSnapshot
                        {
                            WindowKind = UsageWindowKind.FiveHour,
                            DurationSeconds = 5 * 60 * 60,
                            ResetAtUnixSeconds = resetAtUtc.ToUnixTimeSeconds(),
                            UsedPercent = 25,
                            QuotaLeftPercent = 75,
                            TimeLeftPercent = 50,
                            Availability = StatusAvailability.Available(),
                        },
                    ],
                },
            ],
        };
    }

    private static ProfileSnapshot CreateProfileSnapshot(DateTimeOffset capturedAtUtc, IReadOnlyList<SyntheticProfile> profiles)
    {
        var current = profiles.FirstOrDefault(profile => profile.IsCurrent);
        return new ProfileSnapshot
        {
            CapturedAtUtc = capturedAtUtc,
            Paths = new CodexHomePaths
            {
                HomeDirectory = "/codex",
                CodexDirectory = "/codex/.codex",
                CurrentAuthPath = "/codex/.codex/auth.json",
                ProfilesDirectory = "/codex/.codex/profiles",
                ProfilesIndexPath = "/codex/.codex/profiles/profiles.json",
                ProfilesLockPath = "/codex/.codex/profiles/profiles.lock",
                ConfigPath = "/codex/.codex/config.toml",
            },
            Profiles = profiles.Select(ToProfileStatus).ToArray(),
            CurrentProfileId = current?.ProfileId,
            Sources =
            [
                CreateAvailableSource(StatusSourceKind.CurrentAuth, capturedAtUtc),
                CreateAvailableSource(StatusSourceKind.ConfigToml, capturedAtUtc),
                CreateAvailableSource(StatusSourceKind.ProfilesIndex, capturedAtUtc),
                CreateAvailableSource(StatusSourceKind.SavedProfileAuth, capturedAtUtc),
            ],
            CurrentAuthProfile = current is null ? null : ToAuthProfile(current, AuthProfileSourceKind.CurrentAuth),
            SavedProfiles = profiles.Where(profile => profile.ProfileId is not null)
                .Select(profile => ToAuthProfile(profile, AuthProfileSourceKind.SavedProfile))
                .ToArray(),
            UsageCredentialReferences = profiles
                .Where(profile => profile.AuthKind == ProfileAuthKind.Login && profile.Eligibility == ProfileUsageEligibility.Eligible)
                .Select(profile => new ProfileUsageCredentialReference
                {
                    ProfileId = profile.ProfileId,
                    AuthKind = profile.AuthKind,
                    UsageEligibility = profile.Eligibility,
                    LoginName = profile.LoginName,
                    SubscriptionTier = profile.SubscriptionTier,
                    SourcePath = profile.SourcePath,
                    AccountId = profile.AccountId,
                    AccessToken = profile.AccessToken,
                    RefreshToken = profile.RefreshToken,
                })
                .ToArray(),
            RawSources = new RawProfileSnapshot
            {
                ConfigTomlFile = new RawProfileSourceFile
                {
                    SourceKind = StatusSourceKind.ConfigToml,
                    Path = "/codex/.codex/config.toml",
                    ReadState = ProfileSourceParseState.Available,
                    Content = "chatgpt_base_url = \"https://chatgpt.com/backend-api\"\n",
                },
            },
        };
    }

    private static ProfileStatus ToProfileStatus(SyntheticProfile profile)
    {
        return new ProfileStatus
        {
            Profile = new ProfileDescriptor
            {
                ProfileId = profile.ProfileId,
                DisplayName = profile.DisplayName,
                LoginName = profile.LoginName,
                SubscriptionTier = profile.SubscriptionTier,
                IsCurrent = profile.IsCurrent,
                AuthKind = profile.AuthKind,
                UsageEligibility = profile.Eligibility,
                SourceStatus = CreateAvailableSource(profile.IsCurrent ? StatusSourceKind.CurrentAuth : StatusSourceKind.SavedProfileAuth, DateTimeOffset.UnixEpoch),
            },
            Diagnostics = Array.Empty<SourceDiagnostic>(),
        };
    }

    private static AuthProfile ToAuthProfile(SyntheticProfile profile, AuthProfileSourceKind sourceKind)
    {
        return new AuthProfile
        {
            ProfileId = profile.ProfileId,
            SourceKind = sourceKind,
            ParseState = ProfileSourceParseState.Available,
            SourcePath = profile.SourcePath,
            Tokens = new AuthTokens
            {
                AccountId = profile.AccountId,
                AccessToken = profile.AccessToken,
                RefreshToken = profile.RefreshToken,
            },
            IsApiKeyProfile = profile.AuthKind == ProfileAuthKind.ApiKey,
            IndexEntry = new ProfileIndexEntry
            {
                ProfileId = profile.ProfileId ?? string.Empty,
                Label = profile.DisplayName,
                Email = profile.LoginName,
                Plan = profile.SubscriptionTier.ToString(),
                IsApiKey = profile.AuthKind == ProfileAuthKind.ApiKey,
                ParseState = ProfileSourceParseState.Available,
            },
        };
    }

    private static SourceStatus CreateAvailableSource(StatusSourceKind kind, DateTimeOffset observedAtUtc)
    {
        return new SourceStatus
        {
            Source = kind,
            State = SourceStatusState.Available,
            Availability = StatusAvailability.Available(),
            ObservedAtUtc = observedAtUtc,
        };
    }

    private static SyntheticProfile CreateEligibleProfile(
        string profileId,
        bool isCurrent,
        string sourcePath,
        string accountId,
        string? displayName = null)
    {
        return new SyntheticProfile(
            profileId,
            displayName ?? profileId,
            $"{profileId}@example.invalid",
            isCurrent,
            ProfileAuthKind.Login,
            ProfileUsageEligibility.Eligible,
            sourcePath,
            SubscriptionTier.Pro,
            accountId,
            $"access-{profileId}",
            $"refresh-{profileId}");
    }

    private static SyntheticProfile CreateApiKeyProfile(string profileId, bool isCurrent, string sourcePath)
    {
        return new SyntheticProfile(
            profileId,
            profileId,
            null,
            isCurrent,
            ProfileAuthKind.ApiKey,
            ProfileUsageEligibility.ApiKeyProfile,
            sourcePath,
            SubscriptionTier.Unknown,
            null,
            null,
            null);
    }

    private static SyntheticProfile CreateIneligibleLoginProfile(string profileId, ProfileUsageEligibility eligibility, string sourcePath)
    {
        return new SyntheticProfile(
            profileId,
            profileId,
            $"{profileId}@example.invalid",
            false,
            ProfileAuthKind.Login,
            eligibility,
            sourcePath,
            SubscriptionTier.Pro,
            eligibility == ProfileUsageEligibility.MissingAccountId ? null : $"acct-{profileId}",
            eligibility == ProfileUsageEligibility.MissingAccessToken ? null : $"access-{profileId}",
            $"refresh-{profileId}");
    }

    private sealed record SyntheticProfile(
        string? ProfileId,
        string DisplayName,
        string? LoginName,
        bool IsCurrent,
        ProfileAuthKind AuthKind,
        ProfileUsageEligibility Eligibility,
        string SourcePath,
        SubscriptionTier SubscriptionTier,
        string? AccountId,
        string? AccessToken,
        string? RefreshToken);

    private sealed class FakeClock(DateTimeOffset nowUtc) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = nowUtc;

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }

    private sealed class FakeProfileSnapshotReader(params ProfileSnapshot[] snapshots) : IProfileSnapshotReader
    {
        private readonly Queue<ProfileSnapshot> snapshots = new(snapshots);

        public int CallCount { get; private set; }

        public Task<ProfileSnapshot> ReadAsync(ProfileSnapshotReadOptions? options = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            if (snapshots.Count == 0)
            {
                throw new InvalidOperationException("No synthetic profile snapshots remain.");
            }

            return Task.FromResult(snapshots.Dequeue());
        }
    }

    private sealed class FakeUsageClient : IUsageClient
    {
        private readonly Queue<UsageFetchResult> queuedResults = new();
        private int concurrentCalls;
        private int callCount;
        private int maxConcurrentCalls;

        public Func<UsageProfileRequest, Task<UsageFetchResult>>? Handler { get; set; }

        public CancellationToken CurrentCancellationToken { get; private set; }

        public int CallCount => Volatile.Read(ref callCount);

        public int MaxConcurrentCalls => Volatile.Read(ref maxConcurrentCalls);

        public void QueueResult(UsageFetchResult result) => queuedResults.Enqueue(result);

        public void EnterConcurrentCall()
        {
            var currentConcurrentCalls = Interlocked.Increment(ref concurrentCalls);
            while (true)
            {
                var currentMax = Volatile.Read(ref maxConcurrentCalls);
                if (currentConcurrentCalls <= currentMax)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref maxConcurrentCalls, currentConcurrentCalls, currentMax) == currentMax)
                {
                    break;
                }
            }
        }

        public void ExitConcurrentCall()
        {
            Interlocked.Decrement(ref concurrentCalls);
        }

        public async Task<UsageFetchResult> FetchAsync(UsageProfileRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            CurrentCancellationToken = cancellationToken;

            if (queuedResults.Count > 0)
            {
                return queuedResults.Dequeue();
            }

            if (Handler is not null)
            {
                return await Handler(request).ConfigureAwait(false);
            }

            throw new InvalidOperationException("No synthetic usage result or handler configured.");
        }
    }
}
