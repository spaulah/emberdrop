using Emberdrop.Plugins;
using Emberdrop.Plugins.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Emberdrop.Plugins.Tests;

/// <summary>
/// The hosting core is reusable on its own: with the default options, which know nothing about work units, it still loads packages,
/// runs code against their exports under a lease, drains and unloads. (The layering itself is enforced by Emberdrop.Architecture.Tests.)
/// </summary>
public class HostingCoreAloneShould
{
    private static PluginHost NewHost(TempDirectory temp)
    {
        var options = new PluginHostOptions
        {
            StorageRoot = Path.Combine(temp.Path, "plugins"),
            UnloadVerificationAttempts = 20,
            UnloadVerificationDelay = TimeSpan.FromMilliseconds(20),
        };
        options.SharedAssemblies.Add("Fixture.Contracts");
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        var factory = new PluginServiceProviderFactory(services.BuildServiceProvider(), options);
        return new PluginHost(new PluginLoader(options, NullLogger<PluginLoader>.Instance), factory, options, [], NullLogger<PluginHost>.Instance);
    }

    /// <summary>The fixture's manifest, written the way the generic format wants it: exports under <c>exports</c>.</summary>
    private static string GenericPackage(TempDirectory temp)
    {
        var directory = temp.CopyFixture("hello-v1", "generic");
        Packages.ReplaceInManifest("\"workUnits\"", "\"exports\"")(directory);
        return directory;
    }

    [Fact]
    public async Task LoadAPackageAndDescribeItsExportsWithoutKnowingWhatTheyAreFor()
    {
        using var temp = new TempDirectory();
        var host = NewHost(temp);

        var descriptor = await host.LoadAsync(GenericPackage(temp));

        var export = descriptor.Exports.Should().ContainSingle().Which;
        (export.Name, export.Kind).Should().Be(("hello", "default"));
        export.Metadata.Should().BeNull("no handler was given, so nothing was learned about the export");
    }

    [Fact]
    public async Task RunCodeAgainstAnExportUnderALeaseAndReturnWhatItReturns()
    {
        using var temp = new TempDirectory();
        var host = NewHost(temp);
        await host.LoadAsync(GenericPackage(temp));

        var execution = await host.ExecuteExportAsync(
            active => active.SelectMany(p => p.Exports.Select(e => (Plugin: p, Export: e))).Where(x => x.Export.Name == "hello")
                .Select(x => ExportSelection.Found(x.Plugin, x.Export)).DefaultIfEmpty(ExportSelection.NotFound("no hello")).First(),
            invocation => Task.FromResult($"{invocation.Plugin.Id}/{invocation.Plugin.Version}/{invocation.Export.Name}/{invocation.Instance.GetType().Name}"));

        execution.Executed.Should().BeTrue(execution.NotExecutedReason);
        execution.Result.Should().Be("fixture.hello/1.0.0/hello/HelloWorkUnit");
    }

    [Fact]
    public async Task SayWhyNothingRanWhenTheChoiceFindsNoExport()
    {
        using var temp = new TempDirectory();
        var host = NewHost(temp);

        var execution = await host.ExecuteExportAsync<string>(_ => ExportSelection.NotFound("nobody exports that"), _ => Task.FromResult("never"));

        (execution.Executed, execution.NotExecutedReason).Should().Be((false, "nobody exports that"));
    }

    [Fact]
    public async Task ReportAFailureAsTextOnly()
    {
        using var temp = new TempDirectory();
        var host = NewHost(temp);
        await host.LoadAsync(GenericPackage(temp));

        var act = () => host.ExecuteExportAsync<string>(
            active => ExportSelection.Found(active[0], active[0].Exports[0]),
            _ => throw new InvalidOperationException("boom"));

        var thrown = (await act.Should().ThrowAsync<PluginExecutionException>()).Which;
        thrown.Message.Should().Contain("boom").And.Contain("hello");
        thrown.InnerException.Should().BeNull("the original exception object would keep the plugin loaded");
    }

    [Fact]
    public async Task UnloadAPluginItHostedAndFreeItsCode()
    {
        using var temp = new TempDirectory();
        var host = NewHost(temp);
        await host.LoadAsync(GenericPackage(temp));
        (await host.ExecuteExportAsync(active => ExportSelection.Found(active[0], active[0].Exports[0]), _ => Task.FromResult(1))).Executed.Should().BeTrue();

        var result = await host.UnloadAsync("fixture.hello", PluginVersion.Parse("1.0.0"));

        result!.Collected.Should().BeTrue(result.Diagnostic);
    }

    [Fact]
    public async Task RefuseAPackageWhoseManifestUsesTheWorkUnitFormatWhenNoneWasConfigured()
    {
        using var temp = new TempDirectory();
        var host = NewHost(temp);

        var act = () => host.LoadAsync(TestPlugins.FixturePath("hello-v1"));

        (await act.Should().ThrowAsync<PluginLoadException>()).Which.Message.Should().Contain("'exports' is required");
    }
}
