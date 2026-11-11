namespace Emberdrop.Plugins;

/// <summary>An execution that was running on a plugin version.</summary>
/// <param name="ExecutionId">A short identifier for the execution.</param>
/// <param name="ExportName">The logical name of the export that is running.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="CancellationRequested">Whether the host has asked it to stop.</param>
/// <param name="Abandoned">Whether the host stopped waiting for it and reported it failed, while its code may still be running.</param>
public sealed record RunningExecutionInfo(string ExecutionId, string ExportName, DateTimeOffset StartedAt, bool CancellationRequested, bool Abandoned);

/// <summary>Something long-lived that plugin code declared through <see cref="IPluginActivity"/>.</summary>
/// <param name="Kind">What it is (thread, timer, subscription, ...).</param>
/// <param name="Description">A short description supplied by the plugin.</param>
/// <param name="StartedAt">When it was declared.</param>
public sealed record PluginActivityInfo(string Kind, string Description, DateTimeOffset StartedAt);

/// <summary>
/// What the host knows about why a plugin version is still loaded, or is not freeing its code. It is evidence the host
/// can see, not a proof of cause: a static event or a thread the plugin never declared is invisible to it.
/// </summary>
/// <param name="Id">The plugin id.</param>
/// <param name="Version">The plugin version.</param>
/// <param name="State">The current lifecycle state.</param>
/// <param name="Stuck">Whether draining gave up waiting.</param>
/// <param name="ContextAlive">Whether the runtime still holds the plugin's load context. <see langword="null"/> while the version is still loaded.</param>
/// <param name="Executions">Executions still running on this version, including abandoned ones.</param>
/// <param name="Activities">Things the plugin declared through <see cref="IPluginActivity"/> that have not been disposed.</param>
/// <param name="CommonCauses">The usual reasons a load context stays alive, for orientation.</param>
/// <param name="Guidance">How to investigate further.</param>
/// <param name="GeneratedAt">When this report was produced.</param>
public sealed record PluginDiagnosticReport(
    string Id,
    PluginVersion Version,
    PluginState State,
    bool Stuck,
    bool? ContextAlive,
    IReadOnlyList<RunningExecutionInfo> Executions,
    IReadOnlyList<PluginActivityInfo> Activities,
    IReadOnlyList<string> CommonCauses,
    string Guidance,
    DateTimeOffset GeneratedAt);
