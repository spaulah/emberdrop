using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FluentAssertions;

namespace Emberdrop.Architecture.Tests;

/// <summary>
/// The layers of Emberdrop, enforced:
/// <list type="number">
/// <item><description><c>Emberdrop.Hosting</c> (packages, load contexts, lifecycle, draining, quarantine, diagnostics) knows nothing about work units, triggers, schedulers, stores or Hangfire.</description></item>
/// <item><description><c>Emberdrop.Core</c> (work unit semantics) sits on top of it.</description></item>
/// <item><description>Providers (schedulers and stores) depend on the abstractions, not on each other or on the reference host.</description></item>
/// </list>
/// The hosting core is checked by reading its metadata, not by naming conventions: every assembly it references and every type it
/// refers to, in signatures and in method bodies alike.
/// </summary>
public class LayeringShould
{
    private static readonly Assembly Hosting = typeof(Emberdrop.Plugins.PluginHost).Assembly;
    private static readonly Assembly Core = typeof(Emberdrop.WorkUnits.WorkUnitManager).Assembly;

    /// <summary>Assemblies the hosting core may reference: Abstractions (versions, the activity hook), the runtime and the Microsoft.Extensions abstractions it builds its services on.</summary>
    private static bool IsAllowedAssembly(string name) =>
        name == "Emberdrop.Abstractions"
        || name == "System.Private.CoreLib"
        || name.StartsWith("System.", StringComparison.Ordinal)
        || name is "netstandard" or "mscorlib" or "Microsoft.CSharp"
        || name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal);

    /// <summary>Types of Emberdrop.Abstractions that mean "work unit", "trigger", "scheduler" or "store". The hosting core must not mention them.</summary>
    private static readonly string[] ForbiddenNamespaces = ["Emberdrop.Scheduling", "Emberdrop.Storage"];

    private static readonly string[] ForbiddenTypes =
    [
        "Emberdrop.IWorkUnit",
        "Emberdrop.WorkUnit`2",
        "Emberdrop.WorkContext",
        "Emberdrop.WorkResult",
        "Emberdrop.WorkJson",
        "Emberdrop.WorkInputException",
        "Emberdrop.NoInput",
        "Emberdrop.NoOutput",
        "Emberdrop.Trigger",
        "Emberdrop.ManualTrigger",
        "Emberdrop.OneOffTrigger",
        "Emberdrop.RecurringTrigger",
        "Emberdrop.ITriggerKind",
        "Emberdrop.TriggerKindRegistry",
    ];

    private static (HashSet<string> Assemblies, List<string> TypeReferences) ReadReferences(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var assemblies = reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name)).ToHashSet(StringComparer.Ordinal);
        var types = new List<string>();
        foreach (var handle in reader.TypeReferences)
        {
            var reference = reader.GetTypeReference(handle);
            var ns = reader.GetString(reference.Namespace);
            var name = reader.GetString(reference.Name);
            types.Add(ns.Length == 0 ? name : ns + "." + name);
        }

        return (assemblies, types);
    }

    [Fact]
    public void KeepTheHostingCoreIndependentOfCoreSchedulersStoresAndHangfire()
    {
        var (assemblies, _) = ReadReferences(Hosting);

        assemblies.Where(a => !IsAllowedAssembly(a)).Should().BeEmpty(
            "Emberdrop.Hosting may reference Emberdrop.Abstractions, the runtime and the Microsoft.Extensions abstractions, and nothing else " +
            "(not Emberdrop.Core, a scheduler, a store, Hangfire or a database library)");
        assemblies.Should().Contain("Emberdrop.Abstractions", "the hosting core is built on the abstractions");
    }

    [Fact]
    public void KeepWorkUnitsTriggersSchedulingAndStorageOutOfTheHostingCore()
    {
        var (_, types) = ReadReferences(Hosting);

        var offending = types
            .Where(t => ForbiddenTypes.Contains(t, StringComparer.Ordinal) || ForbiddenNamespaces.Any(ns => t.StartsWith(ns + ".", StringComparison.Ordinal)))
            .Distinct()
            .Order()
            .ToList();

        offending.Should().BeEmpty("the hosting core must not know what a work unit, a trigger, a scheduler or a store is");
    }

    [Fact]
    public void ExposeNoWorkUnitTriggerSchedulingOrStorageTypesInTheHostingCoresPublicSurface()
    {
        var surface = Hosting.GetExportedTypes()
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => $"{t.FullName}.{m.Name}"))
            .Concat(Hosting.GetExportedTypes().Select(t => t.FullName!))
            .ToList();

        surface.Where(s => s.Contains("WorkUnit", StringComparison.OrdinalIgnoreCase) || s.Contains("Trigger", StringComparison.OrdinalIgnoreCase)
                || s.Contains("Scheduler", StringComparison.OrdinalIgnoreCase) || s.Contains("Hangfire", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("the hosting core's names say nothing about work units, triggers or schedulers");
    }

    [Fact]
    public void ActuallyDetectWorkUnitsAndSchedulingWhenTheyAreThere()
    {
        // The same check, run on an assembly that does use them: if this did not find anything, the checks above would prove nothing.
        var (assemblies, types) = ReadReferences(Core);

        assemblies.Where(a => !IsAllowedAssembly(a)).Should().Contain("Emberdrop.Hosting");
        types.Should().Contain("Emberdrop.IWorkUnit");
        types.Should().Contain(t => t.StartsWith("Emberdrop.Scheduling.", StringComparison.Ordinal));
        types.Should().Contain(t => t.StartsWith("Emberdrop.Storage.", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildWorkUnitSemanticsOnTopOfTheHostingCore()
    {
        var core = Core.GetReferencedAssemblies().Select(a => a.Name).ToList();

        core.Should().Contain("Emberdrop.Hosting");
        core.Should().Contain("Emberdrop.Abstractions");
        Hosting.GetReferencedAssemblies().Select(a => a.Name).Should().NotContain("Emberdrop.Core");
    }

    [Theory]
    [InlineData("Emberdrop.InMemory")]
    [InlineData("Emberdrop.Hangfire")]
    [InlineData("Emberdrop.Storage.Relational")]
    [InlineData("Emberdrop.Storage.Sqlite")]
    [InlineData("Emberdrop.Storage.SqlServer")]
    public void KeepProvidersFromReachingIntoTheHostingCoreOrTheReferenceHost(string provider)
    {
        var path = Path.Combine(AppContext.BaseDirectory, provider + ".dll");
        File.Exists(path).Should().BeTrue($"{provider} is referenced by this project, so it is copied next to the test");

        var references = Assembly.LoadFrom(path).GetReferencedAssemblies().Select(a => a.Name).ToList();
        references.Should().NotContain("Emberdrop.Server", "a provider must not depend on the reference host");
        references.Should().NotContain("Emberdrop.Hosting", "a provider talks to the abstractions, not to the plugin hosting core");
    }
}
