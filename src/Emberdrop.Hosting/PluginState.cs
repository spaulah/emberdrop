namespace Emberdrop.Plugins;

/// <summary>Where one version of a plugin is in its life.</summary>
public enum PluginState
{
    /// <summary>The package has been received and its manifest read; nothing has been checked yet.</summary>
    Staged = 0,

    /// <summary>The package passed every check that can be done without running its code.</summary>
    Validated = 1,

    /// <summary>Loaded and accepting new executions. Several versions of one plugin can be active at once.</summary>
    Active = 2,

    /// <summary>No new executions are routed here; in-flight ones are finishing.</summary>
    Draining = 3,

    /// <summary>All executions are done and the load context is being released.</summary>
    Unloading = 4,

    /// <summary>Released. Whether the runtime actually freed the code is reported separately.</summary>
    Unloaded = 5,

    /// <summary>Terminal: the version could not be activated.</summary>
    Failed = 6,

    /// <summary>Terminal: the package was rejected and moved aside.</summary>
    Quarantined = 7,
}
