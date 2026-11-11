using Emberdrop.Scheduling;

namespace Emberdrop.Providers.ContractTests.Scheduler;

/// <summary>A running scheduler under test together with the invoker it reports to.</summary>
public interface ISchedulerHarness : IAsyncDisposable
{
    IWorkScheduler Scheduler { get; }

    RecordingInvoker Invoker { get; }
}
