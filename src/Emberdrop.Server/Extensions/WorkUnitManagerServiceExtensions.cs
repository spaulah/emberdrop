using Emberdrop.WorkUnits;
using Emberdrop.Server.Infrastructure;

namespace Emberdrop.Server.WorkUnitManagement;

public static class WorkUnitManagerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the parts of work unit hosting that still live in the server project (type discovery and
    /// work unit invocation). Scheduler and store come from AddEmberdrop.
    /// </summary>
    public static IServiceCollection AddWorkUnitManager(this IServiceCollection services)
    {
        services.AddScoped<IWorkUnitDiscoveryService, WorkUnitDiscoveryService>();
        services.AddScoped<IWorkInvoker, WorkInvoker>();

        return services;
    }
}
