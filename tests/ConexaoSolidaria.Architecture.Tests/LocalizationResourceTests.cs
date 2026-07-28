using System.Xml.Linq;

namespace ConexaoSolidaria.Architecture.Tests;

public sealed class LocalizationResourceTests
{
    [Fact]
    public void Portuguese_and_English_resources_have_the_same_keys()
    {
        var resources = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Web",
            "ConexaoSolidaria.Web",
            "Resources");

        var portuguese = LoadResources(Path.Combine(resources, "WebResources.resx"));
        var english = LoadResources(Path.Combine(resources, "WebResources.en-US.resx"));

        Assert.Equal(portuguese.Keys.Order(), english.Keys.Order());
    }

    [Fact]
    public void Seeded_campaign_descriptions_are_available_in_English()
    {
        var resourceFile = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Web",
            "ConexaoSolidaria.Web",
            "Resources",
            "WebResources.en-US.resx");
        var resources = LoadResources(resourceFile);

        var campaignDescriptions = resources
            .Where(item => item.Key.StartsWith("CampaignDescription.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(9, campaignDescriptions.Length);
        Assert.All(campaignDescriptions, item => Assert.False(string.IsNullOrWhiteSpace(item.Value)));
    }

    private static IReadOnlyDictionary<string, string> LoadResources(string path)
    {
        return XDocument.Load(path)
            .Descendants("data")
            .ToDictionary(
                element => element.Attribute("name")?.Value
                    ?? throw new InvalidDataException($"Resource without a name in {path}."),
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
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
