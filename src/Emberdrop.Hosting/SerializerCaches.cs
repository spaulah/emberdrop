using System.Reflection;
using System.Text.Json;

namespace Emberdrop.Plugins;

/// <summary>
/// Clears System.Text.Json's process-wide caches. Reading, writing or describing a plugin's types with System.Text.Json fills static
/// caches (member accessors, type metadata) that hold those types and so keep the plugin's load context alive after it is unloaded.
/// The runtime clears the same caches itself on a hot reload, through an internal handler; this calls that handler.
/// </summary>
internal static class SerializerCaches
{
    private static readonly MethodInfo? ClearCacheMethod = typeof(JsonSerializerOptions).Assembly
        .GetType("System.Text.Json.JsonSerializerOptionsUpdateHandler")
        ?.GetMethod("ClearCache", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, [typeof(Type[])]);

    /// <summary>Whether this runtime has the handler. When it does not, the caches simply stay, and the unload verification reports a plugin that cannot be freed.</summary>
    public static bool IsAvailable => ClearCacheMethod is not null;

    public static void Clear()
    {
        try
        {
            ClearCacheMethod?.Invoke(null, [null]);
        }
        catch (Exception ex) when (ex is TargetInvocationException or MethodAccessException or ArgumentException)
        {
            // Best effort: a runtime that changed this internal handler just means the caches are not cleared.
        }
    }
}
