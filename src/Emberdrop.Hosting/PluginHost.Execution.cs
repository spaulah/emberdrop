namespace Emberdrop.Plugins;

using Microsoft.Extensions.Logging;

public sealed partial class PluginHost
{
    /// <summary>Returns the keys among <paramref name="keys"/> for which the host has no configuration value.</summary>
    public IReadOnlyList<string> MissingConfiguration(IEnumerable<string> keys) => _services.MissingConfiguration(keys);

    /// <summary>
    /// Runs code against an export of a loaded plugin version and keeps that version loaded for as long as the run is in flight.
    /// The host that uses the hosting core decides which export (and so which version) is wanted, in <paramref name="choose"/>;
    /// the core guarantees the choice is still valid when the lease is taken, because both happen under one lock.
    /// </summary>
    /// <typeparam name="TResult">What the run returns. It must not hold plugin types, or it would keep the plugin alive.</typeparam>
    /// <param name="choose">
    /// Picks an export from the active plugin versions, or says why there is none. It may throw to refuse the run before anything starts.
    /// </param>
    /// <param name="run">
    /// The code to run, with a fresh instance of the export and a service scope of the plugin's own provider. An exception it throws
    /// reaches the caller as a <see cref="PluginExecutionException"/> that holds text only.
    /// </param>
    /// <param name="preflight">Optional: called with the export's type before any instance is created; may throw to refuse the run.</param>
    /// <param name="cancellationToken">Cancels the run; it is also cancelled when the version is retired and the run takes too long.</param>
    /// <returns>Whether the run happened, or why no loaded version could provide the export. Nothing ran in the second case.</returns>
    /// <exception cref="PluginExecutionAbandonedException">The host stopped waiting for the run because its version had to be retired.</exception>
    public async Task<ExportExecution<TResult>> ExecuteExportAsync<TResult>(
        Func<IReadOnlyList<PluginDescriptor>, ExportSelection> choose,
        Func<ExportInvocation, Task<TResult>> run,
        Action<Type>? preflight = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choose);
        ArgumentNullException.ThrowIfNull(run);

        var begun = BeginExecution(choose, cancellationToken);
        if (begun.Lease is null)
        {
            return ExportExecution.NotExecuted<TResult>(begun.Reason!);
        }

        var lease = begun.Lease;
        try
        {
            var result = await lease.RunAsync(run, preflight).ConfigureAwait(false);
            return ExportExecution.Completed(result);
        }
        finally
        {
            lease.Dispose();
        }
    }

    /// <summary>
    /// Runs <paramref name="inspect"/> on the type of the export <paramref name="choose"/> picks, without creating an instance or taking a lease.
    /// Only what the callback returns leaves the call, so it must be plain data.
    /// </summary>
    /// <returns>What <paramref name="inspect"/> returned, or <see langword="default"/> when no active version provides the export.</returns>
    public T? InspectExport<T>(Func<IReadOnlyList<PluginDescriptor>, ExportSelection> choose, Func<Type, T> inspect)
    {
        ArgumentNullException.ThrowIfNull(choose);
        ArgumentNullException.ThrowIfNull(inspect);

        LoadedPlugin plugin;
        string name;
        lock (_gate)
        {
            var live = _records.Where(r => r.State == PluginState.Active && r.Plugin is not null).ToList();
            var selection = choose(live.Select(r => r.Plugin!.Descriptor).ToList());
            if (selection.Plugin is null || selection.Export is null)
            {
                return default;
            }

            plugin = live.First(r => r.Id == selection.Plugin.Id && r.Version == selection.Plugin.Version).Plugin!;
            name = selection.Export.Name;
        }

        return plugin.Inspect(name, inspect);
    }

    /// <summary>
    /// Creates an instance of an export from the newest active version that provides it, from the plugin's root provider, without
    /// running anything or taking a lease. Intended for inspection; use <see cref="ExecuteExportAsync{TResult}"/> to run code safely.
    /// </summary>
    public bool TryCreateExport(string key, out object? instance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        LoadedPlugin? source;
        lock (_gate)
        {
            source = _records
                .Where(r => r.State == PluginState.Active && r.Plugin is not null && r.Plugin.HasExport(key))
                .OrderByDescending(r => r.Version)
                .Select(r => r.Plugin)
                .FirstOrDefault();
        }

        instance = source?.CreateExport(key, source.GetServices(_services));
        return instance is not null;
    }

    private (ExportLease? Lease, string? Reason) BeginExecution(Func<IReadOnlyList<PluginDescriptor>, ExportSelection> choose, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var live = _records.Where(r => r.State == PluginState.Active && r.Plugin is not null).ToList();
            var selection = choose(live.Select(r => r.Plugin!.Descriptor).ToList());
            if (selection.Plugin is null || selection.Export is null)
            {
                return (null, selection.Reason);
            }

            var record = live.First(r => r.Id == selection.Plugin.Id && r.Version == selection.Plugin.Version);
            var execution = new ExecutionRecord(selection.Export.Name, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            record.Executions[execution.Id] = execution;
            return (new ExportLease(this, record, execution, selection.Export), null);
        }
    }

    private void EndExecution(PluginVersionRecord record, ExecutionRecord execution)
    {
        lock (_gate)
        {
            var wasTracked = record.Executions.Remove(execution.Id);
            if (!execution.Abandoned)
            {
                // An abandoned execution's export may still be using its token, so that token source is left alone.
                execution.Cancellation.Dispose();
            }

            execution.StillRunning = execution.Abandoned && execution.StillRunning;
            if (wasTracked && record.State == PluginState.Draining && record.Executions.Count == 0)
            {
                record.Drained.TrySetResult();
            }
        }
    }

}
