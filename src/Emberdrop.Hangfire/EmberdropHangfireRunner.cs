using Emberdrop.WorkUnits;
using Emberdrop.Scheduling;
using Hangfire;

namespace Emberdrop.Hangfire;

/// <summary>
/// The only type Emberdrop ever hands to Hangfire as a job. It carries plain data (a work unit type name,
/// a work unit id and a trigger token) and resolves the real work unit through <see cref="IWorkInvoker"/> when a run fires, so
/// Hangfire's storage never references plugin types and plugin updates cannot invalidate scheduled work.
/// </summary>
public sealed class EmberdropHangfireRunner
{
    private readonly IWorkInvoker _invoker;
    private readonly TriggerKindRegistry _triggerKinds;

    /// <summary>Creates the runner. Hangfire constructs it from the service provider for each run.</summary>
    public EmberdropHangfireRunner(IWorkInvoker invoker, TriggerKindRegistry triggerKinds)
    {
        _invoker = invoker;
        _triggerKinds = triggerKinds;
    }

    /// <summary>Runs one work unit. Called by Hangfire.</summary>
    /// <param name="workUnitType">Name of the work unit type to run.</param>
    /// <param name="workUnitId">Identifier of the stored work unit definition.</param>
    /// <param name="trigger">The trigger that caused the run, as written by <see cref="Trigger.ToToken"/>; <see langword="null"/> when the invocation carried none (treated as manual).</param>
    /// <param name="input">The input for this run as JSON, or <see langword="null"/> to use the stored work unit's input.</param>
    /// <param name="cancellationToken">Hangfire's cancellation token; signalled on server shutdown.</param>
    public Task RunAsync(string workUnitType, int workUnitId, string? trigger, string? input, IJobCancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cancellationToken);
        return _invoker.InvokeAsync(new WorkInvocation(workUnitType, workUnitId, trigger is null ? null : _triggerKinds.Parse(trigger), input), cancellationToken.ShutdownToken);
    }
}
