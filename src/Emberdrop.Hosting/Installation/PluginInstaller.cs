using Microsoft.Extensions.Logging;

namespace Emberdrop.Plugins;

/// <summary>
/// Installs plugin packages safely.
/// </summary>
/// <remarks>
/// <para>Order of events for one package: wait until the file is complete; check its SHA-256 against the allowlist (if one is
/// configured); extract it into a staging directory with traversal, size and expansion limits; read and validate the manifest;
/// prove the package activates; promote the staging directory to its final place with a single rename; activate it.</para>
/// <para><b>Whatever was running before keeps running if any step fails.</b> A different version is activated beside the old
/// one, so a failure leaves the old one untouched. A replacement for the <i>same</i> version is first loaded into a throwaway
/// context to prove it works, and only then is the installed copy retired.
/// A package that fails is moved to quarantine with a reason file.</para>
/// </remarks>
public sealed class PluginInstaller : IAsyncDisposable
{
    private const string HashFileName = "package.sha256";

    private readonly PluginHost _host;
    private readonly PluginQuarantine _quarantine;
    private readonly PluginArchive _archive;
    private readonly PluginHostOptions _options;
    private readonly ILogger<PluginInstaller> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<Task> _background = [];

    /// <summary>Creates the installer.</summary>
    public PluginInstaller(PluginHost host, PluginQuarantine quarantine, PluginArchive archive, PluginHostOptions options, ILogger<PluginInstaller> logger)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(quarantine);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _host = host;
        _quarantine = quarantine;
        _archive = archive;
        _options = options;
        _logger = logger;
    }

    /// <summary>Creates the storage directories and removes whatever an interrupted install left in staging.</summary>
    public Task PrepareAsync()
    {
        Directory.CreateDirectory(_options.InstalledDirectory);
        Directory.CreateDirectory(_options.ArchiveDirectory);
        Directory.CreateDirectory(_options.QuarantineDirectory);
        Directory.CreateDirectory(_options.StagingDirectory);

        foreach (var leftover in Directory.GetDirectories(_options.StagingDirectory))
        {
            _logger.LogWarning("Removing {Directory}, left in staging by an install that did not finish", leftover);
            Directory.Delete(leftover, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Loads every installed version at startup. One that no longer loads (or is no longer on the allowlist) is quarantined and
    /// the others are not affected.
    /// </summary>
    public async Task<IReadOnlyList<InstallResult>> LoadInstalledAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<InstallResult>();
        if (!Directory.Exists(_options.InstalledDirectory))
        {
            return results;
        }

        var directories = Directory.GetDirectories(_options.InstalledDirectory)
            .SelectMany(Directory.GetDirectories)
            .OrderBy(d => d, StringComparer.Ordinal);

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pluginId = Path.GetFileName(Path.GetDirectoryName(directory)!);
            var version = PluginVersion.TryParse(Path.GetFileName(directory), out var parsed) ? parsed : null;

            var allowlistProblem = await AllowlistProblemForInstalledAsync(directory).ConfigureAwait(false);
            if (allowlistProblem is not null)
            {
                results.Add(await QuarantineInstalledAsync(directory, pluginId, version, [allowlistProblem], null).ConfigureAwait(false));
                continue;
            }

            try
            {
                var descriptor = await _host.LoadAsync(directory, cancellationToken).ConfigureAwait(false);
                results.Add(new InstallResult(InstallOutcome.Installed, directory, descriptor.Id, descriptor.Version, [], null));
            }
            catch (PluginLoadException ex)
            {
                _logger.LogError("Installed plugin {Directory} could not be loaded: {Message}", directory, ex.Message);
                results.Add(await QuarantineInstalledAsync(directory, pluginId, version, ex.Reasons, ex.Details).ConfigureAwait(false));
            }
        }

        _logger.LogInformation("Loaded {Count} installed plugin version(s)", _host.GetPlugins().Count);
        return results;
    }

    /// <summary>Installs one package file. See the class remarks for the order of steps and the guarantees.</summary>
    public async Task<InstallResult> InstallAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var path = Path.GetFullPath(packagePath);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await InstallCoreAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Completes when the retirements and archiving started by earlier installs have finished.</summary>
    public async Task WhenIdleAsync()
    {
        Task[] pending;
        lock (_background)
        {
            pending = [.. _background];
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        try
        {
            await WhenIdleAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }

        _shutdown.Dispose();
        _lock.Dispose();
    }

    private async Task<InstallResult> InstallCoreAsync(string path, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Installing plugin package {Package}", path);

        var stable = await StableFileDetector.WaitAsync(
            path, _options.StableFileCheckInterval, _options.StableFileChecks, _options.StableFileTimeout, cancellationToken).ConfigureAwait(false);
        if (stable == StableFileResult.Gone)
        {
            _logger.LogDebug("Package {Package} is gone (already handled)", path);
            return new InstallResult(InstallOutcome.Skipped, path, null, null, [], null);
        }

        if (stable == StableFileResult.TimedOut)
        {
            _logger.LogWarning("Package {Package} did not become complete and readable within {Timeout}; leaving it for a later file event", path, _options.StableFileTimeout);
            return new InstallResult(InstallOutcome.Deferred, path, null, null, ["The file was still being written or was locked until the timeout."], null);
        }

        var hash = await PackageHasher.Sha256Async(path, cancellationToken).ConfigureAwait(false);
        if (!PackageHasher.IsAllowed(hash, _options.AllowedPackageHashes))
        {
            return await RejectAsync(path, [$"The package's SHA-256 ({hash}) is not on the allowlist. Add it to Plugins:AllowedPackageHashes if you trust this package."], null, null, null).ConfigureAwait(false);
        }

        var staging = Path.Combine(_options.StagingDirectory, Guid.NewGuid().ToString("N"));
        string? archivedOld = null;
        PluginManifest? manifest = null;
        try
        {
            try
            {
                await PackageExtractor.ExtractAsync(path, staging, _options, cancellationToken).ConfigureAwait(false);
                manifest = await PluginLoader.ReadManifestAsync(staging, _options.ManifestFormat, cancellationToken).ConfigureAwait(false);
            }
            catch (PluginLoadException ex)
            {
                return await RejectAsync(path, ex.Reasons, ex.Details, null, null).ConfigureAwait(false);
            }

            var destination = Path.Combine(_options.InstalledDirectory, manifest.Id, manifest.Version.ToString());
            var (proceed, replaced, early) = await PrepareDestinationAsync(path, manifest, staging, destination, hash, cancellationToken).ConfigureAwait(false);
            if (!proceed)
            {
                return early!;
            }

            archivedOld = replaced;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);

            try
            {
                await _host.LoadAsync(destination, cancellationToken).ConfigureAwait(false);
            }
            catch (PluginLoadException ex)
            {
                Directory.Delete(destination, recursive: true);
                var restored = archivedOld is not null && await RestoreAsync(archivedOld, destination, manifest).ConfigureAwait(false);
                var note = archivedOld is null ? string.Empty : restored
                    ? $" The previously installed {manifest.Id} {manifest.Version} was restored."
                    : $" The previously installed {manifest.Id} {manifest.Version} could not be restored; it is in the archive.";
                return await RejectAsync(path, [.. ex.Reasons, .. note.Length > 0 ? new[] { note.Trim() } : []], ex.Details, manifest.Id, manifest.Version).ConfigureAwait(false);
            }

            await File.WriteAllTextAsync(Path.Combine(destination, HashFileName), hash, cancellationToken).ConfigureAwait(false);
            File.Move(path, Path.Combine(destination, "package.emberdrop"), overwrite: true);
            _archive.Prune(manifest.Id);

            if (_options.RetireReplacedVersions)
            {
                RetireOtherVersions(manifest);
            }

            _logger.LogInformation("Installed plugin {PluginId} {Version}", manifest.Id, manifest.Version);
            return new InstallResult(InstallOutcome.Installed, path, manifest.Id, manifest.Version, [], null);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>
    /// Deals with an already installed copy of the same id and version. Returns whether to go on, the archived path of the
    /// copy that was replaced (so it can be restored), or an early result.
    /// </summary>
    private async Task<(bool Proceed, string? ArchivedOld, InstallResult? Early)> PrepareDestinationAsync(
        string path, PluginManifest manifest, string staging, string destination, string hash, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(destination))
        {
            return (true, null, null);
        }

        var live = _host.GetVersions().Any(v => v.Id == manifest.Id && v.Version == manifest.Version && PluginLifecycle.IsLive(v.State));
        if (!live)
        {
            // A leftover directory nothing is using.
            return (true, _archive.Archive(destination, manifest.Id, manifest.Version, prune: false), null);
        }

        var storedHash = await ReadStoredHashAsync(destination).ConfigureAwait(false);
        if (string.Equals(storedHash, hash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            _logger.LogInformation("{PluginId} {Version} with this exact content is already installed; nothing to do", manifest.Id, manifest.Version);
            return (false, null, new InstallResult(InstallOutcome.Unchanged, path, manifest.Id, manifest.Version, [], null));
        }

        // Last known good: prove the replacement activates before the installed copy is touched.
        try
        {
            await _host.ValidateActivationAsync(staging, cancellationToken).ConfigureAwait(false);
        }
        catch (PluginLoadException ex)
        {
            var reasons = new List<string>(ex.Reasons) { $"The installed {manifest.Id} {manifest.Version} stays in service." };
            return (false, null, await RejectAsync(path, reasons, ex.Details, manifest.Id, manifest.Version).ConfigureAwait(false));
        }

        var drain = await _host.RetireAsync(manifest.Id, manifest.Version, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (drain.Outcome == DrainOutcome.Stuck)
        {
            return (false, null, await RejectAsync(
                path,
                [$"The installed {manifest.Id} {manifest.Version} still has executions that will not stop, so it cannot be replaced now. It stays in service; drop the package again once it has drained."],
                null, manifest.Id, manifest.Version).ConfigureAwait(false));
        }

        return (true, _archive.Archive(destination, manifest.Id, manifest.Version, prune: false), null);
    }

    private async Task<bool> RestoreAsync(string archivedPath, string destination, PluginManifest manifest)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(archivedPath, destination);
            await _host.LoadAsync(destination).ConfigureAwait(false);
            _logger.LogWarning("Restored {PluginId} {Version} after its replacement failed to activate", manifest.Id, manifest.Version);
            return true;
        }
        catch (Exception ex) when (ex is PluginLoadException or IOException)
        {
            _logger.LogError("Could not restore {PluginId} {Version}: {Message}", manifest.Id, manifest.Version, ex.Message);
            return false;
        }
    }

    private void RetireOtherVersions(PluginManifest installed)
    {
        foreach (var other in _host.GetPlugins().Where(p => p.Id == installed.Id && p.Version != installed.Version).ToList())
        {
            lock (_background)
            {
                _background.RemoveAll(t => t.IsCompleted);
                _background.Add(RetireAndArchiveAsync(other.Id, other.Version, installed.Version));
            }
        }
    }

    private async Task RetireAndArchiveAsync(string pluginId, PluginVersion version, PluginVersion replacedBy)
    {
        try
        {
            _logger.LogInformation("Retiring {PluginId} {Version} because {NewVersion} was installed; its running work may finish first", pluginId, version, replacedBy);
            var result = await _host.RetireAsync(pluginId, version, cancellationToken: _shutdown.Token).ConfigureAwait(false);
            if (result.Outcome == DrainOutcome.Stuck)
            {
                _logger.LogWarning("{PluginId} {Version} is stuck draining; waiting for its executions to end. See GET /api/plugins/{PluginId}/{Version}/report", pluginId, version, pluginId, version);
                result = await _host.RetireAsync(pluginId, version, new DrainOptions { StuckPolicy = StuckPolicy.KeepWaiting }, _shutdown.Token).ConfigureAwait(false);
            }

            if (result.Unload is { Collected: false })
            {
                _logger.LogWarning("{Diagnostic}", result.Unload.Diagnostic);
            }

            var directory = Path.Combine(_options.InstalledDirectory, pluginId, version.ToString());
            if (Directory.Exists(directory))
            {
                _archive.Archive(directory, pluginId, version);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down; the drain finishes with the process.
        }
        catch (Exception ex)
        {
            _logger.LogError("Retiring {PluginId} {Version} failed: {Type}: {Message}", pluginId, version, ex.GetType().Name, ex.Message);
        }
    }

    private async Task<InstallResult> RejectAsync(string path, IReadOnlyList<string> reasons, string? details, string? pluginId, PluginVersion? version)
    {
        var moved = await _quarantine.QuarantineAsync(path, reasons, details).ConfigureAwait(false);
        _host.RecordQuarantine(moved, reasons, pluginId, version);
        return new InstallResult(InstallOutcome.Quarantined, path, pluginId, version, reasons, moved);
    }

    private async Task<InstallResult> QuarantineInstalledAsync(string directory, string pluginId, PluginVersion? version, IReadOnlyList<string> reasons, string? details)
    {
        var moved = await _quarantine.QuarantineAsync(directory, reasons, details).ConfigureAwait(false);
        _host.RecordQuarantine(moved, reasons, pluginId, version);
        var parent = Path.GetDirectoryName(directory)!;
        if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
        }

        return new InstallResult(InstallOutcome.Quarantined, directory, pluginId, version, reasons, moved);
    }

    private async Task<string?> AllowlistProblemForInstalledAsync(string directory)
    {
        if (_options.AllowedPackageHashes.Count == 0)
        {
            return null;
        }

        var stored = await ReadStoredHashAsync(directory).ConfigureAwait(false);
        return stored is not null && PackageHasher.IsAllowed(stored, _options.AllowedPackageHashes)
            ? null
            : "The installed package is not on the SHA-256 allowlist (or its recorded hash is missing), so it is not loaded.";
    }

    private static async Task<string?> ReadStoredHashAsync(string directory)
    {
        var file = Path.Combine(directory, HashFileName);
        return File.Exists(file) ? (await File.ReadAllTextAsync(file).ConfigureAwait(false)).Trim() : null;
    }
}
