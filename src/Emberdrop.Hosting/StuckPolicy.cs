namespace Emberdrop.Plugins;

/// <summary>What to do when a plugin version still has executions running after the drain timeouts.</summary>
public enum StuckPolicy
{
    /// <summary>
    /// Stop waiting, leave the version in <see cref="PluginState.Draining"/>, and flag it as stuck. Nothing is forced.
    /// The version still unloads on its own the moment its last execution ends. This is the default.
    /// </summary>
    LeaveDraining = 0,

    /// <summary>Keep waiting without declaring the version stuck. The drain only ends when the executions do.</summary>
    KeepWaiting = 1,

    /// <summary>
    /// Report the remaining executions as failed and stop counting them, so the version proceeds to unload.
    /// Their code is not stopped (nothing can stop it); if it keeps running, the load context stays alive and the
    /// diagnostic report names those executions.
    /// </summary>
    FailStuckExecutions = 2,
}
