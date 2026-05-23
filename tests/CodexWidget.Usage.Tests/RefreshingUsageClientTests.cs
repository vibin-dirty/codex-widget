using CodexWidget.Core;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexWidget.Usage.Tests;

public sealed class RefreshingUsageClientTests
{
    [Fact]
    public async Task RefreshAsync_SendsDocumentedOAuthRequest_AndParsesAccessTokenOnly()
    {
        CapturedRequest? capturedRequest = null;
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"refreshed-access-token"}""", Encoding.UTF8, "application/json"),
            });
        });

        using var httpClient = new HttpClient(handler);
        var refreshService = new TokenRefreshService(httpClient);

        var result = await refreshService.RefreshAsync(new UsageProfileRequest
        {
            ProfileId = "profile-work",
            RefreshToken = "refresh-token-123",
        });

        Assert.Equal(UsageTokenRefreshOutcome.Succeeded, result.Outcome);
        Assert.Equal("refreshed-access-token", result.AccessToken);
        Assert.Null(result.RefreshToken);
        Assert.Null(result.IdToken);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://auth.openai.com/oauth/token", capturedRequest.Uri.AbsoluteUri);
        Assert.Equal("application/json", capturedRequest.ContentType?.MediaType);
        Assert.Null(capturedRequest.ContentType?.CharSet);

        using var jsonDocument = JsonDocument.Parse(capturedRequest.Body ?? string.Empty);
        Assert.Equal("app_EMoamEEZ73f0CkXaXp7hrann", jsonDocument.RootElement.GetProperty("client_id").GetString());
        Assert.Equal("refresh_token", jsonDocument.RootElement.GetProperty("grant_type").GetString());
        Assert.Equal("refresh-token-123", jsonDocument.RootElement.GetProperty("refresh_token").GetString());
        Assert.Equal("openid profile email", jsonDocument.RootElement.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task FetchAsync_UnauthorizedThenRefreshAndRetry_PreservesExistingRefreshToken_AndUpdatesSavedProfileSource()
    {
        using var fixture = new SyntheticUsageAuthFixture();
        var sourcePath = fixture.WriteSavedProfileJson(
            "saved",
            fixture.CreateLoginAuthJson(
                accountId: "acct-original",
                accessToken: "access-original",
                refreshToken: "refresh-original",
                additionalRootFields: new Dictionary<string, object?>
                {
                    ["profile_name"] = "Saved Profile",
                }));

        var getRequestCount = 0;
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get && ++getRequestCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"access-refreshed"}""", Encoding.UTF8, "application/json"),
                });
            }

            if (request.Method == HttpMethod.Get && getRequestCount == 2)
            {
                Assert.Equal("Bearer", request.Authorization?.Scheme);
                Assert.Equal("access-refreshed", request.Authorization?.Parameter);
                Assert.Equal("acct-original", request.GetSingleHeaderValue("ChatGPT-Account-Id"));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"synthetic":"usage"}""", Encoding.UTF8, "application/json"),
                });
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {request.Uri}");
        });

        using var httpClient = new HttpClient(handler);
        var innerClient = new UsageClient(httpClient, new UsageEndpointResolver(), new StubUsageResponseMapper());
        var refreshingClient = new RefreshingUsageClient(innerClient, new TokenRefreshService(httpClient), new AuthTokenFileUpdater());

        var result = await refreshingClient.FetchAsync(CreateRequest(sourcePath, fixture.ProfilesLockPath));

        Assert.Equal(UsageFetchOutcome.Succeeded, result.Outcome);
        Assert.Equal(UsageTokenRefreshOutcome.Succeeded, result.TokenRefresh.Outcome);
        Assert.Equal(UsageRetryOutcome.Succeeded, result.TokenRefresh.RetryOutcome);
        Assert.Equal(TokenUpdateOutcome.Succeeded, result.TokenRefresh.TokenUpdate.Outcome);
        Assert.Equal(3, handler.CallCount);

        var updatedJson = await File.ReadAllTextAsync(sourcePath);
        using var jsonDocument = JsonDocument.Parse(updatedJson);
        Assert.Equal("Saved Profile", jsonDocument.RootElement.GetProperty("profile_name").GetString());
        var tokens = jsonDocument.RootElement.GetProperty("tokens");
        Assert.Equal("acct-original", tokens.GetProperty("account_id").GetString());
        Assert.Equal("access-refreshed", tokens.GetProperty("access_token").GetString());
        Assert.Equal("refresh-original", tokens.GetProperty("refresh_token").GetString());
    }

    [Fact]
    public async Task FetchAsync_ReturnedIdTokenUpdatesAccountIdBeforeRetryAndWriteback()
    {
        using var fixture = new SyntheticUsageAuthFixture();
        var sourcePath = fixture.WriteSavedProfileJson(
            "saved",
            fixture.CreateLoginAuthJson(
                accountId: "acct-original",
                accessToken: "access-original",
                refreshToken: "refresh-original"));
        var refreshedIdToken = SyntheticUsageAuthFixture.BuildSyntheticJwt(new Dictionary<string, object?>
        {
            ["https://api.openai.com/auth"] = new Dictionary<string, object?>
            {
                ["chatgpt_account_id"] = "acct-updated",
            },
        });

        var getRequestCount = 0;
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get && ++getRequestCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new Dictionary<string, string?>
                        {
                            ["access_token"] = "access-refreshed",
                            ["id_token"] = refreshedIdToken,
                            ["refresh_token"] = "refresh-refreshed",
                        }),
                        Encoding.UTF8,
                        "application/json"),
                });
            }

            if (request.Method == HttpMethod.Get && getRequestCount == 2)
            {
                Assert.Equal("acct-updated", request.GetSingleHeaderValue("ChatGPT-Account-Id"));
                Assert.Equal("access-refreshed", request.Authorization?.Parameter);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"synthetic":"usage"}""", Encoding.UTF8, "application/json"),
                });
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {request.Uri}");
        });

        using var httpClient = new HttpClient(handler);
        var innerClient = new UsageClient(httpClient, new UsageEndpointResolver(), new StubUsageResponseMapper());
        var refreshingClient = new RefreshingUsageClient(innerClient, new TokenRefreshService(httpClient), new AuthTokenFileUpdater());

        var result = await refreshingClient.FetchAsync(CreateRequest(sourcePath, fixture.ProfilesLockPath));

        Assert.Equal(UsageFetchOutcome.Succeeded, result.Outcome);
        Assert.Equal("acct-updated", result.TokenRefresh.AccountId);

        using var jsonDocument = JsonDocument.Parse(await File.ReadAllTextAsync(sourcePath));
        var tokens = jsonDocument.RootElement.GetProperty("tokens");
        Assert.Equal("acct-updated", tokens.GetProperty("account_id").GetString());
        Assert.Equal(refreshedIdToken, tokens.GetProperty("id_token").GetString());
        Assert.Equal("refresh-refreshed", tokens.GetProperty("refresh_token").GetString());
    }

    [Fact]
    public async Task UpdateAsync_PreservesUnrelatedJsonFields_ForSavedProfileSource()
    {
        using var fixture = new SyntheticUsageAuthFixture();
        var sourcePath = fixture.WriteSavedProfileJson(
            "saved",
            fixture.CreateLoginAuthJson(
                accountId: "acct-original",
                accessToken: "access-original",
                refreshToken: "refresh-original",
                additionalRootFields: new Dictionary<string, object?>
                {
                    ["name"] = "Saved Profile",
                    ["metadata"] = new Dictionary<string, object?>
                    {
                        ["theme"] = "dark",
                        ["count"] = 2,
                    },
                },
                additionalTokenFields: new Dictionary<string, object?>
                {
                    ["legacy_field"] = "legacy-value",
                }));

        var updater = new AuthTokenFileUpdater();
        var result = await updater.UpdateAsync(new TokenUpdateRequest
        {
            ProfileId = "saved",
            SourcePath = sourcePath,
            ProfilesLockPath = fixture.ProfilesLockPath,
            AccessToken = "access-updated",
        });

        Assert.Equal(TokenUpdateOutcome.Succeeded, result.Outcome);

        var updatedNode = JsonNode.Parse(await File.ReadAllTextAsync(sourcePath))!.AsObject();
        Assert.Equal("Saved Profile", updatedNode["name"]?.GetValue<string>());
        Assert.Equal("dark", updatedNode["metadata"]?["theme"]?.GetValue<string>());
        Assert.Equal("legacy-value", updatedNode["tokens"]?["legacy_field"]?.GetValue<string>());
        Assert.Equal("acct-original", updatedNode["tokens"]?["account_id"]?.GetValue<string>());
        Assert.Equal("access-updated", updatedNode["tokens"]?["access_token"]?.GetValue<string>());
        Assert.Equal("refresh-original", updatedNode["tokens"]?["refresh_token"]?.GetValue<string>());
    }

    [Fact]
    public async Task UpdateAsync_UpdatesCurrentAuthSource()
    {
        using var fixture = new SyntheticUsageAuthFixture();
        var sourcePath = fixture.WriteCurrentAuthJson(fixture.CreateLoginAuthJson(
            accountId: "acct-current",
            accessToken: "access-current",
            refreshToken: "refresh-current"));

        var updater = new AuthTokenFileUpdater();
        var result = await updater.UpdateAsync(new TokenUpdateRequest
        {
            SourcePath = sourcePath,
            ProfilesLockPath = fixture.ProfilesLockPath,
            AccessToken = "access-current-updated",
            RefreshToken = "refresh-current-updated",
        });

        Assert.Equal(TokenUpdateOutcome.Succeeded, result.Outcome);

        using var jsonDocument = JsonDocument.Parse(await File.ReadAllTextAsync(sourcePath));
        var tokens = jsonDocument.RootElement.GetProperty("tokens");
        Assert.Equal("access-current-updated", tokens.GetProperty("access_token").GetString());
        Assert.Equal("refresh-current-updated", tokens.GetProperty("refresh_token").GetString());
    }

    [Fact]
    public async Task FetchAsync_MissingRefreshToken_DoesNotRetry()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });

        using var httpClient = new HttpClient(handler);
        var innerClient = new UsageClient(httpClient, new UsageEndpointResolver(), new StubUsageResponseMapper());
        var refreshingClient = new RefreshingUsageClient(innerClient, new TokenRefreshService(httpClient), new AuthTokenFileUpdater());

        var result = await refreshingClient.FetchAsync(new UsageProfileRequest
        {
            ProfileId = "profile-work",
            ChatGptBaseUrl = "https://chatgpt.com/backend-api",
            AccountId = "acct-original",
            AccessToken = "access-original",
        });

        Assert.Equal(UsageFetchOutcome.TokenRefreshFailed, result.Outcome);
        Assert.Equal(UsageTokenRefreshOutcome.MissingRefreshToken, result.TokenRefresh.Outcome);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task FetchAsync_RefreshHttpFailure_DoesNotRetryAgain()
    {
        using var fixture = new SyntheticUsageAuthFixture();
        var sourcePath = fixture.WriteSavedProfileJson(
            "saved",
            fixture.CreateLoginAuthJson(
                accountId: "acct-original",
                accessToken: "access-original",
                refreshToken: "refresh-original"));

        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            throw new InvalidOperationException("Unexpected request.");
        });

        using var httpClient = new HttpClient(handler);
        var innerClient = new UsageClient(httpClient, new UsageEndpointResolver(), new StubUsageResponseMapper());
        var refreshingClient = new RefreshingUsageClient(innerClient, new TokenRefreshService(httpClient), new AuthTokenFileUpdater());

        var result = await refreshingClient.FetchAsync(CreateRequest(sourcePath, fixture.ProfilesLockPath));

        Assert.Equal(UsageFetchOutcome.TokenRefreshFailed, result.Outcome);
        Assert.Equal(UsageTokenRefreshOutcome.HttpError, result.TokenRefresh.Outcome);
        Assert.Equal(2, handler.CallCount);
        Assert.Single(handler.Requests, request => request.Method == HttpMethod.Get);
        Assert.Single(handler.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task FetchAsync_MalformedRefreshResponse_DoesNotRetryAgain()
    {
        using var fixture = new SyntheticUsageAuthFixture();
        var sourcePath = fixture.WriteSavedProfileJson(
            "saved",
            fixture.CreateLoginAuthJson(
                accountId: "acct-original",
                accessToken: "access-original",
                refreshToken: "refresh-original"));

        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{}""", Encoding.UTF8, "application/json"),
                });
            }

            throw new InvalidOperationException("Unexpected request.");
        });

        using var httpClient = new HttpClient(handler);
        var innerClient = new UsageClient(httpClient, new UsageEndpointResolver(), new StubUsageResponseMapper());
        var refreshingClient = new RefreshingUsageClient(innerClient, new TokenRefreshService(httpClient), new AuthTokenFileUpdater());

        var result = await refreshingClient.FetchAsync(CreateRequest(sourcePath, fixture.ProfilesLockPath));

        Assert.Equal(UsageFetchOutcome.TokenRefreshFailed, result.Outcome);
        Assert.Equal(UsageTokenRefreshOutcome.MalformedResponse, result.TokenRefresh.Outcome);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task UpdateAsync_LockContentionTimeout_ReturnsFailure()
    {
        using var fixture = new SyntheticUsageAuthFixture();
        var sourcePath = fixture.WriteSavedProfileJson(
            "saved",
            fixture.CreateLoginAuthJson(
                accountId: "acct-original",
                accessToken: "access-original",
                refreshToken: "refresh-original"));
        await using var heldLock = new FileStream(
            fixture.ProfilesLockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        var updater = new AuthTokenFileUpdater(new AuthTokenFileUpdaterOptions
        {
            LockAcquireTimeout = TimeSpan.FromMilliseconds(50),
            LockRetryInterval = TimeSpan.FromMilliseconds(10),
        });

        var result = await updater.UpdateAsync(new TokenUpdateRequest
        {
            SourcePath = sourcePath,
            ProfilesLockPath = fixture.ProfilesLockPath,
            AccessToken = "access-updated",
        });

        Assert.Equal(TokenUpdateOutcome.LockUnavailable, result.Outcome);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Summary.Contains("profiles lock", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FetchAsync_RedactsDiagnosticsAndSerializedResults()
    {
        const string accessToken = "access-sensitive-123456";
        const string refreshToken = "refresh-sensitive-abcdef";
        const string accountId = "acct-sensitive";
        var idToken = SyntheticUsageAuthFixture.BuildSyntheticJwt(new Dictionary<string, object?>
        {
            ["sub"] = "subject-sensitive",
            ["https://api.openai.com/auth"] = new Dictionary<string, object?>
            {
                ["chatgpt_account_id"] = "acct-updated",
            },
        });

        using var fixture = new SyntheticUsageAuthFixture();
        var sourcePath = fixture.WriteSavedProfileJson(
            "saved",
            fixture.CreateLoginAuthJson(
                accountId: accountId,
                accessToken: accessToken,
                refreshToken: refreshToken,
                idToken: idToken));

        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            throw new HttpRequestException(
                $"oauth body refresh_token={refreshToken} authorization=Bearer {accessToken} source={sourcePath} jwt={idToken}");
        });

        using var httpClient = new HttpClient(handler);
        var innerClient = new UsageClient(httpClient, new UsageEndpointResolver(), new StubUsageResponseMapper());
        var refreshingClient = new RefreshingUsageClient(innerClient, new TokenRefreshService(httpClient), new AuthTokenFileUpdater());

        var result = await refreshingClient.FetchAsync(CreateRequest(sourcePath, fixture.ProfilesLockPath) with
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccountId = accountId,
            IdToken = idToken,
        });

        Assert.Equal(UsageFetchOutcome.TokenRefreshFailed, result.Outcome);
        var serialized = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        var diagnosticsText = string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(diagnostic => $"{diagnostic.Summary}|{diagnostic.Detail}|{string.Join(",", diagnostic.Context.Select(pair => $"{pair.Key}:{pair.Value}"))}"));

        Assert.DoesNotContain(accessToken, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(idToken, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(sourcePath, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, diagnosticsText, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, diagnosticsText, StringComparison.Ordinal);
        Assert.DoesNotContain(idToken, diagnosticsText, StringComparison.Ordinal);
        Assert.DoesNotContain(sourcePath, diagnosticsText, StringComparison.Ordinal);
        Assert.Contains("tokenRefresh", serialized, StringComparison.Ordinal);
    }

    private static UsageProfileRequest CreateRequest(string sourcePath, string profilesLockPath)
    {
        return new UsageProfileRequest
        {
            ProfileId = "profile-work",
            LoginName = "person@example.invalid",
            SubscriptionTier = SubscriptionTier.Pro,
            ChatGptBaseUrl = "https://chatgpt.com/backend-api",
            SourcePath = sourcePath,
            ProfilesLockPath = profilesLockPath,
            AccountId = "acct-original",
            AccessToken = "access-original",
            RefreshToken = "refresh-original",
        };
    }

    private sealed class StubUsageResponseMapper : IUsageResponseMapper
    {
        public UsageFetchResult Map(UsageProfileRequest request, string responseBody, DateTimeOffset observedAtUtc)
        {
            return new UsageFetchResult
            {
                ProfileId = request.ProfileId,
                Outcome = UsageFetchOutcome.Succeeded,
                Availability = StatusAvailability.Available(),
                Diagnostics = Array.Empty<SourceDiagnostic>(),
            };
        }
    }

    private sealed class RecordingHttpMessageHandler(Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        private readonly Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> sendAsync = sendAsync;

        public int CallCount { get; private set; }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;

            var capturedRequest = new CapturedRequest
            {
                Method = request.Method,
                Uri = request.RequestUri ?? new Uri("about:blank"),
                Authorization = request.Headers.Authorization,
                Headers = request.Headers.ToDictionary(
                    header => header.Key,
                    header => (IReadOnlyList<string>)header.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase),
                ContentType = request.Content?.Headers.ContentType,
                Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
            };

            Requests.Add(capturedRequest);
            return await sendAsync(capturedRequest, cancellationToken);
        }
    }

    private sealed record CapturedRequest
    {
        public HttpMethod Method { get; init; } = HttpMethod.Get;

        public Uri Uri { get; init; } = new("about:blank");

        public AuthenticationHeaderValue? Authorization { get; init; }

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; init; }
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        public MediaTypeHeaderValue? ContentType { get; init; }

        public string? Body { get; init; }

        public string? GetSingleHeaderValue(string name)
        {
            if (!Headers.TryGetValue(name, out var values) || values.Count == 0)
            {
                return null;
            }

            return values[0];
        }
    }

    private sealed class SyntheticUsageAuthFixture : IDisposable
    {
        private bool disposed;

        public SyntheticUsageAuthFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"CodexWidget.Usage.Tests.{Guid.NewGuid():N}");
            CodexPath = Path.Combine(RootPath, ".codex");
            ProfilesDirectoryPath = Path.Combine(CodexPath, "profiles");
            CurrentAuthPath = Path.Combine(CodexPath, "auth.json");
            ProfilesLockPath = Path.Combine(ProfilesDirectoryPath, "profiles.lock");

            Directory.CreateDirectory(ProfilesDirectoryPath);
        }

        public string RootPath { get; }

        public string CodexPath { get; }

        public string ProfilesDirectoryPath { get; }

        public string CurrentAuthPath { get; }

        public string ProfilesLockPath { get; }

        public string WriteCurrentAuthJson(string jsonContent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CurrentAuthPath)!);
            File.WriteAllText(CurrentAuthPath, jsonContent);
            return CurrentAuthPath;
        }

        public string WriteSavedProfileJson(string profileId, string jsonContent)
        {
            var path = Path.Combine(ProfilesDirectoryPath, $"{profileId}.json");
            File.WriteAllText(path, jsonContent);
            return path;
        }

        public string CreateLoginAuthJson(
            string accountId,
            string accessToken,
            string refreshToken,
            string? idToken = null,
            IReadOnlyDictionary<string, object?>? additionalRootFields = null,
            IReadOnlyDictionary<string, object?>? additionalTokenFields = null)
        {
            var tokens = new Dictionary<string, object?>
            {
                ["account_id"] = accountId,
                ["id_token"] = idToken ?? BuildSyntheticJwt(new Dictionary<string, object?>
                {
                    ["sub"] = "synthetic-subject",
                    ["email"] = "person@example.invalid",
                }),
                ["access_token"] = accessToken,
                ["refresh_token"] = refreshToken,
            };

            if (additionalTokenFields is not null)
            {
                foreach (var pair in additionalTokenFields)
                {
                    tokens[pair.Key] = pair.Value;
                }
            }

            var root = new Dictionary<string, object?>
            {
                ["tokens"] = tokens,
            };

            if (additionalRootFields is not null)
            {
                foreach (var pair in additionalRootFields)
                {
                    root[pair.Key] = pair.Value;
                }
            }

            return JsonSerializer.Serialize(root);
        }

        public static string BuildSyntheticJwt(IReadOnlyDictionary<string, object?> claims)
        {
            var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
            var payload = Base64UrlEncode(JsonSerializer.Serialize(claims));
            return $"{header}.{payload}.synthetic-signature";
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static string Base64UrlEncode(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var encoded = Convert.ToBase64String(bytes);
            return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
