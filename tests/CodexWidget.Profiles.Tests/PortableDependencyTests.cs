using CodexWidget.Core;
using CodexWidget.Profiles;
using System.Xml.Linq;

namespace CodexWidget.Profiles.Tests;

public sealed class PortableDependencyTests
{
    [Fact]
    public void ProfilesProject_DoesNotReferenceHostSpecificPackagesOrHostProjects()
    {
        var project = LoadProject("src/CodexWidget.Profiles/CodexWidget.Profiles.csproj");
        var packageReferences = project.Descendants("PackageReference")
            .Select(node => (string?)node.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .ToArray();
        var projectReferences = project.Descendants("ProjectReference")
            .Select(node => (string?)node.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .ToArray();

        Assert.DoesNotContain(packageReferences, include => IsForbiddenHostReference(include!));
        Assert.DoesNotContain(projectReferences, include => include!.Contains("CodexWidget.App", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectReferences, include => include!.Contains("CodexWidget.Web", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProfilesAssembly_DoesNotReferenceUiAssemblies()
    {
        var referencedAssemblyNames = typeof(ProfilesAssemblyMarker).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        Assert.DoesNotContain(referencedAssemblyNames, name => name!.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name!.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name!.StartsWith("Microsoft.JSInterop", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name!.StartsWith("System.Web", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("CodexWidget.App", referencedAssemblyNames);
    }

    [Fact]
    public void ProfilesSources_DoNotImportHostSpecificNamespacesOrValidationRuntimeCode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceDirectory = Path.Combine(repositoryRoot, "src", "CodexWidget.Profiles");
        var sourceFiles = Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
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
            Assert.DoesNotContain("CODEX_WIDGET_VALIDATION_STATE", content, StringComparison.Ordinal);
            Assert.DoesNotContain("ValidationRuntime", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProfilesProject_CanConsumeCoreContracts()
    {
        Assert.Equal(TimeSpan.Zero, SystemClock.Instance.UtcNow.Offset);
        Assert.Equal("[redacted]…1234", RedactionHelper.RedactSecret("token-1234", visibleSuffixLength: 4));
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

    private static bool IsForbiddenHostReference(string reference)
    {
        return reference.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("Microsoft.JSInterop", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("System.Web", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("System.Windows.Forms", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("NotifyIcon", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Tray", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("WebView", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Blazor", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Razor", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("ValidationRuntime", StringComparison.OrdinalIgnoreCase);
    }
}
