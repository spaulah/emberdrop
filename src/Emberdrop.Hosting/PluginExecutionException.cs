namespace Emberdrop.Plugins;

/// <summary>
/// Thrown to the caller when code in a plugin fails. It carries the failure as text only.
/// </summary>
/// <remarks>
/// An exception object created by plugin code references the plugin's types through its stack trace, so any caller that
/// kept it (a component recording the last error, a logger, a retry queue) would keep the plugin's load context alive
/// for as long as it held on. The host therefore never lets the plugin's own exception object leave the plugin boundary.
/// </remarks>
public sealed class PluginExecutionException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What failed, including the original exception's type name and message.</param>
    /// <param name="details">The original exception rendered as text, including its stack trace.</param>
    public PluginExecutionException(string message, string details)
        : base(message)
    {
        Details = details;
    }

    /// <summary>The original exception rendered as text, including its stack trace.</summary>
    public string Details { get; }
}
