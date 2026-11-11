namespace Emberdrop.Plugins;

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

public sealed partial class PluginHost
{
    private async Task<UnloadResult> RunUnloadAsync(PluginVersionRecord record)
    {
        await record.Drained.Task.ConfigureAwait(false);

        lock (_gate)
        {
            Move(record, PluginState.Unloading);
        }

        // The plugin's own services (and whatever they dispose) go first, while its code is still loaded.
        await DisposeServicesAsync(record).ConfigureAwait(false);

        WeakReference weak;
        lock (_gate)
        {
            weak = DetachAndUnload(record);
            record.ContextReference = weak;
        }

        var watch = Stopwatch.StartNew();
        var attempts = 0;
        while (weak.IsAlive && attempts < _options.UnloadVerificationAttempts)
        {
            attempts++;
            // Typed JSON handling of the plugin's types may have filled serializer caches that reference them.
            SerializerCaches.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (weak.IsAlive)
            {
                await Task.Delay(_options.UnloadVerificationDelay).ConfigureAwait(false);
            }
        }

        var collected = !weak.IsAlive;
        var diagnostic = collected
            ? null
            : $"The load context of {record.Label} was still alive after {attempts} forced collections. Something still references code from the plugin " +
              "(a static event, a running thread, a timer, a serializer or logging cache, a thread-local, or an abandoned execution). " +
              "GET /api/plugins/{id}/{version}/report lists what the host can see; see docs/engineering/known-limits.md.";

        lock (_gate)
        {
            var wasStuck = record.Stuck;
            record.Stuck = false;
            Move(record, PluginState.Unloaded, diagnostic ?? (wasStuck ? "Unloaded after the remaining executions ended." : null));
            TrimHistory();
        }

        if (collected)
        {
            _logger.LogInformation("Unloaded plugin {Plugin}; its load context was collected after {Attempts} collection(s)", record.Label, attempts);
        }
        else
        {
            var report = GetDiagnosticReport(record.Id, record.Version!);
            _logger.LogWarning(
                "Plugin {Plugin} was unloaded but its load context is still alive after {Attempts} collections. Running executions: {Executions}. Declared activity: {Activities}. {Diagnostic}",
                record.Label,
                attempts,
                report?.Executions.Count ?? 0,
                report?.Activities.Count ?? 0,
                diagnostic);
        }

        return new UnloadResult(record.Id, record.Version!, collected, attempts, watch.Elapsed, diagnostic);
    }

    private async Task DisposeServicesAsync(PluginVersionRecord record)
    {
        LoadedPlugin? plugin;
        lock (_gate)
        {
            plugin = record.Plugin;
        }

        var problem = plugin is null ? null : await plugin.DisposeServicesAsync().ConfigureAwait(false);
        if (problem is not null)
        {
            // Text only: an exception from the plugin's disposal code would keep the plugin alive.
            _logger.LogWarning("Disposing the services of {Plugin} failed: {Problem}", record.Label, problem);
        }
    }

    /// <summary>Releases the plugin. The plugin object is touched only here, so no reference to it survives in the caller's frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DetachAndUnload(PluginVersionRecord record)
    {
        var plugin = record.Plugin!;
        record.Plugin = null;
        return plugin.Unload();
    }

}
