namespace Emberdrop.Plugins;

/// <summary>Thrown when a plugin package cannot be loaded. <see cref="Reasons"/> lists every problem found, so one attempt reports everything.</summary>
public sealed class PluginLoadException : Exception
{
    /// <summary>Creates the exception from one or more reasons.</summary>
    /// <param name="reasons">Every problem found.</param>
    /// <param name="details">Technical detail (for example a stack trace) as text. It is text on purpose: an exception object thrown by plugin code would keep the plugin's load context alive.</param>
    public PluginLoadException(IReadOnlyList<string> reasons, string? details = null)
        : base(BuildMessage(reasons))
    {
        Reasons = reasons;
        Details = details;
    }

    /// <summary>Creates the exception from one reason.</summary>
    public PluginLoadException(string reason, string? details = null)
        : this([reason], details)
    {
    }

    /// <summary>Technical detail as text, if there is any.</summary>
    public string? Details { get; }

    /// <summary>Every reason the package was rejected.</summary>
    public IReadOnlyList<string> Reasons { get; }

    /// <summary>
    /// Describes an exception, and the exceptions that caused it, as text: <c>Type: message (caused by Type: message ...)</c>.
    /// Reflection and type initializers wrap the real failure, so the outermost message alone is rarely the useful one.
    /// </summary>
    internal static string Describe(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null && parts.Count < 6; current = current.InnerException!)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
            if (current.InnerException is null)
            {
                break;
            }
        }

        return string.Join(" (caused by ", parts) + new string(')', parts.Count - 1);
    }

    private static string BuildMessage(IReadOnlyList<string> reasons) =>
        reasons.Count == 1 ? reasons[0] : $"{reasons.Count} problems: " + string.Join(" | ", reasons);
}
