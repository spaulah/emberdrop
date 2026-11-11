using Emberdrop.Plugins;
namespace Emberdrop.WorkUnits;

/// <summary>Thrown when a run is refused because no loaded plugin version matches the work unit's binding (see <see cref="MissingPluginPolicy.Fail"/>).</summary>
public sealed class PluginNotAvailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PluginNotAvailableException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown, before the work unit is started, when the input stored with a work unit was written for a different schema version
/// than the plugin version that would run it. Input is never reinterpreted silently.
/// </summary>
public sealed class InputSchemaMismatchException : Exception
{
    /// <summary>Creates the exception.</summary>
    public InputSchemaMismatchException(string message) : base(message)
    {
    }
}

/// <summary>Thrown, before the work unit is started, when configuration keys the work unit declared as required are missing.</summary>
public sealed class PluginConfigurationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PluginConfigurationException(string message) : base(message)
    {
    }
}
