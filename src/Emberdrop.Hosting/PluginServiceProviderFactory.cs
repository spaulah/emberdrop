using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Emberdrop.Plugins;

/// <summary>
/// Builds the service provider of one plugin version. A plugin sees only what the host explicitly offers it:
/// a logger factory and <c>ILogger&lt;T&gt;</c>, a snapshot of the configuration keys its exports declared as required, and the
/// services in <see cref="PluginHostOptions.HostServices"/>. Anything else it needs it registers itself.
/// </summary>
public sealed class PluginServiceProviderFactory
{
    private readonly IServiceProvider _hostServices;
    private readonly PluginHostOptions _options;

    /// <summary>Creates the factory.</summary>
    /// <param name="hostServices">The host's root service provider, from which allow-listed services are taken.</param>
    /// <param name="options">The plugin hosting options.</param>
    public PluginServiceProviderFactory(IServiceProvider hostServices, PluginHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(hostServices);
        ArgumentNullException.ThrowIfNull(options);

        _hostServices = hostServices;
        _options = options;
    }

    /// <summary>Builds the provider. <paramref name="configurePluginServices"/> is where the plugin's own startup code registers services.</summary>
    internal ServiceProvider Build(PluginManifest manifest, Action<IServiceCollection>? configurePluginServices)
    {
        var services = new ServiceCollection();

        // Host-owned objects are registered as instances. The container never disposes instances it did not create,
        // so unloading a plugin cannot dispose the host's logger factory.
        var loggerFactory = _hostServices.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(PluginLogger<>));
        services.AddSingleton<IConfiguration>(SnapshotOfRequiredConfiguration(manifest));

        foreach (var type in _options.HostServices)
        {
            var instance = _hostServices.GetService(type)
                ?? throw new PluginLoadException($"The host service '{type.FullName}' is on the plugin allow-list but is not registered in the host.");
            services.AddSingleton(type, instance);
        }

        configurePluginServices?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>Returns the keys among <paramref name="keys"/> for which the host has no value (neither a value nor any child).</summary>
    public IReadOnlyList<string> MissingConfiguration(IEnumerable<string> keys)
    {
        var configuration = _hostServices.GetService<IConfiguration>();
        return keys
            .Where(key => configuration is null || !configuration.GetSection(key).Exists())
            .ToList();
    }

    /// <summary>
    /// The host's values for exactly the configuration keys the plugin's exports declared as required, copied at build time.
    /// The plugin cannot read any other host setting, secrets included.
    /// </summary>
    private IConfigurationRoot SnapshotOfRequiredConfiguration(PluginManifest manifest)
    {
        var hostConfiguration = _hostServices.GetService<IConfiguration>();
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (hostConfiguration is not null)
        {
            foreach (var key in manifest.Exports.SelectMany(_options.ExportHandler.ConfigurationKeys).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var section = hostConfiguration.GetSection(key);
                if (section.Value is not null)
                {
                    values[key] = section.Value;
                }

                foreach (var child in section.AsEnumerable(makePathsRelative: false).Where(kv => kv.Value is not null))
                {
                    values[child.Key] = child.Value;
                }
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
