namespace CodexWidget.Profiles.Tests;

public sealed class SyntheticFixtureGuardrailTests
{
    [Fact]
    public void Fixture_UsesTemporaryCodexHome_NotRealUserDirectory()
    {
        using var fixture = new SyntheticCodexHomeFixture();

        Assert.StartsWith(Path.GetTempPath(), fixture.RootPath, StringComparison.Ordinal);
        Assert.True(IsPathWithin(fixture.CodexPath, fixture.RootPath));
        Assert.False(IsPathWithin(fixture.CodexPath, fixture.RealUserCodexPath));
    }

    [Fact]
    public void Fixture_WritesOnlyInsideFixtureRoot()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        var jwt = SyntheticCodexHomeFixture.BuildSyntheticJwt(new Dictionary<string, object?>
        {
            ["sub"] = "synthetic-subject",
            ["email"] = "person@example.invalid",
            ["https://api.openai.com/auth"] = new Dictionary<string, object?>
            {
                ["chatgpt_user_id"] = "synthetic-user-id",
                ["chatgpt_plan_type"] = "chatgpt_pro",
            },
        });

        fixture.WriteCurrentAuthJson(SyntheticCodexHomeFixture.CreateSyntheticLoginAuthJson(idToken: jwt));
        fixture.WriteSavedProfileJson("work", SyntheticCodexHomeFixture.CreateSyntheticLoginAuthJson(accountId: "synthetic-account-work"));
        fixture.WriteSavedProfileJson("api", SyntheticCodexHomeFixture.CreateSyntheticApiKeyAuthJson());
        fixture.WriteProfilesIndexJson("""
            {
              "profiles": {
                "work": {
                  "label": "Work",
                  "email": "person@example.invalid",
                  "plan": "plus",
                  "is_api_key": false
                }
              }
            }
            """);
        fixture.WriteConfigToml("cli_auth_credentials_store_mode = \"file\"\n");

        Assert.NotEmpty(fixture.CreatedPaths);
        Assert.All(fixture.CreatedPaths, path => Assert.True(IsPathWithin(path, fixture.RootPath)));
        Assert.All(fixture.CreatedPaths, path => Assert.False(IsPathWithin(path, fixture.RealUserCodexPath)));
    }

    [Fact]
    public void FixtureResolutionOptions_DoNotMutateProcessEnvironment()
    {
        using var fixture = new SyntheticCodexHomeFixture();
        const string envName = "CODEX_PROFILES_HOME";
        var before = Environment.GetEnvironmentVariable(envName);

        var options = fixture.CreateResolutionOptions();

        Assert.Equal(fixture.HomePath, options.EnvironmentVariables[envName]);
        Assert.Equal(before, Environment.GetEnvironmentVariable(envName));
    }

    private static bool IsPathWithin(string candidatePath, string parentPath)
    {
        var fullCandidate = EnsureTrailingSeparator(Path.GetFullPath(candidatePath));
        var fullParent = EnsureTrailingSeparator(Path.GetFullPath(parentPath));
        return fullCandidate.StartsWith(fullParent, StringComparison.Ordinal);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
