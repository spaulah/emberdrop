using System.Collections.Concurrent;
using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Emberdrop.Plugins.Tests.Support;
using FluentAssertions;
using Fixture.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Emberdrop.Plugins.Tests;

/// <summary>Each plugin version gets its own service provider with an explicit allow-list of host services.</summary>
public class PluginServicesShould
{
    private const string Plugin = "fixture.behaviors";
    private static readonly PluginVersion V1 = PluginVersion.Parse("1.0.0");
    private static readonly PluginVersion V2 = PluginVersion.Parse("2.0.0");

    private static string NewName() => Guid.NewGuid().ToString("N");

    private static Action<IServiceCollection> HostWith(Dictionary<string, string?>? configuration = null, ILoggerFactory? loggers = null) => services =>
    {
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration ?? new() { ["Fixture:Setting"] = "value-1", ["Secret:Key"] = "host-secret" }).Build());
        services.AddSingleton<IHostOnlyService>(new HostOnly());
        if (loggers is not null)
        {
            services.AddSingleton(loggers);
        }
    };

    private static async Task RunService(PluginHost host, string name, PluginVersion? pin = null, Trigger? trigger = null)
    {
        var constraint = pin is null ? PluginVersionConstraint.Latest : PluginVersionConstraint.Exactly(pin);
        var result = await host.ExecuteWorkUnitAsync(new WorkUnitBinding(Plugin, "service", constraint), new WorkRequest(name, trigger), 1);
        result.Executed.Should().BeTrue(result.NotExecutedReason);
    }

    [Fact]
    public async Task BuildOneProviderPerVersionAndGiveEveryExecutionItsOwnScope()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, configureHostServices: HostWith());
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        var first = NewName();
        var second = NewName();

        await RunService(host, first);
        await RunService(host, second);

        FixtureControl.OutcomeOf(first + ":builder").Should().Be(FixtureControl.OutcomeOf(second + ":builder"), "singletons live as long as the plugin version");
        FixtureControl.OutcomeOf(first + ":stamp").Should().NotBe(FixtureControl.OutcomeOf(second + ":stamp"), "each execution has its own scope");
        FixtureControl.IsMarked("stamp-disposed:" + FixtureControl.OutcomeOf(first + ":stamp")).Should().BeTrue("the scope ended with the execution");
        FixtureControl.IsMarked("stamp-disposed:" + FixtureControl.OutcomeOf(second + ":stamp")).Should().BeTrue();
    }

    [Fact]
    public async Task GiveTheWorkUnitItsExecutionContext()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, configureHostServices: HostWith());
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        var name = NewName();

        await RunService(host, name, trigger: new RecurringTrigger("*/5 * * * *"));

        FixtureControl.OutcomeOf(name + ":context").Should().Be(
            "trigger=recurring:*/5 * * * *; setting=value-1; secretVisible=False; execution=True; plugin=fixture.behaviors/1.0.0; name=service; sameServices=True");
    }

    [Fact]
    public async Task TreatARunWithoutATriggerAsManual()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, configureHostServices: HostWith());
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        var name = NewName();

        await RunService(host, name);

        FixtureControl.OutcomeOf(name + ":context").Should().StartWith("trigger=manual;");
    }

    [Fact]
    public async Task KeepTheProvidersOfDifferentVersionsApart()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, configureHostServices: HostWith());
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v2"));
        var onV1 = NewName();
        var onV2 = NewName();

        await RunService(host, onV1, pin: V1);
        await RunService(host, onV2, pin: V2);

        FixtureControl.OutcomeOf(onV1 + ":builder").Should().NotBe(FixtureControl.OutcomeOf(onV2 + ":builder"));
    }

    [Fact]
    public async Task HideHostServicesThatAreNotOnTheAllowListAndShowTheOnesThatAre()
    {
        using var temp = new TempDirectory();
        var hidden = NewName();
        var (closed, _) = TestPlugins.CreateHost(temp, configureHostServices: HostWith());
        await closed.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        await RunService(closed, hidden);

        using var temp2 = new TempDirectory();
        var shown = NewName();
        var (open, _) = TestPlugins.CreateHost(temp2, o => o.HostServices.Add(typeof(IHostOnlyService)), HostWith());
        await open.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        await RunService(open, shown);

        FixtureControl.OutcomeOf(hidden + ":host-only").Should().Be("(not visible)");
        FixtureControl.OutcomeOf(shown + ":host-only").Should().Be("top-secret");
    }

    [Fact]
    public async Task ShowThePluginOnlyTheConfigurationKeysItsWorkUnitsDeclared()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, configureHostServices: HostWith());
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        var name = NewName();

        await RunService(host, name);

        FixtureControl.OutcomeOf(name + ":setting").Should().Be("value-1");
        FixtureControl.OutcomeOf(name + ":secret").Should().Be("(null)", "the host has this key, but the plugin did not declare it");
    }

    [Fact]
    public async Task ForwardPluginLoggersToTheHostsLoggerFactoryUnderThePluginTypesName()
    {
        using var temp = new TempDirectory();
        var loggers = new CapturingLoggerFactory();
        var (host, _) = TestPlugins.CreateHost(temp, configureHostServices: HostWith(loggers: loggers));
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));

        await RunService(host, NewName());

        loggers.Messages.Should().Contain(m => m.Category == "Fixture.Behaviors.ServiceWorkUnit" && m.Text.StartsWith("service work unit ran for", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefuseToStartAWorkUnitWhoseRequiredConfigurationIsMissing()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, configureHostServices: HostWith(new Dictionary<string, string?>()));
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        var name = NewName();

        var act = () => host.ExecuteWorkUnitAsync(new WorkUnitBinding(Plugin, "service", PluginVersionConstraint.Latest), new WorkRequest(name));

        (await act.Should().ThrowAsync<PluginConfigurationException>()).WithMessage("*Fixture:Setting*not started*");
        FixtureControl.OutcomeOf(name + ":builder").Should().BeNull("the work unit never started");
    }

    [Fact]
    public async Task DisposeThePluginsProviderWhenItsVersionUnloadsAndStillFreeItsCode()
    {
        using var temp = new TempDirectory();
        var (host, _) = TestPlugins.CreateHost(temp, configureHostServices: HostWith());
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        var name = NewName();
        await RunService(host, name);
        var builder = FixtureControl.OutcomeOf(name + ":builder")!;
        FixtureControl.IsMarked("builder-disposed:" + builder).Should().BeFalse("the provider lives as long as the version");

        var result = await host.RetireAsync(Plugin, V1);

        FixtureControl.IsMarked("builder-disposed:" + builder).Should().BeTrue("unloading disposes the plugin's singletons");
        // The work unit asked for ILogger<ServiceWorkUnit>. Resolved through the host's own container that would have pinned the
        // plugin; resolved through the plugin's container it dies with it.
        result.Unload!.Collected.Should().BeTrue(result.Unload.Diagnostic);
    }

    [Fact]
    public async Task NotDisposeHostServicesWhenAPluginUnloads()
    {
        using var temp = new TempDirectory();
        var disposable = new DisposableLoggerFactory();
        var (host, _) = TestPlugins.CreateHost(temp, configureHostServices: HostWith(loggers: disposable));
        await host.LoadAsync(TestPlugins.FixturePath("behaviors-v1"));
        await RunService(host, NewName());

        await host.RetireAsync(Plugin, V1);

        disposable.Disposed.Should().BeFalse("the host's logger factory belongs to the host");
    }

    [Fact]
    public async Task RejectAPluginWhoseServicesCannotBeBuiltAtActivationNotAtItsFirstRun()
    {
        using var temp = new TempDirectory();
        var directory = temp.CopyFixture("behaviors-v1", "broken");
        var manifest = Path.Combine(directory, "emberdrop.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("Fixture.Behaviors.Startup", "Fixture.Behaviors.BrokenStartup"));
        var (host, _) = TestPlugins.CreateHost(temp, configureHostServices: HostWith());

        var act = () => host.LoadAsync(directory);

        var failure = (await act.Should().ThrowAsync<PluginLoadException>()).Which;
        failure.Message.Should().Contain("could not be built").And.Contain("MissingDependency");
        host.GetPlugins().Should().BeEmpty("a plugin that cannot build its services never becomes active");
        host.GetVersions().Should().ContainSingle(v => v.State == PluginState.Failed && v.Reason!.Contains("could not be built", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShowTheRootCauseWhenAPluginsStartupCodeThrows()
    {
        using var temp = new TempDirectory();
        var directory = temp.CopyFixture("fragile-v2", "explodes");
        var (host, _) = TestPlugins.CreateHost(temp);

        var act = () => host.LoadAsync(directory);

        // The runtime reports a type-initializer failure; the reason must still carry what actually went wrong.
        (await act.Should().ThrowAsync<PluginLoadException>()).WithMessage("*TypeInitializationException*caused by*InvalidOperationException: entry assembly exploded on load*");
    }

    [Theory]
    [InlineData("Fixture.Behaviors.NoSuchStartup", "was not found")]
    [InlineData("Fixture.Behaviors.ReportBuilder", "ConfigureServices")]
    public async Task RejectAManifestWhoseStartupTypeIsMissingOrHasNoConfigureServices(string startup, string expected)
    {
        using var temp = new TempDirectory();
        var directory = temp.CopyFixture("behaviors-v1", "bad-startup");
        var manifest = Path.Combine(directory, "emberdrop.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("Fixture.Behaviors.Startup", startup));
        var (host, _) = TestPlugins.CreateHost(temp, configureHostServices: HostWith());

        var act = () => host.LoadAsync(directory);

        (await act.Should().ThrowAsync<PluginLoadException>()).WithMessage($"*{expected}*");
    }

    private sealed class HostOnly : IHostOnlyService
    {
        public string Secret => "top-secret";
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly ConcurrentQueue<(string Category, string Text)> _messages = new();

        public IReadOnlyCollection<(string Category, string Text)> Messages => _messages.ToArray();

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new Capture(categoryName, _messages);

        public void Dispose()
        {
        }

        private sealed class Capture(string category, ConcurrentQueue<(string, string)> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                sink.Enqueue((category, formatter(state, exception)));
        }
    }

    private sealed class DisposableLoggerFactory : ILoggerFactory
    {
        public bool Disposed { get; private set; }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose() => Disposed = true;
    }
}
