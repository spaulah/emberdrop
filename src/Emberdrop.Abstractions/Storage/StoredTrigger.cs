namespace Emberdrop.Storage;

/// <summary>
/// A trigger an operator added to a stored work unit. Operator triggers are kept apart from the triggers a plugin's manifest
/// declares: the manifest's are defaults that travel with the plugin version; these belong to the operator and survive plugin updates.
/// </summary>
public class StoredTrigger
{
    /// <summary>The trigger's id.</summary>
    public int TriggerId { get; set; }

    /// <summary>The stored work unit the trigger belongs to.</summary>
    public int WorkUnitId { get; set; }

    /// <summary>The kind name (<see cref="Trigger.Kind"/>).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The trigger as its token (<see cref="Trigger.ToToken"/>).</summary>
    public string Definition { get; set; } = string.Empty;

    /// <summary>Whether the trigger is active.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When it was added (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
