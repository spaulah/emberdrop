namespace Emberdrop.Scheduling;

/// <summary>Identifies one execution of a work unit within a scheduler engine. The format is engine-specific; treat it as opaque.</summary>
/// <param name="Value">The engine's identifier for the run.</param>
public readonly record struct ExecutionId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
