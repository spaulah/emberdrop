namespace Emberdrop.Plugins;

/// <summary>
/// Thrown to the caller for an execution that the host stopped waiting for because its plugin version had to be
/// retired. The plugin's code may still be running.
/// </summary>
public sealed class PluginExecutionAbandonedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PluginExecutionAbandonedException(string message) : base(message)
    {
    }
}
