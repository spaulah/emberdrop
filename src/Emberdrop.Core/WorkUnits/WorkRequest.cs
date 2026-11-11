using Emberdrop.Plugins;
namespace Emberdrop.WorkUnits;

/// <summary>What the host passes in to run a plugin work unit: the input and what caused the run. The host builds the <see cref="WorkContext"/> from it.</summary>
/// <param name="Input">The input as JSON, or <see langword="null"/> for none.</param>
/// <param name="Trigger">What caused the run; <see langword="null"/> means <see cref="ManualTrigger"/>.</param>
public sealed record WorkRequest(string? Input = null, Trigger? Trigger = null);
