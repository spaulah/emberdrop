namespace Emberdrop.Plugins;

/// <summary>How an attempt to retire a plugin version ended.</summary>
public enum DrainOutcome
{
    /// <summary>The version drained and was unloaded. <see cref="DrainResult.Unload"/> says whether the runtime freed its code.</summary>
    Unloaded = 0,

    /// <summary>Executions are still running after the timeouts. The version stays <see cref="PluginState.Draining"/> and unloads when they end.</summary>
    Stuck = 1,

    /// <summary>The host has no such version in a state that can be retired.</summary>
    NotFound = 2,
}

/// <summary>The result of <see cref="PluginHost.RetireAsync"/>.</summary>
/// <param name="PluginId">The plugin id.</param>
/// <param name="Version">The plugin version.</param>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="Unload">The unload verification, when <paramref name="Outcome"/> is <see cref="DrainOutcome.Unloaded"/>.</param>
public sealed record DrainResult(string PluginId, PluginVersion Version, DrainOutcome Outcome, UnloadResult? Unload);
