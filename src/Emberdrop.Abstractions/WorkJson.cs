using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Emberdrop;

/// <summary>The JSON conventions for work unit input and output.</summary>
public static class WorkJson
{
    /// <summary>
    /// Creates options with camel-case properties, case-insensitive reading, and strict input: properties the type does not
    /// have are an error, not ignored. A new instance on every call, on purpose: serializer options cache metadata for every
    /// type they have seen, and a cache shared across plugins would keep a plugin's types, and so its load context, alive forever.
    /// </summary>
    public static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // Reflection-based, whatever the host's defaults: plugin types are only known at run time, so no source-generated context can exist for them.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}
