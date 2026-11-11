namespace Emberdrop.Plugins;

/// <summary>Settings for hosting plugins.</summary>
public sealed class PluginHostOptions
{
    /// <summary>
    /// Assemblies that always come from the host, never from a plugin. Plugins and the host must agree on these types,
    /// so they are the contract between them. Everything not listed here (and not part of the .NET runtime) is private to each plugin.
    /// </summary>
    public IList<string> SharedAssemblies { get; } = new List<string>(DefaultSharedAssemblies);

    /// <summary>Directory that holds staging, installed, archived and quarantined packages. Relative paths resolve against the working directory.</summary>
    public string StorageRoot { get; set; } = Path.Combine(".emberdrop", "plugins");

    /// <summary>How many times unload verification forces a garbage collection while waiting for a load context to be collected.</summary>
    public int UnloadVerificationAttempts { get; set; } = 10;

    /// <summary>How long unload verification waits between garbage collections.</summary>
    public TimeSpan UnloadVerificationDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>How the manifest lists a package's exports and what the properties of an export mean. The default is the generic format.</summary>
    public ManifestFormat ManifestFormat { get; set; } = ManifestFormat.Default;

    /// <summary>
    /// What the host that uses the hosting core makes of the exports it loads: it inspects each export's type when a package is loaded
    /// and says which configuration keys the export may see. The default does neither.
    /// </summary>
    public IExportHandler ExportHandler { get; set; } = NullExportHandler.Instance;

    /// <summary>
    /// Host services (beyond logging and a filtered view of configuration, which plugins always get) that are made available to
    /// plugin code. Each must be a singleton registered in the host, and its type must come from a shared assembly or plugins
    /// cannot ask for it. Everything not listed here is invisible to plugins.
    /// </summary>
    public IList<Type> HostServices { get; } = new List<Type>();

    /// <summary>
    /// SHA-256 hashes (hex) of the package files that may be installed. When empty, which is the default, no integrity
    /// check is done. When it has entries, any package whose hash is not listed is rejected. It limits <i>which</i> package
    /// loads, not what the package does (see docs/engineering/known-limits.md); it is not code signing.
    /// </summary>
    public IList<string> AllowedPackageHashes { get; } = new List<string>();

    /// <summary>How many replaced versions of each plugin are kept in the archive; older ones are deleted. Zero keeps none.</summary>
    public int ArchiveRetention { get; set; } = 3;

    /// <summary>
    /// Whether installing a version retires the other versions of the same plugin (the version just installed takes over).
    /// Turn it off to keep several versions active side by side, for example when stored exports are pinned to older ones.
    /// </summary>
    public bool RetireReplacedVersions { get; set; } = true;

    /// <summary>The most entries a package may contain.</summary>
    public int MaxPackageEntries { get; set; } = 5000;

    /// <summary>The most bytes a package may expand to.</summary>
    public long MaxExtractedBytes { get; set; } = 500L * 1024 * 1024;

    /// <summary>The most a package may expand relative to its own size. A zip bomb is a few kilobytes that expand to gigabytes.</summary>
    public int MaxExpansionRatio { get; set; } = 100;

    /// <summary>How often a newly appeared package file is checked to see whether its writer has finished.</summary>
    public TimeSpan StableFileCheckInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How many consecutive checks must see the same size and modification time before the file counts as complete.</summary>
    public int StableFileChecks { get; set; } = 2;

    /// <summary>How long to wait for a file to become complete and readable before leaving it for a later event.</summary>
    public TimeSpan StableFileTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How long executions on a retiring plugin version may finish on their own before they are asked to stop.</summary>
    public TimeSpan DrainGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long executions get to react to the cancellation request before the <see cref="StuckPolicy"/> applies.</summary>
    public TimeSpan DrainCancellationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>What to do with executions that outlast both drain periods. The default forces nothing.</summary>
    public StuckPolicy StuckPolicy { get; set; } = StuckPolicy.LeaveDraining;

    /// <summary>How many finished (unloaded, failed or quarantined) versions are kept in the status list.</summary>
    public int HistoryLimit { get; set; } = 100;

    /// <summary>The assemblies shared by default: the Emberdrop contract plus the abstractions plugins need to log and read configuration.</summary>
    public static IReadOnlyList<string> DefaultSharedAssemblies { get; } =
    [
        "Emberdrop.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.Primitives",
    ];

    /// <summary>Where installed plugin versions live: <c>installed/&lt;id&gt;/&lt;version&gt;</c>.</summary>
    public string InstalledDirectory => Path.Combine(Path.GetFullPath(StorageRoot), "installed");

    /// <summary>Where packages are extracted and checked before they are promoted. Always on the same volume as <see cref="InstalledDirectory"/>, so promotion is a rename.</summary>
    public string StagingDirectory => Path.Combine(Path.GetFullPath(StorageRoot), "staging");

    /// <summary>Where replaced versions are kept: <c>archive/&lt;id&gt;/&lt;version&gt;_&lt;time&gt;</c>.</summary>
    public string ArchiveDirectory => Path.Combine(Path.GetFullPath(StorageRoot), "archive");

    /// <summary>The directory where quarantined packages are placed together with a reason file.</summary>
    public string QuarantineDirectory => Path.Combine(Path.GetFullPath(StorageRoot), "quarantine");
}
