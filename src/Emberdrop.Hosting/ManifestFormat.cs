using System.Text.Json;

namespace Emberdrop.Plugins;

/// <summary>
/// How a host writes the things a package exports in its manifest. The hosting core only needs each export to have a name, a type
/// and a kind; what the rest of an export's object means is the host's business, and <see cref="ValidateExport"/> lets it check that.
/// </summary>
/// <param name="ExportsProperty">The name of the manifest property that holds the array of exports.</param>
/// <param name="ExportNoun">What an export is called in messages, for example <c>export</c>.</param>
/// <param name="DefaultKind">The kind an export has when it does not say.</param>
/// <param name="ValidateExport">
/// Optional: called for every export object with its label (for example <c>exports[2]</c>) and the list to add problems to.
/// It checks the properties the hosting core does not know about.
/// </param>
public sealed record ManifestFormat(
    string ExportsProperty = "exports",
    string ExportNoun = "export",
    string DefaultKind = "default",
    Action<JsonElement, string, IList<string>>? ValidateExport = null)
{
    /// <summary>The generic format: exports in <c>exports</c>.</summary>
    public static ManifestFormat Default { get; } = new();
}
