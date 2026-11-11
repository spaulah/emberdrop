using Emberdrop.Scheduling;

namespace Emberdrop.WorkUnits;

/// <summary>
/// Runs a single work unit. This is the engine-agnostic entry point that scheduler providers
/// call when a run fires; it keeps work unit type resolution out of the providers.
/// </summary>
public interface IWorkInvoker
{
    /// <summary>Resolves the work unit type named by <paramref name="invocation"/> and runs it.</summary>
    /// <returns>What the work unit returned; <see langword="null"/> if it did not run because its plugin is not available yet and the run is waiting for it.</returns>
    /// <exception cref="WorkUnitFailedException">The work unit reported a failure (<see cref="WorkResult.Failed"/>). A thrown exception propagates as it is.</exception>
    Task<WorkResult?> InvokeAsync(WorkInvocation invocation, CancellationToken cancellationToken = default);
}
