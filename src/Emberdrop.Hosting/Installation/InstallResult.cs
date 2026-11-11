namespace Emberdrop.Plugins;

/// <summary>How an attempt to install a package ended.</summary>
public enum InstallOutcome
{
    /// <summary>The package is installed and its version is active.</summary>
    Installed = 0,

    /// <summary>The same package (same id, version and bytes) was already installed; nothing changed.</summary>
    Unchanged = 1,

    /// <summary>The package was rejected and moved to quarantine with a reason file. Whatever was active before is still active.</summary>
    Quarantined = 2,

    /// <summary>The file never became complete and readable within the timeout; it was left where it is for a later event.</summary>
    Deferred = 3,

    /// <summary>The file was gone by the time it was looked at, for example because an earlier event already handled it.</summary>
    Skipped = 4,
}

/// <summary>The result of installing one package.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="PackagePath">The package file that was handled.</param>
/// <param name="PluginId">The plugin id, when the manifest could be read.</param>
/// <param name="Version">The plugin version, when the manifest could be read.</param>
/// <param name="Reasons">Why the package was rejected or deferred.</param>
/// <param name="QuarantinedTo">Where a rejected package was moved.</param>
public sealed record InstallResult(
    InstallOutcome Outcome,
    string PackagePath,
    string? PluginId,
    PluginVersion? Version,
    IReadOnlyList<string> Reasons,
    string? QuarantinedTo);
