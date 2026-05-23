using System.Reflection;
using System.Xml.Linq;

namespace CodexWidget.Web.Tests;

public sealed class WebDependencyGuardrailTests
{
    [Fact]
    public void WebProject_ReferencesExpectedSharedProjectsOnly()
    {
        var project = LoadProject("src/CodexWidget.Web/CodexWidget.Web.csproj");
        var projectReferences = GetProjectReferences(project);

        Assert.Contains(projectReferences, include => include.Contains("CodexWidget.Core", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectReferences, include => include.Contains("CodexWidget.Presentation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectReferences, include => include.Contains("CodexWidget.Runtime", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(projectReferences, include => include.Contains("CodexWidget.App", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectReferences, IsForbiddenDesktopReferenceFragment);
    }

    [Fact]
    public void WebProject_DoesNotReferenceDesktopPackages()
    {
        var project = LoadProject("src/CodexWidget.Web/CodexWidget.Web.csproj");
        var packageReferences = GetPackageReferences(project);

        Assert.DoesNotContain(packageReferences, include => include.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packageReferences, include => include.StartsWith("System.Windows.Forms", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packageReferences, include => include.StartsWith("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packageReferences, IsForbiddenDesktopReferenceFragment);
    }

    [Fact]
    public void WebAssembly_DoesNotReferenceDesktopAssemblies()
    {
        var webAssembly = Assembly.Load("CodexWidget.Web");
        var referencedAssemblyNames = webAssembly.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();

        Assert.DoesNotContain(referencedAssemblyNames, name => name.Equals("CodexWidget.App", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name.StartsWith("System.Windows.Forms", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name.StartsWith("Microsoft.JSInterop", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, IsForbiddenDesktopReferenceFragment);
    }

    [Fact]
    public void WebSources_DoNotImportDesktopValidationRuntimeCode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var webSourceDirectory = Path.Combine(repositoryRoot, "src", "CodexWidget.Web");
        var sourceFiles = Directory.EnumerateFiles(webSourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var sourcePath in sourceFiles)
        {
            var content = File.ReadAllText(sourcePath);
            Assert.DoesNotContain("CODEX_WIDGET_VALIDATION_STATE", content, StringComparison.Ordinal);
            Assert.DoesNotContain("ValidationRuntime", content, StringComparison.Ordinal);
            Assert.DoesNotContain("CodexWidget.App", content, StringComparison.Ordinal);
            Assert.DoesNotContain("using Avalonia", content, StringComparison.Ordinal);
            Assert.DoesNotContain("using System.Windows.Forms", content, StringComparison.Ordinal);
            Assert.DoesNotContain("TrayIcon", content, StringComparison.Ordinal);
            Assert.DoesNotContain("NotifyIcon", content, StringComparison.Ordinal);
        }
    }

    private static IReadOnlyList<string> GetProjectReferences(XDocument project)
    {
        return project.Descendants("ProjectReference")
            .Select(node => (string?)node.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToArray();
    }

    private static IReadOnlyList<string> GetPackageReferences(XDocument project)
    {
        return project.Descendants("PackageReference")
            .Select(node => (string?)node.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToArray();
    }

    private static bool IsForbiddenDesktopReferenceFragment(string reference)
    {
        foreach (var fragment in ForbiddenDesktopReferenceFragments)
        {
            if (reference.Contains(fragment, StringComparison.OrdinalIgnoreCase))
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

    private static readonly string[] ForbiddenDesktopReferenceFragments =
    [
        "CodexWidget.App",
        "Tray",
        "NotifyIcon",
        "Window",
        "Desktop",
        "ValidationRuntime",
        "System.Windows.Forms",
        "Hardcodet",
        "H.NotifyIcon",
        "WebView",
    ];
}
