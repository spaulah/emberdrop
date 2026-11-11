namespace Emberdrop.Plugins;

/// <summary>A snapshot of one plugin version's lifecycle. Holds no plugin types.</summary>
/// <param name="Id">The plugin id, or <c>(unknown)</c> for a package whose manifest could not be read.</param>
/// <param name="Version">The plugin version, or <see langword="null"/> when unknown.</param>
/// <param name="State">The current lifecycle state.</param>
/// <param name="StateSince">When the version entered the current state.</param>
/// <param name="Reason">Why the version is in this state, for failed, quarantined, stuck and unloaded-but-alive versions.</param>
/// <param name="InFlightExecutions">How many executions are running on this version right now.</param>
/// <param name="Stuck">Whether draining gave up waiting for executions that will not finish. See <see cref="PluginDiagnosticReport"/>.</param>
/// <param name="ContextAlive">For an unloaded version: whether the runtime still holds its code (a leak). <see langword="null"/> if not unloaded.</param>
/// <param name="Location">Where the package is or was, if known.</param>
public sealed record PluginVersionStatus(
    string Id,
    PluginVersion? Version,
    PluginState State,
    DateTimeOffset StateSince,
    string? Reason,
    int InFlightExecutions,
    bool Stuck,
    bool? ContextAlive,
    string? Location);
