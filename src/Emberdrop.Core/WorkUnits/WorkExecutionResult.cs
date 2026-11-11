using Emberdrop.Plugins;
namespace Emberdrop.WorkUnits;

/// <summary>Whether <see cref="PluginHostWorkUnitExtensions.ExecuteWorkUnitAsync"/> ran the work unit, and what it returned.</summary>
/// <param name="Executed"><see langword="true"/> if the work unit ran to completion.</param>
/// <param name="NotExecutedReason">When nothing ran: why no loaded plugin version could run the work unit.</param>
/// <param name="Result">When it ran: the outcome the work unit returned.</param>
public readonly record struct WorkExecutionResult(bool Executed, string? NotExecutedReason, WorkResult? Result = null)
{
    /// <summary>The work unit ran and returned <paramref name="result"/>.</summary>
    public static WorkExecutionResult Completed(WorkResult result) => new(true, null, result);

    /// <summary>Nothing ran, for the stated reason.</summary>
    public static WorkExecutionResult NotExecuted(string reason) => new(false, reason);
}
