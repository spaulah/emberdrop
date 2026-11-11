namespace Emberdrop.Plugins;

/// <summary>A package that passed every check that can be done without running its code.</summary>
public sealed class ValidatedPackage
{
    internal ValidatedPackage(PluginManifest manifest, string directory, IReadOnlyList<string> entryPaths, SharedAssemblyPolicy policy)
    {
        Manifest = manifest;
        Directory = directory;
        EntryPaths = entryPaths;
        Policy = policy;
    }

    /// <summary>The package's manifest.</summary>
    public PluginManifest Manifest { get; }

    /// <summary>The extracted package directory.</summary>
    public string Directory { get; }

    internal IReadOnlyList<string> EntryPaths { get; }

    internal SharedAssemblyPolicy Policy { get; }
}
