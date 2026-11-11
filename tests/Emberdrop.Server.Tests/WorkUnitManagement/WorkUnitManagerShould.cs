using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Emberdrop.WorkUnits;
using Emberdrop.Server.WorkUnitManagement;
using Emberdrop.Storage;

namespace Emberdrop.Server.Tests.WorkUnits;

public class WorkUnitManagerShould
{
    private readonly Mock<IWorkStore> _mockRepository;
    private readonly Mock<IWorkUnitRegistrationService> _mockRegistrationService;
    private readonly Mock<IWorkSchedulerService> _mockSchedulerService;
    private readonly Mock<ILogger<WorkUnitManager>> _mockLogger;
    private readonly WorkUnitManager _workUnitManager;

    public WorkUnitManagerShould()
    {
        _mockRepository = new Mock<IWorkStore>();
        _mockRegistrationService = new Mock<IWorkUnitRegistrationService>();
        _mockSchedulerService = new Mock<IWorkSchedulerService>();
        _mockLogger = new Mock<ILogger<WorkUnitManager>>();

        _workUnitManager = new WorkUnitManager(
            _mockRepository.Object,
            _mockRegistrationService.Object,
            _mockSchedulerService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task RegisterDiscoveredWorkUnits_And_ScheduleAllWorkUnits_When_Successful()
    {
        var workUnits = new List<WorkUnitDefinition>
        {
            new() { WorkUnitId = 1, Name = "TestWorkUnit1", Enabled = true },
            new() { WorkUnitId = 2, Name = "TestWorkUnit2", Enabled = true }
        };

        _mockRepository.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(workUnits);

        await _workUnitManager.InitializeWorkUnitsAsync();

        _mockRegistrationService.Verify(x => x.RegisterDiscoveredWorkUnitsAsync(), Times.Once);
        _mockRepository.Verify(x => x.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockSchedulerService.Verify(x => x.ScheduleWorkUnitAsync(It.IsAny<WorkUnitDefinition>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ThrowWorkUnitManagerException_When_RegistrationFails()
    {
        var expectedException = new Exception("Registration failed");
        _mockRegistrationService.Setup(x => x.RegisterDiscoveredWorkUnitsAsync())
            .ThrowsAsync(expectedException);

        var exception = await Assert.ThrowsAsync<WorkUnitManagerException>(
            () => _workUnitManager.InitializeWorkUnitsAsync());

        exception.Message.Should().Be("Failed to initialize work units");
        exception.InnerException.Should().Be(expectedException);
    }

    [Fact]
    public async Task ThrowWorkUnitManagerException_When_SchedulingFails()
    {
        var expectedException = new Exception("Scheduling failed");

        // Setup registration to succeed
        _mockRegistrationService.Setup(x => x.RegisterDiscoveredWorkUnitsAsync())
            .Returns(Task.CompletedTask);

        // Setup GetAllWorkUnits to fail during scheduling phase
        _mockRepository.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var exception = await Assert.ThrowsAsync<WorkUnitManagerException>(
            () => _workUnitManager.InitializeWorkUnitsAsync());

        exception.Message.Should().Be("Failed to initialize work units");
        exception.InnerException.Should().BeOfType<WorkUnitManagerException>();
        exception.InnerException!.Message.Should().Be("Failed to schedule work units");
        exception.InnerException!.InnerException.Should().Be(expectedException);
    }

    [Fact]
    public async Task RegisterWorkUnit_When_ValidParameters()
    {
        var expectedWorkUnit = new WorkUnitDefinition
        {
            WorkUnitId = 1,
            Name = "TestWorkUnit",
            Type = "TestType",
            Schedule = "0 0 * * *",
            Enabled = true
        };

        _mockRegistrationService.Setup(x => x.RegisterWorkUnitAsync("TestWorkUnit", "TestType", "0 0 * * *", true, null))
            .ReturnsAsync(expectedWorkUnit);

        var result = await _workUnitManager.RegisterWorkUnitAsync("TestWorkUnit", "TestType", "0 0 * * *", true);

        result.Should().BeEquivalentTo(expectedWorkUnit);
        _mockRegistrationService.Verify(x => x.RegisterWorkUnitAsync("TestWorkUnit", "TestType", "0 0 * * *", true, null), Times.Once);
    }

    [Fact]
    public async Task ThrowWorkUnitManagerException_When_RegistrationServiceFails()
    {
        var expectedException = new Exception("Registration failed");
        _mockRegistrationService.Setup(x => x.RegisterWorkUnitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
            .ThrowsAsync(expectedException);

        var exception = await Assert.ThrowsAsync<WorkUnitManagerException>(
            () => _workUnitManager.RegisterWorkUnitAsync("TestWorkUnit", "TestType", "0 0 * * *"));

        exception.Message.Should().Be("Failed to register work unit 'TestWorkUnit'");
        exception.InnerException.Should().Be(expectedException);
    }

    [Fact]
    public async Task ReturnWorkUnitMetadata_When_WorkUnitExists()
    {
        const int workUnitId = 1;
        const string expectedMetadata = "test metadata";
        var workUnit = new WorkUnitDefinition { WorkUnitId = workUnitId, Input = expectedMetadata };

        _mockRepository.Setup(x => x.GetAsync(workUnitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workUnit);

        var result = await _workUnitManager.GetWorkUnitMetadataAsync(workUnitId);

        result.Should().Be(expectedMetadata);
    }

    [Fact]
    public async Task ReturnEmptyString_When_WorkUnitNotExists()
    {
        const int workUnitId = 1;
        _mockRepository.Setup(x => x.GetAsync(workUnitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkUnitDefinition?)null);

        var result = await _workUnitManager.GetWorkUnitMetadataAsync(workUnitId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ReturnEmptyString_When_WorkUnitMetadataIsNull()
    {
        const int workUnitId = 1;
        var workUnit = new WorkUnitDefinition { WorkUnitId = workUnitId, Input = null };

        _mockRepository.Setup(x => x.GetAsync(workUnitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workUnit);

        var result = await _workUnitManager.GetWorkUnitMetadataAsync(workUnitId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ThrowWorkUnitManagerException_When_RepositoryFails()
    {
        const int workUnitId = 1;
        var expectedException = new Exception("Database error");
        _mockRepository.Setup(x => x.GetAsync(workUnitId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var exception = await Assert.ThrowsAsync<WorkUnitManagerException>(
            () => _workUnitManager.GetWorkUnitMetadataAsync(workUnitId));

        exception.Message.Should().Be("Failed to get metadata for work unit ID '1'");
        exception.InnerException.Should().Be(expectedException);
    }
}
