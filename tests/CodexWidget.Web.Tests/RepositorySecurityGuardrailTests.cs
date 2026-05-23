using System.Text.RegularExpressions;
using CodexWidget.TestSupport;

namespace CodexWidget.Web.Tests;

public sealed class RepositorySecurityGuardrailTests
{
    [Fact]
    public void RepositoryScans_ExcludeGeneratedArtifactAndBuildDirectories()
    {
        var repositoryRoot = FindRepositoryRoot();
        var scannedFiles = EnumerateRepositoryTextFilesForGuardrails().ToArray();
        var scannedRelativePaths = scannedFiles
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToArray();

        Assert.NotEmpty(scannedFiles);
        Assert.DoesNotContain(scannedRelativePaths, path => ContainsSegment(path, "bin"));
        Assert.DoesNotContain(scannedRelativePaths, path => ContainsSegment(path, "obj"));
        Assert.DoesNotContain(scannedRelativePaths, path => ContainsSegment(path, "artifacts"));
        Assert.DoesNotContain(scannedRelativePaths, path => ContainsSegment(path, "artifact"));
        Assert.DoesNotContain(scannedRelativePaths, path => ContainsSegment(path, "generated"));
        Assert.DoesNotContain(scannedRelativePaths, path => ContainsSegment(path, "tmp"));
        Assert.DoesNotContain(scannedRelativePaths, path => ContainsSegment(path, "temp"));
    }

    [Fact]
    public void ProductSourceAndStaticAssets_DoNotContainSyntheticFixtureLiterals()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceFiles = EnumerateRepositoryTextFilesForGuardrails()
            .Where(path => Path.GetRelativePath(repositoryRoot, path)
                .StartsWith($"src{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(sourceFiles);

        foreach (var sourcePath in sourceFiles)
        {
            var content = File.ReadAllText(sourcePath);
            foreach (var syntheticValue in SyntheticSecurityFixtures.AllSyntheticSensitiveValues)
            {
                Assert.DoesNotContain(syntheticValue, content, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void RepositoryTextFiles_DoNotContainObviousSensitiveLiteralsOutsideSyntheticAllowlist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var filePath in EnumerateRepositoryTextFilesForGuardrails())
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, filePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (!IsSourceOrStaticAssetScope(relativePath))
            {
                continue;
            }

            var content = File.ReadAllText(filePath);

            foreach (var pattern in ForbiddenSensitiveLiteralPatterns)
            {
                foreach (Match match in pattern.Matches(content))
                {
                    if (IsAllowlistedSyntheticMatch(relativePath, match.Value))
                    {
                        continue;
                    }

                    violations.Add($"{relativePath}: `{Truncate(match.Value, 80)}`");
                }
            }
        }

        Assert.True(violations.Count == 0, "Potential sensitive literal matches found:\n" + string.Join("\n", violations));
    }

    private static IEnumerable<string> EnumerateRepositoryTextFilesForGuardrails()
    {
        var repositoryRoot = FindRepositoryRoot();

        return EnumerateTextFiles(repositoryRoot, repositoryRoot);
    }

    private static IEnumerable<string> EnumerateTextFiles(string repositoryRoot, string directory)
    {
        foreach (var filePath in Directory.EnumerateFiles(directory))
        {
            if (!ShouldSkipPath(repositoryRoot, filePath)
                && IncludedTextExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
            {
                yield return filePath;
            }
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(directory))
        {
            if (ShouldSkipDirectory(repositoryRoot, childDirectory))
            {
                continue;
            }

            foreach (var filePath in EnumerateTextFiles(repositoryRoot, childDirectory))
            {
                yield return filePath;
            }
        }
    }

    private static bool ShouldSkipDirectory(string repositoryRoot, string path)
    {
        if (new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return true;
        }

        return ShouldSkipPath(repositoryRoot, path);
    }

    private static bool ShouldSkipPath(string repositoryRoot, string path)
    {
        var relativePath = Path.GetRelativePath(repositoryRoot, path);

        if (ContainsSegment(relativePath, ".git"))
        {
            return true;
        }

        foreach (var segment in ExcludedDirectorySegments)
        {
            if (ContainsSegment(relativePath, segment))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSegment(string path, string segment)
    {
        return path
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.Equals(segment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAllowlistedSyntheticMatch(string relativePath, string matchedValue)
    {
        var normalizedPath = relativePath.Replace('\\', '/');

        if (normalizedPath.StartsWith("tests/", StringComparison.OrdinalIgnoreCase))
        {
            return IsSyntheticValue(matchedValue);
        }

        return false;
    }

    private static bool IsSourceOrStaticAssetScope(string relativePath)
    {
        return relativePath.StartsWith("src/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("dockerfile", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals(".dockerignore", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("docker-compose.yml", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("docker-compose.env.example", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSyntheticValue(string value)
    {
        if (value.Contains("synthetic", StringComparison.OrdinalIgnoreCase)
            || value.Contains("example.invalid", StringComparison.OrdinalIgnoreCase)
            || value.Contains("phase5", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SyntheticSecurityFixtures.AllSyntheticSensitiveValues.Contains(value, StringComparer.Ordinal);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
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

    private static readonly string[] ExcludedDirectorySegments =
    [
        "bin",
        "obj",
        "artifacts",
        "artifact",
        "generated",
        "tmp",
        "temp",
        "node_modules",
        ".vs",
        ".idea",
    ];

    private static readonly string[] IncludedTextExtensions =
    [
        ".cs",
        ".csproj",
        ".props",
        ".targets",
        ".sln",
        ".slnx",
        ".json",
        ".js",
        ".ts",
        ".tsx",
        ".jsx",
        ".css",
        ".html",
        ".svg",
        ".md",
        ".txt",
        ".yml",
        ".yaml",
        ".xml",
        ".ps1",
        ".sh",
        ".cmd",
        ".bat",
    ];

    private static readonly Regex[] ForbiddenSensitiveLiteralPatterns =
    [
        new(@"\bsk-[A-Za-z0-9_-]{16,}\b", RegexOptions.CultureInvariant),
        new(@"\bgh[pousr]_[A-Za-z0-9]{20,}\b", RegexOptions.CultureInvariant),
        new(@"Authorization\s*:\s*Bearer\s+[A-Za-z0-9._-]{16,}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        new(@"\b[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}\b", RegexOptions.CultureInvariant),
    ];
}
