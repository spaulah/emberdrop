using Emberdrop.Scheduling;
using Emberdrop.Storage;
using Emberdrop.WorkUnits;

namespace Emberdrop.Server.Infrastructure;

/// <summary>
/// Runs a work unit by resolving the stored work unit's binding (plugin, version constraint, logical name) to a loaded plugin version
/// through <see cref="PluginWorkUnitRunner"/>, which keeps that version loaded for as long as the run is in flight.
/// </summary>
public class WorkInvoker : IWorkInvoker
{
    private readonly PluginWorkUnitRunner _plugins;
    private readonly IWorkStore _store;

    public WorkInvoker(PluginWorkUnitRunner plugins, IWorkStore store)
    {
        _plugins = plugins;
        _store = store;
    }

    public async Task<WorkResult?> InvokeAsync(WorkInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var definition = await _store.GetAsync(invocation.WorkUnitId, cancellationToken)
            ?? new WorkUnitDefinition { WorkUnitId = invocation.WorkUnitId, Type = invocation.WorkUnitType };
        var trigger = invocation.Trigger ?? ManualTrigger.Instance;

        // Null means the plugin was not available and the run is waiting for it (or an exception explains why it failed).
        var result = await _plugins.RunAsync(definition, new WorkRequest(invocation.Input ?? definition.Input, trigger), cancellationToken).ConfigureAwait(false);

        if (result is { IsSuccess: false })
        {
            throw new WorkUnitFailedException(definition.WorkUnitName ?? invocation.WorkUnitType, result.ErrorCode!, result.ErrorMessage!);
        }

        return result;
    }
}
