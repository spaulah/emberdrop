using FluentAssertions;
using Emberdrop.WorkUnits;
using Emberdrop.Plugins;
using Emberdrop.Scheduling;
using Emberdrop.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Emberdrop.Server.Tests.WorkUnits;

public class CoreWorkSchedulerServiceShould
{
    private readonly Mock<IWorkScheduler> _scheduler = new();
    private readonly Mock<IWorkInvoker> _invoker = new();
    private readonly Mock<IWorkUnitBindingResolver> _bindings = new();
    private readonly Mock<IWorkStore> _store = new();
    private readonly WorkSchedulerService _service;

    public CoreWorkSchedulerServiceShould()
    {
        _bindings.Setup(b => b.Describe(It.IsAny<WorkUnitDefinition>())).Returns(WorkUnitBindingStatus.Ready());
        _store.Setup(x => x.ListTriggersAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _service = new WorkSchedulerService(_store.Object, new TriggerKindRegistry(), _scheduler.Object, _invoker.Object, _bindings.Object, NullLogger<WorkSchedulerService>.Instance);
    }

    private static WorkUnitDefinition WorkUnit(bool enabled = true) => new()
    {
        WorkUnitId = 7, Identifier = "work-unit-7", Type = "MyWorkUnit", Schedule = "*/5 * * * *", Enabled = enabled
    };

    [Fact]
    public async Task UpsertRecurringEntryDescribingTheStoredWorkUnit()
    {
        RecurringWorkSpec? captured = null;
        _scheduler.Setup(s => s.UpsertRecurringAsync(It.IsAny<RecurringWorkSpec>(), It.IsAny<CancellationToken>()))
            .Callback<RecurringWorkSpec, CancellationToken>((spec, _) => captured = spec)
            .Returns(Task.CompletedTask);

        await _service.ScheduleWorkUnitAsync(WorkUnit());

        captured.Should().Be(new RecurringWorkSpec("work-unit-7", "*/5 * * * *", true, new WorkInvocation("MyWorkUnit", 7, new RecurringTrigger("*/5 * * * *"))));
    }

    [Fact]
    public async Task PassDisabledStateToTheSchedulerInsteadOfInventingAnEngineSpecificCron()
    {
        RecurringWorkSpec? captured = null;
        _scheduler.Setup(s => s.UpsertRecurringAsync(It.IsAny<RecurringWorkSpec>(), It.IsAny<CancellationToken>()))
            .Callback<RecurringWorkSpec, CancellationToken>((spec, _) => captured = spec)
            .Returns(Task.CompletedTask);

        await _service.ScheduleWorkUnitAsync(WorkUnit(enabled: false));

        captured!.Enabled.Should().BeFalse();
        captured.Cron.Should().Be("*/5 * * * *");
    }

    [Theory]
    [InlineData(WorkUnitBindingState.WaitingForPlugin)]
    [InlineData(WorkUnitBindingState.MissingConfiguration)]
    [InlineData(WorkUnitBindingState.InputSchemaMismatch)]
    public async Task PauseAWorkUnitThatCannotRunByRemovingItsScheduleInsteadOfSchedulingIt(WorkUnitBindingState state)
    {
        _bindings.Setup(b => b.Describe(It.IsAny<WorkUnitDefinition>())).Returns(new WorkUnitBindingStatus(state, "because", null));

        await _service.ScheduleWorkUnitAsync(WorkUnit());

        _scheduler.Verify(s => s.RemoveRecurringAsync("work-unit-7", It.IsAny<CancellationToken>()), Times.Once);
        _scheduler.Verify(s => s.UpsertRecurringAsync(It.IsAny<RecurringWorkSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SkipSchedulingWhenTypeOrIdentifierIsMissing()
    {
        await _service.ScheduleWorkUnitAsync(new WorkUnitDefinition { WorkUnitId = 1, Type = null, Identifier = "x" });
        await _service.ScheduleWorkUnitAsync(new WorkUnitDefinition { WorkUnitId = 2, Type = "T", Identifier = null });

        _scheduler.Verify(s => s.UpsertRecurringAsync(It.IsAny<RecurringWorkSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WrapSchedulerFailuresInWorkUnitManagerException()
    {
        var failure = new InvalidOperationException("engine down");
        _scheduler.Setup(s => s.UpsertRecurringAsync(It.IsAny<RecurringWorkSpec>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var ex = await Assert.ThrowsAsync<WorkUnitManagerException>(() => _service.ScheduleWorkUnitAsync(WorkUnit()));

        ex.Message.Should().Be("Failed to schedule work unit 'MyWorkUnit'");
        ex.InnerException.Should().Be(failure);
    }

    [Fact]
    public async Task RemoveRecurringEntryByIdentifier()
    {
        await _service.RemoveWorkUnitAsync(WorkUnit());

        _scheduler.Verify(s => s.RemoveRecurringAsync("work-unit-7", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunWorkUnitThroughTheInvoker()
    {
        await _service.RunWorkUnitAsync(WorkUnit());

        _invoker.Verify(i => i.InvokeAsync(new WorkInvocation("MyWorkUnit", 7), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WrapInvokerFailuresInWorkUnitManagerException()
    {
        var failure = new InvalidOperationException("boom");
        _invoker.Setup(i => i.InvokeAsync(It.IsAny<WorkInvocation>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        var ex = await Assert.ThrowsAsync<WorkUnitManagerException>(() => _service.RunWorkUnitAsync(WorkUnit()));

        ex.Message.Should().Be("Failed to run work unit 'MyWorkUnit'");
        ex.InnerException.Should().Be(failure);
    }

    [Fact]
    public async Task ScheduleEachOperatorRecurringTriggerUnderItsOwnKey()
    {
        var specs = new List<RecurringWorkSpec>();
        _scheduler.Setup(x => x.UpsertRecurringAsync(It.IsAny<RecurringWorkSpec>(), It.IsAny<CancellationToken>()))
            .Callback<RecurringWorkSpec, CancellationToken>((spec, _) => specs.Add(spec)).Returns(Task.CompletedTask);
        _store.Setup(x => x.ListTriggersAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new StoredTrigger { TriggerId = 1, WorkUnitId = 7, Kind = "recurring", Definition = "recurring:0 9 * * *", Enabled = true },
            new StoredTrigger { TriggerId = 2, WorkUnitId = 7, Kind = "recurring", Definition = "recurring:0 18 * * *", Enabled = false },
        ]);

        await _service.ScheduleWorkUnitAsync(WorkUnit());

        specs.Select(x => (x.ScheduleKey, x.Cron, x.Enabled, x.Invocation.Trigger)).Should().BeEquivalentTo(new (string, string, bool, Trigger?)[]
        {
            ("work-unit-7", "*/5 * * * *", true, new RecurringTrigger("*/5 * * * *")),
            ("work-unit-7:trigger:1", "0 9 * * *", true, new RecurringTrigger("0 9 * * *")),
            ("work-unit-7:trigger:2", "0 18 * * *", false, new RecurringTrigger("0 18 * * *")),
        });
    }

    [Fact]
    public async Task RemoveOperatorTriggerEntriesWhenTheWorkUnitIsRemoved()
    {
        _store.Setup(x => x.ListTriggersAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new StoredTrigger { TriggerId = 5, WorkUnitId = 7, Kind = "recurring", Definition = "recurring:0 9 * * *" }]);

        await _service.RemoveWorkUnitAsync(WorkUnit());

        _scheduler.Verify(s => s.RemoveRecurringAsync("work-unit-7:trigger:5", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAnExecutionNowWithAManualTriggerAndTheGivenInput()
    {
        _scheduler.Setup(x => x.EnqueueAsync(It.IsAny<WorkInvocation>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ExecutionId("run-1"));

        var result = await _service.StartAsync(WorkUnit(), """{"a":1}""", runAt: null);

        result.Value.Should().Be(new ExecutionId("run-1"));
        _scheduler.Verify(x => x.EnqueueAsync(new WorkInvocation("MyWorkUnit", 7, ManualTrigger.Instance, """{"a":1}"""), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartADelayedExecutionWithAOneOffTrigger()
    {
        var at = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _scheduler.Setup(x => x.ScheduleAsync(It.IsAny<WorkInvocation>(), at, It.IsAny<CancellationToken>())).ReturnsAsync(SchedulerResult.Ok(new ExecutionId("run-2")));

        await _service.StartAsync(WorkUnit(), null, at);

        _scheduler.Verify(x => x.ScheduleAsync(new WorkInvocation("MyWorkUnit", 7, new OneOffTrigger(at)), at, It.IsAny<CancellationToken>()), Times.Once);
    }
}
