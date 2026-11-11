using Emberdrop.Hangfire;
using Emberdrop.Hosting;
using Emberdrop.InMemory;
using Emberdrop.WorkUnits;
using Emberdrop.Scheduling;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Emberdrop.Providers.ContractTests.Scheduler;

/// <summary>Runs the scheduler contract against Hangfire using its in-memory storage, so no database is needed.</summary>
// Hangfire keeps some process-wide static state (JobStorage.Current), so these tests do not run in parallel with each other.
[Collection("Hangfire")]
public sealed class HangfireSchedulerContractTests : SchedulerContract
{
    protected override SchedulerFeatures ExpectedFeatures =>
        SchedulerFeatures.Recurring
        | SchedulerFeatures.Delayed
        | SchedulerFeatures.CancelQueued
        | SchedulerFeatures.History
        | SchedulerFeatures.Retries;

    protected override async Task<ISchedulerHarness> CreateHarnessAsync()
    {
        var harness = new Harness();
        await harness.StartAsync();
        return harness;
    }

    private sealed class Harness : ISchedulerHarness
    {
        private readonly IHost _host;
        private readonly IServiceScope _scope;

        public Harness()
        {
            Invoker = new RecordingInvoker();
            _host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
                    services.AddSingleton<IWorkInvoker>(Invoker);
                    services.AddEmberdrop(b => b
                        .UseHangfire(
                            c => c.UseInMemoryStorage(),
                            server =>
                            {
                                server.WorkerCount = 4;
                                server.SchedulePollingInterval = TimeSpan.FromMilliseconds(200);
                            })
                        .UseInMemoryStore());
                })
                .Build();
            _scope = _host.Services.CreateScope();
            Scheduler = _scope.ServiceProvider.GetRequiredService<IWorkScheduler>();
        }

        public IWorkScheduler Scheduler { get; }

        public RecordingInvoker Invoker { get; }

        public Task StartAsync() => _host.StartAsync();

        public async ValueTask DisposeAsync()
        {
            Invoker.ReleaseBlockedWorkUnit();
            await _host.StopAsync();
            _scope.Dispose();
            _host.Dispose();
        }
    }
}
