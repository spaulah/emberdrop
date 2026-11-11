namespace Emberdrop.Scheduling;

/// <summary>
/// Plain-data description of one work unit execution. It contains no plugin types, so a scheduler
/// engine can persist it safely.
/// </summary>
/// <param name="WorkUnitType">Name of the work unit type to run.</param>
/// <param name="WorkUnitId">Identifier of the stored work unit definition.</param>
/// <param name="Trigger">What causes the execution; <see langword="null"/> means <see cref="ManualTrigger"/>.</param>
/// <param name="Input">Input for this execution as JSON; <see langword="null"/> means the stored work unit's own input.</param>
public sealed record WorkInvocation(string WorkUnitType, int WorkUnitId, Trigger? Trigger = null, string? Input = null);
