namespace Emberdrop.WorkUnits;

/// <summary>Finds the work units that are currently available to run.</summary>
public interface IWorkUnitDiscoveryService
{
    /// <summary>Returns every work unit built into the host plus every work unit of every loaded plugin version.</summary>
    Task<IReadOnlyList<DiscoveredWorkUnit>> DiscoverWorkUnitsAsync();
}
