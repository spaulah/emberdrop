using Emberdrop.Plugins;
using Emberdrop.Storage;

namespace Emberdrop.WorkUnits;

/// <summary>Reads the plugin binding out of a stored work unit.</summary>
public static class WorkUnitBindingExtensions
{
    /// <summary>
    /// The binding a stored work unit asks for. A work unit without a plugin id is an old-style or built-in work unit: it is looked up by name.
    /// </summary>
    /// <exception cref="FormatException">The stored version constraint text is not valid.</exception>
    public static WorkUnitBinding ToBinding(this WorkUnitDefinition workUnit)
    {
        ArgumentNullException.ThrowIfNull(workUnit);

        return new WorkUnitBinding(
            string.IsNullOrWhiteSpace(workUnit.PluginId) ? null : workUnit.PluginId,
            workUnit.WorkUnitName ?? workUnit.Type ?? string.Empty,
            PluginVersionConstraint.Parse(workUnit.VersionConstraint));
    }
}
