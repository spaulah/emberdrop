using Emberdrop.Plugins;
namespace Emberdrop.WorkUnits;

/// <summary>
/// What a stored work unit is bound to: a logical work unit of a plugin and the versions it may run on. Never a CLR type name.
/// </summary>
/// <param name="PluginId">The plugin, or <see langword="null"/> for a work unit from before bindings existed, which is looked up by name in every plugin.</param>
/// <param name="WorkUnitName">The logical work unit name from the plugin's manifest (for an old-style work unit: a work unit name, type name or class name).</param>
/// <param name="Constraint">Which versions of the plugin may run it.</param>
public sealed record WorkUnitBinding(string? PluginId, string WorkUnitName, PluginVersionConstraint Constraint);
