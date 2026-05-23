using System.Text;
using System.Text.Json;

namespace CodexWidget.Profiles.Tests;

internal sealed class SyntheticCodexHomeFixture : IDisposable
{
    private readonly List<string> _createdPaths = [];
    private readonly string _rootPathPrefix;
    private bool _disposed;

    public SyntheticCodexHomeFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"CodexWidget.Profiles.Tests.{Guid.NewGuid():N}");
        HomePath = Path.Combine(RootPath, "synthetic-home");
        CodexPath = Path.Combine(HomePath, ".codex");
        ProfilesDirectoryPath = Path.Combine(CodexPath, "profiles");
        CurrentAuthPath = Path.Combine(CodexPath, "auth.json");
        ProfilesIndexPath = Path.Combine(ProfilesDirectoryPath, "profiles.json");
        ProfilesLockPath = Path.Combine(ProfilesDirectoryPath, "profiles.lock");
        ConfigPath = Path.Combine(CodexPath, "config.toml");
        RealUserCodexPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

        Directory.CreateDirectory(ProfilesDirectoryPath);

        _rootPathPrefix = EnsureTrailingSeparator(Path.GetFullPath(RootPath));
    }

    public string RootPath { get; }

    public string HomePath { get; }

    public string CodexPath { get; }

    public string ProfilesDirectoryPath { get; }

    public string CurrentAuthPath { get; }

    public string ProfilesIndexPath { get; }

    public string ProfilesLockPath { get; }

    public string ConfigPath { get; }

    public string RealUserCodexPath { get; }

    public IReadOnlyList<string> CreatedPaths => _createdPaths;

    public CodexHomeResolutionOptions CreateResolutionOptions()
    {
        return new CodexHomeResolutionOptions
        {
            EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["CODEX_PROFILES_HOME"] = HomePath,
            },
            FallbackUserHomeDirectory = Path.Combine(RootPath, "fallback-home"),
        };
    }

    public string WriteCurrentAuthJson(string jsonContent) => WriteFile(CurrentAuthPath, jsonContent);

    public string WriteSavedProfileJson(string profileId, string jsonContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var profilePath = Path.Combine(ProfilesDirectoryPath, $"{profileId}.json");
        return WriteFile(profilePath, jsonContent);
    }

    public string WriteProfilesIndexJson(string jsonContent) => WriteFile(ProfilesIndexPath, jsonContent);

    public string WriteConfigToml(string tomlContent) => WriteFile(ConfigPath, tomlContent);

    public static string CreateSyntheticLoginAuthJson(
        string accountId = "synthetic-account",
        string? idToken = null,
        string accessToken = "synthetic-access-token",
        string refreshToken = "synthetic-refresh-token")
    {
        var payload = new Dictionary<string, object?>
        {
            ["tokens"] = new Dictionary<string, string?>
            {
                ["account_id"] = accountId,
                ["id_token"] = idToken ?? BuildSyntheticJwt(new Dictionary<string, object?>
                {
                    ["sub"] = "synthetic-subject",
                    ["email"] = "person@example.invalid",
                }),
                ["access_token"] = accessToken,
                ["refresh_token"] = refreshToken,
            },
        };

        return JsonSerializer.Serialize(payload);
    }

    public static string CreateSyntheticApiKeyAuthJson(string apiKey = "sk-synthetic-api-key")
    {
        var payload = new Dictionary<string, string?>
        {
            ["OPENAI_API_KEY"] = apiKey,
        };

        return JsonSerializer.Serialize(payload);
    }

    public static string BuildSyntheticJwt(IReadOnlyDictionary<string, object?> claims)
    {
        var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64UrlEncode(JsonSerializer.Serialize(claims));
        return $"{header}.{payload}.synthetic-signature";
    }

    public static string BuildSyntheticJwtFromPayloadJson(string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64UrlEncode(payloadJson);
        return $"{header}.{payload}.synthetic-signature";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }

    private string WriteFile(string path, string content)
    {
        EnsurePathIsFixtureLocal(path);
        var directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        File.WriteAllText(path, content);
        _createdPaths.Add(Path.GetFullPath(path));
        return path;
    }

    private void EnsurePathIsFixtureLocal(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(_rootPathPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to write outside fixture root. Path: {fullPath}");
        }
    }

    private static string Base64UrlEncode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var encoded = Convert.ToBase64String(bytes);
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
