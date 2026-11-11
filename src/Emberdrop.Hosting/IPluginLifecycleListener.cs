namespace Emberdrop.Plugins;

/// <summary>Told whenever the set of active versions of a plugin changes, so other parts of the system can react.</summary>
public interface IPluginLifecycleListener
{
    /// <summary>
    /// Called after a version of <paramref name="pluginId"/> became active, or stopped being active because it is being retired.
    /// </summary>
    /// <param name="pluginId">The plugin whose active versions changed.</param>
    /// <param name="activeVersions">Every version of that plugin that can accept new work now. Empty when the plugin has none left.</param>
    /// <param name="cancellationToken">Cancels the notification.</param>
    Task PluginVersionsChangedAsync(string pluginId, IReadOnlyList<PluginDescriptor> activeVersions, CancellationToken cancellationToken);
}
