using FluentAssertions;
using Emberdrop.Hangfire;
using Emberdrop.Hosting;
using Emberdrop.WorkUnits;
using Emberdrop.Scheduling;
using Emberdrop.Storage;
using global::Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Emberdrop.Hangfire.Tests;

public class UseHangfireShould
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Mock.Of<IWorkInvoker>());
        services.AddScoped(_ => Mock.Of<IWorkUnitDiscoveryService>());
        services.AddScoped(_ => Mock.Of<IWorkStore>());
        services.AddEmberdrop(b => b
            .UseHangfire(c => c.UseStorage(new Mock<JobStorage>().Object))
            .UseStore<NullStore>());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void SelectHangfireAsTheSchedulerAndRegisterTheRunner()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IWorkScheduler>().Should().BeOfType<HangfireWorkScheduler>();
        scope.ServiceProvider.GetRequiredService<EmberdropHangfireRunner>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<WorkUnitManager>().Should().NotBeNull();
    }

    [Fact]
    public void RejectAnotherSchedulerOnTopOfHangfire()
    {
        var act = () => new ServiceCollection().AddEmberdrop(b => b
            .UseHangfire(c => c.UseStorage(new Mock<JobStorage>().Object))
            .UseScheduler<HangfireWorkScheduler>());

        act.Should().Throw<InvalidOperationException>().WithMessage("*already configured*");
    }

    [Fact]
    public void ReportTheDashboardPathOnceTheDashboardIsMounted()
    {
        using var provider = BuildProvider();
        var app = new ApplicationBuilder(provider);

        app.UseEmberdropHangfireDashboard("/work-units-ui");

        provider.GetRequiredService<HangfireDashboardInfo>().Path.Should().Be("/work-units-ui");
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IWorkScheduler>().Capabilities.DashboardPath.Should().Be("/work-units-ui");
    }

    [Fact]
    public void ExplainWhenTheDashboardIsMountedWithoutHangfire()
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());

        var act = () => app.UseEmberdropHangfireDashboard();

        act.Should().Throw<InvalidOperationException>().WithMessage("*UseHangfire*");
    }

    private sealed class NullStore : IWorkStore
    {
        public Task<IReadOnlyList<WorkUnitDefinition>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkUnitDefinition>>([]);
        public Task<WorkUnitDefinition?> GetAsync(int workUnitId, CancellationToken cancellationToken = default) => Task.FromResult<WorkUnitDefinition?>(null);
        public Task<int> AddAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task UpdateAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> DeleteAsync(int workUnitId, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyList<StoredTrigger>> ListTriggersAsync(int? workUnitId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoredTrigger>>([]);

        public Task<int> AddTriggerAsync(StoredTrigger trigger, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<int> DeleteTriggerAsync(int triggerId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
