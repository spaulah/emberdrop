using Emberdrop.Plugins;

namespace Emberdrop.WorkUnits;

/// <summary>
/// Gives the exports of a package their work unit meaning: checks that each is a work unit the host can run, reads its triggers,
/// capabilities and typed input and output, and keeps what it learned as a <see cref="PluginWorkUnitDescriptor"/>.
/// </summary>
internal sealed class WorkUnitExportHandler : IExportHandler
{
    /// <summary>A capability of the form <c>service:Full.Type.Name</c> asks for a host service; the type must be on <see cref="PluginHostOptions.HostServices"/>.</summary>
    public const string ServiceCapabilityPrefix = "service:";

    private readonly WorkUnitOptions _options;
    private readonly IList<Type> _hostServices;

    public WorkUnitExportHandler(WorkUnitOptions options, IList<Type> hostServices)
    {
        _options = options;
        _hostServices = hostServices;
    }

    public object? Inspect(ManifestExport manifestExport, Type type, IList<string> problems)
    {
        var workUnit = PluginWorkUnitManifest.From(manifestExport);
        var found = new List<string>();

        if (!_options.WorkUnitKinds.Contains(workUnit.Kind))
        {
            found.Add($"Work unit '{workUnit.Name}' has kind '{workUnit.Kind}', which this host cannot run (it supports: {string.Join(", ", _options.WorkUnitKinds)}).");
        }

        var services = _hostServices.Select(t => t.FullName).ToHashSet(StringComparer.Ordinal);
        foreach (var capability in workUnit.Capabilities.Where(c => c.StartsWith(ServiceCapabilityPrefix, StringComparison.Ordinal)))
        {
            var service = capability[ServiceCapabilityPrefix.Length..];
            if (!services.Contains(service))
            {
                found.Add($"Work unit '{workUnit.Name}' declares capability '{capability}', but '{service}' is not on this host's service allow-list.");
            }
        }

        var triggers = ManifestTriggers.Read(workUnit, _options.TriggerKinds, found);

        if (!typeof(IWorkUnit).IsAssignableFrom(type))
        {
            found.Add($"Work unit '{workUnit.Name}' declares type '{workUnit.Type}', which is not a concrete implementation of {nameof(IWorkUnit)}.");
            AddAll(problems, found);
            return null;
        }

        var info = WorkUnitTypeInfo.For(type);
        CheckDeclaredType(workUnit.Name, "inputType", workUnit.InputType, info.InputTypeName, found);
        CheckDeclaredType(workUnit.Name, "outputType", workUnit.OutputType, info.OutputTypeName, found);
        AddAll(problems, found);

        return new PluginWorkUnitDescriptor(
            workUnit.Name,
            type.FullName ?? type.Name,
            type.Assembly.GetName().Name ?? string.Empty,
            workUnit.DefaultSchedule,
            workUnit.RequiredConfiguration,
            workUnit.InputSchemaVersion,
            workUnit.Kind,
            workUnit.Description,
            triggers,
            workUnit.Capabilities,
            info.InputTypeName,
            info.OutputTypeName,
            info.InputSchema,
            info.OutputSchema);
    }

    public IEnumerable<string> ConfigurationKeys(ManifestExport manifestExport) => PluginWorkUnitManifest.From(manifestExport).RequiredConfiguration;

    private static void AddAll(IList<string> problems, List<string> found)
    {
        foreach (var problem in found)
        {
            problems.Add(problem);
        }
    }

    private static void CheckDeclaredType(string workUnit, string property, string? declared, string? actual, List<string> problems)
    {
        if (declared is not null && declared != actual)
        {
            problems.Add(actual is null
                ? $"Work unit '{workUnit}' declares {property} '{declared}', but its class has no typed {(property == "inputType" ? "input" : "output")} (derive from WorkUnit<TInput, TOutput>)."
                : $"Work unit '{workUnit}' declares {property} '{declared}', but its class uses '{actual}'.");
        }
    }
}
