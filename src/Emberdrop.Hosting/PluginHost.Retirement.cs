namespace Emberdrop.Plugins;

using Microsoft.Extensions.Logging;

public sealed partial class PluginHost
{
    /// <summary>
    /// Takes a version out of service: no new executions are routed to it, running ones get the grace period to finish,
    /// are then asked to stop, and the load context is released once none are left. See <see cref="StuckPolicy"/> for what
    /// happens to executions that never end.
    /// </summary>
    /// <param name="pluginId">The plugin id.</param>
    /// <param name="version">The version to retire.</param>
    /// <param name="options">Overrides for the drain timeouts and the stuck policy.</param>
    /// <param name="cancellationToken">Stops this call from waiting. The drain itself carries on in the background.</param>
    public async Task<DrainResult> RetireAsync(string pluginId, PluginVersion version, DrainOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(version);

        var grace = options?.GracePeriod ?? _options.DrainGracePeriod;
        var cancellationTimeout = options?.CancellationTimeout ?? _options.DrainCancellationTimeout;
        var policy = options?.StuckPolicy ?? _options.StuckPolicy;

        Task drained;
        Task<UnloadResult> unload;
        var startedDraining = false;
        lock (_gate)
        {
            var record = _records.LastOrDefault(r => r.Id == pluginId && r.Version == version
                                                     && r.State is PluginState.Active or PluginState.Draining or PluginState.Unloading);
            if (record is null)
            {
                return new DrainResult(pluginId, version, DrainOutcome.NotFound, null);
            }

            if (record.State == PluginState.Active)
            {
                Move(record, PluginState.Draining);
                startedDraining = true;
                record.UnloadTask = Task.Run(() => RunUnloadAsync(record), CancellationToken.None);
                if (record.Executions.Count == 0)
                {
                    record.Drained.TrySetResult();
                }
            }

            drained = record.Drained.Task;
            unload = record.UnloadTask!;
        }

        if (startedDraining)
        {
            // The version just stopped accepting work: tell listeners so they can pause schedules that depended on it.
            await NotifyAsync(pluginId).ConfigureAwait(false);
        }

        // The timeouts apply to waiting for executions to end. Once none are left, the unload and its bounded
        // verification are always awaited in full, so a version with nothing running is never reported as stuck.
        if (await FinishesWithin(drained, grace, cancellationToken).ConfigureAwait(false))
        {
            return await CompleteAsync(pluginId, version, unload, cancellationToken).ConfigureAwait(false);
        }

        RequestCancellation(pluginId, version);
        if (await FinishesWithin(drained, cancellationTimeout, cancellationToken).ConfigureAwait(false))
        {
            return await CompleteAsync(pluginId, version, unload, cancellationToken).ConfigureAwait(false);
        }

        switch (policy)
        {
            case StuckPolicy.KeepWaiting:
                return await CompleteAsync(pluginId, version, unload, cancellationToken).ConfigureAwait(false);

            case StuckPolicy.FailStuckExecutions:
                AbandonExecutions(pluginId, version);
                return await CompleteAsync(pluginId, version, unload, cancellationToken).ConfigureAwait(false);

            default:
                MarkStuck(pluginId, version);
                return new DrainResult(pluginId, version, DrainOutcome.Stuck, null);
        }
    }

    private static async Task<DrainResult> CompleteAsync(string pluginId, PluginVersion version, Task<UnloadResult> unload, CancellationToken cancellationToken)
    {
        var result = await unload.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new DrainResult(pluginId, version, DrainOutcome.Unloaded, result);
    }

    private static async Task<bool> FinishesWithin(Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (task.IsCompleted)
        {
            return true;
        }

        if (timeout <= TimeSpan.Zero)
        {
            return false;
        }

        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(timeout, timer.Token);
        var finished = await Task.WhenAny(task, delay).ConfigureAwait(false);
        await timer.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return finished == task;
    }

    private void RequestCancellation(string pluginId, PluginVersion version)
    {
        lock (_gate)
        {
            var record = _records.LastOrDefault(r => r.Id == pluginId && r.Version == version && r.State == PluginState.Draining);
            if (record is null)
            {
                return;
            }

            foreach (var execution in record.Executions.Values)
            {
                execution.CancellationRequested = true;
                execution.Cancellation.Cancel();
            }

            if (record.Executions.Count > 0)
            {
                _logger.LogWarning("Asking {Count} running execution(s) on {Plugin} to stop; the version is being retired", record.Executions.Count, record.Label);
            }
        }
    }

    private void MarkStuck(string pluginId, PluginVersion version)
    {
        lock (_gate)
        {
            var record = _records.LastOrDefault(r => r.Id == pluginId && r.Version == version && r.State == PluginState.Draining);
            if (record is null)
            {
                return;
            }

            record.Stuck = true;
            record.Reason = $"{record.Executions.Count} execution(s) did not stop after being asked to. The version stays loaded and unloads when they end.";
            _logger.LogWarning("Plugin {Plugin} is stuck draining: {Reason} Nothing is forced. See GET /api/plugins for the diagnostic report.", record.Label, record.Reason);
        }
    }

    private void AbandonExecutions(string pluginId, PluginVersion version)
    {
        lock (_gate)
        {
            var record = _records.LastOrDefault(r => r.Id == pluginId && r.Version == version && r.State == PluginState.Draining);
            if (record is null)
            {
                return;
            }

            foreach (var execution in record.Executions.Values.ToList())
            {
                execution.Abandoned = true;
                record.AbandonedExecutions.Add(execution);
                execution.AbandonSignal.TrySetResult();
                _logger.LogWarning("Abandoning execution {Execution} of {Export} on {Plugin}: reported as failed; its code cannot be stopped and may still be running",
                    execution.Id, execution.ExportName, record.Label);
            }

            record.Executions.Clear();
            record.Reason = "Remaining executions were abandoned (reported failed) so the version could unload; their code may still be running.";
            record.Drained.TrySetResult();
        }
    }

}
