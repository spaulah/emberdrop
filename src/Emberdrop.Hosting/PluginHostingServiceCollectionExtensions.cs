using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Emberdrop.Plugins;

/// <summary>Registers the plugin hosting core.</summary>
public static class PluginHostingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the pieces of the hosting core: options, loader, host, catalog, quarantine, archive, installer and folder watcher.
    /// Register a <see cref="PluginHostOptions"/> instance first to configure it; otherwise the defaults are used. Lifecycle listeners
    /// (<see cref="IPluginLifecycleListener"/>) registered in the container are told when a plugin's active versions change.
    /// </summary>
    public static IServiceCollection AddPluginHosting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<PluginHostOptions>();
        services.TryAddSingleton<PluginLoader>();
        services.TryAddSingleton<PluginServiceProviderFactory>();
        services.TryAddSingleton<PluginHost>();
        services.TryAddSingleton<IPluginCatalog>(provider => provider.GetRequiredService<PluginHost>());
        services.TryAddSingleton<PluginQuarantine>();
        services.TryAddSingleton<PluginArchive>();
        services.TryAddSingleton<PluginInstaller>();
        services.TryAddSingleton<DropFolderWatcher>();
        return services;
    }
}
