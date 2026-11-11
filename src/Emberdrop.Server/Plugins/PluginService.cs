using Emberdrop.Plugins;
using Emberdrop.Server.Configuration;
using Microsoft.Extensions.Options;

namespace Emberdrop.Server.Plugins;

/// <summary>
/// Starts plugin installation for this server: loads what is installed, then watches the configured folders for new packages.
/// The actual work (stable-file detection, safe extraction, validation, atomic promotion, rollback, quarantine, archiving)
/// is in <see cref="PluginInstaller"/>.
/// </summary>
public sealed class PluginService
{
    private readonly PluginConfiguration _config;
    private readonly PluginInstaller _installer;
    private readonly DropFolderWatcher _watcher;
    private readonly ILogger<PluginService> _logger;

    public PluginService(IOptions<PluginConfiguration> config, PluginInstaller installer, DropFolderWatcher watcher, ILogger<PluginService> logger)
    {
        _config = config.Value;
        _installer = installer;
        _watcher = watcher;
        _logger = logger;
    }

    public async Task StartMonitoringAsync()
    {
        await _installer.PrepareAsync();
        await _installer.LoadInstalledAsync();

        if (!_config.EnableFileWatcher)
        {
            _logger.LogInformation("Plugin file watcher is disabled");
            return;
        }

        foreach (var path in _config.MonitoredPaths)
        {
            _watcher.Watch(path);
        }

        _watcher.Start();
    }

    public Task StopMonitoringAsync() => _watcher.StopAsync();

    public async Task ProcessPluginFileAsync(string filePath) => await _installer.InstallAsync(filePath);
}
