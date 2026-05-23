namespace CodexWidget.Profiles;

public sealed record CodexHomePaths
{
    public string HomeDirectory { get; init; } = string.Empty;

    public string CodexDirectory { get; init; } = string.Empty;

    public string CurrentAuthPath { get; init; } = string.Empty;

    public string ProfilesDirectory { get; init; } = string.Empty;

    public string ProfilesIndexPath { get; init; } = string.Empty;

    public string ProfilesLockPath { get; init; } = string.Empty;

    public string ConfigPath { get; init; } = string.Empty;
}

public sealed record CodexHomeResolutionOptions
{
    public string EnvironmentVariableName { get; init; } = "CODEX_PROFILES_HOME";

    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } = new Dictionary<string, string?>(0, StringComparer.Ordinal);

    public string? FallbackUserHomeDirectory { get; init; }

    public string? ExplicitHomeDirectory { get; init; }
}

public interface ICodexHomeResolver
{
    CodexHomePaths Resolve(CodexHomeResolutionOptions? options = null);
}

public sealed class CodexHomeResolver : ICodexHomeResolver
{
    private const string DefaultEnvironmentVariableName = "CODEX_PROFILES_HOME";

    public CodexHomePaths Resolve(CodexHomeResolutionOptions? options = null)
    {
        options ??= new CodexHomeResolutionOptions();

        var homeDirectory = ResolveHomeDirectory(options);
        var codexDirectory = Path.Combine(homeDirectory, ".codex");
        var profilesDirectory = Path.Combine(codexDirectory, "profiles");

        return new CodexHomePaths
        {
            HomeDirectory = homeDirectory,
            CodexDirectory = codexDirectory,
            CurrentAuthPath = Path.Combine(codexDirectory, "auth.json"),
            ProfilesDirectory = profilesDirectory,
            ProfilesIndexPath = Path.Combine(profilesDirectory, "profiles.json"),
            ProfilesLockPath = Path.Combine(profilesDirectory, "profiles.lock"),
            ConfigPath = Path.Combine(codexDirectory, "config.toml"),
        };
    }

    private static string ResolveHomeDirectory(CodexHomeResolutionOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ExplicitHomeDirectory))
        {
            return options.ExplicitHomeDirectory.Trim();
        }

        var environmentVariableName = string.IsNullOrWhiteSpace(options.EnvironmentVariableName)
            ? DefaultEnvironmentVariableName
            : options.EnvironmentVariableName.Trim();

        var environmentValue = ResolveEnvironmentVariable(options.EnvironmentVariables, environmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.FallbackUserHomeDirectory))
        {
            return options.FallbackUserHomeDirectory.Trim();
        }

        var osHomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(osHomeDirectory)
            ? string.Empty
            : osHomeDirectory.Trim();
    }

    private static string? ResolveEnvironmentVariable(
        IReadOnlyDictionary<string, string?> variables,
        string environmentVariableName)
    {
        if (variables.TryGetValue(environmentVariableName, out var configuredValue))
        {
            return configuredValue;
        }

        return Environment.GetEnvironmentVariable(environmentVariableName);
    }
}
