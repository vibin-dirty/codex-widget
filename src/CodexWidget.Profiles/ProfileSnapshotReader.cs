using CodexWidget.Core;
using System.Diagnostics;

namespace CodexWidget.Profiles;

public sealed class ProfileSnapshotReader : IProfileSnapshotReader
{
    private static readonly TimeSpan MinimumRetryDelay = TimeSpan.FromMilliseconds(5);

    private readonly ICodexHomeResolver _homeResolver;
    private readonly IClock _clock;
    private readonly IAuthProfileParser _authParser;
    private readonly IProfilesIndexParser _indexParser;
    private readonly IConfigTomlParser _configParser;
    private readonly ProfileIdentityMatcher _identityMatcher;

    public ProfileSnapshotReader(
        ICodexHomeResolver? homeResolver = null,
        IClock? clock = null,
        IAuthProfileParser? authParser = null,
        IProfilesIndexParser? indexParser = null,
        IConfigTomlParser? configParser = null,
        ProfileIdentityMatcher? identityMatcher = null)
    {
        _homeResolver = homeResolver ?? new CodexHomeResolver();
        _clock = clock ?? SystemClock.Instance;
        _authParser = authParser ?? new AuthProfileParser();
        _indexParser = indexParser ?? new ProfilesIndexParser();
        _configParser = configParser ?? new ConfigTomlParser();
        _identityMatcher = identityMatcher ?? new ProfileIdentityMatcher();
    }

    public async Task<ProfileSnapshot> ReadAsync(ProfileSnapshotReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ProfileSnapshotReadOptions();
        var capturedAtUtc = options.CapturedAtUtcOverride ?? _clock.UtcNow;
        var paths = _homeResolver.Resolve(options.HomeResolution);

        try
        {
            return await ReadCoreAsync(paths, options, capturedAtUtc, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return BuildUnavailableSnapshot(
                paths,
                capturedAtUtc,
                "Profile snapshot read cancelled while waiting for profile lock.",
                SourceDiagnosticCode.Unavailable,
                SourceDiagnosticSeverity.Warning,
                StatusAvailabilityCode.Unavailable);
        }
        catch (Exception exception)
        {
            return BuildUnavailableSnapshot(
                paths,
                capturedAtUtc,
                "Profile snapshot read failed while accessing local profile sources.",
                SourceDiagnosticCode.Error,
                SourceDiagnosticSeverity.Error,
                StatusAvailabilityCode.Error,
                detail: exception.Message);
        }
    }

    private async Task<ProfileSnapshot> ReadCoreAsync(
        CodexHomePaths paths,
        ProfileSnapshotReadOptions options,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.HomeDirectory))
        {
            return BuildUnavailableSnapshot(
                paths,
                capturedAtUtc,
                "Codex home directory is missing; profile snapshot is unavailable.",
                SourceDiagnosticCode.Missing,
                SourceDiagnosticSeverity.Warning,
                StatusAvailabilityCode.Missing,
                context:
                [
                    new KeyValuePair<string, string?>("homeDirectoryPath", paths.HomeDirectory),
                ]);
        }

        if (!Directory.Exists(paths.CodexDirectory))
        {
            return BuildUnavailableSnapshot(
                paths,
                capturedAtUtc,
                "Codex configuration directory is missing; profile snapshot is unavailable.",
                SourceDiagnosticCode.Missing,
                SourceDiagnosticSeverity.Warning,
                StatusAvailabilityCode.Missing,
                context:
                [
                    new KeyValuePair<string, string?>("codexDirectoryPath", paths.CodexDirectory),
                ]);
        }

        if (!Directory.Exists(paths.ProfilesDirectory))
        {
            return BuildUnavailableSnapshot(
                paths,
                capturedAtUtc,
                "Codex profiles directory is missing; saved profile snapshot is unavailable.",
                SourceDiagnosticCode.Missing,
                SourceDiagnosticSeverity.Warning,
                StatusAvailabilityCode.Missing,
                context:
                [
                    new KeyValuePair<string, string?>("profilesDirectoryPath", paths.ProfilesDirectory),
                ]);
        }

        var lockAcquireTimeout = NormalizeDuration(options.LockAcquireTimeout, TimeSpan.FromMilliseconds(500));
        var lockRetryInterval = NormalizeDuration(options.LockRetryInterval, TimeSpan.FromMilliseconds(25));

        using var lockStream = await AcquireProfilesLockAsync(
            paths,
            lockAcquireTimeout,
            lockRetryInterval,
            cancellationToken).ConfigureAwait(false);

        if (lockStream is null)
        {
            return BuildUnavailableSnapshot(
                paths,
                capturedAtUtc,
                "Timed out waiting for exclusive profile lock before reading local profile sources.",
                SourceDiagnosticCode.Unavailable,
                SourceDiagnosticSeverity.Warning,
                StatusAvailabilityCode.Unavailable,
                context:
                [
                    new KeyValuePair<string, string?>("profilesLockPath", paths.ProfilesLockPath),
                    new KeyValuePair<string, string?>("lockAcquireTimeoutMs", ((int)lockAcquireTimeout.TotalMilliseconds).ToString()),
                ]);
        }

        var rawCurrentAuthFile = ReadSourceFile(paths.CurrentAuthPath, StatusSourceKind.CurrentAuth, capturedAtUtc);
        var rawConfigTomlFile = ReadSourceFile(paths.ConfigPath, StatusSourceKind.ConfigToml, capturedAtUtc);
        var rawProfilesIndexFile = ReadSourceFile(paths.ProfilesIndexPath, StatusSourceKind.ProfilesIndex, capturedAtUtc);
        var rawSavedProfileFiles = ReadSavedProfileFiles(paths.ProfilesDirectory, capturedAtUtc);

        var configProfile = ParseConfigProfile(rawConfigTomlFile, capturedAtUtc);
        var indexResult = ParseProfilesIndex(rawProfilesIndexFile, capturedAtUtc);
        var savedProfiles = rawSavedProfileFiles
            .Select(file => ResolveSavedProfile(file, indexResult.Entries, capturedAtUtc))
            .ToArray();
        var currentAuthProfile = ResolveCurrentAuthProfile(rawCurrentAuthFile, configProfile, indexResult.Entries, savedProfiles, capturedAtUtc);

        var duplicateSavedProfileId = SelectDuplicateSavedProfileId(currentAuthProfile, savedProfiles);
        if (duplicateSavedProfileId is not null
            && currentAuthProfile is not null)
        {
            var matchedSavedProfile = savedProfiles.First(profile => string.Equals(profile.AuthProfile.ProfileId, duplicateSavedProfileId, StringComparison.Ordinal));
            currentAuthProfile = currentAuthProfile with
            {
                AuthProfile = currentAuthProfile.AuthProfile with
                {
                    ProfileId = duplicateSavedProfileId,
                    IndexEntry = matchedSavedProfile.AuthProfile.IndexEntry,
                },
            };
        }

        var profileStatuses = new List<ProfileStatus>();
        var usageCredentialReferences = new List<ProfileUsageCredentialReference>();

        if (ShouldIncludeCurrentAuthProfile(rawCurrentAuthFile, currentAuthProfile))
        {
            AddProfileStatus(currentAuthProfile!, isCurrent: true, capturedAtUtc, profileStatuses, usageCredentialReferences);
        }

        foreach (var savedProfile in savedProfiles)
        {
            if (string.Equals(savedProfile.AuthProfile.ProfileId, duplicateSavedProfileId, StringComparison.Ordinal))
            {
                continue;
            }

            AddProfileStatus(savedProfile, isCurrent: false, capturedAtUtc, profileStatuses, usageCredentialReferences);
        }

        var currentProfileId = profileStatuses.FirstOrDefault(profile => profile.Profile.IsCurrent)?.Profile.ProfileId;
        var sourceStatuses = new[]
        {
            BuildCurrentAuthSourceStatus(rawCurrentAuthFile, currentAuthProfile, capturedAtUtc),
            ProfileSourceStatusMapper.ToSourceStatus(StatusSourceKind.ConfigToml, configProfile.ParseState, configProfile.Diagnostics, capturedAtUtc),
            ProfileSourceStatusMapper.ToSourceStatus(StatusSourceKind.ProfilesIndex, indexResult.ParseState, indexResult.Diagnostics, capturedAtUtc),
            BuildSavedProfilesSourceStatus(savedProfiles, capturedAtUtc),
        };

        var diagnostics = sourceStatuses
            .SelectMany(source => source.Diagnostics)
            .ToArray();

        return new ProfileSnapshot
        {
            CapturedAtUtc = capturedAtUtc,
            Paths = paths,
            Profiles = profileStatuses,
            CurrentProfileId = currentProfileId,
            Sources = sourceStatuses,
            Diagnostics = diagnostics,
            CurrentAuthProfile = currentAuthProfile?.AuthProfile,
            SavedProfiles = savedProfiles.Select(profile => profile.AuthProfile).ToArray(),
            SavedProfileIndex = indexResult.Entries,
            UsageCredentialReferences = usageCredentialReferences,
            RawSources = new RawProfileSnapshot
            {
                CurrentAuthFile = rawCurrentAuthFile,
                ConfigTomlFile = rawConfigTomlFile,
                ProfilesIndexFile = rawProfilesIndexFile,
                SavedProfileFiles = rawSavedProfileFiles,
            },
        };
    }

    private CodexConfigProfile ParseConfigProfile(RawProfileSourceFile rawConfigTomlFile, DateTimeOffset capturedAtUtc)
    {
        if (rawConfigTomlFile.ReadState != ProfileSourceParseState.Available)
        {
            return new CodexConfigProfile
            {
                ParseState = rawConfigTomlFile.ReadState,
                Diagnostics = rawConfigTomlFile.Diagnostics,
            };
        }

        return _configParser.Parse(new ConfigTomlParseInput
        {
            SourcePath = rawConfigTomlFile.Path,
            TomlContent = rawConfigTomlFile.Content,
        }, capturedAtUtc);
    }

    private ProfilesIndexParseResult ParseProfilesIndex(RawProfileSourceFile rawProfilesIndexFile, DateTimeOffset capturedAtUtc)
    {
        if (rawProfilesIndexFile.ReadState != ProfileSourceParseState.Available)
        {
            return new ProfilesIndexParseResult
            {
                ParseState = rawProfilesIndexFile.ReadState,
                Diagnostics = rawProfilesIndexFile.Diagnostics,
            };
        }

        return _indexParser.Parse(new ProfilesIndexParseInput
        {
            SourcePath = rawProfilesIndexFile.Path,
            JsonContent = rawProfilesIndexFile.Content,
        }, capturedAtUtc);
    }

    private ResolvedProfileEntry ResolveSavedProfile(
        RawProfileSourceFile rawSavedProfileFile,
        IReadOnlyDictionary<string, ProfileIndexEntry> indexEntries,
        DateTimeOffset capturedAtUtc)
    {
        AuthProfile parsedProfile;
        if (rawSavedProfileFile.ReadState == ProfileSourceParseState.Available)
        {
            parsedProfile = _authParser.Parse(new AuthProfileParseInput
            {
                ProfileId = rawSavedProfileFile.ProfileId,
                SourceKind = AuthProfileSourceKind.SavedProfile,
                SourcePath = rawSavedProfileFile.Path,
                JsonContent = rawSavedProfileFile.Content,
            }, capturedAtUtc);
        }
        else
        {
            parsedProfile = new AuthProfile
            {
                ProfileId = rawSavedProfileFile.ProfileId,
                SourceKind = AuthProfileSourceKind.SavedProfile,
                SourcePath = rawSavedProfileFile.Path,
                ParseState = rawSavedProfileFile.ReadState,
                Diagnostics = rawSavedProfileFile.Diagnostics,
            };
        }

        indexEntries.TryGetValue(rawSavedProfileFile.ProfileId ?? string.Empty, out var indexEntry);
        parsedProfile = parsedProfile with
        {
            IndexEntry = indexEntry,
        };

        return EnrichProfile(parsedProfile, capturedAtUtc);
    }

    private ResolvedProfileEntry? ResolveCurrentAuthProfile(
        RawProfileSourceFile rawCurrentAuthFile,
        CodexConfigProfile configProfile,
        IReadOnlyDictionary<string, ProfileIndexEntry> indexEntries,
        IReadOnlyList<ResolvedProfileEntry> savedProfiles,
        DateTimeOffset capturedAtUtc)
    {
        if (ShouldBlockCurrentAuthFromConfig(configProfile))
        {
            return EnrichProfile(CreateCredentialStoreUnavailableCurrentAuth(rawCurrentAuthFile.Path, configProfile.CredentialsStoreMode, capturedAtUtc), capturedAtUtc);
        }

        if (rawCurrentAuthFile.ReadState != ProfileSourceParseState.Available)
        {
            if (rawCurrentAuthFile.ReadState == ProfileSourceParseState.Missing)
            {
                return null;
            }

            return EnrichProfile(new AuthProfile
            {
                SourceKind = AuthProfileSourceKind.CurrentAuth,
                SourcePath = rawCurrentAuthFile.Path,
                ParseState = rawCurrentAuthFile.ReadState,
                Diagnostics = rawCurrentAuthFile.Diagnostics,
            }, capturedAtUtc);
        }

        var parsedProfile = _authParser.Parse(new AuthProfileParseInput
        {
            SourceKind = AuthProfileSourceKind.CurrentAuth,
            SourcePath = rawCurrentAuthFile.Path,
            JsonContent = rawCurrentAuthFile.Content,
        }, capturedAtUtc);

        var resolvedCurrentProfile = EnrichProfile(parsedProfile, capturedAtUtc);
        var duplicateSavedProfileId = SelectDuplicateSavedProfileId(resolvedCurrentProfile, savedProfiles);
        if (duplicateSavedProfileId is null
            || !indexEntries.TryGetValue(duplicateSavedProfileId, out var matchedIndexEntry))
        {
            return resolvedCurrentProfile;
        }

        return resolvedCurrentProfile with
        {
            AuthProfile = resolvedCurrentProfile.AuthProfile with
            {
                ProfileId = duplicateSavedProfileId,
                IndexEntry = matchedIndexEntry,
            },
        };
    }

    private static bool ShouldBlockCurrentAuthFromConfig(CodexConfigProfile configProfile)
    {
        return !string.IsNullOrWhiteSpace(configProfile.CredentialsStoreMode)
            && !string.Equals(configProfile.CredentialsStoreMode, "file", StringComparison.OrdinalIgnoreCase);
    }

    private static AuthProfile CreateCredentialStoreUnavailableCurrentAuth(
        string sourcePath,
        string? credentialsStoreMode,
        DateTimeOffset observedAtUtc)
    {
        var diagnostic = SourceDiagnostic.Create(
            SourceDiagnosticCode.Unavailable,
            SourceDiagnosticSeverity.Info,
            "Current auth.json credentials are unavailable because credential store mode is not file-backed.",
            context:
            [
                new KeyValuePair<string, string?>("sourceKind", AuthProfileSourceKind.CurrentAuth.ToString()),
                new KeyValuePair<string, string?>("sourcePath", sourcePath),
                new KeyValuePair<string, string?>("credentialsStoreMode", credentialsStoreMode),
            ],
            observedAtUtc: observedAtUtc);

        return new AuthProfile
        {
            SourceKind = AuthProfileSourceKind.CurrentAuth,
            SourcePath = sourcePath,
            ParseState = ProfileSourceParseState.Unavailable,
            Diagnostics = [diagnostic],
        };
    }

    private ResolvedProfileEntry EnrichProfile(AuthProfile authProfile, DateTimeOffset capturedAtUtc)
    {
        var resolution = _identityMatcher.Resolve(authProfile, capturedAtUtc);
        var diagnostics = authProfile.Diagnostics
            .Concat(resolution.Diagnostics)
            .ToArray();

        return new ResolvedProfileEntry
        {
            AuthProfile = authProfile with
            {
                Identity = resolution.Identity,
                Diagnostics = diagnostics,
            },
            Resolution = resolution with
            {
                Diagnostics = diagnostics,
            },
        };
    }

    private static string? SelectDuplicateSavedProfileId(ResolvedProfileEntry? currentAuthProfile, IReadOnlyList<ResolvedProfileEntry> savedProfiles)
    {
        if (currentAuthProfile is null
            || currentAuthProfile.AuthProfile.Identity is null)
        {
            return null;
        }

        return savedProfiles
            .Where(profile => profile.AuthProfile.Identity is not null && currentAuthProfile.AuthProfile.Identity == profile.AuthProfile.Identity)
            .Select(profile => profile.AuthProfile.ProfileId)
            .Where(profileId => !string.IsNullOrWhiteSpace(profileId))
            .OrderBy(profileId => profileId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static bool ShouldIncludeCurrentAuthProfile(RawProfileSourceFile rawCurrentAuthFile, ResolvedProfileEntry? currentAuthProfile)
    {
        return rawCurrentAuthFile.ReadState == ProfileSourceParseState.Available
            && currentAuthProfile is not null
            && currentAuthProfile.AuthProfile.ParseState != ProfileSourceParseState.Unavailable;
    }

    private static SourceStatus BuildCurrentAuthSourceStatus(
        RawProfileSourceFile rawCurrentAuthFile,
        ResolvedProfileEntry? currentAuthProfile,
        DateTimeOffset capturedAtUtc)
    {
        if (currentAuthProfile is not null)
        {
            return ProfileSourceStatusMapper.ToSourceStatus(
                StatusSourceKind.CurrentAuth,
                currentAuthProfile.AuthProfile.ParseState,
                currentAuthProfile.AuthProfile.Diagnostics,
                capturedAtUtc);
        }

        return CreateSourceStatus(rawCurrentAuthFile, capturedAtUtc);
    }

    private static SourceStatus BuildSavedProfilesSourceStatus(
        IReadOnlyList<ResolvedProfileEntry> savedProfiles,
        DateTimeOffset capturedAtUtc)
    {
        var diagnostics = savedProfiles
            .SelectMany(profile => profile.AuthProfile.Diagnostics)
            .ToArray();

        var aggregateParseState = AggregateSavedProfileParseState(savedProfiles);
        return ProfileSourceStatusMapper.ToSourceStatus(
            StatusSourceKind.SavedProfileAuth,
            aggregateParseState,
            diagnostics,
            capturedAtUtc);
    }

    private static ProfileSourceParseState AggregateSavedProfileParseState(IReadOnlyList<ResolvedProfileEntry> savedProfiles)
    {
        if (savedProfiles.Count == 0)
        {
            return ProfileSourceParseState.Available;
        }

        if (savedProfiles.Any(profile => profile.AuthProfile.ParseState == ProfileSourceParseState.Error))
        {
            return ProfileSourceParseState.Error;
        }

        if (savedProfiles.Any(profile => profile.AuthProfile.ParseState == ProfileSourceParseState.Malformed))
        {
            return ProfileSourceParseState.Malformed;
        }

        if (savedProfiles.Any(profile => profile.AuthProfile.ParseState == ProfileSourceParseState.Unavailable))
        {
            return ProfileSourceParseState.Unavailable;
        }

        if (savedProfiles.All(profile => profile.AuthProfile.ParseState == ProfileSourceParseState.Missing))
        {
            return ProfileSourceParseState.Missing;
        }

        return ProfileSourceParseState.Available;
    }

    private static void AddProfileStatus(
        ResolvedProfileEntry resolvedProfile,
        bool isCurrent,
        DateTimeOffset capturedAtUtc,
        ICollection<ProfileStatus> profileStatuses,
        ICollection<ProfileUsageCredentialReference> usageCredentialReferences)
    {
        var authProfile = resolvedProfile.AuthProfile;
        var descriptor = new ProfileDescriptor
        {
            ProfileId = authProfile.ProfileId,
            DisplayName = authProfile.IndexEntry?.Label,
            LoginName = resolvedProfile.Resolution.LoginName,
            SubscriptionTier = resolvedProfile.Resolution.SubscriptionTier,
            IsCurrent = isCurrent,
            AuthKind = resolvedProfile.Resolution.AuthKind,
            UsageEligibility = resolvedProfile.Resolution.UsageEligibility,
            SourceStatus = ProfileSourceStatusMapper.ToSourceStatus(
                authProfile.SourceKind == AuthProfileSourceKind.CurrentAuth
                    ? StatusSourceKind.CurrentAuth
                    : StatusSourceKind.SavedProfileAuth,
                authProfile.ParseState,
                authProfile.Diagnostics,
                capturedAtUtc),
        };

        profileStatuses.Add(new ProfileStatus
        {
            Profile = descriptor,
            Diagnostics = authProfile.Diagnostics,
        });

        if (resolvedProfile.Resolution.AuthKind == ProfileAuthKind.Login
            && resolvedProfile.Resolution.UsageEligibility == ProfileUsageEligibility.Eligible)
        {
            usageCredentialReferences.Add(new ProfileUsageCredentialReference
            {
                ProfileId = descriptor.ProfileId,
                AuthKind = resolvedProfile.Resolution.AuthKind,
                UsageEligibility = resolvedProfile.Resolution.UsageEligibility,
                LoginName = resolvedProfile.Resolution.LoginName,
                SubscriptionTier = resolvedProfile.Resolution.SubscriptionTier,
                SourcePath = authProfile.SourcePath,
                AccountId = authProfile.Tokens.AccountId,
                IdToken = authProfile.Tokens.IdToken,
                AccessToken = authProfile.Tokens.AccessToken,
                RefreshToken = authProfile.Tokens.RefreshToken,
            });
        }
    }

    private static RawProfileSourceFile ReadSourceFile(string path, StatusSourceKind sourceKind, DateTimeOffset observedAtUtc)
    {
        if (!File.Exists(path))
        {
            var diagnostic = SourceDiagnostic.Create(
                SourceDiagnosticCode.Missing,
                SourceDiagnosticSeverity.Info,
                $"{sourceKind} source file is missing.",
                context:
                [
                    new KeyValuePair<string, string?>("sourcePath", path),
                ],
                observedAtUtc: observedAtUtc);

            return new RawProfileSourceFile
            {
                SourceKind = sourceKind,
                Path = path,
                ReadState = ProfileSourceParseState.Missing,
                Diagnostics = [diagnostic],
            };
        }

        try
        {
            var content = File.ReadAllText(path);
            return new RawProfileSourceFile
            {
                SourceKind = sourceKind,
                Path = path,
                ReadState = ProfileSourceParseState.Available,
                Content = content,
            };
        }
        catch (Exception exception)
        {
            var diagnostic = SourceDiagnostic.Create(
                SourceDiagnosticCode.Error,
                SourceDiagnosticSeverity.Warning,
                $"Failed to read {sourceKind} source file.",
                detail: exception.Message,
                context:
                [
                    new KeyValuePair<string, string?>("sourcePath", path),
                ],
                observedAtUtc: observedAtUtc);

            return new RawProfileSourceFile
            {
                SourceKind = sourceKind,
                Path = path,
                ReadState = ProfileSourceParseState.Error,
                Diagnostics = [diagnostic],
            };
        }
    }

    private static IReadOnlyList<RawProfileSourceFile> ReadSavedProfileFiles(string profilesDirectoryPath, DateTimeOffset observedAtUtc)
    {
        var savedProfileFiles = new List<RawProfileSourceFile>();
        var filePaths = Directory.EnumerateFiles(profilesDirectoryPath, "*.json", SearchOption.TopDirectoryOnly)
            .Where(path => !IsReservedProfileFile(path))
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();

        foreach (var filePath in filePaths)
        {
            var sourceFile = ReadSourceFile(filePath, StatusSourceKind.SavedProfileAuth, observedAtUtc) with
            {
                ProfileId = Path.GetFileNameWithoutExtension(filePath),
            };

            savedProfileFiles.Add(sourceFile);
        }

        return savedProfileFiles;
    }

    private static bool IsReservedProfileFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Equals("profiles.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("update.json", StringComparison.OrdinalIgnoreCase);
    }

    private static SourceStatus CreateSourceStatus(RawProfileSourceFile sourceFile, DateTimeOffset observedAtUtc)
    {
        var (state, availability) = sourceFile.ReadState switch
        {
            ProfileSourceParseState.Available => (SourceStatusState.Available, StatusAvailability.Available()),
            ProfileSourceParseState.Missing => (SourceStatusState.Missing, StatusAvailability.Unavailable(StatusAvailabilityCode.Missing)),
            ProfileSourceParseState.Unavailable => (SourceStatusState.Unavailable, StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable)),
            ProfileSourceParseState.Error => (SourceStatusState.Error, StatusAvailability.Unavailable(StatusAvailabilityCode.Error)),
            ProfileSourceParseState.Malformed => (SourceStatusState.Malformed, StatusAvailability.Unavailable(StatusAvailabilityCode.Malformed)),
            _ => (SourceStatusState.Unknown, StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable)),
        };

        return new SourceStatus
        {
            Source = sourceFile.SourceKind,
            State = state,
            Availability = availability,
            ObservedAtUtc = observedAtUtc,
            Diagnostics = sourceFile.Diagnostics,
        };
    }

    private static async Task<FileStream?> AcquireProfilesLockAsync(
        CodexHomePaths paths,
        TimeSpan timeout,
        TimeSpan retryInterval,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed <= timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    paths.ProfilesLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (Exception)
            {
                if (stopwatch.Elapsed >= timeout)
                {
                    break;
                }

                await Task.Delay(retryInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private static ProfileSnapshot BuildUnavailableSnapshot(
        CodexHomePaths paths,
        DateTimeOffset observedAtUtc,
        string summary,
        SourceDiagnosticCode code,
        SourceDiagnosticSeverity severity,
        StatusAvailabilityCode availabilityCode,
        string? detail = null,
        IReadOnlyList<KeyValuePair<string, string?>>? context = null)
    {
        var diagnostic = SourceDiagnostic.Create(
            code,
            severity,
            summary,
            detail: detail,
            context: context,
            observedAtUtc: observedAtUtc);

        var availability = StatusAvailability.Unavailable(availabilityCode, summary);
        var sources = new[]
        {
            BuildUnavailableSource(StatusSourceKind.CurrentAuth, observedAtUtc, diagnostic, availability),
            BuildUnavailableSource(StatusSourceKind.ConfigToml, observedAtUtc, diagnostic, availability),
            BuildUnavailableSource(StatusSourceKind.ProfilesIndex, observedAtUtc, diagnostic, availability),
            BuildUnavailableSource(StatusSourceKind.SavedProfileAuth, observedAtUtc, diagnostic, availability),
        };

        return new ProfileSnapshot
        {
            CapturedAtUtc = observedAtUtc,
            Paths = paths,
            Sources = sources,
            Diagnostics = [diagnostic],
            RawSources = new RawProfileSnapshot(),
        };
    }

    private static SourceStatus BuildUnavailableSource(
        StatusSourceKind sourceKind,
        DateTimeOffset observedAtUtc,
        SourceDiagnostic diagnostic,
        StatusAvailability availability)
    {
        return new SourceStatus
        {
            Source = sourceKind,
            State = SourceStatusState.Unavailable,
            Availability = availability,
            ObservedAtUtc = observedAtUtc,
            Diagnostics = [diagnostic],
        };
    }

    private static TimeSpan NormalizeDuration(TimeSpan requested, TimeSpan fallback)
    {
        if (requested <= TimeSpan.Zero)
        {
            return fallback;
        }

        return requested < MinimumRetryDelay ? MinimumRetryDelay : requested;
    }

    private sealed record ResolvedProfileEntry
    {
        public AuthProfile AuthProfile { get; init; } = new();

        public AuthProfileResolution Resolution { get; init; } = new();
    }
}
