namespace CodexWidget.Profiles.Tests;

public sealed class CodexHomeResolverTests
{
    private readonly CodexHomeResolver _resolver = new();

    [Fact]
    public void Resolve_UsesEnvironmentOverride_WhenPresentAndNonWhitespace()
    {
        var options = new CodexHomeResolutionOptions
        {
            EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["CODEX_PROFILES_HOME"] = "/tmp/synthetic-codex-home",
            },
            FallbackUserHomeDirectory = "/tmp/ignored-fallback",
        };

        var result = _resolver.Resolve(options);

        Assert.Equal("/tmp/synthetic-codex-home", result.HomeDirectory);
        Assert.Equal(Path.Combine("/tmp/synthetic-codex-home", ".codex"), result.CodexDirectory);
        Assert.Equal(Path.Combine("/tmp/synthetic-codex-home", ".codex", "auth.json"), result.CurrentAuthPath);
        Assert.Equal(Path.Combine("/tmp/synthetic-codex-home", ".codex", "profiles"), result.ProfilesDirectory);
        Assert.Equal(Path.Combine("/tmp/synthetic-codex-home", ".codex", "profiles", "profiles.json"), result.ProfilesIndexPath);
        Assert.Equal(Path.Combine("/tmp/synthetic-codex-home", ".codex", "profiles", "profiles.lock"), result.ProfilesLockPath);
        Assert.Equal(Path.Combine("/tmp/synthetic-codex-home", ".codex", "config.toml"), result.ConfigPath);
    }

    [Fact]
    public void Resolve_UsesFallbackHome_WhenEnvironmentOverrideIsWhitespace()
    {
        var options = new CodexHomeResolutionOptions
        {
            EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["CODEX_PROFILES_HOME"] = "   ",
            },
            FallbackUserHomeDirectory = "/tmp/synthetic-fallback-home",
        };

        var result = _resolver.Resolve(options);

        Assert.Equal("/tmp/synthetic-fallback-home", result.HomeDirectory);
    }

    [Fact]
    public void Resolve_UsesOsHome_WhenOverridesAreMissing()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Trim();
        var options = new CodexHomeResolutionOptions
        {
            EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal),
            FallbackUserHomeDirectory = null,
        };

        var result = _resolver.Resolve(options);

        Assert.Equal(expected, result.HomeDirectory);
    }
}
