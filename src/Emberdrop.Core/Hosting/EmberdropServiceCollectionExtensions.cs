using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Emberdrop.Hosting;

/// <summary>Registers Emberdrop with a host's service collection.</summary>
public static class EmberdropServiceCollectionExtensions
{
    /// <summary>
    /// Registers Emberdrop's work unit management services and runs <paramref name="configure"/> to select
    /// the scheduler and store providers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Selects providers, for example <c>b =&gt; b.UseHangfire(...).UseSqlServer(...)</c>.</param>
    /// <exception cref="InvalidOperationException">No scheduler or no store was selected.</exception>
    public static IServiceCollection AddEmberdrop(this IServiceCollection services, Action<IEmberdropBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new EmberdropBuilder(services);
        configure(builder);
        builder.Validate();

        WorkUnitHostingExtensions.EnsureOptions(services);
        services.AddPluginHosting();
        services.AddWorkUnitHosting();
        services.TryAddScoped<WorkUnits.IWorkSchedulerService, WorkUnits.WorkSchedulerService>();
        services.TryAddScoped<WorkUnits.IWorkUnitRegistrationService, WorkUnits.WorkUnitRegistrationService>();
        services.TryAddScoped<WorkUnits.WorkUnitManager>();

        return services;
    }
}
