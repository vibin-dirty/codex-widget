using CodexWidget.Core;
using CodexWidget.Profiles;
using CodexWidget.Usage;

namespace CodexWidget.Status;

public sealed class StatusCacheService : IStatusCacheService
{
    private const string MainBucketId = "codex";
    private const string MainBucketLabel = "codex";

    private readonly IProfileSnapshotReader profileSnapshotReader;
    private readonly IUsageClient usageClient;
    private readonly IClock clock;
    private readonly IConfigTomlParser configTomlParser;
    private readonly StatusCacheServiceOptions options;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly CancellationTokenSource disposeCts = new();

    private StatusSnapshot currentSnapshot = new();
    private ProfileSnapshot? cachedProfileSnapshot;
    private IReadOnlyList<CachedProfileUsageEntry> cachedUsages = Array.Empty<CachedProfileUsageEntry>();
    private bool isDisposed;

    public StatusCacheService(
        IProfileSnapshotReader profileSnapshotReader,
        IUsageClient usageClient,
        IClock? clock = null,
        IConfigTomlParser? configTomlParser = null,
        StatusCacheServiceOptions? options = null)
    {
        this.profileSnapshotReader = profileSnapshotReader ?? throw new ArgumentNullException(nameof(profileSnapshotReader));
        this.usageClient = usageClient ?? throw new ArgumentNullException(nameof(usageClient));
        this.clock = clock ?? SystemClock.Instance;
        this.configTomlParser = configTomlParser ?? new ConfigTomlParser();
        this.options = options ?? new StatusCacheServiceOptions();

        if (this.options.MaxConcurrentUsageFetches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxConcurrentUsageFetches must be greater than zero.");
        }
    }

    public StatusSnapshot CurrentSnapshot => currentSnapshot;

    public CodexHomePaths? CurrentPaths => cachedProfileSnapshot?.Paths;

    public bool IsInitialized { get; private set; }

    public event EventHandler<StatusSnapshotChangedEventArgs>? SnapshotChanged;

    public Task<StatusSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
    {
        return IsInitialized
            ? Task.FromResult(CurrentSnapshot)
            : RefreshAsync(StatusRefreshReason.Startup, StatusRefreshScope.Full, cancellationToken);
    }

    public async Task<StatusSnapshot> RefreshAsync(
        StatusRefreshReason reason,
        StatusRefreshScope scope,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var requestedAtUtc = clock.UtcNow;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(disposeCts.Token, cancellationToken);
        var linkedToken = linkedCts.Token;

        await refreshGate.WaitAsync(linkedToken).ConfigureAwait(false);
        try
        {
            var startedAtUtc = clock.UtcNow;
            PublishSnapshot(currentSnapshot with
            {
                RefreshState = new StatusRefreshState
                {
                    Reason = reason,
                    Scope = scope,
                    Outcome = StatusRefreshOutcome.Running,
                    RequestedAtUtc = requestedAtUtc,
                    StartedAtUtc = startedAtUtc,
                },
            });

            linkedToken.ThrowIfCancellationRequested();

            var profileSnapshot = await ResolveProfileSnapshotAsync(scope, linkedToken).ConfigureAwait(false);
            linkedToken.ThrowIfCancellationRequested();

            var completedAtUtc = clock.UtcNow;

            if (IsCompleteProfileReadFailure(profileSnapshot) && cachedProfileSnapshot is not null && currentSnapshot.Profiles.Count > 0)
            {
                var failure = SelectFirstDiagnostic(profileSnapshot.Diagnostics, profileSnapshot.Sources.SelectMany(source => source.Diagnostics));
                var preservedSnapshot = BuildPreservedSnapshot(
                    currentSnapshot,
                    completedAtUtc,
                    reason,
                    scope,
                    requestedAtUtc,
                    startedAtUtc,
                    failure,
                    options.Preferences);

                PublishSnapshot(preservedSnapshot);
                return preservedSnapshot;
            }

            var mergedSnapshot = await BuildSnapshotAsync(profileSnapshot, scope, linkedToken).ConfigureAwait(false);
            var refreshFailure = SelectRefreshFailure(mergedSnapshot);
            var refreshOutcome = refreshFailure is null
                ? StatusRefreshOutcome.Succeeded
                : StatusRefreshOutcome.Failed;

            mergedSnapshot = mergedSnapshot with
            {
                CapturedAtUtc = completedAtUtc,
                RefreshState = new StatusRefreshState
                {
                    Reason = reason,
                    Scope = scope,
                    Outcome = refreshOutcome,
                    RequestedAtUtc = requestedAtUtc,
                    StartedAtUtc = startedAtUtc,
                    CompletedAtUtc = completedAtUtc,
                    Failure = refreshFailure?.WithRedactedContent(),
                },
                NextScheduledRefreshAtUtc = StatusRefreshScheduling.CalculateNextScheduledRefreshAtUtc(
                    mergedSnapshot.Profiles,
                    completedAtUtc,
                    options.Preferences),
            };

            cachedProfileSnapshot = profileSnapshot;
            cachedUsages = BuildCachedUsages(mergedSnapshot.Profiles, profileSnapshot);
            IsInitialized = true;

            PublishSnapshot(mergedSnapshot);
            return mergedSnapshot;
        }
        catch (OperationCanceledException) when (disposeCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            var completedAtUtc = clock.UtcNow;
            var cancelledSnapshot = currentSnapshot with
            {
                RefreshState = new StatusRefreshState
                {
                    Reason = reason,
                    Scope = scope,
                    Outcome = StatusRefreshOutcome.Cancelled,
                    RequestedAtUtc = requestedAtUtc,
                    StartedAtUtc = currentSnapshot.RefreshState.StartedAtUtc ?? completedAtUtc,
                    CompletedAtUtc = completedAtUtc,
                    Failure = SourceDiagnostic.Create(
                        SourceDiagnosticCode.Error,
                        SourceDiagnosticSeverity.Warning,
                        "Status refresh was canceled.",
                        observedAtUtc: completedAtUtc),
                },
            };

            PublishSnapshot(cancelledSnapshot);
            return cancelledSnapshot;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        disposeCts.Cancel();
        disposeCts.Dispose();
        refreshGate.Dispose();
    }

    private async Task<ProfileSnapshot> ResolveProfileSnapshotAsync(StatusRefreshScope scope, CancellationToken cancellationToken)
    {
        if (scope == StatusRefreshScope.UsageOnly && cachedProfileSnapshot is not null)
        {
            return cachedProfileSnapshot;
        }

        return await profileSnapshotReader.ReadAsync(options.ProfileSnapshotReadOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StatusSnapshot> BuildSnapshotAsync(
        ProfileSnapshot profileSnapshot,
        StatusRefreshScope scope,
        CancellationToken cancellationToken)
    {
        var chatGptBaseUrl = ResolveChatGptBaseUrl(profileSnapshot);
        var workItems = CreateWorkItems(profileSnapshot);
        var usageResultsByKey = scope == StatusRefreshScope.ProfileOnly
            ? new Dictionary<string, UsageFetchResult>(StringComparer.Ordinal)
            : await FetchUsageResultsAsync(workItems, profileSnapshot.Paths.ProfilesLockPath, chatGptBaseUrl, cancellationToken).ConfigureAwait(false);

        var usageSourceStatuses = new List<SourceStatus>();
        var profiles = new List<ProfileStatus>(workItems.Count);

        foreach (var workItem in workItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var previousUsage = FindCachedUsage(workItem.Identity);
            var resolvedProfile = ResolveProfileStatus(workItem, scope, usageResultsByKey, previousUsage, usageSourceStatuses);
            profiles.Add(resolvedProfile);
        }

        var sources = profileSnapshot.Sources
            .Concat(usageSourceStatuses)
            .ToArray();

        return new StatusSnapshot
        {
            CapturedAtUtc = profileSnapshot.CapturedAtUtc,
            Profiles = profiles,
            CurrentProfileId = profileSnapshot.CurrentProfileId,
            Sources = sources,
        };
    }

    private async Task<Dictionary<string, UsageFetchResult>> FetchUsageResultsAsync(
        IReadOnlyList<ProfileWorkItem> workItems,
        string profilesLockPath,
        string? chatGptBaseUrl,
        CancellationToken cancellationToken)
    {
        var eligibleItems = workItems
            .Where(item => item.Credential is not null)
            .ToArray();

        var results = new Dictionary<string, UsageFetchResult>(StringComparer.Ordinal);
        if (eligibleItems.Length == 0)
        {
            return results;
        }

        using var concurrencyGate = new SemaphoreSlim(Math.Min(options.MaxConcurrentUsageFetches, 3), Math.Min(options.MaxConcurrentUsageFetches, 3));
        var tasks = eligibleItems.Select(async item =>
        {
            await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var credential = item.Credential!;
                var request = new UsageProfileRequest
                {
                    ProfileId = credential.ProfileId,
                    LoginName = credential.LoginName,
                    SubscriptionTier = credential.SubscriptionTier,
                    ChatGptBaseUrl = chatGptBaseUrl,
                    SourcePath = credential.SourcePath,
                    ProfilesLockPath = profilesLockPath,
                    AccountId = credential.AccountId,
                    IdToken = credential.IdToken,
                    AccessToken = credential.AccessToken,
                    RefreshToken = credential.RefreshToken,
                };

                var result = await usageClient.FetchAsync(request, cancellationToken).ConfigureAwait(false);
                return (item.CacheKey, Result: result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var observedAtUtc = clock.UtcNow;
                var diagnostic = SourceDiagnostic.Create(
                    SourceDiagnosticCode.Error,
                    SourceDiagnosticSeverity.Error,
                    "Usage refresh failed with an unexpected exception.",
                    detail: exception.Message,
                    context:
                    [
                        new KeyValuePair<string, string?>("profileId", item.BaseStatus.Profile.ProfileId),
                        new KeyValuePair<string, string?>("loginName", item.BaseStatus.Profile.LoginName),
                    ],
                    observedAtUtc: observedAtUtc);

                return (item.CacheKey, Result: new UsageFetchResult
                {
                    ProfileId = item.BaseStatus.Profile.ProfileId,
                    Outcome = UsageFetchOutcome.HttpError,
                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Error),
                    Diagnostics = [diagnostic],
                });
            }
            finally
            {
                concurrencyGate.Release();
            }
        });

        foreach (var completed in await Task.WhenAll(tasks).ConfigureAwait(false))
        {
            results[completed.CacheKey] = completed.Result;
        }

        return results;
    }

    private ProfileStatus ResolveProfileStatus(
        ProfileWorkItem workItem,
        StatusRefreshScope scope,
        IReadOnlyDictionary<string, UsageFetchResult> usageResultsByKey,
        CachedProfileUsageEntry? previousUsage,
        ICollection<SourceStatus> usageSourceStatuses)
    {
        if (workItem.Credential is null)
        {
            return CreateUnavailableProfileStatus(
                workItem.BaseStatus,
                MapEligibilityAvailability(workItem.BaseStatus.Profile.UsageEligibility),
                MapEligibilityDiagnostic(workItem.BaseStatus.Profile.UsageEligibility),
                usageSourceStatuses);
        }

        if (scope == StatusRefreshScope.ProfileOnly)
        {
            if (previousUsage is not null && previousUsage.Identity.Matches(workItem.Identity))
            {
                return MergeUsageIntoProfile(workItem.BaseStatus, previousUsage.AllBuckets, Array.Empty<SourceDiagnostic>());
            }

            var diagnostic = SourceDiagnostic.Create(
                SourceDiagnosticCode.Unavailable,
                SourceDiagnosticSeverity.Info,
                "Usage was not refreshed during a profile-only status update.",
                context:
                [
                    new KeyValuePair<string, string?>("profileId", workItem.BaseStatus.Profile.ProfileId),
                ],
                observedAtUtc: clock.UtcNow);

            return CreateExplicitUsageUnavailable(workItem.BaseStatus, StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable), UsageBucketFetchStatus.NotRequested, [diagnostic], usageSourceStatuses);
        }

        if (!usageResultsByKey.TryGetValue(workItem.CacheKey, out var result))
        {
            var diagnostic = SourceDiagnostic.Create(
                SourceDiagnosticCode.Unavailable,
                SourceDiagnosticSeverity.Warning,
                "Usage refresh did not produce a result for an eligible profile.",
                context:
                [
                    new KeyValuePair<string, string?>("profileId", workItem.BaseStatus.Profile.ProfileId),
                ],
                observedAtUtc: clock.UtcNow);

            return CreateExplicitUsageUnavailable(workItem.BaseStatus, StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable), UsageBucketFetchStatus.Unavailable, [diagnostic], usageSourceStatuses);
        }

        if (IsTransientUsageFailure(result.Outcome) && previousUsage is not null && previousUsage.Identity.Matches(workItem.Identity))
        {
            var staleDiagnostic = SourceDiagnostic.Create(
                SourceDiagnosticCode.Stale,
                SourceDiagnosticSeverity.Warning,
                "Usage refresh failed transiently and previously cached usage was preserved.",
                context:
                [
                    new KeyValuePair<string, string?>("profileId", workItem.BaseStatus.Profile.ProfileId),
                    new KeyValuePair<string, string?>("usageOutcome", result.Outcome.ToString()),
                ],
                observedAtUtc: clock.UtcNow);

            usageSourceStatuses.Add(new SourceStatus
            {
                Source = StatusSourceKind.Cache,
                State = SourceStatusState.Stale,
                Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Stale),
                ObservedAtUtc = staleDiagnostic.ObservedAtUtc,
                Diagnostics = result.Diagnostics.Concat([staleDiagnostic]).ToArray(),
            });

            return MergeUsageIntoProfile(workItem.BaseStatus, previousUsage.AllBuckets, result.Diagnostics.Concat([staleDiagnostic]).ToArray());
        }

        if (result.Diagnostics.Count > 0 || result.EndpointResolution.Diagnostics.Count > 0 || result.Outcome != UsageFetchOutcome.Succeeded)
        {
            usageSourceStatuses.Add(BuildUsageSourceStatus(result));
        }

        if (result.Buckets.Count > 0)
        {
            return MergeUsageIntoProfile(workItem.BaseStatus, result.Buckets, result.Diagnostics);
        }

        return CreateExplicitUsageUnavailable(
            workItem.BaseStatus,
            result.Availability,
            MapFetchStatus(result.Outcome),
            result.Diagnostics,
            usageSourceStatuses);
    }

    private static ProfileStatus MergeUsageIntoProfile(
        ProfileStatus baseStatus,
        IReadOnlyList<UsageBucketSnapshot> buckets,
        IReadOnlyList<SourceDiagnostic> diagnostics)
    {
        var allDiagnostics = baseStatus.Diagnostics.Concat(diagnostics).ToArray();
        var mainBucket = buckets.FirstOrDefault(IsMainBucket);
        var sparkBucket = buckets.FirstOrDefault(bucket => bucket.BucketKind == UsageBucketKind.Spark);

        return baseStatus with
        {
            MainBucket = mainBucket,
            SparkBucket = sparkBucket,
            AllBuckets = buckets,
            Diagnostics = allDiagnostics,
        };
    }

    private static ProfileStatus CreateUnavailableProfileStatus(
        ProfileStatus baseStatus,
        StatusAvailability availability,
        SourceDiagnostic diagnostic,
        ICollection<SourceStatus> usageSourceStatuses)
    {
        usageSourceStatuses.Add(new SourceStatus
        {
            Source = StatusSourceKind.UsageBucket,
            State = SourceStatusState.Unavailable,
            Availability = availability,
            ObservedAtUtc = diagnostic.ObservedAtUtc,
            Diagnostics = [diagnostic],
        });

        return CreateExplicitUsageUnavailable(baseStatus, availability, UsageBucketFetchStatus.Unavailable, [diagnostic], usageSourceStatuses: null);
    }

    private static ProfileStatus CreateExplicitUsageUnavailable(
        ProfileStatus baseStatus,
        StatusAvailability availability,
        UsageBucketFetchStatus fetchStatus,
        IReadOnlyList<SourceDiagnostic> diagnostics,
        ICollection<SourceStatus>? usageSourceStatuses)
    {
        var mainBucket = new UsageBucketSnapshot
        {
            BucketId = MainBucketId,
            BucketLabel = MainBucketLabel,
            BucketKind = UsageBucketKind.MainCodex,
            FetchStatus = fetchStatus,
            Availability = availability,
            Windows = Array.Empty<UsageWindowSnapshot>(),
        };

        return baseStatus with
        {
            MainBucket = mainBucket,
            SparkBucket = null,
            AllBuckets = [mainBucket],
            Diagnostics = baseStatus.Diagnostics.Concat(diagnostics).ToArray(),
        };
    }

    private static UsageBucketFetchStatus MapFetchStatus(UsageFetchOutcome outcome)
    {
        return outcome switch
        {
            UsageFetchOutcome.Unauthorized => UsageBucketFetchStatus.Unauthorized,
            UsageFetchOutcome.Succeeded => UsageBucketFetchStatus.Succeeded,
            UsageFetchOutcome.MissingBucket or UsageFetchOutcome.MissingWindow => UsageBucketFetchStatus.Partial,
            UsageFetchOutcome.EndpointRejected or UsageFetchOutcome.MissingRequiredProfileFields => UsageBucketFetchStatus.Unavailable,
            _ => UsageBucketFetchStatus.Error,
        };
    }

    private static StatusAvailability MapEligibilityAvailability(ProfileUsageEligibility eligibility)
    {
        return eligibility switch
        {
            ProfileUsageEligibility.ApiKeyProfile => StatusAvailability.Unavailable(StatusAvailabilityCode.ApiKeyProfile),
            ProfileUsageEligibility.MissingAccessToken or ProfileUsageEligibility.MissingAccountId or ProfileUsageEligibility.MissingLoginName or ProfileUsageEligibility.MissingSubscriptionTier
                => StatusAvailability.Unavailable(StatusAvailabilityCode.MissingRequiredField),
            ProfileUsageEligibility.MalformedAuth => StatusAvailability.Unavailable(StatusAvailabilityCode.Malformed),
            ProfileUsageEligibility.CredentialsUnavailable or ProfileUsageEligibility.SourceUnavailable => StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable),
            _ => StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable),
        };
    }

    private static SourceDiagnostic MapEligibilityDiagnostic(ProfileUsageEligibility eligibility)
    {
        var (code, summary) = eligibility switch
        {
            ProfileUsageEligibility.ApiKeyProfile => (SourceDiagnosticCode.ApiKeyProfile, "Usage is unavailable for API-key profiles."),
            ProfileUsageEligibility.MissingAccessToken => (SourceDiagnosticCode.MissingRequiredField, "Usage is unavailable because the profile is missing an access token."),
            ProfileUsageEligibility.MissingAccountId => (SourceDiagnosticCode.MissingRequiredField, "Usage is unavailable because the profile is missing an account id."),
            ProfileUsageEligibility.MissingLoginName => (SourceDiagnosticCode.MissingRequiredField, "Usage is unavailable because the profile is missing a decoded login name."),
            ProfileUsageEligibility.MissingSubscriptionTier => (SourceDiagnosticCode.MissingRequiredField, "Usage is unavailable because the profile is missing a subscription tier."),
            ProfileUsageEligibility.MalformedAuth => (SourceDiagnosticCode.Malformed, "Usage is unavailable because the profile auth data is malformed."),
            ProfileUsageEligibility.CredentialsUnavailable => (SourceDiagnosticCode.Unavailable, "Usage is unavailable because credentials are not file-backed."),
            ProfileUsageEligibility.SourceUnavailable => (SourceDiagnosticCode.Unavailable, "Usage is unavailable because the profile auth source is unavailable."),
            _ => (SourceDiagnosticCode.Unavailable, "Usage is unavailable for this profile."),
        };

        return SourceDiagnostic.Create(code, SourceDiagnosticSeverity.Info, summary, observedAtUtc: DateTimeOffset.UtcNow);
    }

    private static bool IsTransientUsageFailure(UsageFetchOutcome outcome)
    {
        return outcome is UsageFetchOutcome.NetworkError
            or UsageFetchOutcome.Timeout
            or UsageFetchOutcome.HttpError
            or UsageFetchOutcome.Canceled;
    }

    private static SourceStatus BuildUsageSourceStatus(UsageFetchResult result)
    {
        var state = result.Outcome switch
        {
            UsageFetchOutcome.Succeeded => SourceStatusState.Available,
            UsageFetchOutcome.EndpointRejected or UsageFetchOutcome.MissingRequiredProfileFields or UsageFetchOutcome.MissingBucket or UsageFetchOutcome.MissingWindow => SourceStatusState.Unavailable,
            UsageFetchOutcome.Unauthorized or UsageFetchOutcome.TokenRefreshFailed => SourceStatusState.Unavailable,
            UsageFetchOutcome.MalformedResponse => SourceStatusState.Malformed,
            UsageFetchOutcome.NetworkError or UsageFetchOutcome.Timeout or UsageFetchOutcome.HttpError => SourceStatusState.Error,
            UsageFetchOutcome.Canceled => SourceStatusState.Stale,
            _ => SourceStatusState.Error,
        };

        return new SourceStatus
        {
            Source = StatusSourceKind.UsageBucket,
            State = state,
            Availability = result.Availability,
            ObservedAtUtc = result.Diagnostics.FirstOrDefault()?.ObservedAtUtc,
            Diagnostics = result.EndpointResolution.Diagnostics.Concat(result.Diagnostics).ToArray(),
        };
    }

    private IReadOnlyList<ProfileWorkItem> CreateWorkItems(ProfileSnapshot profileSnapshot)
    {
        var items = new List<ProfileWorkItem>(profileSnapshot.Profiles.Count);
        foreach (var baseStatus in profileSnapshot.Profiles)
        {
            var authProfile = MatchAuthProfile(profileSnapshot, baseStatus);
            var credential = MatchCredentialReference(profileSnapshot.UsageCredentialReferences, baseStatus, authProfile?.SourcePath);
            var identity = new ProfileUsageIdentity(
                baseStatus.Profile.ProfileId,
                credential?.LoginName ?? baseStatus.Profile.LoginName,
                credential?.SourcePath ?? authProfile?.SourcePath,
                credential?.AccountId ?? authProfile?.Tokens.AccountId);

            items.Add(new ProfileWorkItem(
                BuildCacheKey(identity),
                baseStatus,
                identity,
                credential));
        }

        return items;
    }

    private static AuthProfile? MatchAuthProfile(ProfileSnapshot snapshot, ProfileStatus baseStatus)
    {
        if (baseStatus.Profile.IsCurrent && snapshot.CurrentAuthProfile is not null)
        {
            return snapshot.CurrentAuthProfile;
        }

        if (!string.IsNullOrWhiteSpace(baseStatus.Profile.ProfileId))
        {
            return snapshot.SavedProfiles.FirstOrDefault(profile =>
                string.Equals(profile.ProfileId, baseStatus.Profile.ProfileId, StringComparison.Ordinal));
        }

        return null;
    }

    private static ProfileUsageCredentialReference? MatchCredentialReference(
        IReadOnlyList<ProfileUsageCredentialReference> credentials,
        ProfileStatus baseStatus,
        string? sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(baseStatus.Profile.ProfileId))
        {
            return credentials.FirstOrDefault(credential =>
                string.Equals(credential.ProfileId, baseStatus.Profile.ProfileId, StringComparison.Ordinal));
        }

        return credentials.FirstOrDefault(credential =>
            string.Equals(credential.LoginName, baseStatus.Profile.LoginName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(credential.SourcePath, sourcePath, StringComparison.Ordinal));
    }

    private string? ResolveChatGptBaseUrl(ProfileSnapshot profileSnapshot)
    {
        var configFile = profileSnapshot.RawSources.ConfigTomlFile;
        if (configFile?.ReadState != ProfileSourceParseState.Available || string.IsNullOrWhiteSpace(configFile.Content))
        {
            return null;
        }

        var parsed = configTomlParser.Parse(
            new ConfigTomlParseInput
            {
                SourcePath = configFile.Path,
                TomlContent = configFile.Content,
            },
            profileSnapshot.CapturedAtUtc);

        return parsed.ChatGptBaseUrl;
    }

    private CachedProfileUsageEntry? FindCachedUsage(ProfileUsageIdentity identity)
    {
        return cachedUsages.FirstOrDefault(entry => entry.Identity.Matches(identity));
    }

    private static IReadOnlyList<CachedProfileUsageEntry> BuildCachedUsages(
        IReadOnlyList<ProfileStatus> profiles,
        ProfileSnapshot profileSnapshot)
    {
        var workItems = new List<CachedProfileUsageEntry>();
        foreach (var profile in profiles)
        {
            if (!HasUsableUsage(profile))
            {
                continue;
            }

            var authProfile = MatchAuthProfile(profileSnapshot, profile);
            var credential = MatchCredentialReference(profileSnapshot.UsageCredentialReferences, profile, authProfile?.SourcePath);
            var identity = new ProfileUsageIdentity(
                profile.Profile.ProfileId,
                credential?.LoginName ?? profile.Profile.LoginName,
                credential?.SourcePath ?? authProfile?.SourcePath,
                credential?.AccountId ?? authProfile?.Tokens.AccountId);

            workItems.Add(new CachedProfileUsageEntry(identity, profile.AllBuckets));
        }

        return workItems;
    }

    private static bool HasUsableUsage(ProfileStatus profile)
    {
        return profile.AllBuckets.SelectMany(bucket => bucket.Windows).Any(window => window.Availability.IsAvailable);
    }

    private static bool IsMainBucket(UsageBucketSnapshot bucket)
    {
        return bucket.BucketKind == UsageBucketKind.MainCodex
            || bucket.BucketId.Equals(MainBucketId, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCacheKey(ProfileUsageIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.ProfileId))
        {
            return $"id:{identity.ProfileId}|login:{identity.LoginName}|source:{identity.SourcePath}|acct:{identity.AccountId}";
        }

        return $"login:{identity.LoginName}|source:{identity.SourcePath}|acct:{identity.AccountId}";
    }

    private static bool IsCompleteProfileReadFailure(ProfileSnapshot profileSnapshot)
    {
        return profileSnapshot.Profiles.Count == 0
            && profileSnapshot.Sources.Count > 0
            && profileSnapshot.Sources.All(source => source.State == SourceStatusState.Unavailable);
    }

    private static StatusSnapshot BuildPreservedSnapshot(
        StatusSnapshot previousSnapshot,
        DateTimeOffset completedAtUtc,
        StatusRefreshReason reason,
        StatusRefreshScope scope,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset startedAtUtc,
        SourceDiagnostic? failure,
        WidgetPreferences preferences)
    {
        var redactedFailure = failure?.WithRedactedContent();
        var cacheSource = new SourceStatus
        {
            Source = StatusSourceKind.Cache,
            State = SourceStatusState.Stale,
            Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.Stale),
            ObservedAtUtc = completedAtUtc,
            Diagnostics = redactedFailure is null ? Array.Empty<SourceDiagnostic>() : [redactedFailure],
        };

        return previousSnapshot with
        {
            CapturedAtUtc = completedAtUtc,
            NextScheduledRefreshAtUtc = StatusRefreshScheduling.CalculateNextScheduledRefreshAtUtc(
                previousSnapshot.Profiles,
                completedAtUtc,
                preferences),
            Sources = previousSnapshot.Sources.Concat([cacheSource]).ToArray(),
            RefreshState = new StatusRefreshState
            {
                Reason = reason,
                Scope = scope,
                Outcome = StatusRefreshOutcome.Failed,
                RequestedAtUtc = requestedAtUtc,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                Failure = redactedFailure,
            },
        };
    }

    private static SourceDiagnostic? SelectRefreshFailure(StatusSnapshot snapshot)
    {
        return snapshot.Sources
                   .Where(source => source.State is SourceStatusState.Error or SourceStatusState.Unavailable or SourceStatusState.Malformed or SourceStatusState.Stale)
                   .SelectMany(source => source.Diagnostics)
                   .FirstOrDefault(diagnostic => diagnostic.Severity != SourceDiagnosticSeverity.Info)
               ?? snapshot.Profiles
                   .SelectMany(profile => profile.Diagnostics)
                   .FirstOrDefault(diagnostic => diagnostic.Severity != SourceDiagnosticSeverity.Info);
    }

    private static SourceDiagnostic? SelectFirstDiagnostic(
        IEnumerable<SourceDiagnostic> preferredDiagnostics,
        IEnumerable<SourceDiagnostic> fallbackDiagnostics)
    {
        return preferredDiagnostics.FirstOrDefault(diagnostic => diagnostic.Severity != SourceDiagnosticSeverity.Info)
            ?? preferredDiagnostics.FirstOrDefault()
            ?? fallbackDiagnostics.FirstOrDefault(diagnostic => diagnostic.Severity != SourceDiagnosticSeverity.Info)
            ?? fallbackDiagnostics.FirstOrDefault();
    }

    private void PublishSnapshot(StatusSnapshot nextSnapshot)
    {
        var previousSnapshot = currentSnapshot;
        currentSnapshot = nextSnapshot;

        if (!Equals(previousSnapshot, nextSnapshot))
        {
            SnapshotChanged?.Invoke(this, new StatusSnapshotChangedEventArgs(previousSnapshot, nextSnapshot));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }

    private sealed record ProfileWorkItem(
        string CacheKey,
        ProfileStatus BaseStatus,
        ProfileUsageIdentity Identity,
        ProfileUsageCredentialReference? Credential);

    private sealed record CachedProfileUsageEntry(
        ProfileUsageIdentity Identity,
        IReadOnlyList<UsageBucketSnapshot> AllBuckets);

    private sealed record ProfileUsageIdentity(
        string? ProfileId,
        string? LoginName,
        string? SourcePath,
        string? AccountId)
    {
        public bool Matches(ProfileUsageIdentity? other)
        {
            if (other is null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(ProfileId) && !string.IsNullOrWhiteSpace(other.ProfileId))
            {
                return string.Equals(ProfileId, other.ProfileId, StringComparison.Ordinal)
                    && string.Equals(LoginName, other.LoginName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(SourcePath, other.SourcePath, StringComparison.Ordinal)
                    && string.Equals(AccountId, other.AccountId, StringComparison.Ordinal);
            }

            return string.Equals(LoginName, other.LoginName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(SourcePath, other.SourcePath, StringComparison.Ordinal)
                && string.Equals(AccountId, other.AccountId, StringComparison.Ordinal);
        }
    }
}
