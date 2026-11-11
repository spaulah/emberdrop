namespace Emberdrop.WorkUnits;

/// <summary>What happens when a work unit fires but no loaded plugin version matches its binding.</summary>
public enum MissingPluginPolicy
{
    /// <summary>
    /// The run does nothing and the work unit is shown as <see cref="WorkUnitBindingState.WaitingForPlugin"/> with the reason.
    /// The missed run is repeated as soon as a matching version is loaded. This is the default: a plugin that is
    /// late, being replaced or temporarily removed must not turn its work units into permanent failures.
    /// </summary>
    Wait = 0,

    /// <summary>The run fails with <see cref="PluginNotAvailableException"/>, so the scheduler records and handles it as a failure.</summary>
    Fail = 1,
}
