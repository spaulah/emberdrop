using System.Runtime.CompilerServices;
using System.Text.Json;
using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Emberdrop.Plugins.Tests.Support;
using FluentAssertions;

namespace Emberdrop.Plugins.Tests;

/// <summary>Manifest-declared kinds, triggers and capabilities, generated schemas, and strict typed input, proven against real compiled plugins.</summary>
public class TypedWorkUnitsShould
{
    private const string Plugin = "fixture.behaviors";

    private static WorkUnitBinding Echo => new(Plugin, "echo", PluginVersionConstraint.Latest);

    private static async Task<PluginHost> HostWithBehaviorsAsync(TempDirectory temp, Action<PluginHostOptions>? configure = null)
    {
        var (host, _) = TestPlugins.CreateHost(temp, configure);
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        return host;
    }

    private static PluginWorkUnitDescriptor DescriptorOf(PluginHost host, string name) =>
        host.GetPlugins().Single(p => p.Id == Plugin).WorkUnits().Single(w => w.Name == name);

    [Fact]
    public async Task DescribeAWorkUnitFromItsManifestAndItsTypes()
    {
        using var temp = new TempDirectory();
        var host = await HostWithBehaviorsAsync(temp);

        var echo = DescriptorOf(host, "echo");

        echo.Kind.Should().Be("work-unit");
        echo.Description.Should().Be("Echoes a greeting");
        echo.Capabilities.Should().Equal("network:smtp");
        echo.Triggers.Should().Equal(ManualTrigger.Instance, new RecurringTrigger("30 4 * * *"));
        echo.DefaultSchedule.Should().Be("30 4 * * *", "the first recurring trigger is the default schedule");
        (echo.InputTypeName, echo.OutputTypeName).Should().Be(("Fixture.Behaviors.EchoInput", "Fixture.Behaviors.EchoOutput"));
    }

    [Fact]
    public async Task GenerateJsonSchemasForTheInputAndOutputTypes()
    {
        using var temp = new TempDirectory();
        var host = await HostWithBehaviorsAsync(temp);

        var echo = DescriptorOf(host, "echo");

        using var input = JsonDocument.Parse(echo.InputSchema!);
        input.RootElement.GetRawText().Should().Contain("\"object\"");
        input.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("name", "times");
        input.RootElement.GetProperty("additionalProperties").ValueKind.Should().Be(JsonValueKind.False, "input is read strictly");
        using var output = JsonDocument.Parse(echo.OutputSchema!);
        output.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().Equal("greeting");
    }

    [Fact]
    public async Task GiveAnUntypedWorkUnitNoTypesAndNoSchemas()
    {
        using var temp = new TempDirectory();
        var host = await HostWithBehaviorsAsync(temp);

        var count = DescriptorOf(host, "count");

        (count.InputTypeName, count.OutputTypeName, count.InputSchema, count.OutputSchema).Should().Be((null, null, null, null));
        count.Kind.Should().Be("work-unit");
        count.Description.Should().BeNull();
        count.Capabilities.Should().BeEmpty();
        count.Triggers.Should().Equal(new RecurringTrigger("0 1 * * *"));
    }

    [Fact]
    public async Task RunATypedWorkUnitAndReturnItsOutputAsJson()
    {
        using var temp = new TempDirectory();
        var host = await HostWithBehaviorsAsync(temp);

        var result = await host.ExecuteWorkUnitAsync(Echo, new WorkRequest("""{"name":"Ada","times":2}""", ManualTrigger.Instance));

        result.Executed.Should().BeTrue(result.NotExecutedReason);
        result.Result!.IsSuccess.Should().BeTrue();
        result.Result.OutputJson.Should().Be("""{"greeting":"hello Ada from v1 hello Ada from v1"}""");
    }

    [Theory]
    [InlineData(null, "requires input of type EchoInput, but none was given")]
    [InlineData("""{"name":"Ada","times":"two"}""", "not a valid EchoInput")]
    [InlineData("""{"name":"Ada","times":1,"surprise":true}""", "surprise")]
    [InlineData("not json", "not a valid EchoInput")]
    [InlineData("null", "is JSON null")]
    public async Task RefuseInputThatDoesNotFitBeforeTheWorkUnitStarts(string? input, string expected)
    {
        using var temp = new TempDirectory();
        var host = await HostWithBehaviorsAsync(temp);

        var act = () => host.ExecuteWorkUnitAsync(Echo, new WorkRequest(input));

        (await act.Should().ThrowAsync<WorkInputException>()).WithMessage($"*'echo'*{expected}*");
        host.ValidateInput(Echo, input).Should().Contain(expected, "the API checks the same way before accepting the input");
    }

    [Fact]
    public async Task AcceptInputThatFits()
    {
        using var temp = new TempDirectory();
        var host = await HostWithBehaviorsAsync(temp);

        host.ValidateInput(Echo, """{"name":"Ada","times":1}""").Should().BeNull();
    }

    [Fact]
    public async Task NotCheckInputOfAnUntypedWorkUnit()
    {
        using var temp = new TempDirectory();
        var host = await HostWithBehaviorsAsync(temp);

        host.ValidateInput(new WorkUnitBinding(Plugin, "count", PluginVersionConstraint.Latest), "anything at all").Should().BeNull();
    }

    [Fact]
    public async Task ReturnAFailureAWorkUnitReportsInsteadOfThrowing()
    {
        using var temp = new TempDirectory();
        var host = await HostWithBehaviorsAsync(temp);

        var result = await host.ExecuteWorkUnitAsync(new WorkUnitBinding(Plugin, "reports-failure", PluginVersionConstraint.Latest), new WorkRequest());

        result.Executed.Should().BeTrue();
        (result.Result!.IsSuccess, result.Result.ErrorCode, result.Result.ErrorMessage).Should().Be((false, "expected-failure", "This work unit always reports a failure."));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task RunEchoOnceAsync(PluginHost host)
    {
        var result = await host.ExecuteWorkUnitAsync(Echo, new WorkRequest("""{"name":"Ada","times":1}"""));
        result.Result!.OutputJson.Should().NotBeNull();
        host.ValidateInput(Echo, """{"name":"Ada","times":1}""").Should().BeNull();
    }

    [Fact]
    public async Task StillFreeThePluginsCodeAfterTypedJsonWasReadAndWritten()
    {
        using var temp = new TempDirectory();
        var host = await HostWithBehaviorsAsync(temp);
        await RunEchoOnceAsync(host);

        var result = await host.UnloadAsync(Plugin, PluginVersion.Parse("1.0.0"));

        result.Should().NotBeNull();
        result!.Collected.Should().BeTrue(result.Diagnostic);
    }

    [Theory]
    [InlineData("\"name\": \"echo\"", "\"name\": \"echo\", \"kind\": \"workflow\"", "has kind 'workflow', which this host cannot run (it supports: work-unit)")]
    [InlineData("\"capabilities\": [ \"network:smtp\" ]", "\"capabilities\": [ \"service:Some.Unlisted.IService\" ]", "'Some.Unlisted.IService' is not on this host's service allow-list")]
    [InlineData("{ \"kind\": \"manual\" }", "{ \"kind\": \"webhook\" }", "not a known trigger kind")]
    [InlineData("\"cron\": \"30 4 * * *\"", "\"cron\": \"daily\"", "5 or 6 fields")]
    [InlineData("{ \"kind\": \"manual\" }", "{ \"kind\": \"one-off\", \"at\": \"2030-01-01T00:00:00Z\" }", "cannot be a default that ships with a plugin")]
    [InlineData("\"inputType\": \"Fixture.Behaviors.EchoInput\"", "\"inputType\": \"Fixture.Behaviors.EchoOutput\"", "declares inputType 'Fixture.Behaviors.EchoOutput', but its class uses 'Fixture.Behaviors.EchoInput'")]
    [InlineData("\"outputType\": \"Fixture.Behaviors.EchoOutput\"", "\"outputType\": \"Fixture.Behaviors.EchoOutput\" ,\"x\":1", null)]
    public async Task RejectAPackageWhoseManifestDeclaresWhatTheHostCannotHonor(string find, string replace, string? expected)
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        var directory = temp.CopyFixture("behaviors-v1", "bad");
        Packages.ReplaceInManifest(find, replace)(directory);

        var act = () => host.LoadAsync(directory);

        if (expected is null)
        {
            await act.Should().NotThrowAsync("unknown manifest properties are ignored");
            return;
        }

        (await act.Should().ThrowAsync<PluginLoadException>()).Which.Message.Should().Contain(expected);
    }

    [Fact]
    public async Task AcceptAServiceCapabilityForAServiceOnTheHostsAllowList()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(
            temp,
            o => o.HostServices.Add(typeof(Fixture.Contracts.IHostOnlyService)),
            services => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Fixture.Contracts.IHostOnlyService>(services, new Offered()));
        var directory = temp.CopyFixture("behaviors-v1", "ok");
        Packages.ReplaceInManifest("\"network:smtp\"", "\"service:Fixture.Contracts.IHostOnlyService\"")(directory);

        var act = () => host.LoadAsync(directory);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AcceptAKindTheHostWasToldAbout()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, configureWorkUnits: o => o.WorkUnitKinds.Add("workflow"));
        var directory = temp.CopyFixture("behaviors-v1", "kind");
        Packages.ReplaceInManifest("\"name\": \"echo\"", "\"name\": \"echo\", \"kind\": \"workflow\"")(directory);

        await host.LoadAsync(directory);

        DescriptorOf(host, "echo").Kind.Should().Be("workflow");
    }

    private sealed class Offered : Fixture.Contracts.IHostOnlyService
    {
        public string Secret => "offered";
    }
}
