using Emberdrop.WorkUnits;
using Emberdrop.Plugins;

namespace Emberdrop.Server.WorkUnitManagement;

/// <summary>
/// Reports the work units that can run: the ones of every loaded plugin version, each listed once per plugin id and type.
/// It reads the plugin catalog only; it never scans the process for assemblies.
/// </summary>
public class WorkUnitDiscoveryService : IWorkUnitDiscoveryService
{
    private readonly IPluginCatalog _plugins;
    private readonly ILogger<WorkUnitDiscoveryService> _logger;

    public WorkUnitDiscoveryService(IPluginCatalog plugins, ILogger<WorkUnitDiscoveryService> logger)
    {
        _plugins = plugins;
        _logger = logger;
    }

    public Task<IReadOnlyList<DiscoveredWorkUnit>> DiscoverWorkUnitsAsync()
    {
        var workUnits = new List<DiscoveredWorkUnit>();

        foreach (var plugin in _plugins.GetPlugins())
        {
            foreach (var workUnit in plugin.WorkUnits())
            {
                var simpleName = workUnit.TypeName[(workUnit.TypeName.LastIndexOf('.') + 1)..];
                if (!workUnits.Any(j => j.PluginId == plugin.Id && j.TypeName == workUnit.TypeName))
                {
                    workUnits.Add(new DiscoveredWorkUnit(simpleName, workUnit.TypeName, workUnit.AssemblyName, plugin.Id));
                }
            }
        }

        _logger.LogInformation("Discovered {Count} work unit(s)", workUnits.Count);
        return Task.FromResult<IReadOnlyList<DiscoveredWorkUnit>>(workUnits);
    }
}
