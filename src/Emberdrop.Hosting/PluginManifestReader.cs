using System.Text.Json;
using System.Text.RegularExpressions;

namespace Emberdrop.Plugins;

/// <summary>Reads and validates plugin manifests without loading any plugin code.</summary>
public static partial class PluginManifestReader
{
    /// <summary>The manifest schema version this host understands.</summary>
    public const int SupportedSchemaVersion = 1;

    private const int MaxManifestBytes = 256 * 1024;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 16,
    };

    [GeneratedRegex("^[a-z][a-z0-9-]{0,49}$")]
    private static partial Regex KindPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex PluginIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExportNamePattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.+]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TypeNamePattern();

    /// <summary>Reads the manifest from a package directory.</summary>
    /// <param name="directory">The package directory.</param>
    /// <param name="format">How the exports are written; <see langword="null"/> for the generic format.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<ManifestReadResult> ReadFromDirectoryAsync(string directory, ManifestFormat? format = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var path = Path.Combine(directory, PluginManifest.FileName);
        if (!File.Exists(path))
        {
            return ManifestReadResult.Invalid([$"The package has no {PluginManifest.FileName} manifest at its root."]);
        }

        var length = new FileInfo(path).Length;
        if (length > MaxManifestBytes)
        {
            return ManifestReadResult.Invalid([$"{PluginManifest.FileName} is {length} bytes; the limit is {MaxManifestBytes}."]);
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return Parse(json, format);
    }

    /// <summary>Parses and validates manifest JSON. Never throws for bad input; problems are returned.</summary>
    /// <param name="json">The manifest text.</param>
    /// <param name="format">How the exports are written; <see langword="null"/> for the generic format.</param>
    public static ManifestReadResult Parse(string json, ManifestFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            return ManifestReadResult.Invalid([$"{PluginManifest.FileName} is not valid JSON: {ex.Message}"]);
        }

        using (document)
        {
            return Validate(document.RootElement, format ?? ManifestFormat.Default);
        }
    }

    private static ManifestReadResult Validate(JsonElement root, ManifestFormat format)
    {
        var errors = new List<string>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ManifestReadResult.Invalid(["The manifest must be a JSON object."]);
        }

        if (!root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var schemaVersion))
        {
            errors.Add("'schemaVersion' is required and must be a number.");
        }
        else if (schemaVersion != SupportedSchemaVersion)
        {
            errors.Add($"'schemaVersion' is {schemaVersion}; this host supports {SupportedSchemaVersion}.");
        }

        var id = RequiredString(root, "id", errors);
        if (id is not null && !PluginIdPattern().IsMatch(id))
        {
            errors.Add($"'id' value '{id}' is invalid: use 1-100 lowercase letters, digits, '.', '-' or '_', starting with a letter or digit.");
        }

        var version = RequiredVersion(root, "version", errors);
        var minAbstractions = RequiredVersion(root, "minAbstractionsVersion", errors);
        var description = ManifestJson.OptionalString(root, "description", errors);
        var entryAssemblies = ReadEntryAssemblies(root, errors);
        var exports = ReadExports(root, format, errors);
        var startup = ManifestJson.OptionalString(root, "startup", errors);
        if (startup is not null && !TypeNamePattern().IsMatch(startup))
        {
            errors.Add($"'startup' value '{startup}' is not a valid CLR type name.");
        }

        if (errors.Count > 0 || id is null || version is null || minAbstractions is null)
        {
            return ManifestReadResult.Invalid(errors);
        }

        return ManifestReadResult.Valid(new PluginManifest(id, version, description, entryAssemblies, minAbstractions, exports, startup));
    }

    private static List<string> ReadEntryAssemblies(JsonElement root, List<string> errors)
    {
        var result = new List<string>();
        if (!root.TryGetProperty("entryAssemblies", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            errors.Add("'entryAssemblies' is required and must be an array of file names.");
            return result;
        }

        foreach (var item in element.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add("'entryAssemblies' entries must be non-empty strings.");
            }
            else if (!IsSafeRelativeDllPath(value))
            {
                errors.Add($"Entry assembly '{value}' must be a relative path to a .dll inside the package (no '..', no rooted paths).");
            }
            else if (result.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Entry assembly '{value}' is listed twice.");
            }
            else
            {
                result.Add(value);
            }
        }

        if (result.Count == 0 && errors.All(e => !e.StartsWith("'entryAssemblies'", StringComparison.Ordinal)))
        {
            errors.Add("'entryAssemblies' must list at least one assembly.");
        }

        return result;
    }

    private static List<ManifestExport> ReadExports(JsonElement root, ManifestFormat format, List<string> errors)
    {
        var property = format.ExportsProperty;
        var result = new List<ManifestExport>();
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"'{property}' is required and must be an array.");
            return result;
        }

        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var label = $"{property}[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{label} must be an object.");
                continue;
            }

            var name = RequiredString(item, "name", errors, label);
            if (name is not null && !ExportNamePattern().IsMatch(name))
            {
                errors.Add($"{label}.name '{name}' is invalid: use 1-100 letters, digits, '.', '-' or '_'.");
            }

            var type = RequiredString(item, "type", errors, label);
            if (type is not null && !TypeNamePattern().IsMatch(type))
            {
                errors.Add($"{label}.type '{type}' is not a valid CLR type name.");
            }

            var kind = ManifestJson.OptionalString(item, "kind", errors, label) ?? format.DefaultKind;
            if (!KindPattern().IsMatch(kind))
            {
                errors.Add($"{label}.kind '{kind}' is invalid: use 1-50 lowercase letters, digits or '-', starting with a letter.");
            }

            var before = errors.Count;
            format.ValidateExport?.Invoke(item, label, errors);

            if (name is not null && type is not null && errors.Count == before)
            {
                if (result.Any(e => string.Equals(e.Name, name, StringComparison.Ordinal)))
                {
                    errors.Add($"{char.ToUpperInvariant(format.ExportNoun[0])}{format.ExportNoun[1..]} name '{name}' is declared twice.");
                }
                else
                {
                    result.Add(new ManifestExport(name, type, kind, item.Clone()));
                }
            }
        }

        if (index == 0)
        {
            errors.Add($"'{property}' must declare at least one {format.ExportNoun}.");
        }

        return result;
    }



    private static string? RequiredString(JsonElement parent, string property, List<string> errors, string? label = null)
    {
        var prefix = label is null ? $"'{property}'" : $"{label}.{property}";
        if (!parent.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            errors.Add($"{prefix} is required and must be a non-empty string.");
            return null;
        }

        return element.GetString();
    }


    private static PluginVersion? RequiredVersion(JsonElement parent, string property, List<string> errors)
    {
        var text = RequiredString(parent, property, errors);
        if (text is null)
        {
            return null;
        }

        if (!PluginVersion.TryParse(text, out var version))
        {
            errors.Add($"'{property}' value '{text}' is not a valid version (expected major.minor.patch[-prerelease]).");
            return null;
        }

        return version;
    }

    private static bool IsSafeRelativeDllPath(string path)
    {
        if (Path.IsPathRooted(path) || path.Contains('\\', StringComparison.Ordinal) || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
    }
}
