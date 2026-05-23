using CodexWidget.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexWidget.Usage;

public interface IAuthTokenFileUpdater
{
    Task<TokenUpdateResult> UpdateAsync(TokenUpdateRequest request, CancellationToken cancellationToken = default);
}

public sealed record AuthTokenFileUpdaterOptions
{
    public TimeSpan LockAcquireTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan LockRetryInterval { get; init; } = TimeSpan.FromMilliseconds(25);
}

public sealed class AuthTokenFileUpdater : IAuthTokenFileUpdater
{
    private static readonly TimeSpan MinimumRetryDelay = TimeSpan.FromMilliseconds(5);

    private readonly TimeSpan lockAcquireTimeout;
    private readonly TimeSpan lockRetryInterval;
    private readonly Func<DateTimeOffset> utcNowProvider;

    public AuthTokenFileUpdater(
        AuthTokenFileUpdaterOptions? options = null,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        var resolvedOptions = options ?? new AuthTokenFileUpdaterOptions();
        lockAcquireTimeout = NormalizeDuration(resolvedOptions.LockAcquireTimeout, TimeSpan.FromMilliseconds(500));
        lockRetryInterval = NormalizeDuration(resolvedOptions.LockRetryInterval, TimeSpan.FromMilliseconds(25));
        this.utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<TokenUpdateResult> UpdateAsync(TokenUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var observedAt = utcNowProvider();
        if (string.IsNullOrWhiteSpace(request.SourcePath))
        {
            return CreateFailureResult(
                TokenUpdateOutcome.MissingSourcePath,
                StatusAvailabilityCode.MissingRequiredField,
                SourceDiagnosticCode.MissingRequiredField,
                "Token writeback could not start because the auth source path is missing.",
                observedAt,
                new[] { new KeyValuePair<string, string?>("missingField", "sourcePath") });
        }

        if (string.IsNullOrWhiteSpace(request.ProfilesLockPath))
        {
            return CreateFailureResult(
                TokenUpdateOutcome.MissingProfilesLockPath,
                StatusAvailabilityCode.MissingRequiredField,
                SourceDiagnosticCode.MissingRequiredField,
                "Token writeback could not start because the profiles lock path is missing.",
                observedAt,
                new[] { new KeyValuePair<string, string?>("missingField", "profilesLockPath") });
        }

        if (request.AccountId is null
            && request.IdToken is null
            && request.AccessToken is null
            && request.RefreshToken is null)
        {
            return CreateFailureResult(
                TokenUpdateOutcome.MissingTokenChanges,
                StatusAvailabilityCode.MissingRequiredField,
                SourceDiagnosticCode.MissingRequiredField,
                "Token writeback did not receive any token field updates.",
                observedAt,
                new[] { new KeyValuePair<string, string?>("missingField", "tokenChanges") });
        }

        try
        {
            await using var lockStream = await AcquireProfilesLockAsync(
                request.ProfilesLockPath,
                lockAcquireTimeout,
                lockRetryInterval,
                cancellationToken).ConfigureAwait(false);

            if (lockStream is null)
            {
                return CreateFailureResult(
                    TokenUpdateOutcome.LockUnavailable,
                    StatusAvailabilityCode.Error,
                    SourceDiagnosticCode.Error,
                    "Token writeback could not acquire the profiles lock in time.",
                    observedAt,
                    new[] { new KeyValuePair<string, string?>("profilesLockPath", request.ProfilesLockPath) });
            }

            if (!File.Exists(request.SourcePath))
            {
                return CreateFailureResult(
                    TokenUpdateOutcome.SourceMissing,
                    StatusAvailabilityCode.Missing,
                    SourceDiagnosticCode.Missing,
                    "Token writeback source file is missing.",
                    observedAt,
                    new[] { new KeyValuePair<string, string?>("sourcePath", request.SourcePath) });
            }

            var originalJson = await File.ReadAllTextAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
            JsonNode? parsedNode;
            try
            {
                parsedNode = JsonNode.Parse(originalJson);
            }
            catch (JsonException exception)
            {
                return CreateFailureResult(
                    TokenUpdateOutcome.MalformedSource,
                    StatusAvailabilityCode.Malformed,
                    SourceDiagnosticCode.Malformed,
                    "Token writeback source JSON is malformed.",
                    observedAt,
                    new[] { new KeyValuePair<string, string?>("sourcePath", request.SourcePath) },
                    detail: exception.Message);
            }

            if (parsedNode is not JsonObject rootObject)
            {
                return CreateFailureResult(
                    TokenUpdateOutcome.MalformedSource,
                    StatusAvailabilityCode.Malformed,
                    SourceDiagnosticCode.Malformed,
                    "Token writeback source JSON root must be an object.",
                    observedAt,
                    new[] { new KeyValuePair<string, string?>("sourcePath", request.SourcePath) });
            }

            if (rootObject["tokens"] is not JsonObject tokensObject)
            {
                return CreateFailureResult(
                    TokenUpdateOutcome.MalformedSource,
                    StatusAvailabilityCode.Malformed,
                    SourceDiagnosticCode.Malformed,
                    "Token writeback source JSON must contain a tokens object.",
                    observedAt,
                    new[] { new KeyValuePair<string, string?>("sourcePath", request.SourcePath) });
            }

            ApplyTokenUpdate(tokensObject, request);
            var updatedJson = rootObject.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.General));
            await WriteAtomicallyAsync(request.SourcePath, updatedJson, cancellationToken).ConfigureAwait(false);

            return new TokenUpdateResult
            {
                Outcome = TokenUpdateOutcome.Succeeded,
                Availability = StatusAvailability.Available(),
            };
        }
        catch (OperationCanceledException)
        {
            return CreateFailureResult(
                TokenUpdateOutcome.Canceled,
                StatusAvailabilityCode.Error,
                SourceDiagnosticCode.Error,
                "Token writeback was canceled.",
                observedAt,
                new[] { new KeyValuePair<string, string?>("reason", "operation-canceled") });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CreateFailureResult(
                TokenUpdateOutcome.Error,
                StatusAvailabilityCode.Error,
                SourceDiagnosticCode.Error,
                "Token writeback failed while updating the auth source file.",
                observedAt,
                new[]
                {
                    new KeyValuePair<string, string?>("sourcePath", request.SourcePath),
                    new KeyValuePair<string, string?>("exceptionType", exception.GetType().Name),
                });
        }
    }

    private static void ApplyTokenUpdate(JsonObject tokensObject, TokenUpdateRequest request)
    {
        if (request.AccountId is not null)
        {
            tokensObject["account_id"] = request.AccountId;
        }

        if (request.IdToken is not null)
        {
            tokensObject["id_token"] = request.IdToken;
        }

        if (request.AccessToken is not null)
        {
            tokensObject["access_token"] = request.AccessToken;
        }

        if (request.RefreshToken is not null)
        {
            tokensObject["refresh_token"] = request.RefreshToken;
        }
    }

    private static async Task<FileStream?> AcquireProfilesLockAsync(
        string profilesLockPath,
        TimeSpan timeout,
        TimeSpan retryInterval,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed <= timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    profilesLockPath,
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

    private static async Task WriteAtomicallyAsync(string sourcePath, string content, CancellationToken cancellationToken)
    {
        var tempPath = $"{sourcePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, sourcePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static TokenUpdateResult CreateFailureResult(
        TokenUpdateOutcome outcome,
        StatusAvailabilityCode availabilityCode,
        SourceDiagnosticCode diagnosticCode,
        string summary,
        DateTimeOffset observedAt,
        IEnumerable<KeyValuePair<string, string?>>? context = null,
        string? detail = null)
    {
        var diagnostic = SourceDiagnostic.Create(
            diagnosticCode,
            SourceDiagnosticSeverity.Warning,
            summary,
            detail: detail,
            context: context,
            observedAtUtc: observedAt);

        return new TokenUpdateResult
        {
            Outcome = outcome,
            Availability = StatusAvailability.Unavailable(availabilityCode),
            Diagnostics = new[] { diagnostic },
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
}
