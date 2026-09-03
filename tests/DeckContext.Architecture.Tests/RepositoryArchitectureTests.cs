using System.Xml.Linq;

namespace DeckContext.Architecture.Tests;

public sealed class RepositoryArchitectureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Solution_contains_the_baseline_projects()
    {
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot, "DeckContext.sln"));

        var expectedProjects = new[]
        {
            "DeckContext.App",
            "DeckContext.Application",
            "DeckContext.Domain",
            "DeckContext.OpenXml",
            "DeckContext.Export",
            "DeckContext.Architecture.Tests",
            "DeckContext.OpenXml.Tests",
            "DeckContext.Export.Tests",
            "DeckContext.Verification",
        };

        foreach (var project in expectedProjects)
        {
            Assert.Contains($"= \"{project}\"", solution, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Project_references_follow_the_baseline_dependency_direction()
    {
        AssertProjectReferences(
            "src/DeckContext.Domain/DeckContext.Domain.csproj");
        AssertProjectReferences(
            "src/DeckContext.Application/DeckContext.Application.csproj",
            "src/DeckContext.Domain/DeckContext.Domain.csproj");
        AssertProjectReferences(
            "src/DeckContext.OpenXml/DeckContext.OpenXml.csproj",
            "src/DeckContext.Application/DeckContext.Application.csproj",
            "src/DeckContext.Domain/DeckContext.Domain.csproj");
        AssertProjectReferences(
            "src/DeckContext.Export/DeckContext.Export.csproj",
            "src/DeckContext.Domain/DeckContext.Domain.csproj");
        AssertProjectReferences(
            "src/DeckContext.App/DeckContext.App.csproj",
            "src/DeckContext.Application/DeckContext.Application.csproj");
        AssertProjectReferences(
            "tools/DeckContext.Verification/DeckContext.Verification.csproj",
            "src/DeckContext.Export/DeckContext.Export.csproj",
            "src/DeckContext.OpenXml/DeckContext.OpenXml.csproj");
    }

    [Fact]
    public void OpenXml_is_the_only_product_project_with_the_OpenXml_package()
    {
        var productProjects = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .ToArray();

        var packageOwners = productProjects
            .Where(project => ReadItems(project, "PackageReference")
                .Contains("DocumentFormat.OpenXml", StringComparer.Ordinal))
            .Select(ToRepositoryRelativePath)
            .ToArray();

        Assert.Equal(
            ["src/DeckContext.OpenXml/DeckContext.OpenXml.csproj"],
            packageOwners);
    }

    private static void AssertProjectReferences(string projectPath, params string[] expectedReferences)
    {
        var absoluteProjectPath = Path.Combine(
            RepositoryRoot,
            projectPath.Replace('/', Path.DirectorySeparatorChar));

        var actualReferences = ReadItems(absoluteProjectPath, "ProjectReference")
            .Select(reference => reference
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar))
            .Select(reference => Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(absoluteProjectPath)!, reference)))
            .Select(ToRepositoryRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);
    }

    private static IEnumerable<string> ReadItems(string projectPath, string itemName)
    {
        var document = XDocument.Load(projectPath);

        return document
            .Descendants(itemName)
            .Select(item => item.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>();
    }

    private static string ToRepositoryRelativePath(string absolutePath)
    {
        return Path.GetRelativePath(RepositoryRoot, absolutePath).Replace('\\', '/');
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DeckContext.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the DeckContext repository root from the test output directory.");
    }
}
