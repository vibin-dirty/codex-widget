using System.Xml.Linq;

namespace CodexWidget.App.Tests;

public sealed class ProductionProjectDependencyGuardrailTests
{
    [Theory]
    [InlineData("src/CodexWidget.Core/CodexWidget.Core.csproj")]
    [InlineData("src/CodexWidget.Profiles/CodexWidget.Profiles.csproj")]
    [InlineData("src/CodexWidget.Status/CodexWidget.Status.csproj")]
    [InlineData("src/CodexWidget.Usage/CodexWidget.Usage.csproj")]
    public void PortableProductionProjects_DoNotReferenceAvaloniaOrApp(string relativeProjectPath)
    {
        var project = LoadProject(relativeProjectPath);

        Assert.DoesNotContain(GetPackageReferences(project), include => include.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(GetProjectReferences(project), include => include.Contains("CodexWidget.App", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AppProject_IsTheOnlyAvaloniaDependentProductionLayer()
    {
        var appProject = LoadProject("src/CodexWidget.App/CodexWidget.App.csproj");
        var packageReferences = GetPackageReferences(appProject);
        var projectReferences = GetProjectReferences(appProject);

        Assert.Contains(packageReferences, include => include.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectReferences, include => include.Contains("CodexWidget.Core", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectReferences, include => include.Contains("CodexWidget.Profiles", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectReferences, include => include.Contains("CodexWidget.Status", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectReferences, include => include.Contains("CodexWidget.Usage", StringComparison.OrdinalIgnoreCase));

        foreach (var relativeProjectPath in new[]
                 {
                     "src/CodexWidget.Core/CodexWidget.Core.csproj",
                     "src/CodexWidget.Profiles/CodexWidget.Profiles.csproj",
                     "src/CodexWidget.Status/CodexWidget.Status.csproj",
                     "src/CodexWidget.Usage/CodexWidget.Usage.csproj",
                 })
        {
            var project = LoadProject(relativeProjectPath);
            Assert.DoesNotContain(GetPackageReferences(project), include => include.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void ProductionProjectGraph_IsAcyclic()
    {
        var projectReferences = LoadProductionProjectReferenceGraph();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activePath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectName in projectReferences.Keys)
        {
            Assert.False(HasCycle(projectName, projectReferences, visited, activePath), $"Detected project-reference cycle at {projectName}.");
        }
    }

    [Fact]
    public void ProductionProjectGraph_FollowsExpectedLayerDirection()
    {
        var projectReferences = LoadProductionProjectReferenceGraph();

        Assert.Contains("CodexWidget.App", projectReferences.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CodexWidget.Web", projectReferences.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CodexWidget.Runtime", projectReferences.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CodexWidget.Presentation", projectReferences.Keys, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("CodexWidget.Runtime", projectReferences["CodexWidget.App"], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CodexWidget.Presentation", projectReferences["CodexWidget.App"], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CodexWidget.Runtime", projectReferences["CodexWidget.Web"], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CodexWidget.Presentation", projectReferences["CodexWidget.Web"], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CodexWidget.Core", projectReferences["CodexWidget.Runtime"], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CodexWidget.Profiles", projectReferences["CodexWidget.Runtime"], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CodexWidget.Usage", projectReferences["CodexWidget.Runtime"], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CodexWidget.Status", projectReferences["CodexWidget.Runtime"], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CodexWidget.Presentation", projectReferences["CodexWidget.Runtime"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["CodexWidget.Core"], projectReferences["CodexWidget.Presentation"].OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

        Assert.Equal(["CodexWidget.Core", "CodexWidget.Presentation", "CodexWidget.Runtime"], projectReferences["CodexWidget.Web"].OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

        foreach (var sharedProjectName in SharedProjectNames)
        {
            var references = projectReferences[sharedProjectName];
            Assert.DoesNotContain(references, reference => reference.Equals("CodexWidget.App", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(references, reference => reference.Equals("CodexWidget.Web", StringComparison.OrdinalIgnoreCase));
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

    private static Dictionary<string, string[]> LoadProductionProjectReferenceGraph()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var projectPaths = Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var graph = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var projectPath in projectPaths)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var project = XDocument.Load(projectPath);
            var references = project.Descendants("ProjectReference")
                .Select(node => (string?)node.Attribute("Include"))
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => include!)
                .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => include!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            graph[projectName] = references;
        }

        return graph;
    }

    private static bool HasCycle(
        string projectName,
        IReadOnlyDictionary<string, string[]> graph,
        ISet<string> visited,
        ISet<string> activePath)
    {
        if (activePath.Contains(projectName))
        {
            return true;
        }

        if (!visited.Add(projectName))
        {
            return false;
        }

        activePath.Add(projectName);
        if (graph.TryGetValue(projectName, out var references))
        {
            foreach (var reference in references)
            {
                if (graph.ContainsKey(reference) && HasCycle(reference, graph, visited, activePath))
                {
                    return true;
                }
            }
        }

        activePath.Remove(projectName);
        return false;
    }

    private static readonly string[] SharedProjectNames =
    [
        "CodexWidget.Core",
        "CodexWidget.Profiles",
        "CodexWidget.Usage",
        "CodexWidget.Status",
        "CodexWidget.Presentation",
        "CodexWidget.Runtime",
    ];
}
