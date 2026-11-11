using FluentAssertions;
using Emberdrop.WorkUnits;
using Emberdrop.Plugins;
using Emberdrop.Server.Controllers;
using Emberdrop.Server.WorkUnitManagement;
using Emberdrop.Server.Models;
using Emberdrop.Scheduling;
using Emberdrop.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Moq;

namespace Emberdrop.Server.Tests.Controllers;

/// <summary>Characterization tests: they pin the REST behavior that must not change while internals move.</summary>
public class WorkUnitsControllerShould
{
    private readonly Mock<IWorkStore> _store = new();
    private readonly Mock<IWorkUnitRegistrationService> _registration = new();
    private readonly Mock<IWorkSchedulerService> _scheduling = new();
    private readonly Mock<IWorkUnitBindingResolver> _bindings = new();
    private readonly WorkUnitsController _controller;

    public WorkUnitsControllerShould()
    {
        _bindings.Setup(b => b.Describe(It.IsAny<WorkUnitDefinition>())).Returns(WorkUnitBindingStatus.Ready());
        _store.Setup(s => s.ListTriggersAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _controller = new WorkUnitsController(
            _store.Object,
            Mock.Of<IWorkUnitDiscoveryService>(),
            _registration.Object,
            _scheduling.Object,
            _bindings.Object,
            new TriggerKindRegistry(),
            NullLogger<WorkUnitsController>.Instance);
    }

    private static WorkUnitDefinition WorkUnit(int id = 1) => new()
    {
        WorkUnitId = id, Identifier = $"id-{id}", Name = "n", Type = "T", Schedule = "* * * * *", Input = "m", Enabled = true,
        CreatedAt = new DateTime(2025, 1, 1), UpdatedAt = new DateTime(2025, 1, 2)
    };

    [Fact]
    public async Task ReturnAllWorkUnitsInTheApiResponseEnvelope()
    {
        _store.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { WorkUnit(1), WorkUnit(2) });

        var result = await _controller.GetAllWorkUnits();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<WorkUnitDto>>>().Subject;
        body.Success.Should().BeTrue();
        body.Message.Should().Be("Work units retrieved successfully");
        body.Data!.Select(j => j.WorkUnitId).Should().Equal(1, 2);
    }

    [Fact]
    public async Task MapEveryStoredFieldToTheDto()
    {
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(WorkUnit());

        var result = await _controller.GetWorkUnit(1);

        var dto = result.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<ApiResponse<WorkUnitDto>>().Subject.Data!;
        dto.Should().BeEquivalentTo(new WorkUnitDto
        {
            WorkUnitId = 1, Identifier = "id-1", Name = "n", Type = "T", Schedule = "* * * * *", Input = "m", Enabled = true,
            CreatedAt = new DateTime(2025, 1, 1), UpdatedAt = new DateTime(2025, 1, 2)
        });
    }

    [Fact]
    public async Task Return404WhenWorkUnitDoesNotExist()
    {
        _store.Setup(s => s.GetAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync((WorkUnitDefinition?)null);

        var result = await _controller.GetWorkUnit(9);

        var notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var body = notFound.Value.Should().BeOfType<ApiResponse<WorkUnitDto>>().Subject;
        body.Success.Should().BeFalse();
        body.Message.Should().Be("Work unit with ID 9 not found");
    }

    [Fact]
    public async Task Return500WithErrorListWhenTheStoreFails()
    {
        _store.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _controller.GetAllWorkUnits();

        var status = result.Result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
        var body = status.Value.Should().BeOfType<ApiResponse<IEnumerable<WorkUnitDto>>>().Subject;
        body.Message.Should().Be("Internal server error");
        body.Errors.Should().Equal("db down");
    }

    [Fact]
    public async Task CreateWorkUnitThroughTheRegistrationServiceAndReturn201()
    {
        _registration.Setup(r => r.RegisterWorkUnitAsync("n", "T", "* * * * *", true, "m")).ReturnsAsync(WorkUnit(5));

        var result = await _controller.CreateWorkUnit(new CreateWorkUnitRequest
        {
            Identifier = "ignored", Name = "n", Type = "T", Schedule = "* * * * *", Input = "m", Enabled = true
        });

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(WorkUnitsController.GetWorkUnit));
        created.RouteValues!["id"].Should().Be(5);
    }

    [Fact]
    public async Task UpdateAllFieldsOfAnExistingWorkUnit()
    {
        var existing = WorkUnit();
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await _controller.UpdateWorkUnit(1, new UpdateWorkUnitRequest
        {
            Identifier = "new-id", Name = "new", Type = "T2", Schedule = "0 * * * *", Input = null, Enabled = false
        });

        result.Result.Should().BeOfType<OkObjectResult>();
        _store.Verify(s => s.UpdateAsync(It.Is<WorkUnitDefinition>(j =>
            j.Identifier == "new-id" && j.Name == "new" && j.Type == "T2" && j.Schedule == "0 * * * *" && !j.Enabled && j.Input == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteExistingWorkUnit()
    {
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(WorkUnit());
        _store.Setup(s => s.DeleteAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _controller.DeleteWorkUnit(1);

        var body = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResponse<object>>().Subject;
        body.Message.Should().Be("Work unit deleted successfully");
    }

    [Fact]
    public async Task Return404WhenDeletingUnknownWorkUnit()
    {
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((WorkUnitDefinition?)null);

        var result = await _controller.DeleteWorkUnit(1);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        _store.Verify(s => s.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- plugin binding, status and schedule overrides

    [Fact]
    public async Task ExposeTheBindingTheOverrideAndTheStatusOfAPluginWorkUnit()
    {
        var workUnit = WorkUnit();
        workUnit.PluginId = "acme";
        workUnit.WorkUnitName = "nightly";
        workUnit.VersionConstraint = "^1.0.0";
        workUnit.DefaultSchedule = "0 2 * * *";
        workUnit.ScheduleOverride = "0 4 * * *";
        workUnit.InputSchemaVersion = 2;
        _bindings.Setup(b => b.Describe(workUnit)).Returns(new WorkUnitBindingStatus(WorkUnitBindingState.WaitingForPlugin, "Plugin 'acme' is not loaded.", null));
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(workUnit);

        var result = await _controller.GetWorkUnit(1);

        var dto = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResponse<WorkUnitDto>>().Subject.Data!;
        (dto.PluginId, dto.WorkUnitName, dto.VersionConstraint, dto.DefaultSchedule, dto.ScheduleOverride, dto.InputSchemaVersion)
            .Should().Be(("acme", "nightly", "^1.0.0", "0 2 * * *", "0 4 * * *", 2));
        dto.Status.Should().Be("WaitingForPlugin");
        dto.StatusReason.Should().Be("Plugin 'acme' is not loaded.");
    }

    [Fact]
    public async Task ExposeKindDescriptionCapabilitiesSchemasAndBothKindsOfTriggers()
    {
        var workUnit = WorkUnit();
        workUnit.Kind = "work-unit";
        workUnit.Description = "Does a thing";
        workUnit.Capabilities = """["network:smtp"]""";
        workUnit.InputSchema = """{"type":"object","properties":{"name":{"type":"string"}}}""";
        workUnit.OutputSchema = null;
        _bindings.Setup(b => b.Describe(workUnit)).Returns(WorkUnitBindingStatus.Ready(null, [new RecurringTrigger("0 2 * * *"), ManualTrigger.Instance]));
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(workUnit);
        _store.Setup(s => s.ListTriggersAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StoredTrigger { TriggerId = 4, WorkUnitId = 1, Kind = "recurring", Definition = "recurring:0 9 * * *", Enabled = true }]);
        _store.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([workUnit]);

        var result = await _controller.GetAllWorkUnits();

        var dto = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResponse<IEnumerable<WorkUnitDto>>>().Subject.Data!.Single();
        (dto.Kind, dto.Description).Should().Be(("work-unit", "Does a thing"));
        dto.Capabilities.Should().Equal("network:smtp");
        dto.InputSchema!.Value.GetProperty("properties").GetProperty("name").GetProperty("type").GetString().Should().Be("string");
        dto.OutputSchema.Should().BeNull();
        dto.DeclaredTriggers.Should().Equal("recurring:0 2 * * *", "manual");
        dto.Triggers.Select(t => (t.TriggerId, t.Definition)).Should().Equal((4, "recurring:0 9 * * *"));
    }

    [Fact]
    public async Task RefuseNewInputThatDoesNotFitThePluginsInputTypeWhenCreating()
    {
        _bindings.Setup(b => b.ValidateInput(It.IsAny<WorkUnitDefinition>(), "{}")).Returns("The input of work unit 'echo' is not a valid EchoInput: nope");

        var result = await _controller.CreateWorkUnit(new CreateWorkUnitRequest { Name = "n", Type = "echo", Identifier = "i", PluginId = "acme", WorkUnitName = "echo", Input = "{}" });

        var bad = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject.Value.Should().BeOfType<ApiResponse<WorkUnitDto>>().Subject;
        bad.Errors.Should().ContainSingle().Which.Should().Contain("not a valid EchoInput");
        _registration.Verify(r => r.RegisterWorkUnitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task AddARecurringTriggerStoreItAndScheduleTheWorkUnit()
    {
        var workUnit = WorkUnit();
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(workUnit);
        _store.Setup(s => s.AddTriggerAsync(It.IsAny<StoredTrigger>(), It.IsAny<CancellationToken>())).ReturnsAsync(9);

        var result = await _controller.AddTrigger(1, JsonDocument.Parse("""{ "kind": "recurring", "cron": "0 9 * * *" }""").RootElement);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value.Should().BeOfType<ApiResponse<TriggerDto>>().Subject.Data!;
        (created.TriggerId, created.Kind, created.Definition).Should().Be((9, "recurring", "recurring:0 9 * * *"));
        _store.Verify(s => s.AddTriggerAsync(It.Is<StoredTrigger>(t => t.WorkUnitId == 1 && t.Definition == "recurring:0 9 * * *"), It.IsAny<CancellationToken>()), Times.Once);
        _scheduling.Verify(s => s.ScheduleWorkUnitAsync(workUnit, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("""{ "kind": "recurring", "cron": "daily" }""", "5 or 6 fields")]
    [InlineData("""{ "kind": "webhook" }""", "not a known trigger kind")]
    [InlineData("""{ "kind": "manual" }""", "recurring triggers only")]
    [InlineData("""{ "kind": "one-off", "at": "2030-01-01T00:00:00Z" }""", "recurring triggers only")]
    public async Task RefuseATriggerItCannotStoreAndSayWhy(string body, string expected)
    {
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(WorkUnit());

        var result = await _controller.AddTrigger(1, JsonDocument.Parse(body).RootElement);

        var bad = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject.Value.Should().BeOfType<ApiResponse<TriggerDto>>().Subject;
        bad.Errors.Should().ContainSingle().Which.Should().Contain(expected);
        _store.Verify(s => s.AddTriggerAsync(It.IsAny<StoredTrigger>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnscheduleATriggerBeforeDeletingIt()
    {
        var workUnit = WorkUnit();
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(workUnit);
        _store.Setup(s => s.ListTriggersAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([new StoredTrigger { TriggerId = 9, WorkUnitId = 1, Definition = "recurring:0 9 * * *" }]);

        var result = await _controller.DeleteTrigger(1, 9);

        result.Result.Should().BeOfType<OkObjectResult>();
        _scheduling.Verify(s => s.RemoveTriggerAsync(workUnit, 9, It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.DeleteTriggerAsync(9, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAManualExecutionWithTheGivenInput()
    {
        var workUnit = WorkUnit();
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(workUnit);
        _scheduling.Setup(s => s.StartAsync(workUnit, """{"name":"Ada"}""", null, It.IsAny<CancellationToken>())).ReturnsAsync(SchedulerResult.Ok(new ExecutionId("run-1")));

        var result = await _controller.StartExecution(1, new StartExecutionRequest { Input = JsonDocument.Parse("""{"name":"Ada"}""").RootElement });

        var started = result.Result.Should().BeOfType<AcceptedResult>().Subject.Value.Should().BeOfType<ApiResponse<ExecutionStartedDto>>().Subject.Data!;
        (started.ExecutionId, started.Trigger).Should().Be(("run-1", "manual"));
    }

    [Fact]
    public async Task StartADelayedExecutionAndRecordAOneOffTrigger()
    {
        var workUnit = WorkUnit();
        var at = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(workUnit);
        _scheduling.Setup(s => s.StartAsync(workUnit, null, at, It.IsAny<CancellationToken>())).ReturnsAsync(SchedulerResult.Ok(new ExecutionId("run-2")));

        var result = await _controller.StartExecution(1, new StartExecutionRequest { RunAt = at });

        var started = result.Result.Should().BeOfType<AcceptedResult>().Subject.Value.Should().BeOfType<ApiResponse<ExecutionStartedDto>>().Subject.Data!;
        started.Trigger.Should().Be(new OneOffTrigger(at).ToToken());
    }

    [Fact]
    public async Task RefuseToStartAnExecutionWithInputThatDoesNotFit()
    {
        var workUnit = WorkUnit();
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(workUnit);
        _bindings.Setup(b => b.ValidateInput(workUnit, """{"name":1}""")).Returns("The input of work unit 'echo' is not a valid EchoInput: bad name");

        var result = await _controller.StartExecution(1, new StartExecutionRequest { Input = JsonDocument.Parse("""{"name":1}""").RootElement });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _scheduling.Verify(s => s.StartAsync(It.IsAny<WorkUnitDefinition>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefuseToStartAnExecutionOfAWorkUnitThatCannotRunNow()
    {
        var workUnit = WorkUnit();
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(workUnit);
        _bindings.Setup(b => b.Describe(workUnit)).Returns(new WorkUnitBindingStatus(WorkUnitBindingState.WaitingForPlugin, "Plugin 'acme' is not loaded.", null));

        var result = await _controller.StartExecution(1, new StartExecutionRequest());

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject.Value.Should().BeOfType<ApiResponse<ExecutionStartedDto>>().Subject;
        conflict.Errors.Should().ContainSingle().Which.Should().Be("Plugin 'acme' is not loaded.");
    }

    [Fact]
    public async Task Say501WhenTheSchedulerCannotRunDelayedExecutions()
    {
        var workUnit = WorkUnit();
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(workUnit);
        _scheduling.Setup(s => s.StartAsync(workUnit, null, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ReturnsAsync(SchedulerResult.Unsupported<ExecutionId>(SchedulerFeatures.Delayed));

        var result = await _controller.StartExecution(1, new StartExecutionRequest { RunAt = DateTimeOffset.UtcNow.AddHours(1) });

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(501);
    }

    private async Task<WorkUnitDefinition> UpdateAsync(WorkUnitDefinition existing, string? schedule, string? versionConstraint = null)
    {
        _store.Setup(s => s.GetAsync(existing.WorkUnitId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        await _controller.UpdateWorkUnit(existing.WorkUnitId, new UpdateWorkUnitRequest
        {
            Identifier = existing.Identifier, Name = existing.Name, Type = existing.Type, Schedule = schedule,
            Input = existing.Input, Enabled = existing.Enabled, VersionConstraint = versionConstraint,
        });
        return existing;
    }

    [Fact]
    public async Task RecordAChangedScheduleAsTheOperatorsOverrideAndLeaveThePluginsDefaultAlone()
    {
        var workUnit = WorkUnit();
        workUnit.DefaultSchedule = "* * * * *";
        workUnit.Schedule = "* * * * *";

        await UpdateAsync(workUnit, "0 9 * * *");

        (workUnit.Schedule, workUnit.ScheduleOverride, workUnit.DefaultSchedule).Should().Be(("0 9 * * *", "0 9 * * *", "* * * * *"));
    }

    [Fact]
    public async Task ClearTheOverrideWhenTheScheduleGoesBackToThePluginsDefault()
    {
        var workUnit = WorkUnit();
        workUnit.DefaultSchedule = "* * * * *";
        workUnit.Schedule = "0 9 * * *";
        workUnit.ScheduleOverride = "0 9 * * *";

        await UpdateAsync(workUnit, "* * * * *");

        workUnit.Schedule.Should().Be("* * * * *");
        workUnit.ScheduleOverride.Should().BeNull();
    }

    [Fact]
    public async Task NotTurnAnUnchangedScheduleIntoAnOverrideWhenSomethingElseIsEdited()
    {
        var workUnit = WorkUnit();
        workUnit.DefaultSchedule = "* * * * *";
        workUnit.Schedule = "* * * * *";

        // The UI sends the whole object back. Editing the name must not freeze the schedule as an override.
        await UpdateAsync(workUnit, "* * * * *");

        workUnit.ScheduleOverride.Should().BeNull();
    }

    [Fact]
    public async Task KeepTheScheduleWhenTheRequestOmitsIt()
    {
        var workUnit = WorkUnit();

        await UpdateAsync(workUnit, null);

        workUnit.Schedule.Should().Be("* * * * *");
    }

    [Fact]
    public async Task PinAndUnpinAWorkUnitThroughTheVersionConstraint()
    {
        var workUnit = WorkUnit();

        await UpdateAsync(workUnit, null, "1.2.3");
        workUnit.VersionConstraint.Should().Be("1.2.3");

        await UpdateAsync(workUnit, null, "");
        workUnit.VersionConstraint.Should().BeNull("an empty constraint means the latest version");
    }

    [Fact]
    public async Task RejectAnInvalidVersionConstraintWithAHelpfulMessage()
    {
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(WorkUnit());

        var result = await _controller.UpdateWorkUnit(1, new UpdateWorkUnitRequest { Identifier = "i", Name = "n", Type = "T", VersionConstraint = "newest" });

        var body = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject.Value.Should().BeOfType<ApiResponse<WorkUnitDto>>().Subject;
        body.Errors.Should().ContainSingle().Which.Should().Contain("not a valid version constraint").And.Contain("^1.2");
        _store.Verify(s => s.UpdateAsync(It.IsAny<WorkUnitDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAnUpdateToTheSchedulerImmediately()
    {
        var workUnit = WorkUnit();

        await UpdateAsync(workUnit, "0 9 * * *");

        _scheduling.Verify(s => s.ScheduleWorkUnitAsync(workUnit, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveTheOldScheduleEntryWhenTheIdentifierChanges()
    {
        var workUnit = WorkUnit();
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(workUnit);

        await _controller.UpdateWorkUnit(1, new UpdateWorkUnitRequest { Identifier = "renamed", Name = "n", Type = "T", Enabled = true });

        _scheduling.Verify(s => s.RemoveWorkUnitAsync(It.Is<WorkUnitDefinition>(j => j.Identifier == "id-1"), It.IsAny<CancellationToken>()), Times.Once);
        _scheduling.Verify(s => s.ScheduleWorkUnitAsync(It.Is<WorkUnitDefinition>(j => j.Identifier == "renamed"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopADeletedWorkUnitFromFiring()
    {
        var workUnit = WorkUnit();
        _store.Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(workUnit);
        _store.Setup(s => s.DeleteAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await _controller.DeleteWorkUnit(1);

        _scheduling.Verify(s => s.RemoveWorkUnitAsync(workUnit, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAWorkUnitBoundToAPluginWhenTheRequestNamesOne()
    {
        var created = WorkUnit(5);
        _registration.Setup(r => r.RegisterWorkUnitAsync("n", "T", "* * * * *", true, null)).ReturnsAsync(created);

        await _controller.CreateWorkUnit(new CreateWorkUnitRequest
        {
            Identifier = "x", Name = "n", Type = "T", Schedule = "* * * * *", Enabled = true,
            PluginId = "acme", WorkUnitName = "nightly", VersionConstraint = "^1", InputSchemaVersion = 3,
        });

        (created.PluginId, created.WorkUnitName, created.VersionConstraint, created.InputSchemaVersion).Should().Be(("acme", "nightly", "^1", 3));
        _store.Verify(s => s.UpdateAsync(created, It.IsAny<CancellationToken>()), Times.Once);
    }
}
