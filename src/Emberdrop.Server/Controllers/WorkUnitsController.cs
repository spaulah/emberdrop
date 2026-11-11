using Microsoft.AspNetCore.Mvc;
using Emberdrop;
using Emberdrop.WorkUnits;
using Emberdrop.Plugins;
using Emberdrop.Server.WorkUnitManagement;
using Emberdrop.Server.Models;
using Emberdrop.Storage;

namespace Emberdrop.Server.Controllers;

[ApiController]
[Route("api/work-units")]
public class WorkUnitsController : ControllerBase
{
    private readonly IWorkStore _workStore;
    private readonly IWorkUnitDiscoveryService _workUnitDiscoveryService;
    private readonly IWorkUnitRegistrationService _workUnitRegistrationService;
    private readonly IWorkSchedulerService _workUnitScheduling;
    private readonly IWorkUnitBindingResolver _bindings;
    private readonly TriggerKindRegistry _triggerKinds;
    private readonly ILogger<WorkUnitsController> _logger;

    public WorkUnitsController(
        IWorkStore workStore,
        IWorkUnitDiscoveryService workUnitDiscoveryService,
        IWorkUnitRegistrationService workUnitRegistrationService,
        IWorkSchedulerService workUnitScheduling,
        IWorkUnitBindingResolver bindings,
        TriggerKindRegistry triggerKinds,
        ILogger<WorkUnitsController> logger)
    {
        _workStore = workStore;
        _workUnitDiscoveryService = workUnitDiscoveryService;
        _workUnitRegistrationService = workUnitRegistrationService;
        _workUnitScheduling = workUnitScheduling;
        _bindings = bindings;
        _triggerKinds = triggerKinds;
        _logger = logger;
    }

    /// <summary>
    /// Gets all work units
    /// </summary>
    /// <returns>List of all work units</returns>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<WorkUnitDto>>>> GetAllWorkUnits()
    {
        try
        {
            var workUnits = await _workStore.ListAsync();
            var triggers = (await _workStore.ListTriggersAsync()).ToLookup(t => t.WorkUnitId);
            var workUnitDtos = workUnits.Select(w => ToDto(w, triggers[w.WorkUnitId])).ToList();

            return Ok(new ApiResponse<IEnumerable<WorkUnitDto>>
            {
                Data = workUnitDtos,
                Message = "Work units retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all work units");
            return StatusCode(500, new ApiResponse<IEnumerable<WorkUnitDto>>
            {
                Success = false,
                Message = "Internal server error",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    /// <summary>
    /// Gets a specific work unit by ID
    /// </summary>
    /// <param name="id">Work unit ID</param>
    /// <returns>Work unit details</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<WorkUnitDto>>> GetWorkUnit(int id)
    {
        try
        {
            var workUnit = await _workStore.GetAsync(id);
            if (workUnit == null)
            {
                return NotFound(new ApiResponse<WorkUnitDto>
                {
                    Success = false,
                    Message = $"Work unit with ID {id} not found"
                });
            }

            return Ok(new ApiResponse<WorkUnitDto>
            {
                Data = ToDto(workUnit, await _workStore.ListTriggersAsync(workUnit.WorkUnitId)),
                Message = "Work unit retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving work unit with ID {WorkUnitId}", id);
            return StatusCode(500, new ApiResponse<WorkUnitDto>
            {
                Success = false,
                Message = "Internal server error",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    /// <summary>
    /// Creates a new work unit
    /// </summary>
    /// <param name="request">Work unit creation request</param>
    /// <returns>Created work unit details</returns>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<WorkUnitDto>>> CreateWorkUnit([FromBody] CreateWorkUnitRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<WorkUnitDto>
                {
                    Success = false,
                    Message = "Invalid request data",
                    Errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList()
                });
            }

            if (!PluginVersionConstraint.TryParse(request.VersionConstraint, out _))
            {
                return BadRequest(InvalidConstraint<WorkUnitDto>(request.VersionConstraint));
            }

            if (request.PluginId != null && request.Input != null)
            {
                var problem = _bindings.ValidateInput(
                    new WorkUnitDefinition { PluginId = request.PluginId, WorkUnitName = request.WorkUnitName ?? request.Type, Type = request.Type, VersionConstraint = request.VersionConstraint },
                    request.Input);
                if (problem != null)
                {
                    return BadRequest(InvalidInput<WorkUnitDto>(problem));
                }
            }

            // Use the work unit registration service to register and schedule the work unit
            var systemWorkUnit = await _workUnitRegistrationService.RegisterWorkUnitAsync(
                request.Name!,
                request.Type!,
                request.Schedule ?? "",
                request.Enabled,
                request.Input);

            if (request.PluginId != null || request.WorkUnitName != null || request.VersionConstraint != null || request.InputSchemaVersion != null)
            {
                systemWorkUnit.PluginId = request.PluginId;
                systemWorkUnit.WorkUnitName = request.WorkUnitName;
                systemWorkUnit.VersionConstraint = request.VersionConstraint;
                systemWorkUnit.InputSchemaVersion = request.InputSchemaVersion ?? 1;
                await _workStore.UpdateAsync(systemWorkUnit);
                await _workUnitScheduling.ScheduleWorkUnitAsync(systemWorkUnit);
            }

            return CreatedAtAction(
                nameof(GetWorkUnit),
                new { id = systemWorkUnit.WorkUnitId },
                new ApiResponse<WorkUnitDto>
                {
                    Data = ToDto(systemWorkUnit),
                    Message = "Work unit created successfully"
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating work unit");
            return StatusCode(500, new ApiResponse<WorkUnitDto>
            {
                Success = false,
                Message = "Internal server error",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    /// <summary>
    /// Updates an existing work unit
    /// </summary>
    /// <remarks>
    /// Changing the schedule records it as the operator's override, which a plugin update never replaces; setting it back to the
    /// plugin's default clears the override. The schedule change takes effect immediately.
    /// </remarks>
    /// <param name="id">Work unit ID</param>
    /// <param name="request">Work unit update request</param>
    /// <returns>Updated work unit details</returns>
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<WorkUnitDto>>> UpdateWorkUnit(int id, [FromBody] UpdateWorkUnitRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<WorkUnitDto>
                {
                    Success = false,
                    Message = "Invalid request data",
                    Errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList()
                });
            }

            if (!PluginVersionConstraint.TryParse(request.VersionConstraint, out _))
            {
                return BadRequest(InvalidConstraint<WorkUnitDto>(request.VersionConstraint));
            }

            var existingWorkUnit = await _workStore.GetAsync(id);
            if (existingWorkUnit == null)
            {
                return NotFound(new ApiResponse<WorkUnitDto>
                {
                    Success = false,
                    Message = $"Work unit with ID {id} not found"
                });
            }

            if (request.Input != null && existingWorkUnit.PluginId != null)
            {
                var problem = _bindings.ValidateInput(existingWorkUnit, request.Input);
                if (problem != null)
                {
                    return BadRequest(InvalidInput<WorkUnitDto>(problem));
                }
            }

            var previousIdentifier = existingWorkUnit.Identifier;

            // Update the work unit properties
            existingWorkUnit.Identifier = request.Identifier;
            existingWorkUnit.Name = request.Name;
            existingWorkUnit.Type = request.Type;
            existingWorkUnit.Input = request.Input;
            existingWorkUnit.Enabled = request.Enabled;
            existingWorkUnit.UpdatedAt = DateTime.UtcNow;
            ApplyScheduleChange(existingWorkUnit, request.Schedule);

            if (request.VersionConstraint != null)
            {
                existingWorkUnit.VersionConstraint = string.IsNullOrWhiteSpace(request.VersionConstraint) ? null : request.VersionConstraint;
            }

            if (request.InputSchemaVersion != null)
            {
                existingWorkUnit.InputSchemaVersion = request.InputSchemaVersion.Value;
            }

            await _workStore.UpdateAsync(existingWorkUnit);
            if (previousIdentifier != null && previousIdentifier != existingWorkUnit.Identifier)
            {
                // The scheduler knows the work unit by its identifier; do not leave the old entry running.
                await _workUnitScheduling.RemoveWorkUnitAsync(new WorkUnitDefinition { WorkUnitId = existingWorkUnit.WorkUnitId, Identifier = previousIdentifier });
            }

            await _workUnitScheduling.ScheduleWorkUnitAsync(existingWorkUnit);

            return Ok(new ApiResponse<WorkUnitDto>
            {
                Data = ToDto(existingWorkUnit, await _workStore.ListTriggersAsync(existingWorkUnit.WorkUnitId)),
                Message = "Work unit updated successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating work unit with ID {WorkUnitId}", id);
            return StatusCode(500, new ApiResponse<WorkUnitDto>
            {
                Success = false,
                Message = "Internal server error",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    /// <summary>
    /// Deletes a work unit
    /// </summary>
    /// <param name="id">Work unit ID</param>
    /// <returns>Deletion result</returns>
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteWorkUnit(int id)
    {
        try
        {
            var existingWorkUnit = await _workStore.GetAsync(id);
            if (existingWorkUnit == null)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Work unit with ID {id} not found"
                });
            }

            // A deleted work unit must stop firing, and its operator triggers are looked up through the store, so unschedule first.
            await _workUnitScheduling.RemoveWorkUnitAsync(existingWorkUnit);

            var deletedCount = await _workStore.DeleteAsync(id);
            if (deletedCount == 0)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Work unit with ID {id} could not be deleted"
                });
            }

            return Ok(new ApiResponse<object>
            {
                Message = "Work unit deleted successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting work unit with ID {WorkUnitId}", id);
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "Internal server error",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    /// <summary>Lists the triggers an operator added to a work unit (the ones its plugin declares are in <c>declaredTriggers</c> of the work unit).</summary>
    [HttpGet("{id}/triggers")]
    public async Task<ActionResult<ApiResponse<IEnumerable<TriggerDto>>>> GetTriggers(int id)
    {
        if (await _workStore.GetAsync(id) == null)
        {
            return NotFound(new ApiResponse<IEnumerable<TriggerDto>> { Success = false, Message = $"Work unit with ID {id} not found" });
        }

        var triggers = await _workStore.ListTriggersAsync(id);
        return Ok(new ApiResponse<IEnumerable<TriggerDto>> { Data = triggers.Select(ToDto).ToList(), Message = "Triggers retrieved successfully" });
    }

    /// <summary>
    /// Adds a trigger to a work unit. The body is a trigger object with a <c>kind</c>, for example
    /// <c>{ "kind": "recurring", "cron": "0 9 * * *" }</c>.
    /// </summary>
    /// <remarks>
    /// v0.1 stores recurring triggers only. A manual or one-off execution is requested with <c>POST /api/work-units/{id}/executions</c>
    /// and is recorded with its trigger by the scheduler; it is not a standing trigger.
    /// </remarks>
    [HttpPost("{id}/triggers")]
    public async Task<ActionResult<ApiResponse<TriggerDto>>> AddTrigger(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        var workUnit = await _workStore.GetAsync(id);
        if (workUnit == null)
        {
            return NotFound(new ApiResponse<TriggerDto> { Success = false, Message = $"Work unit with ID {id} not found" });
        }

        Trigger trigger;
        try
        {
            trigger = _triggerKinds.FromJson(body);
        }
        catch (FormatException ex)
        {
            return BadRequest(new ApiResponse<TriggerDto> { Success = false, Message = "Invalid request data", Errors = [char.ToUpperInvariant(ex.Message[0]) + ex.Message[1..]] });
        }

        if (trigger is not RecurringTrigger)
        {
            return BadRequest(new ApiResponse<TriggerDto>
            {
                Success = false,
                Message = "Invalid request data",
                Errors = [$"A '{trigger.Kind}' trigger cannot be stored: v0.1 stores recurring triggers only. Use POST /api/work-units/{id}/executions to start a manual or one-off execution."],
            });
        }

        var stored = new StoredTrigger { WorkUnitId = id, Kind = trigger.Kind, Definition = trigger.ToToken(), Enabled = true };
        stored.TriggerId = await _workStore.AddTriggerAsync(stored);
        await _workUnitScheduling.ScheduleWorkUnitAsync(workUnit);

        return CreatedAtAction(nameof(GetTriggers), new { id }, new ApiResponse<TriggerDto> { Data = ToDto(stored), Message = "Trigger added successfully" });
    }

    /// <summary>Deletes a trigger an operator added.</summary>
    [HttpDelete("{id}/triggers/{triggerId}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTrigger(int id, int triggerId)
    {
        var workUnit = await _workStore.GetAsync(id);
        var trigger = (await _workStore.ListTriggersAsync(id)).FirstOrDefault(t => t.TriggerId == triggerId);
        if (workUnit == null || trigger == null)
        {
            return NotFound(new ApiResponse<object> { Success = false, Message = $"Trigger {triggerId} of work unit {id} not found" });
        }

        await _workUnitScheduling.RemoveTriggerAsync(workUnit, triggerId);
        await _workStore.DeleteTriggerAsync(triggerId);
        return Ok(new ApiResponse<object> { Message = "Trigger deleted successfully" });
    }

    /// <summary>
    /// Starts one execution of a work unit: now, with a manual trigger, or at <c>runAt</c>, with a one-off trigger. The input, if
    /// given, replaces the stored input for this execution and is checked against the work unit's input type first.
    /// </summary>
    [HttpPost("{id}/executions")]
    public async Task<ActionResult<ApiResponse<ExecutionStartedDto>>> StartExecution(int id, [FromBody] StartExecutionRequest request)
    {
        var workUnit = await _workStore.GetAsync(id);
        if (workUnit == null)
        {
            return NotFound(new ApiResponse<ExecutionStartedDto> { Success = false, Message = $"Work unit with ID {id} not found" });
        }

        var binding = _bindings.Describe(workUnit);
        if (binding.State != WorkUnitBindingState.Ready)
        {
            return Conflict(new ApiResponse<ExecutionStartedDto> { Success = false, Message = "The work unit cannot run now", Errors = [binding.Reason ?? binding.State.ToString()] });
        }

        var input = request.Input is { ValueKind: not System.Text.Json.JsonValueKind.Undefined and not System.Text.Json.JsonValueKind.Null } given ? given.GetRawText() : null;
        var problem = _bindings.ValidateInput(workUnit, input ?? workUnit.Input);
        if (problem != null)
        {
            return BadRequest(InvalidInput<ExecutionStartedDto>(problem));
        }

        var result = await _workUnitScheduling.StartAsync(workUnit, input, request.RunAt);
        if (!result.IsOk)
        {
            return StatusCode(501, new ApiResponse<ExecutionStartedDto>
            {
                Success = false,
                Message = "The scheduler cannot do that",
                Errors = [$"The scheduler does not support {result.MissingFeature}."],
            });
        }

        Trigger trigger = request.RunAt is { } at ? new OneOffTrigger(at) : ManualTrigger.Instance;
        return Accepted(new ApiResponse<ExecutionStartedDto>
        {
            Data = new ExecutionStartedDto { ExecutionId = result.Value.Value, Trigger = trigger.ToToken() },
            Message = "Execution started",
        });
    }

    /// <summary>
    /// Gets all available work unit types (from the server and from loaded plugins)
    /// </summary>
    /// <returns>List of available work unit types</returns>
    [HttpGet("types")]
    public async Task<ActionResult<ApiResponse<IEnumerable<WorkUnitTypeDto>>>> GetWorkUnitTypes()
    {
        try
        {
            var discovered = await _workUnitDiscoveryService.DiscoverWorkUnitsAsync();

            // One entry per type name; if several plugin versions provide the same work unit, the first one listed wins.
            var workUnitTypes = discovered
                .GroupBy(workUnit => workUnit.TypeName)
                .Select(group => group.First())
                .Select(workUnit => new WorkUnitTypeDto
                {
                    Name = workUnit.Name,
                    FullName = workUnit.TypeName,
                    AssemblyName = workUnit.AssemblyName,
                    IsFromPlugin = workUnit.PluginId is not null
                })
                .ToDictionary(dto => dto.FullName!);

            return Ok(new ApiResponse<IEnumerable<WorkUnitTypeDto>>
            {
                Data = workUnitTypes.Values.OrderBy(jt => jt.Name),
                Message = "Work unit types retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving work unit types");
            return StatusCode(500, new ApiResponse<IEnumerable<WorkUnitTypeDto>>
            {
                Success = false,
                Message = "Internal server error",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    /// <summary>
    /// A changed schedule becomes the operator's override; the plugin's default is left alone. Returning to the default
    /// clears the override. An unchanged or missing schedule changes nothing.
    /// </summary>
    private static void ApplyScheduleChange(WorkUnitDefinition workUnit, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested) || requested == workUnit.Schedule)
        {
            return;
        }

        workUnit.ScheduleOverride = requested == workUnit.DefaultSchedule ? null : requested;
        workUnit.Schedule = requested;
    }

    private WorkUnitDto ToDto(WorkUnitDefinition workUnit, IEnumerable<StoredTrigger>? triggers = null)
    {
        var binding = _bindings.Describe(workUnit);
        return new WorkUnitDto
        {
            WorkUnitId = workUnit.WorkUnitId,
            Identifier = workUnit.Identifier,
            Name = workUnit.Name,
            Type = workUnit.Type,
            Schedule = workUnit.Schedule,
            Input = workUnit.Input,
            Enabled = workUnit.Enabled,
            CreatedAt = workUnit.CreatedAt,
            UpdatedAt = workUnit.UpdatedAt,
            PluginId = workUnit.PluginId,
            WorkUnitName = workUnit.WorkUnitName,
            VersionConstraint = workUnit.VersionConstraint,
            InputSchemaVersion = workUnit.InputSchemaVersion,
            DefaultSchedule = workUnit.DefaultSchedule,
            ScheduleOverride = workUnit.ScheduleOverride,
            Kind = workUnit.Kind,
            Description = workUnit.Description,
            Capabilities = ReadCapabilities(workUnit.Capabilities),
            InputSchema = ReadJson(workUnit.InputSchema),
            OutputSchema = ReadJson(workUnit.OutputSchema),
            DeclaredTriggers = (binding.DeclaredTriggers ?? []).Select(t => t.ToToken()).ToList(),
            Triggers = (triggers ?? []).Select(ToDto).ToList(),
            Status = binding.State.ToString(),
            StatusReason = binding.Reason,
        };
    }

    private static TriggerDto ToDto(StoredTrigger trigger) => new()
    {
        TriggerId = trigger.TriggerId,
        Kind = trigger.Kind,
        Definition = trigger.Definition,
        Enabled = trigger.Enabled,
        CreatedAt = trigger.CreatedAt,
    };

    private static IReadOnlyList<string> ReadCapabilities(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static System.Text.Json.JsonElement? ReadJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

    private static ApiResponse<T> InvalidInput<T>(string problem) => new()
    {
        Success = false,
        Message = "Invalid request data",
        Errors = [problem],
    };

    private static ApiResponse<T> InvalidConstraint<T>(string? text) => new()
    {
        Success = false,
        Message = "Invalid request data",
        Errors = [$"'{text}' is not a valid version constraint. Use '*' for the latest, '1.2.3' to pin one version, or '^1.2' for the latest with the same major version."],
    };
}
