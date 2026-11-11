using System.Text.Json;
using FluentAssertions;

namespace Emberdrop.Plugins.Tests;

/// <summary>
/// The published manifest schema (docs/manifest.schema.json) must describe what the reader accepts. There is no JSON Schema validator
/// in the build, so this checks the structure: the schema parses, names exactly the properties the reader understands, and the
/// manifests the repository ships (sample plugin, test fixtures) only use properties it names.
/// </summary>
public class ManifestSchemaShould
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "emberdrop.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("emberdrop.sln was not found above " + AppContext.BaseDirectory);
    }

    private static JsonElement Schema()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "manifest.schema.json")));
        return document.RootElement.Clone();
    }

    private static IEnumerable<string> Names(JsonElement properties) => properties.EnumerateObject().Select(p => p.Name);

    [Fact]
    public void NameTheTopLevelPropertiesTheReaderUnderstands()
    {
        Names(Schema().GetProperty("properties")).Should().BeEquivalentTo("schemaVersion", "id", "version", "description", "minAbstractionsVersion", "entryAssemblies", "startup", "workUnits");
    }

    [Fact]
    public void NameTheWorkUnitPropertiesTheReaderUnderstands()
    {
        Names(Schema().GetProperty("$defs").GetProperty("workUnit").GetProperty("properties")).Should().BeEquivalentTo(
            "name", "type", "kind", "description", "inputType", "outputType", "triggers", "requiredConfiguration", "capabilities", "inputSchemaVersion");
    }

    [Fact]
    public void RequireWhatTheReaderRequires()
    {
        var schema = Schema();

        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(
            "schemaVersion", "id", "version", "minAbstractionsVersion", "entryAssemblies", "workUnits");
        schema.GetProperty("$defs").GetProperty("workUnit").GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo("name", "type");
    }

    [Fact]
    public void CoverEveryManifestTheRepositoryShips()
    {
        var schema = Schema();
        var top = Names(schema.GetProperty("properties")).ToHashSet();
        var workUnit = Names(schema.GetProperty("$defs").GetProperty("workUnit").GetProperty("properties")).ToHashSet();
        var manifests = Directory.EnumerateFiles(RepositoryRoot(), "emberdrop.json", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains("node_modules"))
            .ToList();

        manifests.Should().HaveCountGreaterThan(5);
        foreach (var path in manifests)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            Names(document.RootElement).Should().BeSubsetOf(top, $"{path} uses only properties the schema names");
            foreach (var unit in document.RootElement.GetProperty("workUnits").EnumerateArray())
            {
                Names(unit).Should().BeSubsetOf(workUnit, $"{path} uses only work unit properties the schema names");
            }
        }
    }
}
