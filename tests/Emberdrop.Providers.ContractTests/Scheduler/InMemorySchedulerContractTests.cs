using Emberdrop.Hosting;
using Emberdrop.InMemory;
using Emberdrop.WorkUnits;
using Emberdrop.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.Providers.ContractTests.Scheduler;

public sealed class InMemorySchedulerContractTests : SchedulerContract
{
    protected override SchedulerFeatures ExpectedFeatures =>
        SchedulerFeatures.Recurring
        | SchedulerFeatures.Delayed
        | SchedulerFeatures.CancelQueued
        | SchedulerFeatures.CancelRunning
        | SchedulerFeatures.History;

    protected override Task<ISchedulerHarness> CreateHarnessAsync() => Task.FromResult<ISchedulerHarness>(new Harness());

    private sealed class Harness : ISchedulerHarness
    {
        private readonly ServiceProvider _provider;

        public Harness()
        {
            Invoker = new RecordingInvoker();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IWorkInvoker>(Invoker);
            services.AddEmberdrop(b => b.UseInMemoryScheduler().UseInMemoryStore());
            _provider = services.BuildServiceProvider();
            Scheduler = _provider.GetRequiredService<IWorkScheduler>();
        }

        public IWorkScheduler Scheduler { get; }

        public RecordingInvoker Invoker { get; }

        public ValueTask DisposeAsync() => _provider.DisposeAsync();
    }
}
