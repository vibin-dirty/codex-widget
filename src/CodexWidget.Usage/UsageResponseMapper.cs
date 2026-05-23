using CodexWidget.Core;
using System.Text.Json;

namespace CodexWidget.Usage;

public interface IUsageResponseMapper
{
    UsageFetchResult Map(UsageProfileRequest request, string responseBody, DateTimeOffset observedAtUtc);
}

public sealed class UsageResponseMapper : IUsageResponseMapper
{
    private const string MainBucketId = "codex";
    private const string MainBucketLabel = "codex";
    private const string UnknownBucketId = "unknown";

    public UsageFetchResult Map(UsageProfileRequest request, string responseBody, DateTimeOffset observedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            var emptyDiagnostic = CreateDiagnostic(
                SourceDiagnosticCode.Malformed,
                SourceDiagnosticSeverity.Error,
                "Usage response body is empty.",
                observedAtUtc);
            return CreateFailureResult(
                request.ProfileId,
                UsageFetchOutcome.MalformedResponse,
                StatusAvailability.Unavailable(StatusAvailabilityCode.Malformed),
                [emptyDiagnostic],
                Array.Empty<UsageBucketSnapshot>());
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(responseBody);
        }
        catch (JsonException ex)
        {
            var malformedDiagnostic = CreateDiagnostic(
                SourceDiagnosticCode.Malformed,
                SourceDiagnosticSeverity.Error,
                "Usage response JSON is malformed.",
                observedAtUtc,
                context: new[]
                {
                    new KeyValuePair<string, string?>("path", ex.Path),
                });
            return CreateFailureResult(
                request.ProfileId,
                UsageFetchOutcome.MalformedResponse,
                StatusAvailability.Unavailable(StatusAvailabilityCode.Malformed),
                [malformedDiagnostic],
                Array.Empty<UsageBucketSnapshot>());
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                var shapeDiagnostic = CreateDiagnostic(
                    SourceDiagnosticCode.Malformed,
                    SourceDiagnosticSeverity.Error,
                    "Usage response root must be a JSON object.",
                    observedAtUtc);
                return CreateFailureResult(
                    request.ProfileId,
                    UsageFetchOutcome.MalformedResponse,
                    StatusAvailability.Unavailable(StatusAvailabilityCode.Malformed),
                    [shapeDiagnostic],
                    Array.Empty<UsageBucketSnapshot>());
            }

            var diagnostics = new List<SourceDiagnostic>();
            var buckets = new List<UsageBucketSnapshot>();
            var root = document.RootElement;

            if (TryGetObjectProperty(root, "rate_limit", out var mainRateLimit))
            {
                buckets.Add(MapBucket(
                    MainBucketId,
                    MainBucketLabel,
                    UsageBucketKind.MainCodex,
                    mainRateLimit,
                    diagnostics,
                    observedAtUtc));
            }
            else
            {
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.MissingBucket,
                    SourceDiagnosticSeverity.Warning,
                    "Usage response did not include the top-level rate_limit bucket.",
                    observedAtUtc));
            }

            MapAdditionalBuckets(root, diagnostics, buckets, observedAtUtc);

            return CreateResult(request.ProfileId, buckets, diagnostics);
        }
    }

    private static void MapAdditionalBuckets(
        JsonElement root,
        ICollection<SourceDiagnostic> diagnostics,
        ICollection<UsageBucketSnapshot> buckets,
        DateTimeOffset observedAtUtc)
    {
        if (!root.TryGetProperty("additional_rate_limits", out var additionalProperty))
        {
            return;
        }

        if (additionalProperty.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (additionalProperty.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.Malformed,
                SourceDiagnosticSeverity.Warning,
                "additional_rate_limits must be an array.",
                observedAtUtc));
            return;
        }

        var bucketIndex = 0;
        foreach (var additionalEntry in additionalProperty.EnumerateArray())
        {
            bucketIndex++;
            if (additionalEntry.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.Malformed,
                    SourceDiagnosticSeverity.Warning,
                    "An additional usage bucket was malformed.",
                    observedAtUtc,
                    context: new[]
                    {
                        new KeyValuePair<string, string?>("bucketIndex", bucketIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    }));
                continue;
            }

            var bucketId = ReadTrimmedString(additionalEntry, "metered_feature");
            if (string.IsNullOrWhiteSpace(bucketId))
            {
                bucketId = UnknownBucketId;
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.MissingRequiredField,
                    SourceDiagnosticSeverity.Warning,
                    "Additional usage bucket id was missing; using unknown.",
                    observedAtUtc,
                    context: new[]
                    {
                        new KeyValuePair<string, string?>("bucketIndex", bucketIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new KeyValuePair<string, string?>("bucketId", bucketId),
                    }));
            }

            var bucketLabel = ReadTrimmedString(additionalEntry, "limit_name");
            if (string.IsNullOrWhiteSpace(bucketLabel))
            {
                bucketLabel = bucketId;
            }

            if (!TryGetObjectProperty(additionalEntry, "rate_limit", out var rateLimitElement))
            {
                var bucketKind = ResolveAdditionalBucketKind(bucketId, bucketLabel);
                diagnostics.Add(CreateDiagnostic(
                    SourceDiagnosticCode.MissingBucket,
                    SourceDiagnosticSeverity.Warning,
                    "Additional usage bucket did not include a rate_limit object.",
                    observedAtUtc,
                    context: new[]
                    {
                        new KeyValuePair<string, string?>("bucketId", bucketId),
                        new KeyValuePair<string, string?>("bucketLabel", bucketLabel),
                    }));

                buckets.Add(new UsageBucketSnapshot
                {
                    BucketId = bucketId,
                    BucketLabel = bucketLabel,
                    BucketKind = bucketKind,
                    Windows = Array.Empty<UsageWindowSnapshot>(),
                    FetchStatus = UsageBucketFetchStatus.Unavailable,
                    Availability = StatusAvailability.Unavailable(StatusAvailabilityCode.MissingBucket),
                });
                continue;
            }

            var additionalBucketKind = ResolveAdditionalBucketKind(bucketId, bucketLabel);
            buckets.Add(MapBucket(
                bucketId,
                bucketLabel,
                additionalBucketKind,
                rateLimitElement,
                diagnostics,
                observedAtUtc));
        }
    }

    private static UsageFetchResult CreateResult(
        string? profileId,
        IReadOnlyList<UsageBucketSnapshot> buckets,
        IReadOnlyList<SourceDiagnostic> diagnostics)
    {
        var hasMainBucket = buckets.Any(IsMainBucket);
        var availableWindows = buckets.SelectMany(bucket => bucket.Windows).Any(window => window.Availability.IsAvailable);

        if (!hasMainBucket)
        {
            return CreateFailureResult(
                profileId,
                UsageFetchOutcome.MissingBucket,
                StatusAvailability.Unavailable(StatusAvailabilityCode.MissingBucket),
                diagnostics,
                buckets);
        }

        if (!availableWindows)
        {
            return CreateFailureResult(
                profileId,
                UsageFetchOutcome.MissingWindow,
                StatusAvailability.Unavailable(StatusAvailabilityCode.MissingWindow),
                diagnostics,
                buckets);
        }

        return new UsageFetchResult
        {
            ProfileId = profileId,
            Outcome = UsageFetchOutcome.Succeeded,
            Availability = StatusAvailability.Available(),
            Buckets = buckets,
            Diagnostics = diagnostics,
        };
    }

    private static UsageFetchResult CreateFailureResult(
        string? profileId,
        UsageFetchOutcome outcome,
        StatusAvailability availability,
        IReadOnlyList<SourceDiagnostic> diagnostics,
        IReadOnlyList<UsageBucketSnapshot> buckets)
    {
        return new UsageFetchResult
        {
            ProfileId = profileId,
            Outcome = outcome,
            Availability = availability,
            Buckets = buckets,
            Diagnostics = diagnostics,
        };
    }

    private static UsageBucketSnapshot MapBucket(
        string bucketId,
        string bucketLabel,
        UsageBucketKind bucketKind,
        JsonElement rateLimitElement,
        ICollection<SourceDiagnostic> diagnostics,
        DateTimeOffset observedAtUtc)
    {
        var rawWindows = new List<MappedWindow>(2);

        if (TryGetObjectProperty(rateLimitElement, "primary_window", out var primaryWindow))
        {
            rawWindows.Add(ParseWindow(bucketId, bucketLabel, "primary_window", primaryWindow, diagnostics, observedAtUtc, sourceOrder: 0));
        }
        else
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.MissingWindow,
                SourceDiagnosticSeverity.Warning,
                "Usage bucket primary_window is missing.",
                observedAtUtc,
                context: new[]
                {
                    new KeyValuePair<string, string?>("bucketId", bucketId),
                    new KeyValuePair<string, string?>("bucketLabel", bucketLabel),
                }));
        }

        if (TryGetObjectProperty(rateLimitElement, "secondary_window", out var secondaryWindow))
        {
            rawWindows.Add(ParseWindow(bucketId, bucketLabel, "secondary_window", secondaryWindow, diagnostics, observedAtUtc, sourceOrder: 1));
        }
        else
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.MissingWindow,
                SourceDiagnosticSeverity.Warning,
                "Usage bucket secondary_window is missing.",
                observedAtUtc,
                context: new[]
                {
                    new KeyValuePair<string, string?>("bucketId", bucketId),
                    new KeyValuePair<string, string?>("bucketLabel", bucketLabel),
                }));
        }

        var sortedWindows = rawWindows
            .OrderBy(mapped => mapped.DurationSortKey)
            .ThenBy(mapped => mapped.SourceOrder)
            .ToArray();

        var classifiedWindows = new List<UsageWindowSnapshot>(sortedWindows.Length);
        for (var index = 0; index < sortedWindows.Length; index++)
        {
            var windowKind = index switch
            {
                0 => UsageWindowKind.FiveHour,
                1 => UsageWindowKind.Weekly,
                _ => UsageWindowKind.Additional,
            };

            classifiedWindows.Add(sortedWindows[index].Snapshot with
            {
                WindowKind = windowKind,
                TimeLeftPercent = UsageCalculations.CalculateWindowTimeLeftPercent(
                    windowKind,
                    sortedWindows[index].Snapshot.ResetAtUnixSeconds,
                    sortedWindows[index].Snapshot.DurationSeconds,
                    observedAtUtc),
            });
        }

        var availableWindowCount = classifiedWindows.Count(window => window.Availability.IsAvailable);
        var bucketAvailability = availableWindowCount == 0
            ? StatusAvailability.Unavailable(StatusAvailabilityCode.MissingWindow)
            : StatusAvailability.Available();
        var fetchStatus = availableWindowCount switch
        {
            0 => UsageBucketFetchStatus.Unavailable,
            _ when availableWindowCount == classifiedWindows.Count => UsageBucketFetchStatus.Succeeded,
            _ => UsageBucketFetchStatus.Partial,
        };

        return new UsageBucketSnapshot
        {
            BucketId = bucketId,
            BucketLabel = bucketLabel,
            BucketKind = bucketKind,
            Windows = classifiedWindows,
            FetchStatus = fetchStatus,
            Availability = bucketAvailability,
        };
    }

    private static MappedWindow ParseWindow(
        string bucketId,
        string bucketLabel,
        string windowName,
        JsonElement windowElement,
        ICollection<SourceDiagnostic> diagnostics,
        DateTimeOffset observedAtUtc,
        int sourceOrder)
    {
        var usedPercent = ReadNullableDouble(windowElement, "used_percent", bucketId, bucketLabel, windowName, diagnostics, observedAtUtc);
        var duration = ReadNullableInt(windowElement, "limit_window_seconds", bucketId, bucketLabel, windowName, diagnostics, observedAtUtc);
        if (duration.HasValue && duration.Value <= 0)
        {
            diagnostics.Add(CreateDiagnostic(
                SourceDiagnosticCode.MissingTimestampOrDuration,
                SourceDiagnosticSeverity.Warning,
                "Usage window duration must be positive.",
                observedAtUtc,
                context: new[]
                {
                    new KeyValuePair<string, string?>("bucketId", bucketId),
                    new KeyValuePair<string, string?>("bucketLabel", bucketLabel),
                    new KeyValuePair<string, string?>("window", windowName),
                    new KeyValuePair<string, string?>("field", "limit_window_seconds"),
                }));
            duration = null;
        }

        var resetAt = ReadNullableLong(windowElement, "reset_at", bucketId, bucketLabel, windowName, diagnostics, observedAtUtc);

        StatusAvailability availability;
        if (!duration.HasValue || !resetAt.HasValue)
        {
            availability = StatusAvailability.Unavailable(StatusAvailabilityCode.MissingTimestampOrDuration);
        }
        else if (!usedPercent.HasValue)
        {
            availability = StatusAvailability.Unavailable(StatusAvailabilityCode.MissingRequiredField);
        }
        else
        {
            availability = StatusAvailability.Available();
        }

        var snapshot = new UsageWindowSnapshot
        {
            WindowKind = UsageWindowKind.Unknown,
            DurationSeconds = duration,
            ResetAtUnixSeconds = resetAt,
            UsedPercent = usedPercent,
            QuotaLeftPercent = UsageCalculations.CalculateQuotaLeftPercent(usedPercent),
            TimeLeftPercent = null,
            Availability = availability,
        };

        return new MappedWindow(snapshot, sourceOrder, duration ?? int.MaxValue);
    }

    private static int? ReadNullableInt(
        JsonElement element,
        string propertyName,
        string bucketId,
        string bucketLabel,
        string windowName,
        ICollection<SourceDiagnostic> diagnostics,
        DateTimeOffset observedAtUtc)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            diagnostics.Add(CreateFieldMissingDiagnostic(bucketId, bucketLabel, windowName, propertyName, observedAtUtc));
            return null;
        }

        if (propertyValue.ValueKind == JsonValueKind.Number)
        {
            if (propertyValue.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (propertyValue.TryGetInt64(out var longValue)
                && longValue is >= int.MinValue and <= int.MaxValue)
            {
                return (int)longValue;
            }
        }

        diagnostics.Add(CreateFieldMalformedDiagnostic(bucketId, bucketLabel, windowName, propertyName, observedAtUtc));
        return null;
    }

    private static long? ReadNullableLong(
        JsonElement element,
        string propertyName,
        string bucketId,
        string bucketLabel,
        string windowName,
        ICollection<SourceDiagnostic> diagnostics,
        DateTimeOffset observedAtUtc)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            diagnostics.Add(CreateFieldMissingDiagnostic(bucketId, bucketLabel, windowName, propertyName, observedAtUtc));
            return null;
        }

        if (propertyValue.ValueKind == JsonValueKind.Number && propertyValue.TryGetInt64(out var longValue))
        {
            return longValue;
        }

        diagnostics.Add(CreateFieldMalformedDiagnostic(bucketId, bucketLabel, windowName, propertyName, observedAtUtc));
        return null;
    }

    private static double? ReadNullableDouble(
        JsonElement element,
        string propertyName,
        string bucketId,
        string bucketLabel,
        string windowName,
        ICollection<SourceDiagnostic> diagnostics,
        DateTimeOffset observedAtUtc)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            diagnostics.Add(CreateFieldMissingDiagnostic(bucketId, bucketLabel, windowName, propertyName, observedAtUtc));
            return null;
        }

        if (propertyValue.ValueKind == JsonValueKind.Number && propertyValue.TryGetDouble(out var doubleValue))
        {
            return doubleValue;
        }

        diagnostics.Add(CreateFieldMalformedDiagnostic(bucketId, bucketLabel, windowName, propertyName, observedAtUtc));
        return null;
    }

    private static SourceDiagnostic CreateFieldMissingDiagnostic(
        string bucketId,
        string bucketLabel,
        string windowName,
        string fieldName,
        DateTimeOffset observedAtUtc)
    {
        var code = fieldName == "used_percent"
            ? SourceDiagnosticCode.MissingRequiredField
            : SourceDiagnosticCode.MissingTimestampOrDuration;
        return CreateDiagnostic(
            code,
            SourceDiagnosticSeverity.Warning,
            "Usage window field is missing.",
            observedAtUtc,
            context: new[]
            {
                new KeyValuePair<string, string?>("bucketId", bucketId),
                new KeyValuePair<string, string?>("bucketLabel", bucketLabel),
                new KeyValuePair<string, string?>("window", windowName),
                new KeyValuePair<string, string?>("field", fieldName),
            });
    }

    private static SourceDiagnostic CreateFieldMalformedDiagnostic(
        string bucketId,
        string bucketLabel,
        string windowName,
        string fieldName,
        DateTimeOffset observedAtUtc)
    {
        return CreateDiagnostic(
            SourceDiagnosticCode.Malformed,
            SourceDiagnosticSeverity.Warning,
            "Usage window field value was malformed.",
            observedAtUtc,
            context: new[]
            {
                new KeyValuePair<string, string?>("bucketId", bucketId),
                new KeyValuePair<string, string?>("bucketLabel", bucketLabel),
                new KeyValuePair<string, string?>("window", windowName),
                new KeyValuePair<string, string?>("field", fieldName),
            });
    }

    private static SourceDiagnostic CreateDiagnostic(
        SourceDiagnosticCode code,
        SourceDiagnosticSeverity severity,
        string summary,
        DateTimeOffset observedAtUtc,
        IEnumerable<KeyValuePair<string, string?>>? context = null)
    {
        return SourceDiagnostic.Create(
            code,
            severity,
            summary,
            context: context,
            observedAtUtc: observedAtUtc);
    }

    private static bool IsMainBucket(UsageBucketSnapshot bucket)
    {
        if (bucket.BucketKind == UsageBucketKind.MainCodex)
        {
            return true;
        }

        return bucket.BucketId.Equals(MainBucketId, StringComparison.OrdinalIgnoreCase);
    }

    private static UsageBucketKind ResolveAdditionalBucketKind(string bucketId, string bucketLabel)
    {
        return ContainsSparkIdentifier(bucketId) || ContainsSparkIdentifier(bucketLabel)
            ? UsageBucketKind.Spark
            : UsageBucketKind.Additional;
    }

    private static bool ContainsSparkIdentifier(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains("spark", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement objectValue)
    {
        if (element.TryGetProperty(propertyName, out objectValue) && objectValue.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        objectValue = default;
        return false;
    }

    private static string? ReadTrimmedString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        if (propertyValue.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = propertyValue.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private sealed record MappedWindow(
        UsageWindowSnapshot Snapshot,
        int SourceOrder,
        int DurationSortKey);
}
