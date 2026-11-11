using System.Text.Json;
using Emberdrop.Plugins;

namespace Emberdrop.WorkUnits;

/// <summary>A work unit declared in a plugin manifest.</summary>
/// <param name="Name">Logical work unit name, unique within the plugin. This, not the CLR type, is what stored work units refer to.</param>
/// <param name="Type">Full name of the CLR type that implements <see cref="IWorkUnit"/>.</param>
/// <param name="Kind">What sort of work unit this is. A string, not an enum: <c>work-unit</c> by default; a host may register more kinds (see <see cref="WorkUnitOptions.WorkUnitKinds"/>).</param>
/// <param name="Description">Optional human-readable description.</param>
/// <param name="InputType">Optional full name of the input type. When given it must be the work unit's actual input type.</param>
/// <param name="OutputType">Optional full name of the output type. When given it must be the work unit's actual output type.</param>
/// <param name="Triggers">The triggers the work unit declares, as the JSON objects from the manifest. They are defaults; the host interprets them with its trigger kinds.</param>
/// <param name="RequiredConfiguration">Configuration keys the work unit needs; a work unit with missing keys is reported at registration.</param>
/// <param name="Capabilities">Capabilities the work unit declares. Validated and stored; see docs/concepts/capabilities.md for what is and is not enforced.</param>
/// <param name="InputSchemaVersion">The schema version of the stored input the work unit understands. A stored work unit with a different version is refused, not guessed at.</param>
public sealed record PluginWorkUnitManifest(
    string Name,
    string Type,
    string Kind,
    string? Description,
    string? InputType,
    string? OutputType,
    IReadOnlyList<JsonElement> Triggers,
    IReadOnlyList<string> RequiredConfiguration,
    IReadOnlyList<string> Capabilities,
    int InputSchemaVersion = 1)
{
    /// <summary>The kind a work unit has when the manifest does not say.</summary>
    public const string DefaultKind = "work-unit";

    /// <summary>The name of the manifest property that lists the work units.</summary>
    public const string ManifestProperty = "workUnits";

    /// <summary>How work units are written in a manifest, for <see cref="PluginManifestReader"/>: the <c>workUnits</c> array, with the properties below checked.</summary>
    public static ManifestFormat Format { get; } = new(ManifestProperty, "work unit", DefaultKind, Validate);

    /// <summary>The cron expression of the first recurring trigger, or <see langword="null"/>. It becomes the work unit's default schedule.</summary>
    public string? DefaultSchedule => Triggers
        .Where(t => t.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == RecurringTrigger.KindName)
        .Select(t => t.TryGetProperty("cron", out var cron) && cron.ValueKind == JsonValueKind.String ? cron.GetString() : null)
        .FirstOrDefault(c => c is not null);

    /// <summary>Reads the work unit properties of an export of a manifest that was read with <see cref="Format"/>.</summary>
    public static PluginWorkUnitManifest From(ManifestExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        var item = export.Properties;
        var errors = new List<string>();
        var schemaVersion = item.TryGetProperty("inputSchemaVersion", out var element) && element.TryGetInt32(out var parsed) ? parsed : 1;
        return new PluginWorkUnitManifest(
            export.Name,
            export.Type,
            export.Kind,
            ManifestJson.OptionalString(item, "description", errors),
            ManifestJson.OptionalString(item, "inputType", errors),
            ManifestJson.OptionalString(item, "outputType", errors),
            ReadTriggers(item, errors, string.Empty),
            ManifestJson.ReadStringArray(item, "requiredConfiguration", errors, string.Empty),
            ManifestJson.ReadStringArray(item, "capabilities", errors, string.Empty),
            schemaVersion);
    }

    /// <summary>The work units a package declares.</summary>
    public static IReadOnlyList<PluginWorkUnitManifest> OfPackage(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Exports.Select(From).ToList();
    }

    private static void Validate(JsonElement item, string label, IList<string> errors)
    {
        var description = ManifestJson.OptionalString(item, "description", errors, label);
        if (description is { Length: > 1000 })
        {
            errors.Add($"{label}.description is longer than 1000 characters.");
        }

        var inputType = ManifestJson.OptionalString(item, "inputType", errors, label);
        var outputType = ManifestJson.OptionalString(item, "outputType", errors, label);
        foreach (var (property, value) in new[] { ("inputType", inputType), ("outputType", outputType) })
        {
            if (value is not null && !TypeNames.IsValid(value))
            {
                errors.Add($"{label}.{property} '{value}' is not a valid CLR type name.");
            }
        }

        ReadTriggers(item, errors, label);
        var capabilities = ManifestJson.ReadStringArray(item, "capabilities", errors, label);
        foreach (var capability in capabilities.Where(c => !TypeNames.IsValidCapability(c)))
        {
            errors.Add($"{label}.capabilities entry '{capability}' is invalid: use 1-200 letters, digits, ':', '.', '-', '_' or '/'.");
        }

        ManifestJson.ReadStringArray(item, "requiredConfiguration", errors, label);

        if (item.TryGetProperty("inputSchemaVersion", out var schemaElement)
            && (schemaElement.ValueKind != JsonValueKind.Number || !schemaElement.TryGetInt32(out var schemaVersion) || schemaVersion < 1))
        {
            errors.Add($"{label}.inputSchemaVersion must be a whole number of at least 1.");
        }
    }

    private static List<JsonElement> ReadTriggers(JsonElement parent, IList<string> errors, string label)
    {
        var result = new List<JsonElement>();
        if (!parent.TryGetProperty("triggers", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return result;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{label}.triggers must be an array of objects.");
            return result;
        }

        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var triggerLabel = $"{label}.triggers[{index++}]";
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(kind.GetString()))
            {
                errors.Add($"{triggerLabel} must be an object with a 'kind'.");
                continue;
            }

            // What the rest of the object must contain depends on the kind, which only the host knows (see ManifestTriggers).
            result.Add(item.Clone());
        }

        return result;
    }
}

internal static partial class TypeNames
{
    public static bool IsValid(string name) => TypeNamePattern().IsMatch(name);

    public static bool IsValidCapability(string capability) => CapabilityPattern().IsMatch(capability);

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.+]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex TypeNamePattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9:._/-]{0,199}$")]
    private static partial System.Text.RegularExpressions.Regex CapabilityPattern();
}
