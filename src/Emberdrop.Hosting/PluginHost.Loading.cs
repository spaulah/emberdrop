namespace Emberdrop.Plugins;

using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

public sealed partial class PluginHost
{
    /// <summary>Reads, validates and activates the package in <paramref name="directory"/>; the version becomes <see cref="PluginState.Active"/>.</summary>
    /// <exception cref="PluginLoadException">The package is invalid, cannot be activated, or that id and version are already live.</exception>
    public async Task<PluginDescriptor> LoadAsync(string directory, CancellationToken cancellationToken = default)
    {
        var manifest = await PluginLoader.ReadManifestAsync(directory, _options.ManifestFormat, cancellationToken).ConfigureAwait(false);
        var record = Stage(manifest, directory);

        try
        {
            var package = _loader.Validate(manifest, directory);
            Transition(record, PluginState.Validated);

            var descriptor = await ActivateCoreAsync(record, package, cancellationToken).ConfigureAwait(false);
            await NotifyAsync(record.Id).ConfigureAwait(false);
            return descriptor;
        }
        catch (PluginLoadException ex)
        {
            Fail(record, ex.Message);
            throw;
        }
        catch (OperationCanceledException)
        {
            Fail(record, "Loading was cancelled.");
            throw;
        }
    }

    /// <summary>
    /// Proves that the package in <paramref name="directory"/> would activate (it validates, loads, its entry assemblies run
    /// and its services can be built) by loading it into a throwaway context that is discarded. Nothing is registered. Used
    /// to check a replacement for an installed version <i>before</i> that version is touched.
    /// </summary>
    /// <exception cref="PluginLoadException">The package would not activate; the exception lists why.</exception>
    public async Task ValidateActivationAsync(string directory, CancellationToken cancellationToken = default)
    {
        var manifest = await PluginLoader.ReadManifestAsync(directory, _options.ManifestFormat, cancellationToken).ConfigureAwait(false);
        var package = _loader.Validate(manifest, directory);
        await TrialLoadAsync(package, cancellationToken).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task TrialLoadAsync(ValidatedPackage package, CancellationToken cancellationToken)
    {
        var plugin = await _loader.LoadAsync(package, cancellationToken).ConfigureAwait(false);
        try
        {
            await plugin.ValidateServicesAsync(_services).ConfigureAwait(false);
        }
        finally
        {
            plugin.Unload();
        }
    }

    /// <summary>Loads the package and proves its services can be built. If anything fails the half-loaded context is released.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<PluginDescriptor> ActivateCoreAsync(PluginVersionRecord record, ValidatedPackage package, CancellationToken cancellationToken)
    {
        var plugin = await _loader.LoadAsync(package, cancellationToken).ConfigureAwait(false);
        try
        {
            await plugin.ValidateServicesAsync(_services).ConfigureAwait(false);
        }
        catch
        {
            plugin.Unload();
            throw;
        }

        return Activate(record, plugin);
    }

    /// <summary>Records that a package was moved to quarantine. If its version had just failed to load, that version becomes <see cref="PluginState.Quarantined"/>.</summary>
    /// <param name="location">Where the package is now.</param>
    /// <param name="reasons">Why it was rejected.</param>
    /// <param name="pluginId">The plugin id, if the manifest could be read.</param>
    /// <param name="version">The plugin version, if the manifest could be read.</param>
    public void RecordQuarantine(string location, IReadOnlyList<string> reasons, string? pluginId = null, PluginVersion? version = null)
    {
        ArgumentNullException.ThrowIfNull(reasons);

        var reason = string.Join(" | ", reasons);
        lock (_gate)
        {
            var failed = pluginId is null
                ? null
                : _records.LastOrDefault(r => r.Id == pluginId && r.Version == version && r.State == PluginState.Failed);

            if (failed is not null)
            {
                failed.Location = location;
                Move(failed, PluginState.Quarantined, reason);
            }
            else
            {
                var record = new PluginVersionRecord(pluginId ?? "(unknown)", version, location, PluginState.Quarantined) { Reason = reason };
                _records.Add(record);
                _logger.LogWarning("Plugin package {Package} is quarantined: {Reason}", location, reason);
            }

            TrimHistory();
        }
    }

}
