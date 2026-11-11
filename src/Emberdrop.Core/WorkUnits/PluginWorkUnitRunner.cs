using Emberdrop.Plugins;
using Emberdrop.Scheduling;
using Emberdrop.Storage;
using Microsoft.Extensions.Logging;

namespace Emberdrop.WorkUnits;

/// <summary>
/// Runs a stored work unit that is bound to a plugin, and decides what happens when no loaded version matches.
/// </summary>
public sealed class PluginWorkUnitRunner
{
    private readonly PluginHost _host;
    private readonly WorkUnitOptions _options;
    private readonly WaitingInvocations _waiting;
    private readonly ILogger<PluginWorkUnitRunner> _logger;

    /// <summary>Creates the runner.</summary>
    public PluginWorkUnitRunner(PluginHost host, WorkUnitOptions options, WaitingInvocations waiting, ILogger<PluginWorkUnitRunner> logger)
    {
        _host = host;
        _options = options;
        _waiting = waiting;
        _logger = logger;
    }

    /// <summary>
    /// Runs the work unit on the plugin version its binding selects.
    /// </summary>
    /// <returns>What the work unit returned; <see langword="null"/> if no loaded version matched and the policy is to wait.</returns>
    /// <exception cref="PluginNotAvailableException">No version matched and the policy is <see cref="MissingPluginPolicy.Fail"/>.</exception>
    /// <exception cref="InputSchemaMismatchException">The selected version expects different input; the work unit did not start.</exception>
    public async Task<WorkResult?> RunAsync(WorkUnitDefinition workUnit, WorkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workUnit);
        ArgumentNullException.ThrowIfNull(request);

        var binding = workUnit.ToBinding();
        var result = await _host.ExecuteWorkUnitAsync(binding, request, workUnit.InputSchemaVersion, cancellationToken).ConfigureAwait(false);
        if (result.Executed)
        {
            return result.Result;
        }

        var reason = result.NotExecutedReason!;
        if (_options.MissingPlugin == MissingPluginPolicy.Fail)
        {
            throw new PluginNotAvailableException($"Work unit '{binding.WorkUnitName}' (id {workUnit.WorkUnitId}) could not run: {reason}");
        }

        _waiting.Add(new WorkInvocation(workUnit.WorkUnitName ?? workUnit.Type ?? string.Empty, workUnit.WorkUnitId, request.Trigger), reason);
        _logger.LogWarning("Work unit {WorkUnitName} (id {WorkUnitId}) is waiting for its plugin: {Reason} The run will be repeated when a matching version is loaded.",
            binding.WorkUnitName, workUnit.WorkUnitId, reason);
        return null;
    }
}
