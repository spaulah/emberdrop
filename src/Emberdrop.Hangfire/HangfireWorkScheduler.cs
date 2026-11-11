using Emberdrop.Scheduling;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;

namespace Emberdrop.Hangfire;

/// <summary>
/// <see cref="IWorkScheduler"/> implementation built on Hangfire's recurring and background jobs.
/// </summary>
/// <remarks>
/// Hangfire cannot cancel a job that is already running (its cancellation is cooperative through a token the job
/// polls, which Emberdrop's work unit contract does not expose), so <see cref="SchedulerFeatures.CancelRunning"/> is not declared.
/// </remarks>
public sealed class HangfireWorkScheduler : IWorkScheduler
{
    /// <summary>Hangfire has no pause for a recurring entry, so a disabled work unit gets a cron that never matches.</summary>
    internal const string NeverCron = "0 0 31 2 *";

    private const SchedulerFeatures Declared =
        SchedulerFeatures.Recurring
        | SchedulerFeatures.Delayed
        | SchedulerFeatures.CancelQueued
        | SchedulerFeatures.History
        | SchedulerFeatures.Retries;

    private readonly IRecurringJobManager _recurringJobs;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly JobStorage _storage;
    private readonly HangfireDashboardInfo _dashboard;
    private readonly TriggerKindRegistry _triggerKinds;

    /// <summary>Creates the scheduler.</summary>
    public HangfireWorkScheduler(
        IRecurringJobManager recurringJobs,
        IBackgroundJobClient backgroundJobs,
        JobStorage storage,
        HangfireDashboardInfo dashboard,
        TriggerKindRegistry triggerKinds)
    {
        _recurringJobs = recurringJobs;
        _backgroundJobs = backgroundJobs;
        _storage = storage;
        _dashboard = dashboard;
        _triggerKinds = triggerKinds;
    }

    /// <inheritdoc />
    public SchedulerCapabilities Capabilities => new("Hangfire", Declared, _dashboard.Path);

    /// <inheritdoc />
    public Task UpsertRecurringAsync(RecurringWorkSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var cron = spec.Enabled ? spec.Cron : NeverCron;
        var workUnitType = spec.Invocation.WorkUnitType;
        var workUnitId = spec.Invocation.WorkUnitId;
        var trigger = spec.Invocation.Trigger?.ToToken();
        var input = spec.Invocation.Input;

        _recurringJobs.AddOrUpdate<EmberdropHangfireRunner>(
            spec.ScheduleKey,
            runner => runner.RunAsync(workUnitType, workUnitId, trigger, input, JobCancellationToken.Null),
            cron);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveRecurringAsync(string scheduleKey, CancellationToken cancellationToken = default)
    {
        _recurringJobs.RemoveIfExists(scheduleKey);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SchedulerResult<ExecutionId>> TriggerAsync(string scheduleKey, CancellationToken cancellationToken = default)
    {
        // IRecurringJobManager.Trigger does not return the created run's id, so create the run from the stored entry.
        using var connection = _storage.GetConnection();
        var entry = connection.GetRecurringJobs([scheduleKey]).FirstOrDefault(e => e?.Job != null);
        if (entry?.Job is null)
        {
            return Task.FromResult(SchedulerResult.NotFound<ExecutionId>());
        }

        var id = _backgroundJobs.Create(entry.Job, new EnqueuedState());
        return Task.FromResult(SchedulerResult.Ok(new ExecutionId(id)));
    }

    /// <inheritdoc />
    public Task<ExecutionId> EnqueueAsync(WorkInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var workUnitType = invocation.WorkUnitType;
        var workUnitId = invocation.WorkUnitId;
        var trigger = invocation.Trigger?.ToToken();
        var input = invocation.Input;
        var id = _backgroundJobs.Enqueue<EmberdropHangfireRunner>(
            runner => runner.RunAsync(workUnitType, workUnitId, trigger, input, JobCancellationToken.Null));
        return Task.FromResult(new ExecutionId(id));
    }

    /// <inheritdoc />
    public Task<SchedulerResult<ExecutionId>> ScheduleAsync(WorkInvocation invocation, DateTimeOffset runAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var workUnitType = invocation.WorkUnitType;
        var workUnitId = invocation.WorkUnitId;
        var trigger = invocation.Trigger?.ToToken();
        var input = invocation.Input;
        var id = _backgroundJobs.Schedule<EmberdropHangfireRunner>(
            runner => runner.RunAsync(workUnitType, workUnitId, trigger, input, JobCancellationToken.Null),
            runAt);
        return Task.FromResult(SchedulerResult.Ok(new ExecutionId(id)));
    }

    /// <inheritdoc />
    public Task<SchedulerResult<bool>> CancelAsync(ExecutionId runId, CancellationToken cancellationToken = default)
    {
        var details = _storage.GetMonitoringApi().JobDetails(runId.Value);
        var state = HangfireRunMapper.StateOf(details);
        if (state is null)
        {
            return Task.FromResult(SchedulerResult.NotFound<bool>());
        }

        switch (state.Value)
        {
            case ExecutionState.Queued:
            case ExecutionState.Scheduled:
                // Delete only transitions jobs that are still waiting; false means it moved on in the meantime.
                var deleted = _backgroundJobs.Delete(runId.Value);
                return Task.FromResult(SchedulerResult.Ok(deleted));
            case ExecutionState.Running:
                return Task.FromResult(SchedulerResult.Unsupported<bool>(SchedulerFeatures.CancelRunning));
            default:
                return Task.FromResult(SchedulerResult.Ok(false));
        }
    }

    /// <inheritdoc />
    public Task<ExecutionInfo?> GetRunAsync(ExecutionId runId, CancellationToken cancellationToken = default)
    {
        var details = _storage.GetMonitoringApi().JobDetails(runId.Value);
        return Task.FromResult(HangfireRunMapper.ToRunInfo(runId.Value, details, _triggerKinds));
    }

    /// <inheritdoc />
    public Task<SchedulerResult<IReadOnlyList<ExecutionInfo>>> GetHistoryAsync(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var monitoring = _storage.GetMonitoringApi();
        var window = query.Skip + query.Take;
        var finished = new List<ExecutionInfo>();

        foreach (var (id, dto) in monitoring.SucceededJobs(0, window))
        {
            finished.Add(HangfireRunMapper.Finished(_triggerKinds, id, dto.Job, ExecutionState.Succeeded, dto.SucceededAt, null));
        }

        foreach (var (id, dto) in monitoring.FailedJobs(0, window))
        {
            finished.Add(HangfireRunMapper.Finished(_triggerKinds, id, dto.Job, ExecutionState.Failed, dto.FailedAt, dto.ExceptionMessage));
        }

        foreach (var (id, dto) in monitoring.DeletedJobs(0, window))
        {
            finished.Add(HangfireRunMapper.Finished(_triggerKinds, id, dto.Job, ExecutionState.Canceled, dto.DeletedAt, null));
        }

        IReadOnlyList<ExecutionInfo> page = finished
            .Where(r => r.Invocation.WorkUnitType.Length > 0)
            .OrderByDescending(r => r.FinishedAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToList();
        return Task.FromResult(SchedulerResult.Ok(page));
    }
}
