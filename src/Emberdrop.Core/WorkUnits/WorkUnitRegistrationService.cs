using Emberdrop.Storage;
using Microsoft.Extensions.Logging;

namespace Emberdrop.WorkUnits;

/// <summary>Default <see cref="IWorkUnitRegistrationService"/>.</summary>
public sealed class WorkUnitRegistrationService : IWorkUnitRegistrationService
{
    private readonly IWorkStore _store;
    private readonly IWorkUnitDiscoveryService _discoveryService;
    private readonly IWorkSchedulerService _schedulerService;
    private readonly ILogger<WorkUnitRegistrationService> _logger;

    /// <summary>Creates the service.</summary>
    public WorkUnitRegistrationService(
        IWorkStore store,
        IWorkUnitDiscoveryService discoveryService,
        IWorkSchedulerService schedulerService,
        ILogger<WorkUnitRegistrationService> logger)
    {
        _store = store;
        _discoveryService = discoveryService;
        _schedulerService = schedulerService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RegisterDiscoveredWorkUnitsAsync()
    {
        try
        {
            _logger.LogInformation("Starting work unit discovery and registration process");

            var discoveredWorkUnits = await _discoveryService.DiscoverWorkUnitsAsync();
            var existingWorkUnits = await _store.ListAsync();
            var existingWorkUnitTypes = existingWorkUnits.Select(j => j.Type).ToHashSet();

            var newWorkUnitsCount = 0;
            var scheduledWorkUnitsCount = 0;

            foreach (var discovered in discoveredWorkUnits.Where(d => d.PluginId is null))
            {
                // Work units that plugins provide are registered by the plugin work unit reconciler, which knows their manifest.
                var typeName = discovered.TypeName;
                var simpleName = discovered.Name;

                // Check if work unit already exists by simple name (since we store simple names)
                var workUnitExists = existingWorkUnitTypes.Contains(simpleName);

                if (!workUnitExists)
                {
                    await CreateNewWorkUnitAsync(simpleName, typeName);
                    newWorkUnitsCount++;
                }

                // Find existing work unit by simple name for scheduling
                var registeredWorkUnit = existingWorkUnits.FirstOrDefault(j => j.Type == simpleName);
                if (registeredWorkUnit != null)
                {
                    await _schedulerService.ScheduleWorkUnitAsync(registeredWorkUnit);
                    scheduledWorkUnitsCount++;
                }
            }

            _logger.LogInformation("Work unit registration completed. New work units: {NewWorkUnits}, Scheduled work units: {ScheduledWorkUnits}",
                newWorkUnitsCount, scheduledWorkUnitsCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register discovered work units");
            throw new WorkUnitManagerException("Failed to register discovered work units", ex);
        }
    }

    /// <inheritdoc />
    public async Task<WorkUnitDefinition> RegisterWorkUnitAsync(string name, string type, string schedule, bool enabled = true, string? metadata = null)
    {
        try
        {
            _logger.LogInformation("Registering new work unit: {Name} of type {Type}", name, type);

            var newWorkUnit = new WorkUnitDefinition
            {
                Identifier = GenerateWorkUnitGuid(name),
                Name = name,
                Type = type,
                Enabled = enabled,
                Schedule = schedule,
                Input = metadata,
            };

            var workUnitId = await _store.AddAsync(newWorkUnit);
            var savedWorkUnit = await _store.GetAsync(workUnitId);

            if (savedWorkUnit != null)
            {
                await _schedulerService.ScheduleWorkUnitAsync(savedWorkUnit);
            }

            _logger.LogInformation("Successfully registered and scheduled work unit: {Name}", name);
            return savedWorkUnit ?? newWorkUnit;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register work unit: {Name} of type {Type}", name, type);
            throw new WorkUnitManagerException($"Failed to register work unit '{name}'", ex);
        }
    }

    private async Task CreateNewWorkUnitAsync(string simpleName, string fullTypeName)
    {
        var newWorkUnit = new WorkUnitDefinition
        {
            Identifier = GenerateWorkUnitGuid(simpleName),
            Name = simpleName,
            Type = simpleName, // Store simple name for API consistency
            Enabled = false,
            Schedule = WorkUnitScheduleDefaults.NeverSchedule,
            Input = null,
        };

        await _store.AddAsync(newWorkUnit);
        _logger.LogInformation("Created new work unit entry for type: {TypeName} with name: {Name}", simpleName, simpleName);
    }

    private static string GenerateWorkUnitGuid(string name)
    {
        var guid = Guid.NewGuid();
        var workUnitGuid = $"{name.Trim()}-{guid}";
        return workUnitGuid.Length > 30 ? workUnitGuid.Substring(0, 30) : workUnitGuid;
    }
}
