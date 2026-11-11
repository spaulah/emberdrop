using Emberdrop.Scheduling;
using FluentAssertions;

namespace Emberdrop.Providers.ContractTests.Scheduler;

/// <summary>
/// Behavior every <see cref="IWorkScheduler"/> must show. A provider runs this suite by subclassing it and
/// stating the features it expects to declare. Where a feature is optional, the test checks both sides:
/// a declared feature works, an undeclared one is refused with <see cref="SchedulerResultStatus.Unsupported"/>.
/// </summary>
public abstract class SchedulerContract : IAsyncLifetime
{
    private ISchedulerHarness _harness = null!;

    /// <summary>Exactly the optional features the provider is expected to declare.</summary>
    protected abstract SchedulerFeatures ExpectedFeatures { get; }

    protected abstract Task<ISchedulerHarness> CreateHarnessAsync();

    protected IWorkScheduler Scheduler => _harness.Scheduler;

    protected RecordingInvoker Invoker => _harness.Invoker;

    public async Task InitializeAsync() => _harness = await CreateHarnessAsync();

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    private static WorkInvocation WorkUnit(string type = "ok", int id = 1) => new(type, id);

    private Task<ExecutionInfo?> Run(ExecutionId id) => Scheduler.GetRunAsync(id);

    private Task WaitForState(ExecutionId id, ExecutionState state) =>
        Eventually.Async(async () => (await Run(id))?.State == state, $"run {id} to reach {state}");

    [Fact]
    public void DeclaresExactlyTheExpectedFeatures()
    {
        Scheduler.Capabilities.EngineName.Should().NotBeNullOrWhiteSpace();
        Scheduler.Capabilities.Features.Should().Be(ExpectedFeatures);
    }

    [Fact]
    public async Task EnqueuedRunExecutesOnceAndSucceeds()
    {
        var id = await Scheduler.EnqueueAsync(WorkUnit("ok", 7));

        await WaitForState(id, ExecutionState.Succeeded);

        Invoker.Calls.Should().Equal(WorkUnit("ok", 7));
        var run = await Run(id);
        run!.Invocation.Should().Be(WorkUnit("ok", 7));
        run.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ARunRecordsTheTriggerThatCausedItAndTheInputItWasGiven()
    {
        var invocation = new WorkInvocation("ok", 8, new OneOffTrigger(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)), """{"name":"Ada"}""");

        var id = await Scheduler.EnqueueAsync(invocation);

        await WaitForState(id, ExecutionState.Succeeded);
        Invoker.Calls.Should().Equal(invocation);
        (await Run(id))!.Invocation.Should().Be(invocation);
    }

    [Fact]
    public async Task FailedRunIsReportedWithItsErrorOrAwaitsRetryIfTheEngineRetries()
    {
        var id = await Scheduler.EnqueueAsync(WorkUnit(RecordingInvoker.Fail));
        var retries = Scheduler.Capabilities.Supports(SchedulerFeatures.Retries);
        var expected = retries ? ExecutionState.Scheduled : ExecutionState.Failed;

        await WaitForState(id, expected);

        (await Run(id))!.Error.Should().Contain("boom");
    }

    [Fact]
    public async Task UnknownRunIsNotFound()
    {
        var unknown = new ExecutionId("does-not-exist");

        (await Run(unknown)).Should().BeNull();
        (await Scheduler.CancelAsync(unknown)).Status.Should().Be(SchedulerResultStatus.NotFound);
    }

    [Fact]
    public async Task TriggerRunsTheRecurringEntryEvenWhenItIsDisabled()
    {
        await Scheduler.UpsertRecurringAsync(new RecurringWorkSpec("k1", "*/5 * * * *", false, WorkUnit("ok", 3)));

        var result = await Scheduler.TriggerAsync("k1");

        result.Status.Should().Be(SchedulerResultStatus.Ok);
        await WaitForState(result.Value, ExecutionState.Succeeded);
        Invoker.Calls.Should().Equal(WorkUnit("ok", 3));
    }

    [Fact]
    public async Task UpsertReplacesAnExistingEntry()
    {
        await Scheduler.UpsertRecurringAsync(new RecurringWorkSpec("k2", "*/5 * * * *", true, WorkUnit("ok", 1)));
        await Scheduler.UpsertRecurringAsync(new RecurringWorkSpec("k2", "*/5 * * * *", true, WorkUnit("ok", 2)));

        var result = await Scheduler.TriggerAsync("k2");

        await WaitForState(result.Value, ExecutionState.Succeeded);
        Invoker.Calls.Should().Equal(WorkUnit("ok", 2));
    }

    [Fact]
    public async Task TriggerOfAnUnknownOrRemovedEntryIsNotFound()
    {
        (await Scheduler.TriggerAsync("never-registered")).Status.Should().Be(SchedulerResultStatus.NotFound);

        await Scheduler.UpsertRecurringAsync(new RecurringWorkSpec("k3", "*/5 * * * *", true, WorkUnit()));
        await Scheduler.RemoveRecurringAsync("k3");

        (await Scheduler.TriggerAsync("k3")).Status.Should().Be(SchedulerResultStatus.NotFound);
    }

    [Fact]
    public async Task RemovingAnUnknownEntryIsNotAnError()
    {
        var act = () => Scheduler.RemoveRecurringAsync("never-registered");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DelayedRunWaitsForItsTimeOrIsRefusedWhenUnsupported()
    {
        var result = await Scheduler.ScheduleAsync(WorkUnit("ok", 5), DateTimeOffset.UtcNow.AddSeconds(2));

        if (!Scheduler.Capabilities.Supports(SchedulerFeatures.Delayed))
        {
            result.Status.Should().Be(SchedulerResultStatus.Unsupported);
            result.MissingFeature.Should().Be(SchedulerFeatures.Delayed);
            return;
        }

        result.Status.Should().Be(SchedulerResultStatus.Ok);
        (await Run(result.Value))!.State.Should().Be(ExecutionState.Scheduled);
        Invoker.Calls.Should().BeEmpty("the run is not due yet");

        await WaitForState(result.Value, ExecutionState.Succeeded);
        Invoker.Calls.Should().Equal(WorkUnit("ok", 5));
    }

    [Fact]
    public async Task QueuedRunCanBeCancelledBeforeItStartsOrIsRefusedWhenUnsupported()
    {
        if (!Scheduler.Capabilities.Supports(SchedulerFeatures.Delayed))
        {
            // Without delayed runs the suite has no portable way to hold a run back, so there is nothing to cancel.
            return;
        }

        var scheduled = await Scheduler.ScheduleAsync(WorkUnit("ok", 9), DateTimeOffset.UtcNow.AddHours(1));
        var result = await Scheduler.CancelAsync(scheduled.Value);

        if (!Scheduler.Capabilities.Supports(SchedulerFeatures.CancelQueued))
        {
            result.Status.Should().Be(SchedulerResultStatus.Unsupported);
            result.MissingFeature.Should().Be(SchedulerFeatures.CancelQueued);
            return;
        }

        result.Status.Should().Be(SchedulerResultStatus.Ok);
        result.Value.Should().BeTrue();
        await WaitForState(scheduled.Value, ExecutionState.Canceled);
        Invoker.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunningRunIsCancelledThroughItsTokenOrRefusedWhenUnsupported()
    {
        var id = await Scheduler.EnqueueAsync(WorkUnit(RecordingInvoker.Block));
        await Invoker.BlockStarted.WaitAsync(Eventually.DefaultTimeout);
        await WaitForState(id, ExecutionState.Running);

        var result = await Scheduler.CancelAsync(id);

        if (Scheduler.Capabilities.Supports(SchedulerFeatures.CancelRunning))
        {
            result.Status.Should().Be(SchedulerResultStatus.Ok);
            result.Value.Should().BeTrue();
            await WaitForState(id, ExecutionState.Canceled);
            await Eventually.Async(() => Invoker.BlockedWorkUnitSawCancellation, "the work unit to observe cancellation");
        }
        else
        {
            result.Status.Should().Be(SchedulerResultStatus.Unsupported);
            result.MissingFeature.Should().Be(SchedulerFeatures.CancelRunning);
            (await Run(id))!.State.Should().Be(ExecutionState.Running, "an unsupported cancel must not disturb the run");
            Invoker.BlockedWorkUnitSawCancellation.Should().BeFalse();

            Invoker.ReleaseBlockedWorkUnit();
            await WaitForState(id, ExecutionState.Succeeded);
        }
    }

    [Fact]
    public async Task CancellingAFinishedRunReportsItAlreadyFinished()
    {
        var id = await Scheduler.EnqueueAsync(WorkUnit());
        await WaitForState(id, ExecutionState.Succeeded);

        var result = await Scheduler.CancelAsync(id);

        result.Status.Should().Be(SchedulerResultStatus.Ok);
        result.Value.Should().BeFalse();
        (await Run(id))!.State.Should().Be(ExecutionState.Succeeded);
    }

    [Fact]
    public async Task HistoryListsFinishedRunsNewestFirstOrIsRefusedWhenUnsupported()
    {
        var ids = new List<ExecutionId>();
        for (var workUnitId = 1; workUnitId <= 3; workUnitId++)
        {
            var id = await Scheduler.EnqueueAsync(WorkUnit("ok", workUnitId));
            await WaitForState(id, ExecutionState.Succeeded);
            ids.Add(id);
            await Task.Delay(50);
        }

        var firstPage = await Scheduler.GetHistoryAsync(new ExecutionHistoryQuery { Take = 2 });

        if (!Scheduler.Capabilities.Supports(SchedulerFeatures.History))
        {
            firstPage.Status.Should().Be(SchedulerResultStatus.Unsupported);
            firstPage.MissingFeature.Should().Be(SchedulerFeatures.History);
            return;
        }

        firstPage.Status.Should().Be(SchedulerResultStatus.Ok);
        firstPage.Value!.Select(r => r.Id).Should().Equal(ids[2], ids[1]);
        firstPage.Value!.Should().OnlyContain(r => r.State == ExecutionState.Succeeded && r.FinishedAt != null);

        var secondPage = await Scheduler.GetHistoryAsync(new ExecutionHistoryQuery { Skip = 2, Take = 2 });
        secondPage.Value!.Select(r => r.Id).Should().Equal(ids[0]);
    }
}
