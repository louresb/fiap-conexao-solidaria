using System.Xml.Linq;

namespace ConexaoSolidaria.Architecture.Tests;

public sealed class ServiceBoundaryTests
{
    [Fact]
    public void Services_do_not_reference_other_bounded_contexts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var servicesRoot = Path.Combine(repositoryRoot, "src", "Services");
        var violations = new List<string>();

        foreach (var projectFile in Directory.EnumerateFiles(servicesRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var sourceContext = ContextName(servicesRoot, projectFile);
            var document = XDocument.Load(projectFile);
            var references = document.Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value));

            foreach (var reference in references)
            {
                var referencedProject = Path.GetFullPath(reference!, Path.GetDirectoryName(projectFile)!);
                if (!referencedProject.StartsWith(servicesRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var targetContext = ContextName(servicesRoot, referencedProject);
                if (!string.Equals(sourceContext, targetContext, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{Path.GetFileName(projectFile)} -> {Path.GetFileName(referencedProject)}");
                }
            }
        }

        Assert.True(violations.Count == 0, $"Cross-context project references found: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Repository_has_no_generic_building_blocks_folder()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "BuildingBlocks");
        Assert.False(Directory.Exists(path), $"Remove generic shared scaffolding at {path}.");
    }

    [Fact]
    public void Contracts_do_not_reference_runtime_projects()
    {
        var project = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Contracts",
            "ConexaoSolidaria.Contracts",
            "ConexaoSolidaria.Contracts.csproj"));

        Assert.Empty(project.Descendants("ProjectReference"));
    }

    private static string ContextName(string servicesRoot, string projectFile)
    {
        return Path.GetRelativePath(servicesRoot, projectFile)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ConexaoSolidaria.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
