using Emberdrop.Scheduling;
using Emberdrop.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.Hosting;

internal sealed class EmberdropBuilder : IEmberdropBuilder
{
    private string? _scheduler;
    private string? _store;

    public EmberdropBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }

    public IEmberdropBuilder UseScheduler<TScheduler>(ServiceLifetime lifetime = ServiceLifetime.Scoped) where TScheduler : class, IWorkScheduler
    {
        if (_scheduler != null)
        {
            throw new InvalidOperationException(
                $"A scheduler is already configured ({_scheduler}); Emberdrop supports exactly one. Cannot also use {typeof(TScheduler).Name}.");
        }

        _scheduler = typeof(TScheduler).Name;
        Services.Add(new ServiceDescriptor(typeof(IWorkScheduler), typeof(TScheduler), lifetime));
        return this;
    }

    public IEmberdropBuilder UseStore<TStore>(ServiceLifetime lifetime = ServiceLifetime.Scoped) where TStore : class, IWorkStore
    {
        if (_store != null)
        {
            throw new InvalidOperationException(
                $"A work unit store is already configured ({_store}); Emberdrop supports exactly one. Cannot also use {typeof(TStore).Name}.");
        }

        _store = typeof(TStore).Name;
        Services.Add(new ServiceDescriptor(typeof(IWorkStore), typeof(TStore), lifetime));
        return this;
    }

    public void Validate()
    {
        if (_scheduler == null)
        {
            throw new InvalidOperationException(
                "Emberdrop has no scheduler configured. Select one in AddEmberdrop, for example builder.UseHangfire(...).");
        }

        if (_store == null)
        {
            throw new InvalidOperationException(
                "Emberdrop has no work unit store configured. Select one in AddEmberdrop, for example builder.UseSqlServer(...).");
        }
    }
}
