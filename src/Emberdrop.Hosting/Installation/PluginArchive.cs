using Microsoft.Extensions.Logging;

namespace Emberdrop.Plugins;

/// <summary>Keeps replaced plugin versions on disk, newest first, and removes the oldest beyond the retention limit.</summary>
public sealed class PluginArchive
{
    private readonly PluginHostOptions _options;
    private readonly ILogger<PluginArchive> _logger;

    /// <summary>Creates the archive.</summary>
    public PluginArchive(PluginHostOptions options, ILogger<PluginArchive> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <summary>Moves an installed version's directory into the archive.</summary>
    /// <param name="directory">The installed directory to move.</param>
    /// <param name="pluginId">The plugin id.</param>
    /// <param name="version">The version being archived.</param>
    /// <param name="prune">Whether to delete the plugin's older archived versions beyond the retention limit straight away.
    /// Pass <see langword="false"/> while the archived copy might still be needed to roll back.</param>
    /// <returns>Where the directory now is.</returns>
    public string Archive(string directory, string pluginId, PluginVersion version, bool prune = true)
    {
        var folder = Path.Combine(_options.ArchiveDirectory, pluginId);
        Directory.CreateDirectory(folder);

        // A fixed-width UTC stamp, so names sort by time.
        var target = Path.Combine(folder, $"{version}_{DateTime.UtcNow:yyyyMMddTHHmmssfff}");
        for (var counter = 1; Directory.Exists(target); counter++)
        {
            target = Path.Combine(folder, $"{version}_{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{counter}");
        }

        Directory.Move(directory, target);
        _logger.LogInformation("Archived {PluginId} {Version} to {Target}", pluginId, version, target);
        if (prune)
        {
            Prune(pluginId);
        }

        return target;
    }

    /// <summary>Deletes the oldest archived versions of a plugin beyond <see cref="PluginHostOptions.ArchiveRetention"/>.</summary>
    public void Prune(string pluginId)
    {
        var folder = Path.Combine(_options.ArchiveDirectory, pluginId);
        if (!Directory.Exists(folder))
        {
            return;
        }

        var surplus = Directory.GetDirectories(folder)
            .OrderByDescending(d => StampOf(d), StringComparer.Ordinal)
            .Skip(Math.Max(0, _options.ArchiveRetention))
            .ToList();

        foreach (var old in surplus)
        {
            try
            {
                Directory.Delete(old, recursive: true);
                _logger.LogInformation("Removed archived plugin version {Directory} (retention is {Retention})", old, _options.ArchiveRetention);
            }
            catch (IOException ex)
            {
                _logger.LogWarning("Could not remove archived plugin version {Directory}: {Message}", old, ex.Message);
            }
        }
    }

    private static string StampOf(string directory)
    {
        var name = Path.GetFileName(directory);
        return name[(name.LastIndexOf('_') + 1)..];
    }
}
