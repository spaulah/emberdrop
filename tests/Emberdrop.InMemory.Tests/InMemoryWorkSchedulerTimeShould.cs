using System.Collections.Concurrent;
using Cronos;
using Emberdrop.InMemory;
using Emberdrop.WorkUnits;
using Emberdrop.Scheduling;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Emberdrop.InMemory.Tests;

/// <summary>Time-driven behavior, checked against a fake clock so nothing sleeps.</summary>
public class InMemoryWorkSchedulerTimeShould : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly Invoker _invoker = new();
    private readonly ServiceProvider _provider;
    private readonly InMemorySchedulerOptions _options;
    private InMemoryWorkScheduler _scheduler = null!;

    public InMemoryWorkSchedulerTimeShould()
    {
        _options = new InMemorySchedulerOptions { TimeProvider = _time, ShutdownTimeout = TimeSpan.FromSeconds(2) };
        _provider = new ServiceCollection().AddSingleton<IWorkInvoker>(_invoker).BuildServiceProvider();
    }

    public Task InitializeAsync()
    {
        _scheduler = new InMemoryWorkScheduler(_provider.GetRequiredService<IServiceScopeFactory>(), _options, NullLogger<InMemoryWorkScheduler>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _scheduler.DisposeAsync();
        await _provider.DisposeAsync();
    }

    private static RecurringWorkSpec Every5Minutes(bool enabled = true, int workUnitId = 1, string key = "k") =>
        new(key, "*/5 * * * *", enabled, new WorkInvocation("ok", workUnitId));

    private async Task WaitForCalls(int count) =>
        await Eventually(() => _invoker.Calls.Count >= count, $"{count} invocation(s)");

    private static Task Eventually(Func<bool> condition, string what) =>
        Eventually(() => Task.FromResult(condition()), what);

    private static async Task Eventually(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new Xunit.Sdk.XunitException($"Timed out waiting for: {what}");
            }

            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task FireAtEachCronBoundaryAndNotBefore()
    {
        await _scheduler.UpsertRecurringAsync(Every5Minutes());

        _time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        await Task.Delay(50);
        _invoker.Calls.Should().BeEmpty("12:05:00 has not been reached");

        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitForCalls(1);

        _time.Advance(TimeSpan.FromMinutes(5));
        await WaitForCalls(2);
        _invoker.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task NeverFireADisabledEntryOnItsOwn()
    {
        await _scheduler.UpsertRecurringAsync(Every5Minutes(enabled: false));

        _time.Advance(TimeSpan.FromDays(1));
        await Task.Delay(50);

        _invoker.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptAnUnparseableScheduleOnADisabledEntry()
    {
        var act = () => _scheduler.UpsertRecurringAsync(new RecurringWorkSpec("k", "", false, new WorkInvocation("ok", 1)));

        await act.Should().NotThrowAsync("disabled work units are stored with whatever schedule the user typed");
    }

    [Fact]
    public async Task RejectAnUnparseableScheduleOnAnEnabledEntry()
    {
        var act = () => _scheduler.UpsertRecurringAsync(new RecurringWorkSpec("k", "not a cron", true, new WorkInvocation("ok", 1)));

        await act.Should().ThrowAsync<CronFormatException>();
    }

    [Fact]
    public async Task StopFiringAReplacedEntryAndUseTheNewOne()
    {
        await _scheduler.UpsertRecurringAsync(Every5Minutes(workUnitId: 1));
        await _scheduler.UpsertRecurringAsync(Every5Minutes(workUnitId: 2));

        _time.Advance(TimeSpan.FromMinutes(5));
        await WaitForCalls(1);
        await Task.Delay(50);

        _invoker.Calls.Should().Equal(new WorkInvocation("ok", 2));
    }

    [Fact]
    public async Task StopFiringARemovedEntry()
    {
        await _scheduler.UpsertRecurringAsync(Every5Minutes());
        await _scheduler.RemoveRecurringAsync("k");

        _time.Advance(TimeSpan.FromHours(1));
        await Task.Delay(50);

        _invoker.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task FireAnEntryWhoseNextOccurrenceIsFurtherAwayThanOneTimerStep()
    {
        // "midnight on 1 January" is 55 days away from 7 November; timers are armed one day at a time.
        await _scheduler.UpsertRecurringAsync(new RecurringWorkSpec("yearly", "0 0 1 1 *", true, new WorkInvocation("ok", 1)));

        for (var day = 0; day < 56; day++)
        {
            _time.Advance(TimeSpan.FromDays(1));
        }

        await WaitForCalls(1);
        _invoker.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task RunADelayedWorkUnitOnlyOnceItsTimeIsReached()
    {
        var result = await _scheduler.ScheduleAsync(new WorkInvocation("ok", 4), _time.GetUtcNow().AddMinutes(10));

        _time.Advance(TimeSpan.FromMinutes(9));
        await Task.Delay(50);
        (await _scheduler.GetRunAsync(result.Value))!.State.Should().Be(ExecutionState.Scheduled);

        _time.Advance(TimeSpan.FromMinutes(1));
        await WaitForCalls(1);
        await Eventually(async () => (await _scheduler.GetRunAsync(result.Value))!.State == ExecutionState.Succeeded, "delayed run to finish");
    }

    [Fact]
    public async Task RunADelayedWorkUnitImmediatelyWhenItsTimeHasAlreadyPassed()
    {
        await _scheduler.ScheduleAsync(new WorkInvocation("ok", 4), _time.GetUtcNow().AddMinutes(-1));

        await WaitForCalls(1);
    }

    [Fact]
    public async Task StampFinishTimesFromTheInjectedClock()
    {
        var id = await _scheduler.EnqueueAsync(new WorkInvocation("ok", 1));
        await Eventually(async () => (await _scheduler.GetRunAsync(id))!.State == ExecutionState.Succeeded, "run to finish");

        (await _scheduler.GetRunAsync(id))!.FinishedAt.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task KeepOnlyTheConfiguredNumberOfFinishedRuns()
    {
        _options.MaxFinishedRuns = 2;
        var ids = new List<ExecutionId>();
        for (var i = 1; i <= 3; i++)
        {
            var id = await _scheduler.EnqueueAsync(new WorkInvocation("ok", i));
            ids.Add(id);
            await Eventually(async () => (await _scheduler.GetRunAsync(id))?.State == ExecutionState.Succeeded, "run to finish");
        }

        var history = (await _scheduler.GetHistoryAsync(new ExecutionHistoryQuery())).Value!;
        history.Select(r => r.Id).Should().Equal(ids[2], ids[1]);
        (await _scheduler.GetRunAsync(ids[0])).Should().BeNull("the oldest finished run was trimmed");
    }

    [Fact]
    public async Task CancelRunningWorkUnitsAndRefuseNewWorkOnceDisposed()
    {
        var id = await _scheduler.EnqueueAsync(new WorkInvocation("block", 1));
        await _invoker.BlockStarted.WaitAsync(TimeSpan.FromSeconds(10));

        await _scheduler.DisposeAsync();

        _invoker.BlockSawCancellation.Should().BeTrue();
        (await _scheduler.GetRunAsync(id))!.State.Should().Be(ExecutionState.Canceled);
        var act = () => _scheduler.EnqueueAsync(new WorkInvocation("ok", 2));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task StopTimersAndCancelRunsOnSynchronousDisposeToo()
    {
        await _scheduler.UpsertRecurringAsync(Every5Minutes());
        var id = await _scheduler.EnqueueAsync(new WorkInvocation("block", 1));
        await _invoker.BlockStarted.WaitAsync(TimeSpan.FromSeconds(10));

        _scheduler.Dispose();

        await Eventually(() => _invoker.BlockSawCancellation, "the blocked work unit to observe cancellation");
        (await _scheduler.GetRunAsync(id))!.State.Should().Be(ExecutionState.Canceled);
        _time.Advance(TimeSpan.FromHours(1));
        await Task.Delay(50);
        _invoker.Calls.Should().ContainSingle(c => c.WorkUnitType == "block", "the recurring timer was stopped");
    }

    private sealed class Invoker : IWorkInvoker
    {
        private readonly ConcurrentQueue<WorkInvocation> _calls = new();
        private readonly TaskCompletionSource _blockStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _sawCancellation;

        public IReadOnlyCollection<WorkInvocation> Calls => _calls.ToArray();
        public Task BlockStarted => _blockStarted.Task;
        public bool BlockSawCancellation => Volatile.Read(ref _sawCancellation) == 1;

        public async Task<WorkResult?> InvokeAsync(WorkInvocation invocation, CancellationToken cancellationToken = default)
        {
            _calls.Enqueue(invocation);
            if (invocation.WorkUnitType == "block")
            {
                _blockStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Volatile.Write(ref _sawCancellation, 1);
                    throw;
                }
            }

            return WorkResult.Succeeded();
        }
    }
}
