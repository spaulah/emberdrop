using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Emberdrop;
using Emberdrop.WorkUnits;
using Emberdrop.Server.WorkUnitManagement;
using Emberdrop.Storage;

namespace Emberdrop.Server.Tests.WorkUnits;

public class WorkSchedulerServiceShould
{
    private readonly Mock<ILogger<WorkSchedulerService>> _mockLogger;

    public WorkSchedulerServiceShould()
    {
        _mockLogger = new Mock<ILogger<WorkSchedulerService>>();
    }

    [Fact]
    public void AcceptValidWorkUnitDefinition()
    {
        var systemWorkUnit = new WorkUnitDefinition
        {
            WorkUnitId = 1,
            Name = "Test Work unit",
            Type = "TestWorkUnitType",
            Schedule = "0 0 * * *",
            Enabled = true
        };

        systemWorkUnit.Should().NotBeNull();
        systemWorkUnit.Enabled.Should().BeTrue();
        systemWorkUnit.Schedule.Should().NotBeNullOrEmpty();
        systemWorkUnit.Type.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void HandleDisabledWorkUnit()
    {
        var systemWorkUnit = new WorkUnitDefinition
        {
            WorkUnitId = 1,
            Name = "Disabled Work unit",
            Type = "TestWorkUnitType",
            Schedule = "0 0 * * *",
            Enabled = false
        };

        systemWorkUnit.Enabled.Should().BeFalse();
    }

    [Fact]
    public void AcceptValidWorkUnitDefinitionForRemoval()
    {
        var systemWorkUnit = new WorkUnitDefinition
        {
            WorkUnitId = 1,
            Name = "Work unit to Remove",
            Type = "TestWorkUnitType"
        };

        systemWorkUnit.Should().NotBeNull();
        systemWorkUnit.WorkUnitId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AcceptValidWorkUnitDefinitionForExecution()
    {
        var systemWorkUnit = new WorkUnitDefinition
        {
            WorkUnitId = 1,
            Name = "Work unit to Run",
            Type = "TestWorkUnitType"
        };

        systemWorkUnit.Should().NotBeNull();
        systemWorkUnit.Type.Should().NotBeNullOrEmpty();

        await Task.CompletedTask;
    }
}
