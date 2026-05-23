using CodexWidget.Core;
using System.Xml.Linq;

namespace CodexWidget.Core.Tests;

public sealed class PortableGuardrailTests
{
    [Theory]
    [InlineData("src/CodexWidget.Core/CodexWidget.Core.csproj")]
    [InlineData("src/CodexWidget.Profiles/CodexWidget.Profiles.csproj")]
    [InlineData("src/CodexWidget.Status/CodexWidget.Status.csproj")]
    [InlineData("src/CodexWidget.Usage/CodexWidget.Usage.csproj")]
    [InlineData("src/CodexWidget.Presentation/CodexWidget.Presentation.csproj")]
    [InlineData("src/CodexWidget.Runtime/CodexWidget.Runtime.csproj")]
    public void PortableProjects_DoNotReferenceHostSpecificPackages(string relativeProjectPath)
    {
        var project = LoadProject(relativeProjectPath);
        var packageReferences = GetPackageReferences(project);
        Assert.DoesNotContain(packageReferences, IsForbiddenHostPackageReference);
    }

    [Theory]
    [InlineData("src/CodexWidget.Core/CodexWidget.Core.csproj")]
    [InlineData("src/CodexWidget.Profiles/CodexWidget.Profiles.csproj")]
    [InlineData("src/CodexWidget.Status/CodexWidget.Status.csproj")]
    [InlineData("src/CodexWidget.Usage/CodexWidget.Usage.csproj")]
    [InlineData("src/CodexWidget.Presentation/CodexWidget.Presentation.csproj")]
    [InlineData("src/CodexWidget.Runtime/CodexWidget.Runtime.csproj")]
    public void PortableProjects_DoNotReferenceCodexWidgetApp(string relativeProjectPath)
    {
        var project = LoadProject(relativeProjectPath);
        var projectReferences = project.Descendants("ProjectReference")
            .Select(node => (string?)node.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .ToArray();

        Assert.DoesNotContain(projectReferences, include => include!.Contains("CodexWidget.App", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CoreAssemblyReferences_DoNotIncludeUiDependencies()
    {
        var referencedAssemblyNames = typeof(IClock).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        Assert.DoesNotContain(referencedAssemblyNames, name => name!.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("CodexWidget.App", referencedAssemblyNames);
    }

    [Theory]
    [InlineData("src/CodexWidget.Core")]
    [InlineData("src/CodexWidget.Profiles")]
    [InlineData("src/CodexWidget.Status")]
    [InlineData("src/CodexWidget.Usage")]
    [InlineData("src/CodexWidget.Presentation")]
    [InlineData("src/CodexWidget.Runtime")]
    public void PortableProjectSources_DoNotImportUiNamespaces(string relativeProjectDirectory)
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectDirectory = Path.Combine(repositoryRoot, relativeProjectDirectory.Replace('/', Path.DirectorySeparatorChar));
        var sourceFiles = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var sourcePath in sourceFiles)
        {
            var content = File.ReadAllText(sourcePath);
            Assert.DoesNotContain("using Avalonia", content, StringComparison.Ordinal);
            Assert.DoesNotContain("using Microsoft.AspNetCore", content, StringComparison.Ordinal);
            Assert.DoesNotContain("using Microsoft.JSInterop", content, StringComparison.Ordinal);
            Assert.DoesNotContain("using System.Web", content, StringComparison.Ordinal);
            Assert.DoesNotContain("using System.Windows.Forms", content, StringComparison.Ordinal);
            Assert.DoesNotContain("CodexWidget.App", content, StringComparison.Ordinal);
            Assert.DoesNotContain("CODEX_WIDGET_VALIDATION_STATE", content, StringComparison.Ordinal);
            Assert.DoesNotContain("ValidationRuntime", content, StringComparison.Ordinal);
        }
    }

    private static IReadOnlyList<string> GetPackageReferences(XDocument project)
    {
        return project.Descendants("PackageReference")
            .Select(node => (string?)node.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToArray();
    }

    private static bool IsForbiddenHostPackageReference(string packageReference)
    {
        foreach (var prefix in ForbiddenHostPackagePrefixes)
        {
            if (packageReference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var fragment in ForbiddenHostPackageFragments)
        {
            if (packageReference.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static XDocument LoadProject(string relativeProjectPath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
        return XDocument.Load(projectPath);
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

    private static readonly string[] ForbiddenHostPackagePrefixes =
    [
        "Avalonia",
        "Microsoft.AspNetCore",
        "Microsoft.JSInterop",
        "Microsoft.NET.Sdk.Web",
        "Microsoft.WindowsDesktop.App",
        "System.Web",
        "System.Windows.Forms",
    ];

    private static readonly string[] ForbiddenHostPackageFragments =
    [
        "NotifyIcon",
        "Tray",
        "WebView",
        "Blazor",
        "Razor",
        "Mvc",
        "Kestrel",
        "WinForms",
        "Wpf",
        "WindowsDesktop",
        "Maui",
        "Gtk",
        "ValidationRuntime",
    ];
}

public sealed class CoreHelperSmokeTests
{
    [Fact]
    public void SystemClock_UsesUtcOffset()
    {
        Assert.Equal(TimeSpan.Zero, SystemClock.Instance.UtcNow.Offset);
    }

    [Fact]
    public void RedactionHelper_RedactsAndPreservesSuffix()
    {
        Assert.Equal("[redacted]…7890", RedactionHelper.RedactSecret("abc1234567890", visibleSuffixLength: 4));
    }

    [Fact]
    public void RedactionHelper_RedactsShortOrMissingValues()
    {
        Assert.Equal("[redacted]", RedactionHelper.RedactSecret("abcd", visibleSuffixLength: 4));
        Assert.Equal("[redacted]", RedactionHelper.RedactSecret(null));
    }

}
