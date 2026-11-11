using Emberdrop.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.InMemory;

/// <summary>Selects the in-memory providers.</summary>
public static class EmberdropBuilderInMemoryExtensions
{
    /// <summary>Uses the timer-based in-memory scheduler. Schedules are lost when the process stops.</summary>
    /// <param name="builder">The Emberdrop builder.</param>
    /// <param name="configure">Optionally adjusts <see cref="InMemorySchedulerOptions"/>.</param>
    public static IEmberdropBuilder UseInMemoryScheduler(this IEmberdropBuilder builder, Action<InMemorySchedulerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new InMemorySchedulerOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        return builder.UseScheduler<InMemoryWorkScheduler>(ServiceLifetime.Singleton);
    }

    /// <summary>Uses the in-memory work unit store. Work unit definitions are lost when the process stops.</summary>
    /// <param name="builder">The Emberdrop builder.</param>
    public static IEmberdropBuilder UseInMemoryStore(this IEmberdropBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.UseStore<InMemoryWorkStore>(ServiceLifetime.Singleton);
    }
}
