using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Emberdrop.WorkUnits;
using Emberdrop.Server.WorkUnitManagement;
using Emberdrop.Storage;
using Emberdrop.Server.Tests.TestUtilities;

namespace Emberdrop.Server.Tests.WorkUnits;

public class WorkUnitRegistrationServiceShould
{
    private readonly Mock<IWorkStore> _mockRepository;
    private readonly Mock<IWorkUnitDiscoveryService> _mockDiscoveryService;
    private readonly Mock<IWorkSchedulerService> _mockSchedulerService;
    private readonly Mock<ILogger<WorkUnitRegistrationService>> _mockLogger;
    private readonly WorkUnitRegistrationService _workUnitRegistrationService;

    public WorkUnitRegistrationServiceShould()
    {
        _mockRepository = new Mock<IWorkStore>();
        _mockDiscoveryService = new Mock<IWorkUnitDiscoveryService>();
        _mockSchedulerService = new Mock<IWorkSchedulerService>();
        _mockLogger = new Mock<ILogger<WorkUnitRegistrationService>>();

        _workUnitRegistrationService = new WorkUnitRegistrationService(
            _mockRepository.Object,
            _mockDiscoveryService.Object,
            _mockSchedulerService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task RegisterNewWorkUnitsWhenWorkUnitsAreDiscovered()
    {
        var discoveredTypes = new List<DiscoveredWorkUnit> { Discovered(typeof(TestWorkUnit)), Discovered(typeof(AnotherTestWorkUnit)) };
        var existingWorkUnits = new List<WorkUnitDefinition>();

        _mockDiscoveryService.Setup(x => x.DiscoverWorkUnitsAsync())
            .ReturnsAsync(discoveredTypes);

        _mockRepository.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWorkUnits);

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<WorkUnitDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _workUnitRegistrationService.RegisterDiscoveredWorkUnitsAsync();

        _mockRepository.Verify(x => x.AddAsync(It.Is<WorkUnitDefinition>(workUnit =>
            workUnit.Type == typeof(TestWorkUnit).Name), It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(x => x.AddAsync(It.Is<WorkUnitDefinition>(workUnit =>
            workUnit.Type == typeof(AnotherTestWorkUnit).Name), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotRegisterExistingWorkUnitsWhenWorkUnitsAlreadyExist()
    {
        var discoveredTypes = new List<DiscoveredWorkUnit> { Discovered(typeof(TestWorkUnit)) };
        var existingWorkUnits = new List<WorkUnitDefinition>
        {
            new() { Type = typeof(TestWorkUnit).Name }
        };

        _mockDiscoveryService.Setup(x => x.DiscoverWorkUnitsAsync())
            .ReturnsAsync(discoveredTypes);

        _mockRepository.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWorkUnits);

        await _workUnitRegistrationService.RegisterDiscoveredWorkUnitsAsync();

        _mockRepository.Verify(x => x.AddAsync(It.IsAny<WorkUnitDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogInformationWhenNoNewWorkUnitsFound()
    {
        var discoveredTypes = new List<DiscoveredWorkUnit> { Discovered(typeof(TestWorkUnit)) };
        var existingWorkUnits = new List<WorkUnitDefinition>
        {
            new() { Type = typeof(TestWorkUnit).Name }
        };

        _mockDiscoveryService.Setup(x => x.DiscoverWorkUnitsAsync())
            .ReturnsAsync(discoveredTypes);

        _mockRepository.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWorkUnits);

        await _workUnitRegistrationService.RegisterDiscoveredWorkUnitsAsync();

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Work unit registration completed. New work units: 0")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAndReturnWorkUnitDefinitionWhenValidParameters()
    {
        const string name = "TestWorkUnit";
        const string type = "TestWorkUnitType";
        const string schedule = "0 0 * * *";
        const bool enabled = true;
        const int expectedWorkUnitId = 42;

        var expectedWorkUnit = TestFixtures.CreateTestWorkUnitDefinition(
            workUnitId: expectedWorkUnitId,
            name: name,
            type: type,
            schedule: schedule,
            enabled: enabled
        );

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<WorkUnitDefinition>(), It.IsAny<CancellationToken>()))
            .Callback<WorkUnitDefinition, CancellationToken>((workUnit, _) => workUnit.WorkUnitId = expectedWorkUnitId)
            .ReturnsAsync(expectedWorkUnitId);

        _mockRepository.Setup(x => x.GetAsync(expectedWorkUnitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedWorkUnit);

        var result = await _workUnitRegistrationService.RegisterWorkUnitAsync(name, type, schedule, enabled);

        result.Should().NotBeNull();
        result.WorkUnitId.Should().Be(expectedWorkUnitId);
        result.Name.Should().Be(name);
        result.Type.Should().Be(type);
        result.Schedule.Should().Be(schedule);
        result.Enabled.Should().Be(enabled);
    }

    [Fact]
    public async Task GenerateIdentifierWhenNameProvided()
    {
        const string name = "Test Work unit With Spaces";
        const int expectedWorkUnitId = 1;

        var expectedWorkUnit = TestFixtures.CreateTestWorkUnitDefinition(
            workUnitId: expectedWorkUnitId,
            name: name,
            type: "type"
        );

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<WorkUnitDefinition>(), It.IsAny<CancellationToken>()))
            .Callback<WorkUnitDefinition, CancellationToken>((workUnit, _) => workUnit.WorkUnitId = expectedWorkUnitId)
            .ReturnsAsync(expectedWorkUnitId);

        _mockRepository.Setup(x => x.GetAsync(expectedWorkUnitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedWorkUnit);

        var result = await _workUnitRegistrationService.RegisterWorkUnitAsync(name, "type", "schedule");

        result.Should().NotBeNull();
        _mockRepository.Verify(x => x.AddAsync(It.Is<WorkUnitDefinition>(workUnit =>
            workUnit.Identifier != null && workUnit.Identifier.Contains("Test Work unit With Spaces")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CallRepositoryAddWorkUnitWhenValidParameters()
    {
        const string name = "TestWorkUnit";
        const string type = "TestWorkUnitType";
        const string schedule = "0 0 * * *";
        const bool enabled = false;

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<WorkUnitDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _workUnitRegistrationService.RegisterWorkUnitAsync(name, type, schedule, enabled);

        _mockRepository.Verify(x => x.AddAsync(It.Is<WorkUnitDefinition>(workUnit =>
            workUnit.Name == name &&
            workUnit.Type == type &&
            workUnit.Schedule == schedule &&
            workUnit.Enabled == enabled), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DiscoveredWorkUnit Discovered(Type type) => new(type.Name, type.FullName!, type.Assembly.GetName().Name!, null);

    private class TestWorkUnit : IWorkUnit
    {
        public Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken) => Task.FromResult(WorkResult.Succeeded());
    }

    private class AnotherTestWorkUnit : IWorkUnit
    {
        public Task<WorkResult> ExecuteAsync(WorkContext context, CancellationToken cancellationToken) => Task.FromResult(WorkResult.Succeeded());
    }
}
