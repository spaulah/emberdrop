using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Emberdrop.Plugins.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Fixture.Contracts;

namespace Emberdrop.Plugins.Tests;

/// <summary>
/// The core promises of plugin hosting, proven against real compiled plugins: type identity for the shared
/// contract, private dependencies that may differ from the host's, unloadability, and no file locks.
/// </summary>
public class PluginIsolationShould
{
    // Everything that touches a plugin's types lives in these small methods and returns plain strings, so no
    // reference into the plugin's load context survives on the test method's stack when it checks for collection.

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> RunAndDescribeAsync(PluginHost host, string workUnitKey, int workUnitId)
    {
        host.TryCreateWorkUnit(workUnitKey, out var workUnit).Should().BeTrue();
        await workUnit!.ExecuteAsync(
            new WorkContext
            {
                ExecutionId = "test",
                WorkUnitName = workUnitKey,
                Trigger = ManualTrigger.Instance,
                Logger = NullLogger.Instance,
                Services = new ServiceCollection().BuildServiceProvider(),
                Configuration = new Dictionary<string, string?>(),
                Input = workUnitId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            CancellationToken.None);
        return ((IProbe)workUnit).Describe();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool WorkUnitIsCastableToTheHostsInterfaces(PluginHost host, string workUnitKey)
    {
        host.TryCreateWorkUnit(workUnitKey, out var workUnit);
        return workUnit is IWorkUnit and IProbe;
    }

    [Fact]
    public async Task LoadAWorkUnitWhoseContractTypesAreTheHostsOwn()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("hello-v1"));

        WorkUnitIsCastableToTheHostsInterfaces(host, "hello").Should().BeTrue("Emberdrop.Abstractions and the shared Fixture.Contracts resolve from the host");
        var description = await RunAndDescribeAsync(host, "hello", 42);

        description.Should().Contain("hello from v1").And.Contain("started=42");
        description.Should().Contain("contract context='Default'", "the plugin's view of IWorkUnit is the host's copy, not its own bundled one");
        description.Should().Contain("plugin context='plugin:fixture.hello@1.0.0'");
    }

    [Fact]
    public async Task IgnoreTheCopyOfTheContractAssemblyThatEveryPackageCarries()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        File.Exists(Path.Combine(TestPlugins.FixturePath("hello-v1"), "Emberdrop.Abstractions.dll")).Should().BeTrue("the fixture ships its own copy, like a real package");

        await host.LoadAsync(TestPlugins.FixturePath("hello-v1"));

        WorkUnitIsCastableToTheHostsInterfaces(host, "hello").Should().BeTrue();
    }

    [Fact]
    public async Task FindAWorkUnitByLogicalNameFullTypeNameOrSimpleName()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("hello-v1"));

        foreach (var key in new[] { "hello", "Fixture.Hello.HelloWorkUnit", "HelloWorkUnit" })
        {
            (await RunAndDescribeAsync(host, key, 1)).Should().Contain("hello from v1");
        }

        host.TryCreateWorkUnit("nope", out var missing).Should().BeFalse();
        missing.Should().BeNull();
    }

    [Fact]
    public async Task RunAPluginWithItsOwnVersionOfALibraryNextToTheHostsDifferentVersion()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("uses-conflict"));

        var pluginView = await RunAndDescribeAsync(host, "uses-conflict", 1);

        pluginView.Should().Contain("Fixture.Conflict 2.0.0.0").And.Contain("plugin:fixture.uses-conflict@1.0.0");
        var hostView = Fixture.Conflict.ConflictLibrary.Describe();
        hostView.Should().Contain("Fixture.Conflict 1.0.0.0").And.Contain("'Default'");
    }

    [Fact]
    public async Task KeepTwoVersionsOfOnePluginSideBySideEachWithItsOwnTypes()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("hello-v1"));
        await host.LoadAsync(TestPlugins.FixturePath("hello-v2"));

        host.GetPlugins().Select(p => p.Version.ToString()).Should().Equal("2.0.0", "1.0.0");

        // A work unit asked for by name comes from the newest version.
        (await RunAndDescribeAsync(host, "hello", 1)).Should().Contain("hello from v2");

        // Unloading the old version frees it and leaves the new one working.
        var result = await host.UnloadAsync("fixture.hello", PluginVersion.Parse("1.0.0"));
        result!.Collected.Should().BeTrue(result.Diagnostic);
        (await RunAndDescribeAsync(host, "hello", 2)).Should().Contain("hello from v2");
    }

    [Fact]
    public async Task RefuseToLoadTheSameVersionTwice()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("hello-v1"));

        var act = () => host.LoadAsync(temp.CopyFixture("hello-v1", "again"));

        (await act.Should().ThrowAsync<PluginLoadException>()).WithMessage("*already loaded*");
        host.GetPlugins().Should().ContainSingle();
    }

    [Fact]
    public async Task FreeThePluginsCodeWhenItIsUnloaded()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("hello-v1"));
        (await RunAndDescribeAsync(host, "hello", 7)).Should().Contain("started=7");

        var result = await host.UnloadAsync("fixture.hello", PluginVersion.Parse("1.0.0"));

        result.Should().NotBeNull();
        result!.Collected.Should().BeTrue(result.Diagnostic);
        result.Diagnostic.Should().BeNull();
        host.GetPlugins().Should().BeEmpty();
        host.TryCreateWorkUnit("hello", out _).Should().BeFalse("an unloaded plugin provides no work units");
    }

    [Fact]
    public async Task FreeAPluginThatBundlesItsOwnDependencyToo()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(TestPlugins.FixturePath("uses-conflict"));
        await RunAndDescribeAsync(host, "uses-conflict", 1);

        var result = await host.UnloadAsync("fixture.uses-conflict", PluginVersion.Parse("1.0.0"));

        result!.Collected.Should().BeTrue(result.Diagnostic);
    }

    [Fact]
    public async Task DetectHostCodeThatKeepsAPluginTypeAlive()
    {
        // Negative control: proves the collectibility checks above can fail. Host code that caches a plugin
        // type (here a static dictionary, the classic mistake) keeps the whole load context alive.
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, o =>
        {
            o.UnloadVerificationAttempts = 3;
            o.UnloadVerificationDelay = TimeSpan.FromMilliseconds(10);
        });
        await host.LoadAsync(TestPlugins.FixturePath("hello-v1"));
        LeakyHostCache.Remember(host, "hello");

        try
        {
            var result = await host.UnloadAsync("fixture.hello", PluginVersion.Parse("1.0.0"));

            result!.Collected.Should().BeFalse("a static cache holds a plugin type");
            result.Diagnostic.Should().Contain("fixture.hello@1.0.0").And.Contain("still alive");
        }
        finally
        {
            LeakyHostCache.Clear();
        }
    }

    [Fact]
    public async Task NeverMapOrLockThePluginsFilesSoTheDirectoryCanBeDeletedWhileLoaded()
    {
        using var temp = new TempDirectory();
        var directory = temp.CopyFixture("hello-v1", "installed");
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(directory);

        // On Windows, an assembly loaded from a path keeps its file open and this delete would fail.
        Directory.Delete(directory, recursive: true);

        var description = await RunAndDescribeAsync(host, "hello", 3);
        description.Should().Contain("hello from v1").And.Contain("location=''", "an assembly loaded from memory has no file location");

        var result = await host.UnloadAsync("fixture.hello", PluginVersion.Parse("1.0.0"));
        result!.Collected.Should().BeTrue(result.Diagnostic);
    }

    [Fact]
    public async Task LetTheDirectoryBeDeletedAfterUnload()
    {
        using var temp = new TempDirectory();
        var directory = temp.CopyFixture("uses-conflict", "installed");
        var (host, _) = TestPlugins.CreateHost(temp);
        await host.LoadAsync(directory);
        await RunAndDescribeAsync(host, "uses-conflict", 1);
        (await host.UnloadAsync("fixture.uses-conflict", PluginVersion.Parse("1.0.0")))!.Collected.Should().BeTrue();

        var act = () => Directory.Delete(directory, recursive: true);

        act.Should().NotThrow();
        Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public async Task RejectAPluginThatNeedsANewerSharedAssemblyThanTheHostHas()
    {
        using var temp = new TempDirectory();
        // The host has Fixture.Conflict 1.0.0.0. Declare it shared, and the plugin's need for 2.0.0.0 can no longer be met privately.
        var (host, _) = TestPlugins.CreateHost(temp, o => o.SharedAssemblies.Add("Fixture.Conflict"));

        var act = () => host.LoadAsync(TestPlugins.FixturePath("uses-conflict"));

        var exception = (await act.Should().ThrowAsync<PluginLoadException>()).Which;
        exception.Message.Should().Contain("Fixture.Conflict").And.Contain("2.0.0.0").And.Contain("1.0.0.0").And.Contain("shared with the host");
        host.GetPlugins().Should().BeEmpty();
    }

    [Fact]
    public async Task RejectAPluginBuiltForANewerAbstractionsVersionThanTheHostProvides()
    {
        using var temp = new TempDirectory();
        var directory = temp.CopyFixture("hello-v1", "future");
        var manifestPath = Path.Combine(directory, PluginManifest.FileName);
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("\"minAbstractionsVersion\": \"1.0.0\"", "\"minAbstractionsVersion\": \"99.1.0\""));
        var (host, _) = TestPlugins.CreateHost(temp);

        var act = () => host.LoadAsync(directory);

        (await act.Should().ThrowAsync<PluginLoadException>()).WithMessage("*Emberdrop.Abstractions 99.1.0*this host provides 1.0.0*");
    }

    [Fact]
    public async Task ReportEveryProblemWithAnInvalidManifestAndLoadNothing()
    {
        using var temp = new TempDirectory();
        var directory = temp.CopyFixture("hello-v1", "broken");
        File.WriteAllText(Path.Combine(directory, PluginManifest.FileName), """{ "schemaVersion": 1, "id": "Bad Id", "version": "1", "minAbstractionsVersion": "1.0.0", "entryAssemblies": [], "workUnits": [] }""");
        var (host, _) = TestPlugins.CreateHost(temp);

        var act = () => host.LoadAsync(directory);

        var exception = (await act.Should().ThrowAsync<PluginLoadException>()).Which;
        exception.Reasons.Count.Should().BeGreaterThanOrEqualTo(3);
        host.GetPlugins().Should().BeEmpty();
    }

    [Fact]
    public async Task RejectAManifestThatNamesAnEntryAssemblyThePackageDoesNotContain()
    {
        using var temp = new TempDirectory();
        var directory = temp.CopyFixture("hello-v1", "missing-entry");
        File.Delete(Path.Combine(directory, "Fixture.Hello.dll"));
        var (host, _) = TestPlugins.CreateHost(temp);

        var act = () => host.LoadAsync(directory);

        (await act.Should().ThrowAsync<PluginLoadException>()).WithMessage("*Fixture.Hello.dll*not in the package*");
    }

    [Fact]
    public async Task RejectAManifestThatDeclaresAWorkUnitTheAssemblyDoesNotHaveAndFreeTheHalfLoadedContext()
    {
        using var temp = new TempDirectory();
        var directory = temp.CopyFixture("hello-v1", "wrong-type");
        var manifestPath = Path.Combine(directory, PluginManifest.FileName);
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("Fixture.Hello.HelloWorkUnit", "Fixture.Hello.NoSuchWorkUnit"));
        var (host, _) = TestPlugins.CreateHost(temp);

        var act = () => host.LoadAsync(directory);

        (await act.Should().ThrowAsync<PluginLoadException>()).WithMessage("*Work unit 'hello'*Fixture.Hello.NoSuchWorkUnit*not found*");
    }

    [Fact]
    public async Task QuarantineAnInvalidPackageWithAReasonFileBesideIt()
    {
        using var temp = new TempDirectory();
        var directory = temp.CopyFixture("hello-v1", "to-quarantine");
        File.WriteAllText(Path.Combine(directory, PluginManifest.FileName), "{ not json");
        var (host, options) = TestPlugins.CreateHost(temp);
        var quarantine = new PluginQuarantine(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginQuarantine>.Instance);

        var failure = (await FluentActions.Awaiting(() => host.LoadAsync(directory)).Should().ThrowAsync<PluginLoadException>()).Which;
        var destination = await quarantine.QuarantineAsync(directory, failure.Reasons, failure.Details);

        Directory.Exists(directory).Should().BeFalse("the package was moved out of the way");
        Directory.Exists(destination).Should().BeTrue();
        Path.GetDirectoryName(destination).Should().Be(options.QuarantineDirectory);
        var reason = File.ReadAllText(destination + ".reason.json");
        reason.Should().Contain("not valid JSON").And.Contain("quarantinedAtUtc").And.Contain("to-quarantine");
    }

    [Fact]
    public async Task QuarantineAPackageFileTooAndNeverOverwriteAnEarlierOne()
    {
        using var temp = new TempDirectory();
        var (_, options) = TestPlugins.CreateHost(temp);
        var quarantine = new PluginQuarantine(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginQuarantine>.Instance);

        var destinations = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var file = Path.Combine(temp.Path, "bad.emberdrop");
            await File.WriteAllTextAsync(file, "garbage " + i);
            destinations.Add(await quarantine.QuarantineAsync(file, ["not a zip"]));
        }

        destinations.Distinct().Should().HaveCount(2);
        destinations.Should().OnlyContain(d => File.Exists(d) && File.Exists(d + ".reason.json"));
    }
}

/// <summary>Stands in for a host that caches plugin types in static state, which is exactly what the design forbids.</summary>
internal static class LeakyHostCache
{
    private static readonly List<Type> Cache = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Remember(PluginHost host, string workUnitKey)
    {
        host.TryCreateWorkUnit(workUnitKey, out var workUnit);
        Cache.Add(workUnit!.GetType());
    }

    public static void Clear() => Cache.Clear();
}
