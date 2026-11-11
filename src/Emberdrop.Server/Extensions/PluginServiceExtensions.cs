using Emberdrop.Server.Configuration;
using Emberdrop.Server.Plugins;

namespace Emberdrop.Server.Extensions;

public static class PluginServiceExtensions
{
    public static IServiceCollection AddPluginServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PluginConfiguration>(configuration.GetSection(PluginConfiguration.SectionName));

        // A singleton: it owns the file watchers, which must outlive any request or startup scope.
        services.AddSingleton<PluginService>();

        return services;
    }
}
