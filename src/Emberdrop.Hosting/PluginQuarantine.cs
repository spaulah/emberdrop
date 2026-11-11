using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Emberdrop.Plugins;

/// <summary>
/// Moves packages that cannot be used out of the way and records why, next to them, so an operator can see
/// the reason without reading logs.
/// </summary>
public sealed class PluginQuarantine
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly PluginHostOptions _options;
    private readonly ILogger<PluginQuarantine> _logger;

    /// <summary>Creates the quarantine.</summary>
    public PluginQuarantine(PluginHostOptions options, ILogger<PluginQuarantine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Moves a package file or directory into the quarantine directory and writes <c>&lt;name&gt;.reason.json</c> beside it.
    /// </summary>
    /// <returns>The new location of the quarantined package.</returns>
    public async Task<string> QuarantineAsync(string path, IReadOnlyList<string> reasons, string? details = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(reasons);

        var source = Path.GetFullPath(path);
        var isDirectory = Directory.Exists(source);
        if (!isDirectory && !File.Exists(source))
        {
            throw new FileNotFoundException("The package to quarantine does not exist.", source);
        }

        var root = _options.QuarantineDirectory;
        Directory.CreateDirectory(root);

        var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var destination = Path.Combine(root, $"{stamp}-{Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar))}");
        for (var counter = 1; File.Exists(destination) || Directory.Exists(destination); counter++)
        {
            destination = Path.Combine(root, $"{stamp}-{counter}-{Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar))}");
        }

        if (isDirectory)
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }

        var report = new
        {
            quarantinedAtUtc = DateTime.UtcNow,
            originalPath = source,
            reasons,
            details,
        };
        await File.WriteAllTextAsync(destination + ".reason.json", JsonSerializer.Serialize(report, JsonOptions), cancellationToken).ConfigureAwait(false);

        _logger.LogWarning("Quarantined plugin package {Source} to {Destination}: {Reasons}", source, destination, string.Join(" | ", reasons));
        return destination;
    }
}
