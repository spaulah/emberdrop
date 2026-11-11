using Microsoft.Extensions.Logging;

namespace Emberdrop.Plugins;

/// <summary>
/// Watches folders for <c>.emberdrop</c> packages and hands each to the installer, one at a time. The several change events
/// one copy produces are merged into one piece of work (see <see cref="PendingPaths"/>), and the installer itself waits for
/// the file to be complete.
/// </summary>
public sealed class DropFolderWatcher : IAsyncDisposable
{
    private const string Filter = "*.emberdrop";

    private readonly PluginInstaller _installer;
    private readonly ILogger<DropFolderWatcher> _logger;
    private readonly PendingPaths _pending = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly CancellationTokenSource _stop = new();
    private Task? _consumer;

    /// <summary>Creates the watcher.</summary>
    public DropFolderWatcher(PluginInstaller installer, ILogger<DropFolderWatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(logger);

        _installer = installer;
        _logger = logger;
    }

    /// <summary>Starts watching a folder (creating it if needed) and queues the packages already in it.</summary>
    public void Watch(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var full = Path.GetFullPath(directory);
        Directory.CreateDirectory(full);

        var watcher = new FileSystemWatcher(full, Filter)
        {
            NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        watcher.Created += (_, e) => Queue(e.FullPath);
        watcher.Changed += (_, e) => Queue(e.FullPath);
        watcher.Renamed += (_, e) => Queue(e.FullPath);
        watcher.Error += (_, e) => _logger.LogError("FileSystemWatcher error: {Type}: {Message}", e.GetException().GetType().Name, e.GetException().Message);
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
        _logger.LogInformation("Watching {Directory} for plugin packages", full);

        foreach (var existing in Directory.GetFiles(full, Filter))
        {
            Queue(existing);
        }
    }

    /// <summary>Starts processing queued packages.</summary>
    public void Start() => _consumer ??= Task.Run(ConsumeAsync);

    /// <summary>Stops watching and waits for the package being installed, if any, to finish or be cancelled.</summary>
    public async Task StopAsync()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _pending.Complete();
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_consumer is not null)
        {
            try
            {
                await _consumer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on stop.
            }
        }

        _logger.LogInformation("Stopped watching for plugin packages");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stop.Dispose();
    }

    private void Queue(string path)
    {
        if (_pending.Enqueue(Path.GetFullPath(path)))
        {
            _logger.LogDebug("Queued plugin package {Package}", path);
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var path in _pending.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                try
                {
                    await _installer.InstallAsync(path, _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // One bad package must not stop the watcher; log text only, as the exception may come from plugin code.
                    _logger.LogError("Installing {Package} failed unexpectedly: {Type}: {Message}", path, ex.GetType().Name, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }
}
