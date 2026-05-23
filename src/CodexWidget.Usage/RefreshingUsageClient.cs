using CodexWidget.Core;

namespace CodexWidget.Usage;

public sealed class RefreshingUsageClient : IUsageClient
{
    private readonly IUsageClient innerClient;
    private readonly ITokenRefreshService tokenRefreshService;
    private readonly IAuthTokenFileUpdater authTokenFileUpdater;
    private readonly Func<DateTimeOffset> utcNowProvider;

    public RefreshingUsageClient(
        IUsageClient innerClient,
        ITokenRefreshService tokenRefreshService,
        IAuthTokenFileUpdater authTokenFileUpdater,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        ArgumentNullException.ThrowIfNull(innerClient);
        ArgumentNullException.ThrowIfNull(tokenRefreshService);
        ArgumentNullException.ThrowIfNull(authTokenFileUpdater);

        this.innerClient = innerClient;
        this.tokenRefreshService = tokenRefreshService;
        this.authTokenFileUpdater = authTokenFileUpdater;
        this.utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<UsageFetchResult> FetchAsync(UsageProfileRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var initialResult = await innerClient.FetchAsync(request, cancellationToken).ConfigureAwait(false);
        if (initialResult.Outcome != UsageFetchOutcome.Unauthorized)
        {
            return EnsureTokenRefreshState(initialResult);
        }

        var observedAt = utcNowProvider();
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return CreateRefreshFailureResult(
                request.ProfileId,
                initialResult.EndpointResolution,
                observedAt,
                new UsageTokenRefreshResult
                {
                    Outcome = UsageTokenRefreshOutcome.MissingRefreshToken,
                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.MissingRequiredField),
                    Diagnostics =
                    [
                        SourceDiagnostic.Create(
                            SourceDiagnosticCode.MissingRequiredField,
                            SourceDiagnosticSeverity.Warning,
                            "Usage request returned unauthorized and the profile is missing a refresh token.",
                            context:
                            [
                                new KeyValuePair<string, string?>("missingField", "refreshToken"),
                            ],
                            observedAtUtc: observedAt),
                    ],
                    RetryOutcome = UsageRetryOutcome.NotAttempted,
                    TokenUpdate = new TokenUpdateResult
                    {
                        Outcome = TokenUpdateOutcome.NotAttempted,
                    },
                });
        }

        if (string.IsNullOrWhiteSpace(request.SourcePath))
        {
            return CreateRefreshFailureResult(
                request.ProfileId,
                initialResult.EndpointResolution,
                observedAt,
                new UsageTokenRefreshResult
                {
                    Outcome = UsageTokenRefreshOutcome.MissingSourcePath,
                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.MissingRequiredField),
                    Diagnostics =
                    [
                        SourceDiagnostic.Create(
                            SourceDiagnosticCode.MissingRequiredField,
                            SourceDiagnosticSeverity.Warning,
                            "Usage request returned unauthorized and the auth source path is missing.",
                            context:
                            [
                                new KeyValuePair<string, string?>("missingField", "sourcePath"),
                            ],
                            observedAtUtc: observedAt),
                    ],
                    RetryOutcome = UsageRetryOutcome.NotAttempted,
                    TokenUpdate = new TokenUpdateResult
                    {
                        Outcome = TokenUpdateOutcome.NotAttempted,
                    },
                });
        }

        if (string.IsNullOrWhiteSpace(request.ProfilesLockPath))
        {
            return CreateRefreshFailureResult(
                request.ProfileId,
                initialResult.EndpointResolution,
                observedAt,
                new UsageTokenRefreshResult
                {
                    Outcome = UsageTokenRefreshOutcome.TokenWriteFailed,
                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.TokenRefreshFailed),
                    Diagnostics =
                    [
                        SourceDiagnostic.Create(
                            SourceDiagnosticCode.MissingRequiredField,
                            SourceDiagnosticSeverity.Warning,
                            "Usage request returned unauthorized and the profiles lock path is missing.",
                            context:
                            [
                                new KeyValuePair<string, string?>("missingField", "profilesLockPath"),
                            ],
                            observedAtUtc: observedAt),
                    ],
                    RetryOutcome = UsageRetryOutcome.NotAttempted,
                    TokenUpdate = new TokenUpdateResult
                    {
                        Outcome = TokenUpdateOutcome.MissingProfilesLockPath,
                        Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.MissingRequiredField),
                    },
                });
        }

        var refreshResult = await tokenRefreshService.RefreshAsync(request, cancellationToken).ConfigureAwait(false);
        if (refreshResult.Outcome != UsageTokenRefreshOutcome.Succeeded)
        {
            return CreateRefreshFailureResult(request.ProfileId, initialResult.EndpointResolution, observedAt, refreshResult);
        }

        var updateRequest = new TokenUpdateRequest
        {
            ProfileId = request.ProfileId,
            SourcePath = request.SourcePath,
            ProfilesLockPath = request.ProfilesLockPath,
            AccountId = refreshResult.AccountId,
            IdToken = refreshResult.IdToken,
            AccessToken = refreshResult.AccessToken,
            RefreshToken = refreshResult.RefreshToken,
        };

        var tokenUpdateResult = await authTokenFileUpdater.UpdateAsync(updateRequest, cancellationToken).ConfigureAwait(false);
        var refreshWithWriteback = refreshResult with
        {
            TokenUpdate = tokenUpdateResult,
            Diagnostics = refreshResult.Diagnostics.Concat(tokenUpdateResult.Diagnostics).ToArray(),
        };

        if (tokenUpdateResult.Outcome != TokenUpdateOutcome.Succeeded)
        {
            return CreateRefreshFailureResult(
                request.ProfileId,
                initialResult.EndpointResolution,
                observedAt,
                refreshWithWriteback with
                {
                    Outcome = UsageTokenRefreshOutcome.TokenWriteFailed,
                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.TokenRefreshFailed),
                });
        }

        var retryRequest = request with
        {
            AccountId = refreshResult.AccountId ?? request.AccountId,
            IdToken = refreshResult.IdToken ?? request.IdToken,
            AccessToken = refreshResult.AccessToken ?? request.AccessToken,
            RefreshToken = refreshResult.RefreshToken ?? request.RefreshToken,
        };

        var retryResult = await innerClient.FetchAsync(retryRequest, cancellationToken).ConfigureAwait(false);
        return retryResult with
        {
            Diagnostics = retryResult.Diagnostics.Concat(refreshWithWriteback.Diagnostics).ToArray(),
            TokenRefresh = refreshWithWriteback with
            {
                RetryOutcome = MapRetryOutcome(retryResult.Outcome),
            },
        };
    }

    private static UsageFetchResult EnsureTokenRefreshState(UsageFetchResult result)
    {
        return result.TokenRefresh.Outcome == UsageTokenRefreshOutcome.Unknown
            ? result with
            {
                TokenRefresh = new UsageTokenRefreshResult
                {
                    Outcome = UsageTokenRefreshOutcome.NotAttempted,
                    RetryOutcome = UsageRetryOutcome.NotAttempted,
                    TokenUpdate = new TokenUpdateResult
                    {
                        Outcome = TokenUpdateOutcome.NotAttempted,
                    },
                },
            }
            : result;
    }

    private static UsageRetryOutcome MapRetryOutcome(UsageFetchOutcome outcome)
    {
        return outcome switch
        {
            UsageFetchOutcome.Succeeded => UsageRetryOutcome.Succeeded,
            UsageFetchOutcome.Unauthorized => UsageRetryOutcome.Unauthorized,
            UsageFetchOutcome.NetworkError => UsageRetryOutcome.NetworkError,
            UsageFetchOutcome.Timeout => UsageRetryOutcome.Timeout,
            UsageFetchOutcome.HttpError => UsageRetryOutcome.HttpError,
            UsageFetchOutcome.MalformedResponse => UsageRetryOutcome.MalformedResponse,
            UsageFetchOutcome.Canceled => UsageRetryOutcome.Canceled,
            _ => UsageRetryOutcome.Error,
        };
    }

    private static UsageFetchResult CreateRefreshFailureResult(
        string? profileId,
        UsageEndpointResolutionResult endpointResolution,
        DateTimeOffset observedAt,
        UsageTokenRefreshResult tokenRefresh)
    {
        var diagnostics = new List<SourceDiagnostic>
        {
            SourceDiagnostic.Create(
                SourceDiagnosticCode.TokenRefreshFailed,
                SourceDiagnosticSeverity.Warning,
                "Usage request returned unauthorized and the token refresh flow did not complete.",
                context:
                [
                    new KeyValuePair<string, string?>("refreshOutcome", tokenRefresh.Outcome.ToString()),
                    new KeyValuePair<string, string?>("retryOutcome", tokenRefresh.RetryOutcome.ToString()),
                ],
                observedAtUtc: observedAt),
        };
        diagnostics.AddRange(tokenRefresh.Diagnostics);

        return new UsageFetchResult
        {
            ProfileId = profileId,
            Outcome = UsageFetchOutcome.TokenRefreshFailed,
            Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.TokenRefreshFailed),
            EndpointResolution = endpointResolution,
            Diagnostics = diagnostics,
            TokenRefresh = tokenRefresh,
        };
    }
}
