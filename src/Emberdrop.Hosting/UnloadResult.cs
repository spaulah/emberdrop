namespace Emberdrop.Plugins;

/// <summary>The outcome of releasing a plugin version's load context.</summary>
/// <param name="PluginId">The plugin's id.</param>
/// <param name="Version">The plugin's version.</param>
/// <param name="Collected">Whether the runtime actually freed the plugin's load context.</param>
/// <param name="Attempts">How many garbage collections were forced while verifying.</param>
/// <param name="Elapsed">How long verification took.</param>
/// <param name="Diagnostic">When <paramref name="Collected"/> is false, what is known about why.</param>
public sealed record UnloadResult(string PluginId, PluginVersion Version, bool Collected, int Attempts, TimeSpan Elapsed, string? Diagnostic);
