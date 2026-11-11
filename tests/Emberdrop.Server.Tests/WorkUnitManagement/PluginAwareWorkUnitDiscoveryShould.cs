using FluentAssertions;
using Emberdrop.WorkUnits;
using Emberdrop.Plugins;
using Emberdrop.Server.WorkUnitManagement;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Emberdrop.Server.Tests.WorkUnits;

public class PluginAwareWorkUnitDiscoveryShould
{
    private static PluginDescriptor Plugin(string id, string version, params string[] typeNames) =>
        new(id, PluginVersion.Parse(version), ["Plugin.dll"],
            typeNames.Select(t =>
            {
                var name = t.Split('.').Last().ToLowerInvariant();
                return new ExportDescriptor(name, "work-unit", t, "Plugin", new PluginWorkUnitDescriptor(name, t, "Plugin", null, []));
            }).ToList());

    private static WorkUnitDiscoveryService Discovery(params PluginDescriptor[] plugins)
    {
        var catalog = new Mock<IPluginCatalog>();
        catalog.Setup(c => c.GetPlugins()).Returns(plugins);
        return new WorkUnitDiscoveryService(catalog.Object, NullLogger<WorkUnitDiscoveryService>.Instance);
    }

    [Fact]
    public async Task ListNothingWhenNoPluginIsLoaded()
    {
        var workUnits = await Discovery().DiscoverWorkUnitsAsync();

        workUnits.Should().BeEmpty();
    }

    [Fact]
    public async Task ListPluginWorkUnitsWithTheirPluginAndShortName()
    {
        var workUnits = await Discovery(Plugin("acme.reports", "1.0.0", "Acme.Reports.NightlyWorkUnit")).DiscoverWorkUnitsAsync();

        workUnits.Should().Contain(new DiscoveredWorkUnit("NightlyWorkUnit", "Acme.Reports.NightlyWorkUnit", "Plugin", "acme.reports"));
    }

    [Fact]
    public async Task ListAWorkUnitOnceEvenWhenSeveralVersionsOfItsPluginAreLoaded()
    {
        var workUnits = await Discovery(
            Plugin("acme.reports", "1.0.0", "Acme.Reports.NightlyWorkUnit"),
            Plugin("acme.reports", "2.0.0", "Acme.Reports.NightlyWorkUnit")).DiscoverWorkUnitsAsync();

        workUnits.Count(j => j.TypeName == "Acme.Reports.NightlyWorkUnit").Should().Be(1);
    }

    [Fact]
    public async Task KeepWorkUnitsOfDifferentPluginsApartEvenWhenTheTypeNamesMatch()
    {
        var workUnits = await Discovery(
            Plugin("a", "1.0.0", "Shared.SameName"),
            Plugin("b", "1.0.0", "Shared.SameName")).DiscoverWorkUnitsAsync();

        workUnits.Where(j => j.TypeName == "Shared.SameName").Select(j => j.PluginId).Should().BeEquivalentTo("a", "b");
    }
}
