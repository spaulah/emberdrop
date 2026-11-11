using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Emberdrop.Plugins.Tests.Support;
using FluentAssertions;
using Fixture.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.Plugins.Tests;

/// <summary>Stored work units refer to a plugin, a version constraint and a logical work unit name. These tests check how that resolves against what is loaded.</summary>
public class WorkUnitBindingShould
{
    private const string Plugin = "fixture.behaviors";

    private static string NewName() => Guid.NewGuid().ToString("N");

    private static Task<WorkExecutionResult> Run(PluginHost host, string pluginId, string workUnit, string constraint, string metadata, int schemaVersion = 1) =>
        host.ExecuteWorkUnitAsync(new WorkUnitBinding(pluginId, workUnit, PluginVersionConstraint.Parse(constraint)), new WorkRequest(metadata), schemaVersion);

    private static async Task<(PluginHost Host, TempDirectory Temp)> BothVersionsAsync()
    {
        var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v2"));
        return (host, temp);
    }

    [Theory]
    [InlineData("*", "2.0.0")]
    [InlineData("", "2.0.0")]
    [InlineData("^1", "1.0.0")]
    [InlineData("^2", "2.0.0")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("2.0.0", "2.0.0")]
    public async Task RunOnTheVersionTheConstraintSelects(string constraint, string expected)
    {
        var (host, temp) = await BothVersionsAsync();
        using (temp)
        {
            var id = NewName();

            var result = await Run(host, Plugin, "count", constraint, id);

            result.Executed.Should().BeTrue(result.NotExecutedReason);
            FixtureControl.VersionOf(id).Should().Be(expected);
        }
    }

    [Theory]
    [InlineData("fixture.absent", "count", "*", "Plugin 'fixture.absent' is not loaded.")]
    [InlineData(Plugin, "count", "3.0.0", "No loaded version of plugin 'fixture.behaviors' satisfies 3.0.0; loaded: ")]
    [InlineData(Plugin, "count", "^3", "No loaded version of plugin 'fixture.behaviors' satisfies ^3.0.0; loaded: ")]
    [InlineData(Plugin, "no-such-work-unit", "*", "do not provide a work unit named 'no-such-work-unit'")]
    public async Task SayExactlyWhyNothingRanWhenNoLoadedVersionMatches(string pluginId, string workUnit, string constraint, string expectedReason)
    {
        var (host, temp) = await BothVersionsAsync();
        using (temp)
        {
            var id = NewName();

            var result = await Run(host, pluginId, workUnit, constraint, id);

            result.Executed.Should().BeFalse();
            result.NotExecutedReason.Should().Contain(expectedReason);
            FixtureControl.CountOf(id).Should().Be(0, "nothing ran");
        }
    }

    [Fact]
    public async Task FindAWorkStoredWithoutAPluginByItsNameTypeOrClassName()
    {
        var (host, temp) = await BothVersionsAsync();
        using (temp)
        {
            foreach (var key in new[] { "count", "Fixture.Behaviors.CountWorkUnit", "CountWorkUnit" })
            {
                var id = NewName();

                var result = await host.ExecuteWorkUnitAsync(new WorkUnitBinding(null, key, PluginVersionConstraint.Latest), new WorkRequest(id));

                result.Executed.Should().BeTrue(key);
                FixtureControl.VersionOf(id).Should().Be("2.0.0");
            }
        }
    }

    [Fact]
    public async Task FailBeforeStartingWhenTheStoredInputHasADifferentSchemaVersion()
    {
        var (host, temp) = await BothVersionsAsync();
        using (temp)
        {
            var id = NewName();

            // "schema2" declares inputSchemaVersion 2 in its manifest; the stored work unit was written for version 1.
            var act = () => Run(host, Plugin, "schema2", "*", id, schemaVersion: 1);

            var message = (await act.Should().ThrowAsync<InputSchemaMismatchException>()).Which.Message;
            message.Should().Contain("schema version 2").And.Contain("schema version 1").And.Contain("schema2").And.Contain("2.0.0").And.Contain("was not started");
            FixtureControl.IsMarked("schema2-ran:" + id).Should().BeFalse();
        }
    }

    [Fact]
    public async Task RunWhenTheSchemaVersionsAgree()
    {
        var (host, temp) = await BothVersionsAsync();
        using (temp)
        {
            var id = NewName();

            var result = await Run(host, Plugin, "schema2", "*", id, schemaVersion: 2);

            result.Executed.Should().BeTrue();
            FixtureControl.IsMarked("schema2-ran:" + id).Should().BeTrue();
        }
    }

    [Fact]
    public async Task ResolveWithoutRunningAnythingWhenAskedToDescribeAWorkUnit()
    {
        var (host, temp) = await BothVersionsAsync();
        using (temp)
        {
            var hostServices = new ServiceCollection();
            hostServices.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Fixture:Setting"] = "x" }).Build());
            var resolver = new WorkUnitBindingResolver(
                host,
                new PluginServiceProviderFactory(hostServices.BuildServiceProvider(), new PluginHostOptions()),
                [new FakeHostWorkUnits("BuiltIn")]);

            Describe(resolver, null, "BuiltIn", "").State.Should().Be(WorkUnitBindingState.Ready, "a host work unit is always ready");
            var count = Describe(resolver, Plugin, "count", "^1");
            (count.State, count.ResolvedVersion).Should().Be((WorkUnitBindingState.Ready, PluginVersion.Parse("1.0.0")));
            count.DeclaredTriggers.Should().Equal(new RecurringTrigger("0 1 * * *"));
            Describe(resolver, Plugin, "service", "*").State.Should().Be(WorkUnitBindingState.Ready, "its required setting is configured");
            Describe(resolver, Plugin, "count", "9.9.9").State.Should().Be(WorkUnitBindingState.WaitingForPlugin);
            Describe(resolver, Plugin, "count", "9.9.9").Reason.Should().Contain("satisfies 9.9.9").And.Contain("2.0.0, 1.0.0");
            Describe(resolver, Plugin, "count", "garbage").Reason.Should().Contain("invalid");
            Describe(resolver, Plugin, "schema2", "*", schemaVersion: 1).State.Should().Be(WorkUnitBindingState.InputSchemaMismatch);
        }
    }

    private static WorkUnitBindingStatus Describe(WorkUnitBindingResolver resolver, string? pluginId, string workUnit, string constraint, int schemaVersion = 1) =>
        resolver.Describe(new Storage.WorkUnitDefinition { WorkUnitId = 1, PluginId = pluginId, WorkUnitName = workUnit, Type = workUnit, VersionConstraint = constraint, InputSchemaVersion = schemaVersion });
}
