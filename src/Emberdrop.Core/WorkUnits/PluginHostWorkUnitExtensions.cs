using Emberdrop.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Emberdrop.WorkUnits;

/// <summary>What work units add to the plugin host: running a work unit on the version its binding selects, and checking its input.</summary>
public static class PluginHostWorkUnitExtensions
{
    /// <summary>The work units a plugin version provides.</summary>
    public static IReadOnlyList<PluginWorkUnitDescriptor> WorkUnits(this PluginDescriptor plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        return plugin.Exports.Select(e => e.Metadata).OfType<PluginWorkUnitDescriptor>().ToList();
    }

    /// <summary>
    /// Runs the work unit a binding points at, on the highest-precedence active version that satisfies the binding's version
    /// constraint and provides the work unit. The version cannot be unloaded while the run is in flight.
    /// </summary>
    /// <param name="host">The plugin host.</param>
    /// <param name="binding">Which plugin work unit, and which versions of the plugin may run it.</param>
    /// <param name="request">The run's input and trigger; the host builds the <see cref="WorkContext"/> (execution id, plugin, version, logger, services, configuration) from it.</param>
    /// <param name="storedInputSchemaVersion">The schema version the work unit's stored input was written with.</param>
    /// <param name="cancellationToken">Cancels the run; it is also cancelled when the version is retired and the run takes too long.</param>
    /// <returns>Whether the work unit ran, or why no loaded version could run it. Nothing ran in the second case.</returns>
    /// <exception cref="InputSchemaMismatchException">The chosen version expects a different schema version of the stored input; the work unit was not started.</exception>
    /// <exception cref="PluginConfigurationException">Configuration keys the work unit declared as required are missing; the work unit was not started.</exception>
    /// <exception cref="WorkInputException">The input does not fit the work unit's input type; the work unit was not started.</exception>
    /// <exception cref="PluginExecutionAbandonedException">The host stopped waiting for the run because its version had to be retired.</exception>
    public static async Task<WorkExecutionResult> ExecuteWorkUnitAsync(
        this PluginHost host,
        WorkUnitBinding binding,
        WorkRequest request,
        int storedInputSchemaVersion = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(request);

        var execution = await host.ExecuteExportAsync(
            active => BindingResolution.Select(active, binding, (plugin, workUnit) => Verify(host, plugin, workUnit, storedInputSchemaVersion)),
            async invocation =>
            {
                var workUnit = (IWorkUnit)invocation.Instance;
                var result = await workUnit.ExecuteAsync(BuildContext(invocation, request), invocation.CancellationToken).ConfigureAwait(false);
                return result ?? WorkResult.Succeeded();
            },
            // Input that does not fit is refused before any work unit code runs, with a message that says what is wrong.
            type =>
            {
                if (WorkUnitTypeInfo.ValidateInput(type, binding.WorkUnitName, request.Input) is { } problem)
                {
                    throw new WorkInputException(problem);
                }
            },
            cancellationToken).ConfigureAwait(false);

        return execution.Executed
            ? WorkExecutionResult.Completed(execution.Result!)
            : WorkExecutionResult.NotExecuted(execution.NotExecutedReason!);
    }

    /// <summary>
    /// Checks, without running the work unit, that <paramref name="input"/> can be read as the input type of the work unit a binding
    /// points at. The reading is strict: properties the type does not have are an error, not ignored.
    /// </summary>
    /// <returns>A message fit to show to a user, or <see langword="null"/> when the input is acceptable or no loaded version matches the binding (that is reported when the work unit runs).</returns>
    public static string? ValidateInput(this PluginHost host, WorkUnitBinding binding, string? input)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(binding);

        return host.InspectExport(
            active => BindingResolution.Select(active, binding),
            type => WorkUnitTypeInfo.ValidateInput(type, binding.WorkUnitName, input));
    }

    /// <summary>
    /// Creates a work unit instance from the newest active version that provides it, from the plugin's root provider, without
    /// running it or taking a lease. Intended for inspection; use <see cref="ExecuteWorkUnitAsync"/> to run work safely.
    /// </summary>
    public static bool TryCreateWorkUnit(this PluginHost host, string key, out IWorkUnit? workUnit)
    {
        ArgumentNullException.ThrowIfNull(host);

        var found = host.TryCreateExport(key, out var instance);
        workUnit = instance as IWorkUnit;
        return found && workUnit is not null;
    }

    private static void Verify(PluginHost host, PluginDescriptor plugin, PluginWorkUnitDescriptor workUnit, int storedSchemaVersion)
    {
        if (workUnit.InputSchemaVersion != storedSchemaVersion)
        {
            throw new InputSchemaMismatchException(
                $"Work unit '{workUnit.Name}' of plugin '{plugin.Id}' {plugin.Version} expects input with schema version {workUnit.InputSchemaVersion}, " +
                $"but the stored work unit has schema version {storedSchemaVersion}. The work unit was not started. Update the work unit's input, or pin it to a plugin version that expects version {storedSchemaVersion}.");
        }

        var missing = workUnit.RequiredConfiguration.Count == 0 ? [] : host.MissingConfiguration(workUnit.RequiredConfiguration);
        if (missing.Count > 0)
        {
            throw new PluginConfigurationException(
                $"Work unit '{workUnit.Name}' of plugin '{plugin.Id}' {plugin.Version} needs configuration that is not set: {string.Join(", ", missing)}. The work unit was not started.");
        }
    }

    private static WorkContext BuildContext(ExportInvocation invocation, WorkRequest request)
    {
        var configuration = invocation.Services.GetService<IConfiguration>()?.AsEnumerable()
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        return new WorkContext
        {
            ExecutionId = invocation.ExecutionId,
            WorkUnitName = invocation.Export.Name,
            PluginId = invocation.Plugin.Id,
            PluginVersion = invocation.Plugin.Version,
            Trigger = request.Trigger ?? ManualTrigger.Instance,
            Logger = invocation.Services.GetRequiredService<ILoggerFactory>().CreateLogger($"{invocation.Plugin.Id}.{invocation.Export.Name}"),
            Services = invocation.Services,
            Configuration = configuration,
            Input = request.Input,
            Activity = invocation.Activity,
        };
    }
}
