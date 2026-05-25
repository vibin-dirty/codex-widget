using System.Text.RegularExpressions;
using CodexWidget.TestSupport;

namespace CodexWidget.Web.Tests;

public sealed class StaticBrowserUiSecurityRegressionTests
{
    [Fact]
    public void IndexHtml_UsesSelfHostedAssetsWithoutExternalNetworkDependencies()
    {
        var indexPath = GetStaticAssetPath("index.html");
        var indexContent = File.ReadAllText(indexPath);

        Assert.Contains("<link rel=\"stylesheet\" href=\"/css/app.css\">", indexContent, StringComparison.Ordinal);
        Assert.Contains("<script src=\"/js/app.js\" defer></script>", indexContent, StringComparison.Ordinal);

        var srcOrHrefPattern = new Regex("(?:src|href)\\s*=\\s*[\"'](?<value>[^\"']+)[\"']", RegexOptions.IgnoreCase);
        foreach (Match match in srcOrHrefPattern.Matches(indexContent))
        {
            var value = match.Groups["value"].Value;
            Assert.DoesNotContain("http://", value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("//cdn", value, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void StaticAssets_DoNotUseUnsafeHtmlInjectionOrRuntimeCodeEvaluation()
    {
        var forbiddenPatterns = new[]
        {
            "innerHTML",
            "outerHTML",
            "insertAdjacentHTML",
            "document.write(",
            "eval(",
            "new Function(",
        };

        foreach (var filePath in EnumerateStaticAssetFiles())
        {
            var content = File.ReadAllText(filePath);
            foreach (var forbiddenPattern in forbiddenPatterns)
            {
                Assert.DoesNotContain(forbiddenPattern, content, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void StaticAssets_DoNotContainSyntheticSecretsOrRealLookingCodexPaths()
    {
        foreach (var filePath in EnumerateStaticAssetFiles())
        {
            var content = File.ReadAllText(filePath);
            SecurityRedactionAssertions.AssertNoSyntheticSecrets(content);

            Assert.DoesNotMatch(@"/home/[^/\s]+/\.codex/", content);
            Assert.DoesNotMatch(@"/Users/[^/\s]+/\.codex/", content);
            Assert.DoesNotMatch(@"[A-Za-z]:\\\\Users\\\\[^\\\\]+\\\\\.codex\\\\", content);
        }
    }

    [Fact]
    public void StaticAssets_DoNotContainTodoOrFixmeMarkers()
    {
        var markerPattern = new Regex("\\b(?:TODO|FIXME)\\b", RegexOptions.IgnoreCase);

        foreach (var filePath in EnumerateStaticAssetFiles())
        {
            var content = File.ReadAllText(filePath);
            Assert.False(markerPattern.IsMatch(content), $"Unexpected TODO/FIXME marker in static asset: {filePath}");
        }
    }

    [Fact]
    public void StaticAssets_DoNotExposeSourceMapOrDebugArtifacts()
    {
        var staticAssetFiles = EnumerateStaticAssetFiles();

        Assert.DoesNotContain(staticAssetFiles, path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase));

        foreach (var filePath in staticAssetFiles)
        {
            var content = File.ReadAllText(filePath);
            Assert.DoesNotContain("sourceMappingURL=", content, StringComparison.Ordinal);
            Assert.DoesNotContain("//# sourceURL=", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppScript_ContainsExplicitPollingOverlapGuardWithoutManualRefreshControls()
    {
        var scriptPath = GetStaticAssetPath(Path.Combine("js", "app.js"));
        var scriptContent = File.ReadAllText(scriptPath);

        Assert.Contains("if (state.pollRequestInFlight)", scriptContent, StringComparison.Ordinal);
        Assert.Contains("state.pollRequestInFlight = true", scriptContent, StringComparison.Ordinal);
        Assert.Contains("state.pollRequestInFlight = false", scriptContent, StringComparison.Ordinal);

        Assert.DoesNotContain("manualRefreshRequestInFlight", scriptContent, StringComparison.Ordinal);
        Assert.DoesNotContain("manual-refresh", scriptContent, StringComparison.Ordinal);
        Assert.Contains("Polling is retrying after a transport failure. Showing the last safe server snapshot.", scriptContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AppScript_PreservesMissingVersusExplicitZeroRenderingRules()
    {
        var scriptPath = GetStaticAssetPath(Path.Combine("js", "app.js"));
        var scriptContent = File.ReadAllText(scriptPath);

        Assert.Contains("normalizedPercent === null ? unavailablePercentText : `${normalizedPercent}%`", scriptContent, StringComparison.Ordinal);
        Assert.Contains("fill.style.width = normalizedPercent === null ? \"0%\" : `${normalizedPercent}%`", scriptContent, StringComparison.Ordinal);
        Assert.Contains("return null;", scriptContent, StringComparison.Ordinal);
        Assert.Contains("const rounded = Math.round(value);", scriptContent, StringComparison.Ordinal);
        Assert.Contains("return Math.max(0, Math.min(100, rounded));", scriptContent, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticAssets_UseDesktopQuotaFillToneRules()
    {
        var scriptPath = GetStaticAssetPath(Path.Combine("js", "app.js"));
        var scriptContent = File.ReadAllText(scriptPath);
        var stylePath = GetStaticAssetPath(Path.Combine("css", "app.css"));
        var styleContent = File.ReadAllText(stylePath);

        Assert.Contains("resolveQuotaFillTone", scriptContent, StringComparison.Ordinal);
        Assert.Contains("calculateUsageGatePercent", scriptContent, StringComparison.Ordinal);
        Assert.Contains("const oldGatePercent = (100 * quotaLeftPercent) / timeLeftPercent;", scriptContent, StringComparison.Ordinal);
        Assert.Contains("const newGatePercent = 100 + (quotaLeftPercent - timeLeftPercent);", scriptContent, StringComparison.Ordinal);
        Assert.Contains("const transitionWeight = (quotaLeftPercent - 5) / 10;", scriptContent, StringComparison.Ordinal);
        Assert.Contains("const redGateThresholdPercent = 70;", scriptContent, StringComparison.Ordinal);
        Assert.Contains("const yellowGateThresholdPercent = 90;", scriptContent, StringComparison.Ordinal);
        Assert.Contains("const blueSurplusGateThresholdPercent = 110;", scriptContent, StringComparison.Ordinal);
        Assert.Contains("const pinkSurplusGateThresholdPercent = 130;", scriptContent, StringComparison.Ordinal);
        Assert.Contains("gatePercent < redGateThresholdPercent", scriptContent, StringComparison.Ordinal);
        Assert.Contains("gatePercent < yellowGateThresholdPercent", scriptContent, StringComparison.Ordinal);
        Assert.Contains("useSurplusFillColors && gatePercent > pinkSurplusGateThresholdPercent", scriptContent, StringComparison.Ordinal);
        Assert.Contains("useSurplusFillColors && gatePercent > blueSurplusGateThresholdPercent", scriptContent, StringComparison.Ordinal);
        Assert.Contains("fill.dataset.tone = resolveQuotaFillTone", scriptContent, StringComparison.Ordinal);

        Assert.Contains("--fill-normal: #18a24a;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--fill-blue: #2563eb;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--fill-pink: #ec4899;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--fill-yellow: #eab308;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--fill-red: #dc2626;", styleContent, StringComparison.Ordinal);
        Assert.DoesNotContain(".usage-row[data-window=\"weekly\"] .meter-fill", styleContent, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticShell_RemovesFullDetailsAndRefreshControls()
    {
        var indexPath = GetStaticAssetPath("index.html");
        var indexContent = File.ReadAllText(indexPath);
        var scriptPath = GetStaticAssetPath(Path.Combine("js", "app.js"));
        var scriptContent = File.ReadAllText(scriptPath);

        Assert.Contains("id=\"status-board\"", indexContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Full Details", indexContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Refresh Controls", indexContent, StringComparison.Ordinal);
        Assert.DoesNotContain("view-full-button", indexContent, StringComparison.Ordinal);
        Assert.DoesNotContain("manual-refresh-button", indexContent, StringComparison.Ordinal);

        Assert.Contains("appendBucketGroup(card, \"main\", profile?.mainBucket)", scriptContent, StringComparison.Ordinal);
        Assert.Contains("appendBucketGroup(card, \"spark\", profile?.sparkBucket)", scriptContent, StringComparison.Ordinal);
        Assert.DoesNotContain("renderFullDetails", scriptContent, StringComparison.Ordinal);
        Assert.DoesNotContain("renderRefreshDetailsSection", scriptContent, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticShell_ProvidesAccessibleThemeSwitchWithEarlyThemeBootstrap()
    {
        var indexPath = GetStaticAssetPath("index.html");
        var indexContent = File.ReadAllText(indexPath);

        Assert.Contains("id=\"theme-toggle\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("type=\"button\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("role=\"switch\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("aria-checked=\"false\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Switch to dark theme\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("id=\"theme-toggle-label\"", indexContent, StringComparison.Ordinal);

        var bootstrapIndex = indexContent.IndexOf("codexWidget.webTheme", StringComparison.Ordinal);
        var stylesheetIndex = indexContent.IndexOf("<link rel=\"stylesheet\" href=\"/css/app.css\">", StringComparison.Ordinal);
        Assert.True(
            bootstrapIndex >= 0 && bootstrapIndex < stylesheetIndex,
            "The saved theme bootstrap must run before the stylesheet to reduce initial theme flash.");
    }

    [Fact]
    public void StaticAssets_PersistWebThemeWithLightDefaultAndDarkPaletteTokens()
    {
        var scriptPath = GetStaticAssetPath(Path.Combine("js", "app.js"));
        var scriptContent = File.ReadAllText(scriptPath);
        var stylePath = GetStaticAssetPath(Path.Combine("css", "app.css"));
        var styleContent = File.ReadAllText(stylePath);

        Assert.Contains("const themeStorageKey = \"codexWidget.webTheme\";", scriptContent, StringComparison.Ordinal);
        Assert.Contains("const lightTheme = \"Light\";", scriptContent, StringComparison.Ordinal);
        Assert.Contains("const darkTheme = \"Dark\";", scriptContent, StringComparison.Ordinal);
        Assert.Contains("window.localStorage.getItem(themeStorageKey) === darkTheme ? darkTheme : lightTheme", scriptContent, StringComparison.Ordinal);
        Assert.Contains("window.localStorage.setItem(themeStorageKey", scriptContent, StringComparison.Ordinal);
        Assert.Contains("elements.themeToggle.addEventListener(\"click\", toggleTheme)", scriptContent, StringComparison.Ordinal);
        Assert.Contains("elements.themeToggle.setAttribute(\"aria-checked\", isDark ? \"true\" : \"false\")", scriptContent, StringComparison.Ordinal);
        Assert.Contains("document.documentElement.dataset.theme", scriptContent, StringComparison.Ordinal);

        Assert.Contains(":root[data-theme=\"Dark\"]", styleContent, StringComparison.Ordinal);
        Assert.Contains("--widget-bg: #151a20;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--widget-card-bg: #1b222b;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--text: #e8eef6;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--active: #78e6a0;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--track: #344253;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--fill-normal: #35d07f;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--fill-blue: #60a5fa;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--fill-pink: #f472b6;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--fill-yellow: #facc15;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--fill-red: #ff6b6b;", styleContent, StringComparison.Ordinal);
        Assert.Contains("--marker: #e8eef6;", styleContent, StringComparison.Ordinal);
        Assert.Contains(".theme-toggle:focus-visible", styleContent, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> EnumerateStaticAssetFiles()
    {
        var root = GetWwwRootDirectory();
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string GetStaticAssetPath(string relativePath)
    {
        return Path.Combine(GetWwwRootDirectory(), relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetWwwRootDirectory()
    {
        return Path.Combine(FindRepositoryRoot(), "src", "CodexWidget.Web", "wwwroot");
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
