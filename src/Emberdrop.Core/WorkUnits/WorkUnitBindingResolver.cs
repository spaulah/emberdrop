using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Emberdrop.Storage;

namespace Emberdrop.WorkUnits;

/// <summary>Default <see cref="IWorkUnitBindingResolver"/>: the same resolution the host performs when the work unit fires.</summary>
public sealed class WorkUnitBindingResolver : IWorkUnitBindingResolver
{
    private readonly PluginHost _host;
    private readonly PluginServiceProviderFactory _services;
    private readonly IEnumerable<IHostWorkUnitCatalog> _hostWorkUnits;

    /// <summary>Creates the resolver.</summary>
    public WorkUnitBindingResolver(PluginHost host, PluginServiceProviderFactory services, IEnumerable<IHostWorkUnitCatalog> hostWorkUnits)
    {
        _host = host;
        _services = services;
        _hostWorkUnits = hostWorkUnits;
    }

    /// <inheritdoc />
    public WorkUnitBindingStatus Describe(WorkUnitDefinition workUnit)
    {
        ArgumentNullException.ThrowIfNull(workUnit);

        WorkUnitBinding binding;
        try
        {
            binding = workUnit.ToBinding();
        }
        catch (FormatException ex)
        {
            return new WorkUnitBindingStatus(WorkUnitBindingState.WaitingForPlugin, "The stored version constraint is invalid: " + ex.Message, null);
        }

        if (binding.PluginId is null && _hostWorkUnits.Any(h => h.Provides(binding.WorkUnitName)))
        {
            return WorkUnitBindingStatus.Ready();
        }

        var (plugin, workUnitDescriptor, reason) = BindingResolution.Resolve(_host.GetPlugins(), binding);
        if (plugin is null || workUnitDescriptor is null)
        {
            return new WorkUnitBindingStatus(WorkUnitBindingState.WaitingForPlugin, reason, null);
        }

        if (workUnitDescriptor.InputSchemaVersion != workUnit.InputSchemaVersion)
        {
            return new WorkUnitBindingStatus(
                WorkUnitBindingState.InputSchemaMismatch,
                $"Plugin {plugin.Id} {plugin.Version} expects input with schema version {workUnitDescriptor.InputSchemaVersion}; the stored work unit has {workUnit.InputSchemaVersion}.",
                plugin.Version,
                workUnitDescriptor.Triggers);
        }

        var missing = _services.MissingConfiguration(workUnitDescriptor.RequiredConfiguration);
        return missing.Count > 0
            ? new WorkUnitBindingStatus(WorkUnitBindingState.MissingConfiguration, $"Required configuration is not set: {string.Join(", ", missing)}.", plugin.Version, workUnitDescriptor.Triggers)
            : WorkUnitBindingStatus.Ready(plugin.Version, workUnitDescriptor.Triggers);
    }

    /// <inheritdoc />
    public string? ValidateInput(WorkUnitDefinition workUnit, string? input)
    {
        ArgumentNullException.ThrowIfNull(workUnit);

        if (string.IsNullOrWhiteSpace(workUnit.PluginId))
        {
            return null;
        }

        try
        {
            return _host.ValidateInput(workUnit.ToBinding(), input);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
