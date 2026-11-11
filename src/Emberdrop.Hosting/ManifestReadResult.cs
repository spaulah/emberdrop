namespace Emberdrop.Plugins;

/// <summary>The outcome of reading a manifest: either a valid manifest or every problem found.</summary>
public sealed class ManifestReadResult
{
    private ManifestReadResult(PluginManifest? manifest, IReadOnlyList<string> errors)
    {
        Manifest = manifest;
        Errors = errors;
    }

    /// <summary>The manifest, when it is valid.</summary>
    public PluginManifest? Manifest { get; }

    /// <summary>Every validation problem. Empty when the manifest is valid.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Whether the manifest is valid.</summary>
    public bool IsValid => Manifest is not null;

    internal static ManifestReadResult Valid(PluginManifest manifest) => new(manifest, []);

    internal static ManifestReadResult Invalid(IReadOnlyList<string> errors) => new(null, errors);
}
