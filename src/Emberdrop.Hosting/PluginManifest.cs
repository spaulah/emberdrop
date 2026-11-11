using System.Text.Json;

namespace Emberdrop.Plugins;

/// <summary>A validated plugin manifest (<c>emberdrop.json</c>): package metadata and the list of things the package exports.</summary>
/// <param name="Id">Plugin id: lowercase letters, digits, dots, dashes and underscores.</param>
/// <param name="Version">Plugin version.</param>
/// <param name="Description">Optional human-readable description.</param>
/// <param name="EntryAssemblies">Package-relative paths of the assemblies that contain the plugin's exported types.</param>
/// <param name="MinAbstractionsVersion">The oldest <c>Emberdrop.Abstractions</c> version the plugin was built for.</param>
/// <param name="Exports">What the plugin exports. The hosting core knows each one's name, kind and type; the rest is for the host that uses them.</param>
/// <param name="Startup">
/// Optional full name of a class with a public parameterless constructor and a method
/// <c>void ConfigureServices(IServiceCollection services)</c>, where the plugin registers the services its exports need.
/// </param>
public sealed record PluginManifest(
    string Id,
    PluginVersion Version,
    string? Description,
    IReadOnlyList<string> EntryAssemblies,
    PluginVersion MinAbstractionsVersion,
    IReadOnlyList<ManifestExport> Exports,
    string? Startup = null)
{
    /// <summary>The file name of the manifest inside a package.</summary>
    public const string FileName = "emberdrop.json";
}

/// <summary>One thing a package exports, as its manifest declares it.</summary>
/// <param name="Name">Logical name, unique within the plugin. This, not the CLR type, is what hosts refer to.</param>
/// <param name="Type">Full name of the CLR type in the entry assemblies.</param>
/// <param name="Kind">A string saying what sort of export this is. Not an enum: hosts decide which kinds they handle.</param>
/// <param name="Properties">The whole JSON object from the manifest, including the properties only the host understands.</param>
public sealed record ManifestExport(string Name, string Type, string Kind, JsonElement Properties);
