using Emberdrop.Plugins;
namespace Emberdrop.WorkUnits;

/// <summary>Whether a stored work unit can run right now.</summary>
public enum WorkUnitBindingState
{
    /// <summary>The work unit is not bound to a plugin (it is built into the host) or the binding resolves to a loaded version.</summary>
    Ready = 0,

    /// <summary>No loaded plugin version matches the binding. The work unit is waiting for one.</summary>
    WaitingForPlugin = 1,

    /// <summary>A matching version is loaded but configuration keys its work unit requires are missing.</summary>
    MissingConfiguration = 2,

    /// <summary>A matching version is loaded but it expects a different schema version of the work unit's input.</summary>
    InputSchemaMismatch = 3,
}

/// <summary>The result of resolving a stored work unit against the plugins that are loaded now.</summary>
/// <param name="State">Whether the work unit can run.</param>
/// <param name="Reason">When it cannot: why, in words an operator can act on.</param>
/// <param name="ResolvedVersion">The plugin version the work unit would run on, if one matches.</param>
/// <param name="DeclaredTriggers">The triggers the resolved plugin version declares in its manifest (defaults), if one matches.</param>
public sealed record WorkUnitBindingStatus(WorkUnitBindingState State, string? Reason, PluginVersion? ResolvedVersion, IReadOnlyList<Trigger>? DeclaredTriggers = null)
{
    /// <summary>A work unit that can run.</summary>
    public static WorkUnitBindingStatus Ready(PluginVersion? version = null, IReadOnlyList<Trigger>? declaredTriggers = null) => new(WorkUnitBindingState.Ready, null, version, declaredTriggers);
}
