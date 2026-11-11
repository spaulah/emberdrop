namespace Emberdrop.Storage;

/// <summary>
/// A stored work unit: which work unit type to run, on which schedule, and whether it is enabled.
/// </summary>
public class WorkUnitDefinition
{
    /// <summary>Store-assigned numeric identifier.</summary>
    public int WorkUnitId { get; set; }

    /// <summary>Stable identifier used as the schedule key in the scheduler.</summary>
    public string? Identifier { get; set; }

    /// <summary>Display name.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// What to show as the work unit's type: the logical work unit name for a plugin work unit, the class name for a work unit built into the host.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>The plugin that provides the work unit, or <see langword="null"/> for a work unit built into the host (or a work unit from before bindings existed).</summary>
    public string? PluginId { get; set; }

    /// <summary>The logical work unit name within the plugin, from its manifest.</summary>
    public string? WorkUnitName { get; set; }

    /// <summary>Which plugin versions may run the work unit, as text (see <c>PluginVersionConstraint</c>). Empty means the latest.</summary>
    public string? VersionConstraint { get; set; }

    /// <summary>The schema version of <see cref="Input"/>. A work unit refuses to run on a plugin that expects a different one.</summary>
    public int InputSchemaVersion { get; set; } = 1;

    /// <summary>The schedule the plugin's manifest suggests. Updated by plugin updates; never changes <see cref="ScheduleOverride"/>.</summary>
    public string? DefaultSchedule { get; set; }

    /// <summary>The schedule an operator chose. When set it wins over <see cref="DefaultSchedule"/> and survives plugin updates.</summary>
    public string? ScheduleOverride { get; set; }

    /// <summary>The effective cron expression: the override if there is one, otherwise the plugin's default.</summary>
    public string? Schedule { get; set; }

    /// <summary>Free-form metadata, typically JSON.</summary>
    public string? Input { get; set; }

    /// <summary>Whether the work unit should run on its schedule.</summary>
    public bool Enabled { get; set; }

    /// <summary>When the definition was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When the definition was last changed.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>The kind of work unit (<c>work-unit</c> unless the manifest says otherwise).</summary>
    public string Kind { get; set; } = "work-unit";

    /// <summary>The description the manifest declares, or <see langword="null"/>.</summary>
    public string? Description { get; set; }

    /// <summary>The capabilities the manifest declares, as a JSON array of strings, or <see langword="null"/>. Informational in v0.1.</summary>
    public string? Capabilities { get; set; }

    /// <summary>The JSON Schema of the work unit's input type, or <see langword="null"/> when it takes no input.</summary>
    public string? InputSchema { get; set; }

    /// <summary>The JSON Schema of the work unit's output type, or <see langword="null"/> when it produces no output.</summary>
    public string? OutputSchema { get; set; }
}
