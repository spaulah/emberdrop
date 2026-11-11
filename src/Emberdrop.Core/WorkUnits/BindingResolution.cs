using Emberdrop.Plugins;

namespace Emberdrop.WorkUnits;

/// <summary>
/// Decides which loaded plugin version runs a bound work unit, and explains in words why none does. The host uses it when a work unit
/// fires and the status endpoint uses it to describe a stored work unit, so the two can never disagree.
/// </summary>
internal static class BindingResolution
{
    public static bool Matches(PluginWorkUnitDescriptor workUnit, string key) =>
        workUnit.Name == key || workUnit.TypeName == key || SimpleName(workUnit.TypeName) == key;

    public static string SimpleName(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..];

    /// <summary>Picks the highest-precedence active version that satisfies the binding and provides the work unit.</summary>
    public static (PluginDescriptor? Plugin, PluginWorkUnitDescriptor? WorkUnit, string? Reason) Resolve(IReadOnlyList<PluginDescriptor> active, WorkUnitBinding binding)
    {
        var candidates = binding.PluginId is null ? active : active.Where(p => p.Id == binding.PluginId).ToList();
        if (candidates.Count == 0)
        {
            return (null, null, binding.PluginId is null
                ? $"No loaded plugin provides a work unit named '{binding.WorkUnitName}'."
                : $"Plugin '{binding.PluginId}' is not loaded.");
        }

        var allowed = candidates.Where(p => binding.Constraint.IsSatisfiedBy(p.Version)).ToList();
        if (allowed.Count == 0)
        {
            return (null, null,
                $"No loaded version of plugin '{binding.PluginId}' satisfies {binding.Constraint}; loaded: {string.Join(", ", candidates.Select(p => p.Version.ToString()))}.");
        }

        var providing = allowed
            .Select(p => (Plugin: p, WorkUnit: p.WorkUnits().FirstOrDefault(j => Matches(j, binding.WorkUnitName))))
            .Where(x => x.WorkUnit is not null)
            .OrderByDescending(x => x.Plugin.Version)
            .FirstOrDefault();
        if (providing.Plugin is null)
        {
            return (null, null,
                $"Loaded version(s) {string.Join(", ", allowed.Select(p => p.Version.ToString()))} of plugin '{binding.PluginId}' do not provide a work unit named '{binding.WorkUnitName}'.");
        }

        return (providing.Plugin, providing.WorkUnit, null);
    }

    /// <summary>
    /// <see cref="Resolve"/> in the form the hosting core wants: the export to use, or the reason there is none.
    /// </summary>
    public static ExportSelection Select(IReadOnlyList<PluginDescriptor> active, WorkUnitBinding binding, Action<PluginDescriptor, PluginWorkUnitDescriptor>? verify = null)
    {
        var (plugin, workUnit, reason) = Resolve(active, binding);
        if (plugin is null || workUnit is null)
        {
            return ExportSelection.NotFound(reason!);
        }

        verify?.Invoke(plugin, workUnit);
        return ExportSelection.Found(plugin, plugin.Exports.First(e => e.Name == workUnit.Name));
    }
}
