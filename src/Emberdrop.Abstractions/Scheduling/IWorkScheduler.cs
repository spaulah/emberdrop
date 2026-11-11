namespace Emberdrop.Scheduling;

/// <summary>
/// The scheduling engine behind Emberdrop (for example Hangfire or an in-memory timer).
/// Implementations are scheduler providers.
/// </summary>
/// <remarks>
/// Operations every engine must support are plain methods. Operations that depend on an optional
/// <see cref="SchedulerFeatures"/> return a <see cref="SchedulerResult{T}"/> and report
/// <see cref="SchedulerResultStatus.Unsupported"/> instead of throwing when the feature is not declared in
/// <see cref="Capabilities"/>. Callers can check <see cref="Capabilities"/> first to avoid the round trip.
/// Engines run work by calling the host's invoker with a <see cref="WorkInvocation"/>; they never see plugin types.
/// </remarks>
public interface IWorkScheduler
{
    /// <summary>What this engine supports.</summary>
    SchedulerCapabilities Capabilities { get; }

    /// <summary>Creates or replaces the recurring entry identified by <see cref="RecurringWorkSpec.ScheduleKey"/>.</summary>
    Task UpsertRecurringAsync(RecurringWorkSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Removes a recurring entry. Removing an entry that does not exist is not an error.</summary>
    Task RemoveRecurringAsync(string scheduleKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a recurring entry now, regardless of whether it is enabled.
    /// Needs <see cref="SchedulerFeatures.Recurring"/>; returns not-found when no such entry exists.
    /// </summary>
    Task<SchedulerResult<ExecutionId>> TriggerAsync(string scheduleKey, CancellationToken cancellationToken = default);

    /// <summary>Runs a work unit once, as soon as possible.</summary>
    Task<ExecutionId> EnqueueAsync(WorkInvocation invocation, CancellationToken cancellationToken = default);

    /// <summary>Runs a work unit once at <paramref name="runAt"/>. Needs <see cref="SchedulerFeatures.Delayed"/>.</summary>
    Task<SchedulerResult<ExecutionId>> ScheduleAsync(WorkInvocation invocation, DateTimeOffset runAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a run. A run that has not started needs <see cref="SchedulerFeatures.CancelQueued"/>; a run that is
    /// executing needs <see cref="SchedulerFeatures.CancelRunning"/>. The value is <see langword="true"/> when the run
    /// was cancelled and <see langword="false"/> when it had already finished.
    /// </summary>
    Task<SchedulerResult<bool>> CancelAsync(ExecutionId runId, CancellationToken cancellationToken = default);

    /// <summary>Returns a snapshot of a run, or <see langword="null"/> if the engine does not know it.</summary>
    Task<ExecutionInfo?> GetRunAsync(ExecutionId runId, CancellationToken cancellationToken = default);

    /// <summary>Lists finished runs, most recently finished first. Needs <see cref="SchedulerFeatures.History"/>.</summary>
    Task<SchedulerResult<IReadOnlyList<ExecutionInfo>>> GetHistoryAsync(ExecutionHistoryQuery query, CancellationToken cancellationToken = default);
}
