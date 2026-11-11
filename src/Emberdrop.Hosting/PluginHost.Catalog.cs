namespace Emberdrop.Plugins;

using Microsoft.Extensions.Logging;

public sealed partial class PluginHost
{
    /// <inheritdoc />
    public IReadOnlyList<PluginDescriptor> GetPlugins()
    {
        lock (_gate)
        {
            return _records
                .Where(r => r.State == PluginState.Active && r.Plugin is not null)
                .Select(r => r.Plugin!.Descriptor)
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .ThenByDescending(d => d.Version)
                .ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginVersionStatus> GetVersions()
    {
        lock (_gate)
        {
            return _records.Select(ToStatus).ToList();
        }
    }

    /// <inheritdoc />
    public PluginDiagnosticReport? GetDiagnosticReport(string pluginId, PluginVersion version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(version);

        lock (_gate)
        {
            var record = _records.LastOrDefault(r => r.Id == pluginId && r.Version == version);
            return record is null ? null : BuildReport(record);
        }
    }

    private async Task NotifyAsync(string pluginId)
    {
        if (_listeners.Count == 0)
        {
            return;
        }

        IReadOnlyList<PluginDescriptor> active;
        lock (_gate)
        {
            active = _records
                .Where(r => r.Id == pluginId && r.State == PluginState.Active && r.Plugin is not null)
                .Select(r => r.Plugin!.Descriptor)
                .OrderByDescending(d => d.Version)
                .ToList();
        }

        foreach (var listener in _listeners)
        {
            try
            {
                await listener.PluginVersionsChangedAsync(pluginId, active, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError("A plugin lifecycle listener failed for {PluginId}: {Type}: {Message}", pluginId, ex.GetType().Name, ex.Message);
            }
        }
    }

    private static PluginVersionStatus ToStatus(PluginVersionRecord record) => new(
        record.Id,
        record.Version,
        record.State,
        record.StateSince,
        record.Reason,
        record.Executions.Count,
        record.Stuck,
        record.IsUnloaded ? record.ContextReference?.IsAlive : null,
        record.Location);

    private static PluginDiagnosticReport BuildReport(PluginVersionRecord record)
    {
        var executions = record.Executions.Values
            .Concat(record.AbandonedExecutions.Where(e => e.StillRunning))
            .OrderBy(e => e.StartedAt)
            .Select(e => new RunningExecutionInfo(e.Id, e.ExportName, e.StartedAt, e.CancellationRequested, e.Abandoned))
            .ToList();

        return new PluginDiagnosticReport(
            record.Id,
            record.Version!,
            record.State,
            record.Stuck,
            record.IsUnloaded ? record.ContextReference?.IsAlive : null,
            executions,
            record.Activities.Snapshot(),
            CommonCauses,
            Guidance,
            DateTimeOffset.UtcNow);
    }

}
