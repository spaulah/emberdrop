using System.ComponentModel.DataAnnotations;

namespace Emberdrop.Server.Models;

public class WorkUnitDto
{
    public int WorkUnitId { get; set; }
    public string? Identifier { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Schedule { get; set; }
    public string? Input { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>The plugin that provides the work unit, or null for a built-in work unit.</summary>
    public string? PluginId { get; set; }

    /// <summary>The logical work unit name within the plugin.</summary>
    public string? WorkUnitName { get; set; }

    /// <summary>Which plugin versions may run the work unit: empty or "*" for the latest, "1.2.3" to pin, "^1" for the latest within a major.</summary>
    public string? VersionConstraint { get; set; }

    public int InputSchemaVersion { get; set; } = 1;

    /// <summary>The schedule the plugin suggests.</summary>
    public string? DefaultSchedule { get; set; }

    /// <summary>The schedule an operator chose; it wins over the default and survives plugin updates.</summary>
    public string? ScheduleOverride { get; set; }

    /// <summary>Ready, WaitingForPlugin, MissingConfiguration or InputSchemaMismatch.</summary>
    /// <summary>The kind of work unit (<c>work-unit</c> unless the plugin's manifest says otherwise).</summary>
    public string Kind { get; set; } = "work-unit";

    /// <summary>The description from the plugin's manifest, if any.</summary>
    public string? Description { get; set; }

    /// <summary>The capabilities the plugin's manifest declares. Informational in v0.1: they are validated and shown, not enforced (see docs/concepts/capabilities.md).</summary>
    public IReadOnlyList<string> Capabilities { get; set; } = [];

    /// <summary>The JSON Schema of the work unit's input, if it takes typed input.</summary>
    public System.Text.Json.JsonElement? InputSchema { get; set; }

    /// <summary>The JSON Schema of the work unit's output, if it produces typed output.</summary>
    public System.Text.Json.JsonElement? OutputSchema { get; set; }

    /// <summary>The triggers the plugin version declares as defaults, as tokens (for example <c>recurring:0 2 * * *</c>).</summary>
    public IReadOnlyList<string> DeclaredTriggers { get; set; } = [];

    /// <summary>The triggers an operator added. They are kept apart from the declared ones and survive plugin updates.</summary>
    public IReadOnlyList<TriggerDto> Triggers { get; set; } = [];

    public string Status { get; set; } = "Ready";

    /// <summary>Why the work unit cannot run, when Status is not Ready.</summary>
    public string? StatusReason { get; set; }
}

public class CreateWorkUnitRequest
{
    [Required]
    [StringLength(255)]
    public string? Identifier { get; set; }

    [Required]
    [StringLength(255)]
    public string? Name { get; set; }

    [Required]
    [StringLength(500)]
    public string? Type { get; set; }

    [StringLength(255)]
    public string? Schedule { get; set; }

    public string? Input { get; set; }

    public bool Enabled { get; set; } = true;

    [StringLength(100)]
    public string? PluginId { get; set; }

    [StringLength(100)]
    public string? WorkUnitName { get; set; }

    [StringLength(50)]
    public string? VersionConstraint { get; set; }

    [Range(1, int.MaxValue)]
    public int? InputSchemaVersion { get; set; }
}

public class UpdateWorkUnitRequest
{
    [StringLength(255)]
    public string? Identifier { get; set; }

    [StringLength(255)]
    public string? Name { get; set; }

    [StringLength(500)]
    public string? Type { get; set; }

    [StringLength(255)]
    public string? Schedule { get; set; }

    public string? Input { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Optional. Pins or unpins the work unit: "1.2.3", "^1" or "*". Empty clears the constraint (latest).</summary>
    [StringLength(50)]
    public string? VersionConstraint { get; set; }

    /// <summary>Optional. The schema version of Input; set it together with new Input when a plugin changes its input.</summary>
    [Range(1, int.MaxValue)]
    public int? InputSchemaVersion { get; set; }
}

/// <summary>An operator-created trigger of a stored work unit.</summary>
public class TriggerDto
{
    public int TriggerId { get; set; }

    public string Kind { get; set; } = string.Empty;

    /// <summary>The trigger as a token, for example <c>recurring:0 9 * * *</c>.</summary>
    public string Definition { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Request to start one execution of a stored work unit.</summary>
public class StartExecutionRequest
{
    /// <summary>Input for this execution as JSON. When omitted the stored work unit's own input is used.</summary>
    public System.Text.Json.JsonElement? Input { get; set; }

    /// <summary>When to run. When omitted the execution starts now (a manual trigger); otherwise it is a one-off trigger at that time.</summary>
    public DateTimeOffset? RunAt { get; set; }
}

/// <summary>An execution that was started.</summary>
public class ExecutionStartedDto
{
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>The trigger recorded for the execution, as a token.</summary>
    public string Trigger { get; set; } = string.Empty;
}

/// <summary>One execution as the scheduler recorded it.</summary>
public class ExecutionDto
{
    public string ExecutionId { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string WorkUnitType { get; set; } = string.Empty;

    public int WorkUnitId { get; set; }

    /// <summary>The trigger that caused the execution, as a token, or <see langword="null"/> when the scheduler recorded none.</summary>
    public string? Trigger { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? Error { get; set; }
}

public class WorkUnitTypeDto
{
    public string? Name { get; set; }
    public string? FullName { get; set; }
    public string? AssemblyName { get; set; }
    public bool IsFromPlugin { get; set; }
}

public class ApiResponse<T>
{
    public bool Success { get; set; } = true;
    public T? Data { get; set; }
    public string? Message { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class SchedulerInfoDto
{
    public string? Engine { get; set; }
    public IEnumerable<string> Features { get; set; } = Enumerable.Empty<string>();
    public string? DashboardPath { get; set; }
}
