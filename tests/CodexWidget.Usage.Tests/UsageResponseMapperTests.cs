using CodexWidget.Core;
using System.Text.Json;

namespace CodexWidget.Usage.Tests;

public sealed class UsageResponseMapperTests
{
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private readonly UsageResponseMapper mapper = new();

    [Fact]
    public void Map_FullMainAndSparkPayload_MapsBucketsAndWindows()
    {
        var responseBody =
            """
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 25.0,
                  "limit_window_seconds": 18000,
                  "reset_at": 1700009000
                },
                "secondary_window": {
                  "used_percent": 70.0,
                  "limit_window_seconds": 604800,
                  "reset_at": 1700600000
                }
              },
              "additional_rate_limits": [
                {
                  "metered_feature": "  feature-spark-plus  ",
                  "limit_name": "  Spark compute  ",
                  "rate_limit": {
                    "primary_window": {
                      "used_percent": 10.0,
                      "limit_window_seconds": 18000,
                      "reset_at": 1700006000
                    },
                    "secondary_window": {
                      "used_percent": 35.0,
                      "limit_window_seconds": 604800,
                      "reset_at": 1700300000
                    }
                  }
                }
              ]
            }
            """;

        var result = mapper.Map(CreateRequest(), responseBody, ObservedAtUtc);

        Assert.Equal(UsageFetchOutcome.Succeeded, result.Outcome);
        Assert.True(result.Availability.IsAvailable);
        Assert.Equal(2, result.Buckets.Count);

        var mainBucket = Assert.Single(result.Buckets, bucket => bucket.BucketKind == UsageBucketKind.MainCodex);
        Assert.Equal("codex", mainBucket.BucketId);
        Assert.Equal("codex", mainBucket.BucketLabel);
        Assert.Equal(2, mainBucket.Windows.Count);
        Assert.Equal(UsageWindowKind.FiveHour, mainBucket.Windows[0].WindowKind);
        Assert.Equal(18000, mainBucket.Windows[0].DurationSeconds);
        Assert.Equal(75, mainBucket.Windows[0].QuotaLeftPercent);
        Assert.Equal(50, mainBucket.Windows[0].TimeLeftPercent);
        Assert.Equal(UsageWindowKind.Weekly, mainBucket.Windows[1].WindowKind);

        var sparkBucket = Assert.Single(result.Buckets, bucket => bucket.BucketId.Contains("spark", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(UsageBucketKind.Spark, sparkBucket.BucketKind);
        Assert.Equal("feature-spark-plus", sparkBucket.BucketId);
        Assert.Equal("Spark compute", sparkBucket.BucketLabel);
        Assert.Equal(UsageBucketFetchStatus.Succeeded, sparkBucket.FetchStatus);
        Assert.All(sparkBucket.Windows, window => Assert.True(window.Availability.IsAvailable));
    }

    [Fact]
    public void Map_CalculatesWeeklyTimeLeftAfterWindowClassification()
    {
        var observedAtUtc = new DateTimeOffset(2026, 1, 5, 7, 0, 0, TimeSpan.FromHours(1));
        var fiveHourResetAt = observedAtUtc.AddSeconds(9_000).ToUnixTimeSeconds();
        var weeklyResetAt = observedAtUtc.AddDays(7).ToUnixTimeSeconds();
        var responseBody =
            $$"""
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 20,
                  "limit_window_seconds": 604800,
                  "reset_at": {{weeklyResetAt}}
                },
                "secondary_window": {
                  "used_percent": 30,
                  "limit_window_seconds": 18000,
                  "reset_at": {{fiveHourResetAt}}
                }
              }
            }
            """;

        var result = mapper.Map(CreateRequest(), responseBody, observedAtUtc);
        var mainBucket = Assert.Single(result.Buckets);

        Assert.Equal(UsageWindowKind.FiveHour, mainBucket.Windows[0].WindowKind);
        Assert.Equal(50, mainBucket.Windows[0].TimeLeftPercent);
        Assert.Equal(UsageWindowKind.Weekly, mainBucket.Windows[1].WindowKind);
        Assert.Equal(100, mainBucket.Windows[1].TimeLeftPercent);
    }

    [Fact]
    public void Map_SparkClassification_UsesBucketIdWhenLabelDoesNotContainSpark()
    {
        var responseBody =
            """
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 25,
                  "limit_window_seconds": 18000,
                  "reset_at": 1700009000
                },
                "secondary_window": {
                  "used_percent": 70,
                  "limit_window_seconds": 604800,
                  "reset_at": 1700600000
                }
              },
              "additional_rate_limits": [
                {
                  "metered_feature": "spark-credits",
                  "limit_name": "Credits",
                  "rate_limit": {
                    "primary_window": {
                      "used_percent": 10,
                      "limit_window_seconds": 18000,
                      "reset_at": 1700006000
                    },
                    "secondary_window": {
                      "used_percent": 35,
                      "limit_window_seconds": 604800,
                      "reset_at": 1700300000
                    }
                  }
                }
              ]
            }
            """;

        var result = mapper.Map(CreateRequest(), responseBody, ObservedAtUtc);
        var sparkBucket = Assert.Single(result.Buckets, bucket => bucket.BucketId == "spark-credits");

        Assert.Equal(UsageFetchOutcome.Succeeded, result.Outcome);
        Assert.Equal(UsageBucketKind.Spark, sparkBucket.BucketKind);
    }

    [Fact]
    public void Map_SparkClassification_UsesBucketLabelWhenIdDoesNotContainSpark()
    {
        var responseBody =
            """
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 25,
                  "limit_window_seconds": 18000,
                  "reset_at": 1700009000
                },
                "secondary_window": {
                  "used_percent": 70,
                  "limit_window_seconds": 604800,
                  "reset_at": 1700600000
                }
              },
              "additional_rate_limits": [
                {
                  "metered_feature": "compute-credits",
                  "limit_name": "Spark Shared Pool",
                  "rate_limit": {
                    "primary_window": {
                      "used_percent": 10,
                      "limit_window_seconds": 18000,
                      "reset_at": 1700006000
                    },
                    "secondary_window": {
                      "used_percent": 35,
                      "limit_window_seconds": 604800,
                      "reset_at": 1700300000
                    }
                  }
                }
              ]
            }
            """;

        var result = mapper.Map(CreateRequest(), responseBody, ObservedAtUtc);
        var sparkBucket = Assert.Single(result.Buckets, bucket => bucket.BucketId == "compute-credits");

        Assert.Equal(UsageFetchOutcome.Succeeded, result.Outcome);
        Assert.Equal(UsageBucketKind.Spark, sparkBucket.BucketKind);
    }

    [Fact]
    public void Map_AdditionalBuckets_ArePreservedIncludingUnknownIds()
    {
        var responseBody =
            """
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 50,
                  "limit_window_seconds": 18000,
                  "reset_at": 1700009000
                },
                "secondary_window": {
                  "used_percent": 60,
                  "limit_window_seconds": 604800,
                  "reset_at": 1700600000
                }
              },
              "additional_rate_limits": [
                {
                  "metered_feature": "rendering",
                  "limit_name": "Rendering",
                  "rate_limit": {
                    "primary_window": {
                      "used_percent": 10,
                      "limit_window_seconds": 18000,
                      "reset_at": 1700009000
                    },
                    "secondary_window": {
                      "used_percent": 20,
                      "limit_window_seconds": 604800,
                      "reset_at": 1700600000
                    }
                  }
                },
                {
                  "limit_name": "No Id Provided",
                  "rate_limit": {
                    "primary_window": {
                      "used_percent": 5,
                      "limit_window_seconds": 18000,
                      "reset_at": 1700009000
                    },
                    "secondary_window": {
                      "used_percent": 15,
                      "limit_window_seconds": 604800,
                      "reset_at": 1700600000
                    }
                  }
                }
              ]
            }
            """;

        var result = mapper.Map(CreateRequest(), responseBody, ObservedAtUtc);

        Assert.Equal(UsageFetchOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, result.Buckets.Count);
        var additionalBuckets = result.Buckets.Where(bucket => bucket.BucketKind == UsageBucketKind.Additional).ToArray();
        Assert.Equal(2, additionalBuckets.Length);
        Assert.Contains(additionalBuckets, bucket => bucket.BucketId == "rendering");
        Assert.Contains(additionalBuckets, bucket => bucket.BucketId == "unknown");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.MissingRequiredField);
    }

    [Fact]
    public void Map_NoSparkBucket_PreservesAdditionalBuckets()
    {
        var responseBody =
            """
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 25,
                  "limit_window_seconds": 18000,
                  "reset_at": 1700009000
                },
                "secondary_window": {
                  "used_percent": 80,
                  "limit_window_seconds": 604800,
                  "reset_at": 1700600000
                }
              },
              "additional_rate_limits": [
                {
                  "metered_feature": "vision",
                  "limit_name": "Vision",
                  "rate_limit": {
                    "primary_window": {
                      "used_percent": 1,
                      "limit_window_seconds": 18000,
                      "reset_at": 1700009000
                    },
                    "secondary_window": {
                      "used_percent": 2,
                      "limit_window_seconds": 604800,
                      "reset_at": 1700600000
                    }
                  }
                }
              ]
            }
            """;

        var result = mapper.Map(CreateRequest(), responseBody, ObservedAtUtc);

        Assert.Equal(UsageFetchOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, result.Buckets.Count);
        Assert.Single(result.Buckets, bucket => bucket.BucketKind == UsageBucketKind.Additional);
        Assert.DoesNotContain(result.Buckets, bucket => bucket.BucketKind == UsageBucketKind.Spark);
        Assert.DoesNotContain(result.Buckets, bucket => ContainsSparkIdentifier(bucket.BucketId) || ContainsSparkIdentifier(bucket.BucketLabel));
    }

    [Fact]
    public void Map_MissingMainBucket_ReturnsMissingBucketButPreservesAdditional()
    {
        var responseBody =
            """
            {
              "additional_rate_limits": [
                {
                  "metered_feature": "spark-tier",
                  "limit_name": "Spark Tier",
                  "rate_limit": {
                    "primary_window": {
                      "used_percent": 1,
                      "limit_window_seconds": 18000,
                      "reset_at": 1700009000
                    },
                    "secondary_window": {
                      "used_percent": 2,
                      "limit_window_seconds": 604800,
                      "reset_at": 1700600000
                    }
                  }
                }
              ]
            }
            """;

        var result = mapper.Map(CreateRequest(), responseBody, ObservedAtUtc);

        Assert.Equal(UsageFetchOutcome.MissingBucket, result.Outcome);
        Assert.False(result.Availability.IsAvailable);
        Assert.Equal(StatusAvailabilityCode.MissingBucket, result.Availability.Code);
        Assert.Single(result.Buckets);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.MissingBucket);
    }

    [Fact]
    public void Map_MissingWindows_ReturnsMissingWindow()
    {
        var responseBody =
            """
            {
              "rate_limit": {}
            }
            """;

        var result = mapper.Map(CreateRequest(), responseBody, ObservedAtUtc);

        Assert.Equal(UsageFetchOutcome.MissingWindow, result.Outcome);
        Assert.False(result.Availability.IsAvailable);
        var mainBucket = Assert.Single(result.Buckets);
        Assert.Empty(mainBucket.Windows);
        Assert.Equal(UsageBucketFetchStatus.Unavailable, mainBucket.FetchStatus);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.MissingWindow);
    }

    [Fact]
    public void Map_InvalidDuration_CreatesUnavailableWindowWithoutFailingOtherWindows()
    {
        var responseBody =
            """
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 10,
                  "limit_window_seconds": 0,
                  "reset_at": 1700009000
                },
                "secondary_window": {
                  "used_percent": 20,
                  "limit_window_seconds": 604800,
                  "reset_at": 1700600000
                }
              }
            }
            """;

        var result = mapper.Map(CreateRequest(), responseBody, ObservedAtUtc);

        Assert.Equal(UsageFetchOutcome.Succeeded, result.Outcome);
        var mainBucket = Assert.Single(result.Buckets);
        Assert.Equal(UsageBucketFetchStatus.Partial, mainBucket.FetchStatus);
        Assert.Contains(mainBucket.Windows, window => !window.Availability.IsAvailable && window.Availability.Code == StatusAvailabilityCode.MissingTimestampOrDuration);
        Assert.Contains(mainBucket.Windows, window => window.Availability.IsAvailable);
    }

    [Fact]
    public void Map_ResetTimestampInPast_ClampsTimeLeftToZero()
    {
        var responseBody =
            """
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 20,
                  "limit_window_seconds": 18000,
                  "reset_at": 1699999900
                },
                "secondary_window": {
                  "used_percent": 30,
                  "limit_window_seconds": 604800,
                  "reset_at": 1700600000
                }
              }
            }
            """;

        var result = mapper.Map(CreateRequest(), responseBody, ObservedAtUtc);
        var mainBucket = Assert.Single(result.Buckets);

        Assert.Equal(UsageFetchOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, mainBucket.Windows[0].TimeLeftPercent);
    }

    [Fact]
    public void Map_UsedPercentOutsideRange_ClampsQuotaLeft()
    {
        var responseBody =
            """
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": -20,
                  "limit_window_seconds": 18000,
                  "reset_at": 1700009000
                },
                "secondary_window": {
                  "used_percent": 120,
                  "limit_window_seconds": 604800,
                  "reset_at": 1700600000
                }
              }
            }
            """;

        var result = mapper.Map(CreateRequest(), responseBody, ObservedAtUtc);
        var mainBucket = Assert.Single(result.Buckets);

        Assert.Equal(100, mainBucket.Windows[0].QuotaLeftPercent);
        Assert.Equal(0, mainBucket.Windows[1].QuotaLeftPercent);
    }

    [Fact]
    public void Map_MalformedJson_ReturnsMalformedResponseAndRedactedDiagnostics()
    {
        const string malformedBody = "{\"rate_limit\": {\"primary_window\": {\"used_percent\": 22, \"limit_window_seconds\": 18000, \"reset_at\": 1700009000},";

        var result = mapper.Map(CreateRequest(), malformedBody, ObservedAtUtc);
        var serialized = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Assert.Equal(UsageFetchOutcome.MalformedResponse, result.Outcome);
        Assert.False(result.Availability.IsAvailable);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Malformed);
        Assert.DoesNotContain(malformedBody, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_PartialSuccessWithDiagnostics_DoesNotInventValues()
    {
        var responseBody =
            """
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": "oops",
                  "limit_window_seconds": 18000,
                  "reset_at": 1700009000
                },
                "secondary_window": {
                  "used_percent": 70,
                  "limit_window_seconds": 604800,
                  "reset_at": 1700600000
                }
              },
              "additional_rate_limits": [
                {
                  "metered_feature": "spark-runtime",
                  "limit_name": "Spark Runtime",
                  "rate_limit": {
                    "primary_window": {
                      "used_percent": 2,
                      "limit_window_seconds": 18000,
                      "reset_at": 1700009000
                    }
                  }
                }
              ]
            }
            """;

        var result = mapper.Map(CreateRequest(), responseBody, ObservedAtUtc);

        Assert.Equal(UsageFetchOutcome.Succeeded, result.Outcome);
        Assert.True(result.Availability.IsAvailable);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.Malformed);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SourceDiagnosticCode.MissingWindow);

        var mainBucket = Assert.Single(result.Buckets, bucket => bucket.BucketKind == UsageBucketKind.MainCodex);
        var unavailableWindow = Assert.Single(mainBucket.Windows, window => !window.Availability.IsAvailable);
        Assert.Null(unavailableWindow.UsedPercent);
        Assert.Null(unavailableWindow.QuotaLeftPercent);
    }

    private static UsageProfileRequest CreateRequest()
    {
        return new UsageProfileRequest
        {
            ProfileId = "profile-a",
            LoginName = "user@example.com",
            SubscriptionTier = SubscriptionTier.Pro,
        };
    }

    private static bool ContainsSparkIdentifier(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains("spark", StringComparison.OrdinalIgnoreCase);
    }
}
