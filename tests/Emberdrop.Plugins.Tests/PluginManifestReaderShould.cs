using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using FluentAssertions;

namespace Emberdrop.Plugins.Tests;

public class PluginManifestReaderShould
{
    private const string Valid = """
        {
          "schemaVersion": 1,
          "id": "acme.reports",
          "version": "1.2.3-beta.1",
          "description": "Nightly reports",
          "entryAssemblies": [ "Acme.Reports.dll", "lib/Acme.More.dll" ],
          "minAbstractionsVersion": "1.0.0",
          "workUnits": [
            { "name": "nightly", "type": "Acme.Reports.NightlyWorkUnit", "kind": "work-unit", "description": "Builds the nightly report", "inputType": "Acme.Reports.ReportRequest", "outputType": "Acme.Reports.ReportResult",
              "triggers": [ { "kind": "recurring", "cron": "0 2 * * *" }, { "kind": "manual" } ],
              "capabilities": [ "network:smtp", "service:Acme.Shared.IClock" ],
              "requiredConfiguration": [ "Reports:Target", "Reports:Target" ] },
            { "name": "weekly", "type": "Acme.Reports.WeeklyWorkUnit" }
          ]
        }
        """;

    [Fact]
    public void ReadAValidManifest()
    {
        var result = PluginManifestReader.Parse(Valid, PluginWorkUnitManifest.Format);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        var manifest = result.Manifest!;
        var workUnits = PluginWorkUnitManifest.OfPackage(manifest);
        manifest.Id.Should().Be("acme.reports");
        manifest.Version.Should().Be(PluginVersion.Parse("1.2.3-beta.1"));
        manifest.EntryAssemblies.Should().Equal("Acme.Reports.dll", "lib/Acme.More.dll");
        manifest.MinAbstractionsVersion.Should().Be(PluginVersion.Parse("1.0.0"));
        workUnits.Should().HaveCount(2);
        workUnits[0].DefaultSchedule.Should().Be("0 2 * * *");
        workUnits[0].RequiredConfiguration.Should().Equal("Reports:Target");
        workUnits[0].Kind.Should().Be("work-unit");
        workUnits[0].Description.Should().Be("Builds the nightly report");
        (workUnits[0].InputType, workUnits[0].OutputType).Should().Be(("Acme.Reports.ReportRequest", "Acme.Reports.ReportResult"));
        workUnits[0].Triggers.Should().HaveCount(2);
        workUnits[0].Capabilities.Should().Equal("network:smtp", "service:Acme.Shared.IClock");
        workUnits[1].DefaultSchedule.Should().BeNull();
        workUnits[1].Kind.Should().Be("work-unit", "work-unit is the default kind");
        workUnits[1].Triggers.Should().BeEmpty();
        workUnits[1].Capabilities.Should().BeEmpty();
        workUnits[1].RequiredConfiguration.Should().BeEmpty();
    }

    [Fact]
    public void AcceptCommentsAndTrailingCommasBecauseHumansWriteManifests()
    {
        var json = Valid.Replace("\"id\"", "// the id\n \"id\"").Replace("\"type\": \"Acme.Reports.WeeklyWorkUnit\" }", "\"type\": \"Acme.Reports.WeeklyWorkUnit\", }");

        PluginManifestReader.Parse(json, PluginWorkUnitManifest.Format).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("""{ "not": "json" """, "not valid JSON")]
    [InlineData("[]", "must be a JSON object")]
    [InlineData("{}", "'schemaVersion' is required")]
    public void RejectMalformedDocuments(string json, string expected)
    {
        var result = PluginManifestReader.Parse(json, PluginWorkUnitManifest.Format);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 2", "supports 1")]
    [InlineData("\"id\": \"acme.reports\"", "\"id\": \"Acme Reports\"", "'id' value 'Acme Reports' is invalid")]
    [InlineData("\"id\": \"acme.reports\"", "\"id\": \"\"", "'id' is required")]
    [InlineData("\"version\": \"1.2.3-beta.1\"", "\"version\": \"1.2\"", "'version' value '1.2' is not a valid version")]
    [InlineData("\"minAbstractionsVersion\": \"1.0.0\"", "\"minAbstractionsVersion\": \"latest\"", "'minAbstractionsVersion' value 'latest'")]
    [InlineData("\"Acme.Reports.dll\"", "\"../Acme.Reports.dll\"", "must be a relative path")]
    [InlineData("\"Acme.Reports.dll\"", "\"/abs/Acme.Reports.dll\"", "must be a relative path")]
    [InlineData("\"Acme.Reports.dll\"", "\"Acme.Reports.exe\"", "must be a relative path")]
    [InlineData("\"Acme.Reports.dll\"", "\"Acme.More.dll\"", "listed twice")]
    [InlineData("\"name\": \"weekly\"", "\"name\": \"nightly\"", "declared twice")]
    [InlineData("\"name\": \"weekly\"", "\"name\": \"bad name!\"", "workUnits[1].name 'bad name!' is invalid")]
    [InlineData("\"type\": \"Acme.Reports.WeeklyWorkUnit\"", "\"type\": \"not a type\"", "not a valid CLR type name")]
    [InlineData("\"kind\": \"work-unit\"", "\"kind\": \"Task!\"", "workUnits[0].kind 'Task!' is invalid")]
    [InlineData("\"inputType\": \"Acme.Reports.ReportRequest\"", "\"inputType\": \"not a type\"", "inputType 'not a type' is not a valid CLR type name")]
    [InlineData("{ \"kind\": \"manual\" }", "{ \"cron\": \"0 2 * * *\" }", "triggers[1] must be an object with a 'kind'")]
    [InlineData("\"network:smtp\"", "\"has spaces\"", "capabilities entry 'has spaces' is invalid")]
    public void RejectInvalidFieldsAndSayWhichOneAndWhy(string find, string replace, string expected)
    {
        // "Acme.More.dll" is also referenced by a lib/ path in the base manifest; the duplicate case needs the swapped name to collide.
        var json = find == "\"Acme.Reports.dll\"" && replace == "\"Acme.More.dll\""
            ? Valid.Replace("lib/Acme.More.dll", "Acme.More.dll")
            : Valid;
        json = json.Replace(find, replace);

        var result = PluginManifestReader.Parse(json, PluginWorkUnitManifest.Format);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains(expected, StringComparison.Ordinal), string.Join(" | ", result.Errors));
    }

    [Fact]
    public void RejectAManifestWithNoWorkUnitsOrNoEntryAssemblies()
    {
        var json = """
            { "schemaVersion": 1, "id": "x", "version": "1.0.0", "minAbstractionsVersion": "1.0.0", "entryAssemblies": [], "workUnits": [] }
            """;

        var result = PluginManifestReader.Parse(json, PluginWorkUnitManifest.Format);

        result.Errors.Should().Contain(e => e.Contains("at least one assembly", StringComparison.Ordinal));
        result.Errors.Should().Contain(e => e.Contains("at least one work unit", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportEveryProblemAtOnce()
    {
        var result = PluginManifestReader.Parse("""{ "schemaVersion": 7, "id": "BAD", "version": "x", "minAbstractionsVersion": "y", "entryAssemblies": [], "workUnits": [] }""", PluginWorkUnitManifest.Format);

        result.Errors.Count.Should().BeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public async Task ReportAMissingManifestFile()
    {
        using var temp = new Support.TempDirectory();

        var result = await PluginManifestReader.ReadFromDirectoryAsync(temp.Path);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("emberdrop.json");
    }

    [Fact]
    public async Task ReadTheManifestOfAPublishedFixture()
    {
        var result = await PluginManifestReader.ReadFromDirectoryAsync(Support.TestPlugins.FixturePath("hello-v2"), PluginWorkUnitManifest.Format);

        result.Manifest!.Id.Should().Be("fixture.hello");
        result.Manifest.Version.Should().Be(PluginVersion.Parse("2.0.0"));
    }

    [Fact]
    public void KeepTheKindsOfDeclaredTriggersForTheHostToInterpret()
    {
        var manifest = PluginManifestReader.Parse(Valid, PluginWorkUnitManifest.Format).Manifest!;

        var kinds = PluginWorkUnitManifest.OfPackage(manifest)[0].Triggers.Select(t => t.GetProperty("kind").GetString());

        kinds.Should().Equal("recurring", "manual");
    }
}
