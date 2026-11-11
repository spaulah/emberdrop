namespace Emberdrop.Plugins;

/// <summary>Read-only view of the plugins the host knows about.</summary>
public interface IPluginCatalog
{
    /// <summary>Returns a snapshot of the plugin versions that are <see cref="PluginState.Active"/> and can run new work.</summary>
    IReadOnlyList<PluginDescriptor> GetPlugins();

    /// <summary>Returns the lifecycle status of every version the host knows about, in any state, including recent history.</summary>
    IReadOnlyList<PluginVersionStatus> GetVersions();

    /// <summary>Returns the diagnostic report for one version, or <see langword="null"/> if the host does not know it.</summary>
    PluginDiagnosticReport? GetDiagnosticReport(string pluginId, PluginVersion version);
}
