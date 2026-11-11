using Emberdrop.Scheduling;
using Emberdrop.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.Hosting;

/// <summary>
/// Fluent configuration surface passed to <see cref="EmberdropServiceCollectionExtensions.AddEmberdrop"/>.
/// Provider packages add <c>Use*</c> extension methods to this interface.
/// </summary>
public interface IEmberdropBuilder
{
    /// <summary>The service collection being configured, for provider packages to register their own services.</summary>
    IServiceCollection Services { get; }

    /// <summary>Selects the scheduler engine. Exactly one scheduler must be selected.</summary>
    /// <typeparam name="TScheduler">Scheduler implementation.</typeparam>
    /// <param name="lifetime">Service lifetime. Engines that own timers or workers register as singletons.</param>
    IEmberdropBuilder UseScheduler<TScheduler>(ServiceLifetime lifetime = ServiceLifetime.Scoped) where TScheduler : class, IWorkScheduler;

    /// <summary>Selects the work unit store. Exactly one store must be selected.</summary>
    /// <typeparam name="TStore">Store implementation.</typeparam>
    /// <param name="lifetime">Service lifetime. Stores that hold state in memory register as singletons.</param>
    IEmberdropBuilder UseStore<TStore>(ServiceLifetime lifetime = ServiceLifetime.Scoped) where TStore : class, IWorkStore;
}
