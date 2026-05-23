using System.Reflection;
using System.Xml.Linq;

namespace CodexWidget.Runtime.Tests;

public sealed class PortableDependencyGuardrailTests
{
    [Fact]
    public void RuntimeProject_UsesExpectedPortableProjectReferences()
    {
        var project = LoadProject("src/CodexWidget.Runtime/CodexWidget.Runtime.csproj");
        var projectReferences = GetProjectReferences(project);

        Assert.Contains(projectReferences, include => include.Contains("CodexWidget.Core", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectReferences, include => include.Contains("CodexWidget.Profiles", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectReferences, include => include.Contains("CodexWidget.Usage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectReferences, include => include.Contains("CodexWidget.Status", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectReferences, include => include.Contains("CodexWidget.Presentation", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(projectReferences, include => include.Contains("CodexWidget.App", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectReferences, IsForbiddenReferenceFragment);
    }

    [Fact]
    public void RuntimeProject_DoesNotReferenceDesktopOrWebPackages()
    {
        var project = LoadProject("src/CodexWidget.Runtime/CodexWidget.Runtime.csproj");
        var packageReferences = GetPackageReferences(project);

        Assert.DoesNotContain(packageReferences, IsForbiddenReferencePrefix);
        Assert.DoesNotContain(packageReferences, IsForbiddenReferenceFragment);
    }

    [Fact]
    public void RuntimeAssembly_DoesNotReferenceDesktopOrWebAssemblies()
    {
        var runtimeAssembly = Assembly.Load("CodexWidget.Runtime");
        var referencedAssemblyNames = runtimeAssembly.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();

        Assert.DoesNotContain(referencedAssemblyNames, name => name.Equals("CodexWidget.App", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name.StartsWith("System.Web", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblyNames, name => name.StartsWith("Microsoft.Extensions.Hosting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuntimeSources_DoNotImportDesktopWebOrValidationOnlyCode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtimeSourceDirectory = Path.Combine(repositoryRoot, "src", "CodexWidget.Runtime");
        var sourceFiles = Directory.EnumerateFiles(runtimeSourceDirectory, "*.cs", SearchOption.AllDirectories)
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
            Assert.DoesNotContain("CODEX_WIDGET_VALIDATION_STATE", content, StringComparison.Ordinal);
            Assert.DoesNotContain("ValidationRuntime", content, StringComparison.Ordinal);
            Assert.DoesNotContain("TrayIcon", content, StringComparison.Ordinal);
            Assert.DoesNotContain("NotifyIcon", content, StringComparison.Ordinal);
            Assert.DoesNotContain("WebApplication", content, StringComparison.Ordinal);
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

    private static bool IsForbiddenReferencePrefix(string reference)
    {
        foreach (var prefix in ForbiddenReferencePrefixes)
        {
            if (reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsForbiddenReferenceFragment(string reference)
    {
        foreach (var fragment in ForbiddenReferenceFragments)
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

    private static readonly string[] ForbiddenReferencePrefixes =
    [
        "Avalonia",
        "Microsoft.AspNetCore",
        "Microsoft.JSInterop",
        "Microsoft.NET.Sdk.Web",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Hosting.WindowsServices",
        "Microsoft.Extensions.Hosting.Systemd",
        "Microsoft.WindowsDesktop.App",
        "System.Web",
        "System.Windows.Forms",
    ];

    private static readonly string[] ForbiddenReferenceFragments =
    [
        "CodexWidget.App",
        "Tray",
        "NotifyIcon",
        "Window",
        "WebView",
        "Blazor",
        "Razor",
        "Mvc",
        "Kestrel",
        "Yarp",
        "SignalR",
        "Blazorise",
        "MudBlazor",
        "CODEX_WIDGET_VALIDATION_STATE",
        "ValidationRuntime",
    ];
}
