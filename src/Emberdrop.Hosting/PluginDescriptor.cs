namespace Emberdrop.Plugins;

/// <summary>A read-only description of a loaded plugin version. It contains no plugin types, so holding one never keeps a plugin alive.</summary>
/// <param name="Id">The plugin's id.</param>
/// <param name="Version">The plugin's version.</param>
/// <param name="EntryAssemblies">The entry assembly file names declared by the plugin.</param>
/// <param name="Exports">What the plugin exports.</param>
public sealed record PluginDescriptor(
    string Id,
    PluginVersion Version,
    IReadOnlyList<string> EntryAssemblies,
    IReadOnlyList<ExportDescriptor> Exports);

/// <summary>A read-only description of one export of a loaded plugin version.</summary>
/// <param name="Name">The export's logical name, unique within the plugin.</param>
/// <param name="Kind">The export's kind, a string.</param>
/// <param name="TypeName">The full name of the CLR type, as a string.</param>
/// <param name="AssemblyName">The simple name of the assembly that contains the type.</param>
/// <param name="Metadata">What the host's <see cref="IExportHandler"/> chose to keep when the package was loaded. Plain data only.</param>
public sealed record ExportDescriptor(string Name, string Kind, string TypeName, string AssemblyName, object? Metadata = null);
