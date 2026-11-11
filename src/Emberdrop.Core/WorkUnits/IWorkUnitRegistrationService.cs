using Emberdrop.Storage;

namespace Emberdrop.WorkUnits;

/// <summary>Creates work unit definitions in the store and keeps the scheduler in step.</summary>
public interface IWorkUnitRegistrationService
{
    /// <summary>Stores a definition for each newly discovered work unit type and schedules known ones.</summary>
    Task RegisterDiscoveredWorkUnitsAsync();

    /// <summary>Stores a new work unit definition and schedules it.</summary>
    Task<WorkUnitDefinition> RegisterWorkUnitAsync(string name, string type, string schedule, bool enabled = true, string? metadata = null);
}
