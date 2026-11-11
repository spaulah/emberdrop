using Emberdrop.Hosting;
using Emberdrop.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Emberdrop.WorkUnits;

/// <summary>Configures how work units are hosted: the plugin hosting core, told what the exports of a package mean.</summary>
public static class WorkUnitHostingExtensions
{
    /// <summary>Adjusts how plugins are hosted, for example the shared-assembly list. Call inside <c>AddEmberdrop</c>.</summary>
    public static IEmberdropBuilder ConfigurePlugins(this IEmberdropBuilder builder, Action<PluginHostOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        configure(EnsureOptions(builder.Services).Host);
        return builder;
    }

    /// <summary>Adjusts how work units are hosted: the work unit kinds the host runs, its trigger kinds, what happens when a plugin is missing.</summary>
    public static IEmberdropBuilder ConfigureWorkUnits(this IEmberdropBuilder builder, Action<WorkUnitOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        configure(EnsureOptions(builder.Services).WorkUnits);
        return builder;
    }

    /// <summary>
    /// Teaches the host a new kind of trigger: it can then be declared in manifests and read from API requests.
    /// See <c>docs/concepts/triggers.md</c>. Call inside <c>AddEmberdrop</c>.
    /// </summary>
    public static IEmberdropBuilder AddTriggerKind(this IEmberdropBuilder builder, ITriggerKind kind)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(kind);

        EnsureOptions(builder.Services).WorkUnits.TriggerKinds.Add(kind);
        return builder;
    }

    /// <summary>Points the plugin hosting options at the work unit meaning of exports: the manifest's <c>workUnits</c> array and the work unit handler.</summary>
    public static PluginHostOptions UseWorkUnits(this PluginHostOptions host, WorkUnitOptions workUnits)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(workUnits);

        host.ManifestFormat = PluginWorkUnitManifest.Format;
        host.ExportHandler = new WorkUnitExportHandler(workUnits, host.HostServices);
        return host;
    }

    internal static IServiceCollection AddWorkUnitHosting(this IServiceCollection services)
    {
        var (_, workUnits) = EnsureOptions(services);

        services.TryAddSingleton(workUnits.TriggerKinds);
        services.TryAddSingleton<WaitingInvocations>();
        services.TryAddSingleton<PluginWorkUnitRunner>();
        if (!services.Any(d => d.ServiceType == typeof(PluginWorkUnitReconciler)))
        {
            // One instance, registered as itself and as the listener the host notifies.
            services.AddSingleton<PluginWorkUnitReconciler>();
            services.AddSingleton<IPluginLifecycleListener>(provider => provider.GetRequiredService<PluginWorkUnitReconciler>());
        }

        services.TryAddScoped<IWorkUnitBindingResolver, WorkUnitBindingResolver>();
        return services;
    }

    // One options instance of each per host: later calls adjust the one an earlier call created instead of replacing it.
    internal static (PluginHostOptions Host, WorkUnitOptions WorkUnits) EnsureOptions(IServiceCollection services)
    {
        var workUnits = InstanceOf<WorkUnitOptions>(services);
        if (workUnits is null)
        {
            workUnits = new WorkUnitOptions();
            services.AddSingleton(workUnits);
        }

        var host = InstanceOf<PluginHostOptions>(services);
        if (host is null)
        {
            host = new PluginHostOptions().UseWorkUnits(workUnits);
            services.Replace(ServiceDescriptor.Singleton(host));
        }

        return (host, workUnits);
    }

    private static T? InstanceOf<T>(IServiceCollection services)
        where T : class =>
        services.Where(d => d.ServiceType == typeof(T)).Select(d => d.ImplementationInstance).OfType<T>().LastOrDefault();
}
