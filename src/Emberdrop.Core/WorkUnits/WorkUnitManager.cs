using Emberdrop.Storage;
using Microsoft.Extensions.Logging;

namespace Emberdrop.WorkUnits;

/// <summary>Application service that coordinates work unit registration, scheduling and on-demand runs.</summary>
public sealed class WorkUnitManager
{
    private readonly IWorkStore _store;
    private readonly IWorkUnitRegistrationService _registrationService;
    private readonly IWorkSchedulerService _schedulerService;
    private readonly ILogger<WorkUnitManager> _logger;

    /// <summary>Creates the manager.</summary>
    public WorkUnitManager(
        IWorkStore store,
        IWorkUnitRegistrationService registrationService,
        IWorkSchedulerService schedulerService,
        ILogger<WorkUnitManager> logger)
    {
        _store = store;
        _registrationService = registrationService;
        _schedulerService = schedulerService;
        _logger = logger;
    }

    /// <summary>Registers newly discovered work units and schedules every stored work unit.</summary>
    public async Task InitializeWorkUnitsAsync()
    {
        try
        {
            _logger.LogInformation("Initializing work units...");

            await _registrationService.RegisterDiscoveredWorkUnitsAsync();
            await ScheduleAllWorkUnitsAsync();

            _logger.LogInformation("Work unit initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize work units");
            throw new WorkUnitManagerException("Failed to initialize work units", ex);
        }
    }

    /// <summary>Registers and schedules a new work unit.</summary>
    public async Task<WorkUnitDefinition> RegisterWorkUnitAsync(string name, string type, string schedule, bool enabled = true)
    {
        try
        {
            return await _registrationService.RegisterWorkUnitAsync(name, type, schedule, enabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register work unit: {Name}", name);
            throw new WorkUnitManagerException($"Failed to register work unit '{name}'", ex);
        }
    }

    /// <summary>Registers newly discovered work units.</summary>
    public async Task DiscoverAndRegisterWorkUnitsAsync()
    {
        try
        {
            await _registrationService.RegisterDiscoveredWorkUnitsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover and register work units");
            throw new WorkUnitManagerException("Failed to discover and register work units", ex);
        }
    }

    private async Task ScheduleAllWorkUnitsAsync()
    {
        try
        {
            var workUnits = await _store.ListAsync();
            foreach (var workUnit in workUnits)
            {
                await _schedulerService.ScheduleWorkUnitAsync(workUnit);
            }
            _logger.LogInformation("Scheduled {Count} work units", workUnits.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule work units");
            throw new WorkUnitManagerException("Failed to schedule work units", ex);
        }
    }

    /// <summary>Returns the metadata of a work unit, or an empty string when there is none.</summary>
    public async Task<string> GetWorkUnitMetadataAsync(int workUnitId)
    {
        try
        {
            var workUnit = await _store.GetAsync(workUnitId);
            return workUnit?.Input ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metadata for work unit ID: {WorkUnitId}", workUnitId);
            throw new WorkUnitManagerException($"Failed to get metadata for work unit ID '{workUnitId}'", ex);
        }
    }

    /// <summary>Replaces the metadata of a work unit.</summary>
    public async Task UpdateWorkUnitMetadataAsync(int workUnitId, string metadata)
    {
        try
        {
            var workUnit = await _store.GetAsync(workUnitId);
            if (workUnit == null)
            {
                throw new InvalidOperationException($"Work unit with ID {workUnitId} not found");
            }

            workUnit.Input = metadata;
            await _store.UpdateAsync(workUnit);

            _logger.LogInformation("Updated metadata for work unit ID: {WorkUnitId}", workUnitId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update metadata for work unit ID: {WorkUnitId}", workUnitId);
            throw new WorkUnitManagerException($"Failed to update metadata for work unit ID '{workUnitId}'", ex);
        }
    }

    /// <summary>Runs a work unit immediately.</summary>
    public async Task RunWorkUnitAsync(WorkUnitDefinition workUnit)
    {
        try
        {
            await _schedulerService.RunWorkUnitAsync(workUnit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run work unit: {WorkUnitType}", workUnit?.Type);
            throw new WorkUnitManagerException($"Failed to run work unit '{workUnit?.Type}'", ex);
        }
    }

    /// <summary>Removes the scheduled entry for a work unit.</summary>
    public async Task RemoveWorkUnitAsync(WorkUnitDefinition workUnit)
    {
        try
        {
            await _schedulerService.RemoveWorkUnitAsync(workUnit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove work unit: {WorkUnitType}", workUnit?.Type);
            throw new WorkUnitManagerException($"Failed to remove work unit '{workUnit?.Type}'", ex);
        }
    }

}
