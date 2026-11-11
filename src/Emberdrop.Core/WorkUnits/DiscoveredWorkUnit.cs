namespace Emberdrop.WorkUnits;

/// <summary>
/// A work unit that can currently be run, described with strings only so that holding one never keeps a plugin's code alive.
/// </summary>
/// <param name="Name">The short name stored as the work unit type.</param>
/// <param name="TypeName">The full CLR type name.</param>
/// <param name="AssemblyName">The simple name of the assembly that contains the work unit.</param>
/// <param name="PluginId">The plugin that provides the work unit, or <see langword="null"/> for a work unit built into the host.</param>
public sealed record DiscoveredWorkUnit(string Name, string TypeName, string AssemblyName, string? PluginId);
