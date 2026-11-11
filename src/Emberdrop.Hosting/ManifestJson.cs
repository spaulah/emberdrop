using System.Text.Json;

namespace Emberdrop.Plugins;

/// <summary>Small readers for manifest properties, shared by the manifest reader and by hosts that validate the extra properties of their own exports.</summary>
public static class ManifestJson
{
    /// <summary>
    /// Reads an optional string property. A property that is present but not a string adds an error and gives <see langword="null"/>;
    /// a blank string counts as absent.
    /// </summary>
    public static string? OptionalString(JsonElement parent, string property, IList<string> errors, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (!parent.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{(label is null ? $"'{property}'" : $"{label}.{property}")} must be a string.");
            return null;
        }

        var value = element.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Reads an optional array of non-empty strings, without duplicates. Problems are added to <paramref name="errors"/>.</summary>
    public static List<string> ReadStringArray(JsonElement parent, string property, IList<string> errors, string label)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var result = new List<string>();
        if (!parent.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return result;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{label}.{property} must be an array of strings.");
            return result;
        }

        foreach (var item in element.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{label}.{property} entries must be non-empty strings.");
            }
            else if (!result.Contains(value, StringComparer.Ordinal))
            {
                result.Add(value);
            }
        }

        return result;
    }
}
