using Emberdrop.Plugins;
using Emberdrop.Scheduling;
using Emberdrop.Storage;
using Microsoft.Extensions.Logging;

namespace Emberdrop.WorkUnits;

/// <summary>
/// Default <see cref="IWorkSchedulerService"/>: translates work unit definitions into scheduler
/// requests and wraps failures in <see cref="WorkUnitManagerException"/>.
/// </summary>
public sealed class WorkSchedulerService : IWorkSchedulerService
{
    private readonly IWorkStore _store;
    private readonly TriggerKindRegistry _triggerKinds;
    private readonly IWorkScheduler _scheduler;
    private readonly IWorkInvoker _invoker;
    private readonly IWorkUnitBindingResolver _bindings;
    private readonly ILogger<WorkSchedulerService> _logger;

    /// <summary>Creates the service.</summary>
    public WorkSchedulerService(IWorkStore store, TriggerKindRegistry triggerKinds, IWorkScheduler scheduler, IWorkInvoker invoker, IWorkUnitBindingResolver bindings, ILogger<WorkSchedulerService> logger)
    {
        _store = store;
        _triggerKinds = triggerKinds;
        _scheduler = scheduler;
        _invoker = invoker;
        _bindings = bindings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ScheduleWorkUnitAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default)
    {
        try
        {
            if (workUnit?.Type == null || workUnit.Identifier == null)
            {
                _logger.LogWarning("Cannot schedule work unit with null Type or Identifier: {WorkUnitId}", workUnit?.WorkUnitId);
                return;
            }

            // A work unit that cannot run now (its plugin is not loaded, a required setting is missing, its input does not fit)
            // is paused: removed from the scheduler but kept in the store, and resumed by the next call once it can run.
            var binding = _bindings.Describe(workUnit);
            if (binding.State != WorkUnitBindingState.Ready)
            {
                await _scheduler.RemoveRecurringAsync(workUnit.Identifier, cancellationToken).ConfigureAwait(false);
                await RemoveTriggerEntriesAsync(workUnit, cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Paused work unit {WorkUnitType} ({Identifier}): {State}. {Reason}", workUnit.Type, workUnit.Identifier, binding.State, binding.Reason);
                return;
            }

            var spec = new RecurringWorkSpec(
                workUnit.Identifier,
                workUnit.Schedule ?? string.Empty,
                workUnit.Enabled,
                new WorkInvocation(workUnit.Type, workUnit.WorkUnitId, string.IsNullOrWhiteSpace(workUnit.Schedule) ? null : new RecurringTrigger(workUnit.Schedule)));

            await _scheduler.UpsertRecurringAsync(spec, cancellationToken).ConfigureAwait(false);
            await ScheduleOperatorTriggersAsync(workUnit, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Scheduled work unit {WorkUnitType} with identifier {Identifier} using schedule {Schedule} (enabled: {Enabled})",
                workUnit.Type, workUnit.Identifier, workUnit.Schedule, workUnit.Enabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule work unit {WorkUnitType} with identifier {Identifier}",
                workUnit?.Type, workUnit?.Identifier);
            throw new WorkUnitManagerException($"Failed to schedule work unit '{workUnit?.Type}'", ex);
        }
    }

    /// <inheritdoc />
    public async Task RemoveWorkUnitAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default)
    {
        try
        {
            if (workUnit?.Identifier == null)
            {
                _logger.LogWarning("Cannot remove work unit with null Identifier: {WorkUnitId}", workUnit?.WorkUnitId);
                return;
            }

            await _scheduler.RemoveRecurringAsync(workUnit.Identifier, cancellationToken).ConfigureAwait(false);
            await RemoveTriggerEntriesAsync(workUnit, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Removed work unit with identifier {Identifier}", workUnit.Identifier);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove work unit with identifier {Identifier}", workUnit?.Identifier);
            throw new WorkUnitManagerException($"Failed to remove work unit '{workUnit?.Identifier}'", ex);
        }
    }

    /// <inheritdoc />
    public async Task RunWorkUnitAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default)
    {
        try
        {
            if (workUnit?.Type == null)
            {
                _logger.LogWarning("Cannot run work unit with null Type: {WorkUnitId}", workUnit?.WorkUnitId);
                return;
            }

            _logger.LogInformation("Running work unit {WorkUnitType} with ID {WorkUnitId}", workUnit.Type, workUnit.WorkUnitId);

            await _invoker.InvokeAsync(new WorkInvocation(workUnit.Type, workUnit.WorkUnitId), cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Completed work unit {WorkUnitType} with ID {WorkUnitId}", workUnit.Type, workUnit.WorkUnitId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run work unit {WorkUnitType} with ID {WorkUnitId}", workUnit?.Type, workUnit?.WorkUnitId);
            throw new WorkUnitManagerException($"Failed to run work unit '{workUnit?.Type}'", ex);
        }
    }

    /// <inheritdoc />
    public async Task<SchedulerResult<ExecutionId>> StartAsync(WorkUnitDefinition workUnit, string? input, DateTimeOffset? runAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workUnit);

        if (runAt is { } at)
        {
            return await _scheduler.ScheduleAsync(new WorkInvocation(workUnit.Type ?? string.Empty, workUnit.WorkUnitId, new OneOffTrigger(at), input), at, cancellationToken).ConfigureAwait(false);
        }

        var id = await _scheduler.EnqueueAsync(new WorkInvocation(workUnit.Type ?? string.Empty, workUnit.WorkUnitId, ManualTrigger.Instance, input), cancellationToken).ConfigureAwait(false);
        return SchedulerResult.Ok(id);
    }

    /// <inheritdoc />
    public Task RemoveTriggerAsync(WorkUnitDefinition workUnit, int triggerId, CancellationToken cancellationToken = default) =>
        _scheduler.RemoveRecurringAsync(TriggerKey(workUnit, triggerId), cancellationToken);

    /// <summary>The scheduler key under which an operator-created recurring trigger is registered.</summary>
    public static string TriggerKey(WorkUnitDefinition workUnit, int triggerId) => $"{workUnit.Identifier}:trigger:{triggerId}";

    private async Task ScheduleOperatorTriggersAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken)
    {
        foreach (var stored in await _store.ListTriggersAsync(workUnit.WorkUnitId, cancellationToken).ConfigureAwait(false))
        {
            Trigger trigger;
            try
            {
                trigger = _triggerKinds.Parse(stored.Definition);
            }
            catch (FormatException ex)
            {
                _logger.LogWarning("Trigger {TriggerId} of work unit {WorkUnitId} cannot be read and is skipped: {Message}", stored.TriggerId, workUnit.WorkUnitId, ex.Message);
                continue;
            }

            if (trigger is RecurringTrigger recurring)
            {
                var spec = new RecurringWorkSpec(
                    TriggerKey(workUnit, stored.TriggerId),
                    recurring.Cron,
                    workUnit.Enabled && stored.Enabled,
                    new WorkInvocation(workUnit.Type ?? string.Empty, workUnit.WorkUnitId, recurring));
                await _scheduler.UpsertRecurringAsync(spec, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RemoveTriggerEntriesAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken)
    {
        foreach (var stored in await _store.ListTriggersAsync(workUnit.WorkUnitId, cancellationToken).ConfigureAwait(false))
        {
            await _scheduler.RemoveRecurringAsync(TriggerKey(workUnit, stored.TriggerId), cancellationToken).ConfigureAwait(false);
        }
    }
}
