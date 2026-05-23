using System.Text.RegularExpressions;

namespace CodexWidget.Web.Tests;

public sealed class ContainerComposeDocumentationGuardrailTests
{
    [Fact]
    public void ContainerAssets_ArePresent_AndReferenceCodexWidgetWeb()
    {
        var repositoryRoot = FindRepositoryRoot();

        var dockerfilePath = Path.Combine(repositoryRoot, "Dockerfile");
        var dockerIgnorePath = Path.Combine(repositoryRoot, ".dockerignore");
        var composePath = Path.Combine(repositoryRoot, "docker-compose.yml");
        var composeEnvPath = Path.Combine(repositoryRoot, "docker-compose.env.example");

        Assert.True(File.Exists(dockerfilePath), "Expected root Dockerfile to exist.");
        Assert.True(File.Exists(dockerIgnorePath), "Expected root .dockerignore to exist.");
        Assert.True(File.Exists(composePath), "Expected root docker-compose.yml to exist.");
        Assert.True(File.Exists(composeEnvPath), "Expected docker-compose.env.example to exist.");

        var dockerfile = File.ReadAllText(dockerfilePath);
        Assert.Contains("src/CodexWidget.Web/CodexWidget.Web.csproj", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"CodexWidget.Web.dll\"]", dockerfile, StringComparison.Ordinal);

        var compose = File.ReadAllText(composePath);
        Assert.Contains("codex-widget-web", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_EnforcesTrustedLanGuardrails()
    {
        var compose = ReadRepositoryFile("docker-compose.yml");

        Assert.Contains("8787:8787", compose, StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_URLS: http://0.0.0.0:8787", compose, StringComparison.Ordinal);
        Assert.Contains("CodexWidgetWeb__AllowLanBinding: \"true\"", compose, StringComparison.Ordinal);

        if (Regex.IsMatch(compose, @"CodexWidgetWeb__EnableCors:\s*[""']?true[""']?", RegexOptions.IgnoreCase))
        {
            Assert.Contains("CodexWidgetWeb__AllowedCorsOrigins", compose, StringComparison.Ordinal);
            Assert.DoesNotContain("CodexWidgetWeb__AllowedCorsOrigins: \"*\"", compose, StringComparison.Ordinal);
            Assert.DoesNotContain("CodexWidgetWeb__AllowedCorsOrigins: *", compose, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("CodexWidgetWeb__EnableCors: \"false\"", compose, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DockerIgnore_ExcludesBuildAndSensitiveDataPatterns()
    {
        var dockerIgnore = NormalizeLineEndings(ReadRepositoryFile(".dockerignore"));

        AssertPatternPresent(dockerIgnore, @"(?im)^\*\*/bin/$");
        AssertPatternPresent(dockerIgnore, @"(?im)^\*\*/obj/$");
        AssertPatternPresent(dockerIgnore, @"(?im)^artifacts/$");
        AssertPatternPresent(dockerIgnore, @"(?im)^\.git$");

        Assert.True(
            Regex.IsMatch(dockerIgnore, @"(?im)^logs/$") || Regex.IsMatch(dockerIgnore, @"(?im)^\*\*/logs/$"),
            "Expected .dockerignore to exclude local logs.");
        Assert.True(
            Regex.IsMatch(dockerIgnore, @"(?im)^tmp/$") || Regex.IsMatch(dockerIgnore, @"(?im)^\*\*/tmp/$"),
            "Expected .dockerignore to exclude temporary output.");
        Assert.True(
            Regex.IsMatch(dockerIgnore, @"(?im)^temp/$") || Regex.IsMatch(dockerIgnore, @"(?im)^\*\*/temp/$"),
            "Expected .dockerignore to exclude temp output.");

        AssertPatternPresent(dockerIgnore, @"(?im)^\.codex/$");
        AssertPatternPresent(dockerIgnore, @"(?im)^\*\*/\.codex/$");
        AssertPatternPresent(dockerIgnore, @"(?im)^\*\*/auth/$");
        AssertPatternPresent(dockerIgnore, @"(?im)^\*\*/profile-snapshot/$");
        AssertPatternPresent(dockerIgnore, @"(?im)^\*\*/profile-snapshots/$");
    }

    [Fact]
    public void TrackedContainerAssets_ContainCriticalPublishDataAndHealthStrings()
    {
        var dockerfile = ReadRepositoryFile("Dockerfile");
        var compose = ReadRepositoryFile("docker-compose.yml");
        var composeEnv = ReadRepositoryFile("docker-compose.env.example");
        var combined = string.Join("\n", dockerfile, compose, composeEnv);

        Assert.Contains("dotnet publish src/CodexWidget.Web/CodexWidget.Web.csproj -c Release", combined, StringComparison.Ordinal);
        Assert.Contains("/srv/data/codex-widget", combined, StringComparison.Ordinal);
        Assert.Contains("8787", combined, StringComparison.Ordinal);
        Assert.Contains("/health", combined, StringComparison.Ordinal);
    }

    private static void AssertPatternPresent(string content, string pattern)
    {
        Assert.True(Regex.IsMatch(content, pattern), $"Expected pattern `{pattern}` to be present.");
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
    }

    private static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var markerPath = Path.Combine(directory.FullName, "CodexWidget.slnx");
            if (File.Exists(markerPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }
}
