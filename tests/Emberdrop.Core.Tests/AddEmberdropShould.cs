using FluentAssertions;
using Emberdrop.Hosting;
using Emberdrop.WorkUnits;
using Emberdrop.Scheduling;
using Emberdrop.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Emberdrop.Core.Tests;

public class AddEmberdropShould
{
    [Fact]
    public void FailWithAnActionableMessageWhenNoSchedulerIsSelected()
    {
        var act = () => new ServiceCollection().AddEmberdrop(b => b.UseStore<FakeStore>());

        act.Should().Throw<InvalidOperationException>().WithMessage("*no scheduler*UseHangfire*");
    }

    [Fact]
    public void FailWithAnActionableMessageWhenNoStoreIsSelected()
    {
        var act = () => new ServiceCollection().AddEmberdrop(b => b.UseScheduler<FakeScheduler>());

        act.Should().Throw<InvalidOperationException>().WithMessage("*no work unit store*");
    }

    [Fact]
    public void RejectASecondScheduler()
    {
        var act = () => new ServiceCollection().AddEmberdrop(b => b.UseScheduler<FakeScheduler>().UseScheduler<FakeScheduler>());

        act.Should().Throw<InvalidOperationException>().WithMessage("*already configured*");
    }

    [Fact]
    public void RejectASecondStore()
    {
        var act = () => new ServiceCollection().AddEmberdrop(b => b.UseStore<FakeStore>().UseStore<FakeStore>());

        act.Should().Throw<InvalidOperationException>().WithMessage("*already configured*");
    }

    [Fact]
    public void RegisterTheSelectedProvidersAndWorkUnitManagement()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IWorkInvoker, FakeInvoker>();
        services.AddScoped<IWorkUnitDiscoveryService, FakeDiscovery>();
        services.AddEmberdrop(b => b.UseScheduler<FakeScheduler>().UseStore<FakeStore>());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IWorkScheduler>().Should().BeOfType<FakeScheduler>();
        scope.ServiceProvider.GetRequiredService<IWorkStore>().Should().BeOfType<FakeStore>();
        scope.ServiceProvider.GetRequiredService<WorkUnitManager>().Should().NotBeNull();
    }

    private sealed class FakeScheduler : IWorkScheduler
    {
        public SchedulerCapabilities Capabilities { get; } = new("fake", SchedulerFeatures.None);
        public Task UpsertRecurringAsync(RecurringWorkSpec spec, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveRecurringAsync(string scheduleKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SchedulerResult<ExecutionId>> TriggerAsync(string scheduleKey, CancellationToken cancellationToken = default) => Task.FromResult(SchedulerResult.NotFound<ExecutionId>());
        public Task<ExecutionId> EnqueueAsync(WorkInvocation invocation, CancellationToken cancellationToken = default) => Task.FromResult(new ExecutionId("1"));
        public Task<SchedulerResult<ExecutionId>> ScheduleAsync(WorkInvocation invocation, DateTimeOffset runAt, CancellationToken cancellationToken = default) => Task.FromResult(SchedulerResult.Unsupported<ExecutionId>(SchedulerFeatures.Delayed));
        public Task<SchedulerResult<bool>> CancelAsync(ExecutionId runId, CancellationToken cancellationToken = default) => Task.FromResult(SchedulerResult.NotFound<bool>());
        public Task<ExecutionInfo?> GetRunAsync(ExecutionId runId, CancellationToken cancellationToken = default) => Task.FromResult<ExecutionInfo?>(null);
        public Task<SchedulerResult<IReadOnlyList<ExecutionInfo>>> GetHistoryAsync(ExecutionHistoryQuery query, CancellationToken cancellationToken = default) => Task.FromResult(SchedulerResult.Unsupported<IReadOnlyList<ExecutionInfo>>(SchedulerFeatures.History));
    }

    private sealed class FakeStore : IWorkStore
    {
        public Task<IReadOnlyList<WorkUnitDefinition>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkUnitDefinition>>([]);
        public Task<WorkUnitDefinition?> GetAsync(int workUnitId, CancellationToken cancellationToken = default) => Task.FromResult<WorkUnitDefinition?>(null);
        public Task<int> AddAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task UpdateAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> DeleteAsync(int workUnitId, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyList<StoredTrigger>> ListTriggersAsync(int? workUnitId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoredTrigger>>([]);

        public Task<int> AddTriggerAsync(StoredTrigger trigger, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<int> DeleteTriggerAsync(int triggerId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeInvoker : IWorkInvoker
    {
        public Task<WorkResult?> InvokeAsync(WorkInvocation invocation, CancellationToken cancellationToken = default) => Task.FromResult<WorkResult?>(WorkResult.Succeeded());
    }

    private sealed class FakeDiscovery : IWorkUnitDiscoveryService
    {
        public Task<IReadOnlyList<DiscoveredWorkUnit>> DiscoverWorkUnitsAsync() => Task.FromResult<IReadOnlyList<DiscoveredWorkUnit>>([]);
    }
}
