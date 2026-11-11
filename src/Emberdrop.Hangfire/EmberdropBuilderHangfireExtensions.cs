using Emberdrop.Hosting;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.Hangfire;

/// <summary>Selects Hangfire as Emberdrop's scheduler engine.</summary>
public static class EmberdropBuilderHangfireExtensions
{
    /// <summary>
    /// Uses Hangfire to schedule and run work units. Emberdrop integrates with Hangfire; it does not hide it:
    /// the caller chooses Hangfire's own storage.
    /// </summary>
    /// <param name="builder">The Emberdrop builder.</param>
    /// <param name="configureHangfire">Configures Hangfire, including its storage, for example <c>c =&gt; c.UseSqlServerStorage(cs)</c>.</param>
    /// <param name="configureServer">Optionally configures the in-process Hangfire server.</param>
    public static IEmberdropBuilder UseHangfire(
        this IEmberdropBuilder builder,
        Action<IGlobalConfiguration> configureHangfire,
        Action<BackgroundJobServerOptions>? configureServer = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureHangfire);

        builder.Services.AddHangfire(configureHangfire);
        if (configureServer is null)
        {
            builder.Services.AddHangfireServer();
        }
        else
        {
            builder.Services.AddHangfireServer(configureServer);
        }

        builder.Services.AddSingleton<HangfireDashboardInfo>();
        builder.Services.AddScoped<EmberdropHangfireRunner>();

        return builder.UseScheduler<HangfireWorkScheduler>();
    }
}
