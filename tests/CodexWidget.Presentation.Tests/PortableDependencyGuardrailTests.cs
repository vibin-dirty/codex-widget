using System.Reflection;
using System.Xml.Linq;

namespace CodexWidget.Presentation.Tests;

public sealed class PortableDependencyGuardrailTests
{
    [Fact]
    public void PresentationProject_ReferencesOnlyCoreAndPortablePackages()
    {
        var project = LoadProject("src/CodexWidget.Presentation/CodexWidget.Presentation.csproj");
        var packageReferences = GetPackageReferences(project);
        var projectReferences = GetProjectReferences(project);

        Assert.DoesNotContain(projectReferences, include => include.Contains("CodexWidget.App", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectReferences, include => include.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectReferences, include => include.Contains("Web", StringComparison.OrdinalIgnoreCase));
        Assert.All(projectReferences, include => Assert.Contains("CodexWidget.Core", include, StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(packageReferences, IsForbiddenPackageReference);
    }

    [Fact]
    public void PresentationAssembly_DoesNotReferenceDesktopOrWebAssemblies()
    {
        var presentationAssembly = Assembly.Load("CodexWidget.Presentation");
        var referencedAssemblyNames = presentationAssembly.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();

        Assert.DoesNotContain(referencedAssemblyNames, name => name.Equals("CodexWidget.App", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name.StartsWith("System.Web", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name.StartsWith("Microsoft.JSInterop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PresentationSources_DoNotImportHostSpecificNamespacesOrValidationRuntime()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceDirectory = Path.Combine(repositoryRoot, "src", "CodexWidget.Presentation");
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
            Assert.DoesNotContain("using CodexWidget.App", content, StringComparison.Ordinal);
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

    private static IReadOnlyList<string> GetProjectReferences(XDocument project)
    {
        return project.Descendants("ProjectReference")
            .Select(node => (string?)node.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToArray();
    }

    private static bool IsForbiddenPackageReference(string packageReference)
    {
        foreach (var prefix in ForbiddenPackagePrefixes)
        {
            if (packageReference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var fragment in ForbiddenPackageFragments)
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

    private static readonly string[] ForbiddenPackagePrefixes =
    [
        "Avalonia",
        "Microsoft.AspNetCore",
        "Microsoft.NET.Sdk.Web",
        "Microsoft.Extensions.WebEncoders",
        "Microsoft.JSInterop",
        "Microsoft.WindowsDesktop.App",
        "System.Windows.Forms",
        "System.Web",
    ];

    private static readonly string[] ForbiddenPackageFragments =
    [
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
        "NotifyIcon",
        "Tray",
        "ValidationRuntime",
    ];
}
