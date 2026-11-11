namespace Emberdrop.Plugins;

/// <summary>An optional way for plugin code to declare long-lived work, so the host can name it if the plugin refuses to unload.</summary>
public interface IPluginActivity
{
    /// <summary>
    /// Records that something long-lived was started. Dispose the result when it stops.
    /// </summary>
    /// <param name="kind">What it is, for example <c>thread</c>, <c>timer</c>, <c>subscription</c>.</param>
    /// <param name="description">A short human-readable description.</param>
    IDisposable Track(string kind, string description);
}
