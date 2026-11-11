
namespace Emberdrop.Plugins;

/// <summary>
/// The hook through which the host that uses the hosting core gives meaning to a package's exports. The core resolves each export's
/// type and loads it; the handler decides whether the export is acceptable and what the host should remember about it.
/// </summary>
public interface IExportHandler
{
    /// <summary>
    /// Called once per export while a package is being loaded, with the export's resolved (concrete) type. Add a message to
    /// <paramref name="problems"/> to refuse the package.
    /// </summary>
    /// <param name="manifestExport">The export as the manifest declares it.</param>
    /// <param name="type">The export's type, from the plugin's load context. Do not keep it: only plain data may leave this call.</param>
    /// <param name="problems">Where to add what is wrong. The package is rejected, with all problems listed, if there are any.</param>
    /// <returns>Plain data to keep with the export (it appears as <see cref="ExportDescriptor.Metadata"/>), or <see langword="null"/>. It must not reference plugin types.</returns>
    object? Inspect(ManifestExport manifestExport, Type type, IList<string> problems);

    /// <summary>The host configuration keys the export may see. The plugin's configuration view contains exactly the keys of all its exports.</summary>
    IEnumerable<string> ConfigurationKeys(ManifestExport manifestExport);
}

internal sealed class NullExportHandler : IExportHandler
{
    public static NullExportHandler Instance { get; } = new();

    public object? Inspect(ManifestExport manifestExport, Type type, IList<string> problems) => null;

    public IEnumerable<string> ConfigurationKeys(ManifestExport manifestExport) => [];
}
