namespace Emberdrop.Plugins;

using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

public sealed partial class PluginHost
{
    private PluginVersionRecord Stage(PluginManifest manifest, string directory)
    {
        lock (_gate)
        {
            if (_records.Any(r => r.Id == manifest.Id && r.Version == manifest.Version && PluginLifecycle.IsLive(r.State)))
            {
                throw new PluginLoadException($"Plugin '{manifest.Id}' {manifest.Version} is already loaded.");
            }

            var record = new PluginVersionRecord(manifest.Id, manifest.Version, Path.GetFullPath(directory), PluginState.Staged);
            _records.Add(record);
            _logger.LogInformation("Plugin {Plugin} staged from {Location}", record.Label, record.Location);
            return record;
        }
    }

    private void Transition(PluginVersionRecord record, PluginState next, string? reason = null)
    {
        lock (_gate)
        {
            Move(record, next, reason);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private PluginDescriptor Activate(PluginVersionRecord record, LoadedPlugin plugin)
    {
        lock (_gate)
        {
            record.Plugin = plugin;
            Move(record, PluginState.Active);
            return plugin.Descriptor;
        }
    }

    private void Fail(PluginVersionRecord record, string reason)
    {
        lock (_gate)
        {
            if (!PluginLifecycle.IsTerminal(record.State))
            {
                Move(record, PluginState.Failed, reason);
            }

            TrimHistory();
        }
    }

    /// <summary>Changes state and logs it. Caller holds the lock.</summary>
    private void Move(PluginVersionRecord record, PluginState next, string? reason = null)
    {
        var previous = record.State;
        record.MoveTo(next, reason);
        _logger.LogInformation("Plugin {Plugin}: {From} -> {To}{Reason}", record.Label, previous, next, reason is null ? string.Empty : " (" + reason + ")");
    }

    private void TrimHistory()
    {
        var finished = _records.Where(r => PluginLifecycle.IsTerminal(r.State)).ToList();
        for (var i = 0; i < finished.Count - _options.HistoryLimit; i++)
        {
            _records.Remove(finished[i]);
        }
    }

}
