namespace Emberdrop.Server.Configuration;

public class PluginConfiguration
{
    public const string SectionName = "Plugins";

    public List<string> MonitoredPaths { get; set; } = new();
    public bool EnableFileWatcher { get; set; } = true;

    /// <summary>Where installed, archived and quarantined plugins live. Empty means the library default (.emberdrop/plugins).</summary>
    public string? StorageRoot { get; set; }

    /// <summary>SHA-256 hashes (hex) of the packages that may be installed. Empty, the default, turns the check off.</summary>
    public List<string> AllowedPackageHashes { get; set; } = new();

    /// <summary>How many replaced versions of each plugin to keep in the archive. Empty means the library default (3).</summary>
    public int? ArchiveRetention { get; set; }
}
