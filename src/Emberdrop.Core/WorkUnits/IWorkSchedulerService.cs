using Emberdrop.Scheduling;
using Emberdrop.Storage;

namespace Emberdrop.WorkUnits;

/// <summary>Applies stored <see cref="WorkUnitDefinition"/>s to the configured scheduler and runs them on demand.</summary>
public interface IWorkSchedulerService
{
    /// <summary>Creates or updates the recurring entry for a work unit definition.</summary>
    Task ScheduleWorkUnitAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default);

    /// <summary>Removes the recurring entry for a work unit definition.</summary>
    Task RemoveWorkUnitAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default);

    /// <summary>Runs a work unit immediately, outside its schedule.</summary>
    Task RunWorkUnitAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default);

    /// <summary>Removes the scheduler entry of one operator-created trigger. Call it before the trigger row is deleted.</summary>
    Task RemoveTriggerAsync(WorkUnitDefinition workUnit, int triggerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts one execution of the stored work unit through the scheduler: now (a <see cref="ManualTrigger"/>) or at <paramref name="runAt"/>
    /// (a <see cref="OneOffTrigger"/>). The execution is recorded by the scheduler with that trigger.
    /// </summary>
    /// <param name="workUnit">The stored work unit.</param>
    /// <param name="input">Input for this execution as JSON, or <see langword="null"/> to use the stored input.</param>
    /// <param name="runAt">When to run, or <see langword="null"/> for now.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The execution id, or why the scheduler could not do it (for example it has no delayed runs).</returns>
    Task<SchedulerResult<ExecutionId>> StartAsync(WorkUnitDefinition workUnit, string? input, DateTimeOffset? runAt, CancellationToken cancellationToken = default);
}
