using Microsoft.Extensions.Logging;
using Moq;
using Emberdrop.WorkUnits;
using Emberdrop.Server.WorkUnitManagement;
using Emberdrop.Storage;
using System.Reflection;

namespace Emberdrop.Server.Tests.TestUtilities;

/// <summary>
/// Test fixture for common test data and utilities
/// </summary>
public static class TestFixtures
{
    public static WorkUnitDefinition CreateTestWorkUnitDefinition(
        int workUnitId = 1,
        string? identifier = "test-work-unit",
        string? name = "Test Work unit",
        string? type = "TestWorkUnitType",
        string? schedule = "0 0 * * *",
        string? metadata = null,
        bool enabled = true)
    {
        var workUnit = new WorkUnitDefinition
        {
            WorkUnitId = workUnitId,
            Identifier = identifier,
            Name = name,
            Type = type,
            Schedule = schedule,
            Input = metadata,
            Enabled = enabled
        };

        // Use reflection to set internal properties for testing
        var createdAtProperty = typeof(WorkUnitDefinition).GetProperty(nameof(WorkUnitDefinition.CreatedAt));
        var updatedAtProperty = typeof(WorkUnitDefinition).GetProperty(nameof(WorkUnitDefinition.UpdatedAt));

        createdAtProperty?.SetValue(workUnit, DateTime.UtcNow);
        updatedAtProperty?.SetValue(workUnit, DateTime.UtcNow);

        return workUnit;
    }

    public static List<WorkUnitDefinition> CreateTestWorkUnitDefinitions(int count = 3)
    {
        var workUnits = new List<WorkUnitDefinition>();
        for (int i = 1; i <= count; i++)
        {
            workUnits.Add(CreateTestWorkUnitDefinition(
                workUnitId: i,
                identifier: $"test-work-unit-{i}",
                name: $"Test Work unit {i}",
                type: $"TestWorkUnitType{i}"));
        }
        return workUnits;
    }
}

/// <summary>
/// Logger verification helpers for testing
/// </summary>
public static class LoggerVerification
{
    public static void VerifyLogCalled<T>(
        Mock<ILogger<T>> mockLogger,
        LogLevel logLevel,
        string message,
        Times times)
    {
        mockLogger.Verify(
            x => x.Log(
                logLevel,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    public static void VerifyLogCalledWithException<T>(
        Mock<ILogger<T>> mockLogger,
        LogLevel logLevel,
        Exception expectedException,
        Times times)
    {
        mockLogger.Verify(
            x => x.Log(
                logLevel,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }
}

/// <summary>
/// Builder pattern for creating test objects
/// </summary>
public class WorkUnitDefinitionBuilder
{
    private int _workUnitId = 1;
    private string? _identifier = "test-work-unit";
    private string? _name = "Test Work unit";
    private string? _type = "TestWorkUnitType";
    private string? _schedule = "0 0 * * *";
    private string? _metadata;
    private bool _enabled = true;
    private DateTime _createdAt = DateTime.UtcNow;
    private DateTime _updatedAt = DateTime.UtcNow;

    public WorkUnitDefinitionBuilder WithWorkUnitId(int workUnitId)
    {
        _workUnitId = workUnitId;
        return this;
    }

    public WorkUnitDefinitionBuilder WithIdentifier(string? identifier)
    {
        _identifier = identifier;
        return this;
    }

    public WorkUnitDefinitionBuilder WithName(string? name)
    {
        _name = name;
        return this;
    }

    public WorkUnitDefinitionBuilder WithType(string? type)
    {
        _type = type;
        return this;
    }

    public WorkUnitDefinitionBuilder WithSchedule(string? schedule)
    {
        _schedule = schedule;
        return this;
    }

    public WorkUnitDefinitionBuilder WithMetadata(string? metadata)
    {
        _metadata = metadata;
        return this;
    }

    public WorkUnitDefinitionBuilder WithEnabled(bool enabled)
    {
        _enabled = enabled;
        return this;
    }

    public WorkUnitDefinitionBuilder WithCreatedAt(DateTime createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public WorkUnitDefinitionBuilder WithUpdatedAt(DateTime updatedAt)
    {
        _updatedAt = updatedAt;
        return this;
    }

    public WorkUnitDefinition Build()
    {
        var workUnit = new WorkUnitDefinition
        {
            WorkUnitId = _workUnitId,
            Identifier = _identifier,
            Name = _name,
            Type = _type,
            Schedule = _schedule,
            Input = _metadata,
            Enabled = _enabled
        };

        // Use reflection to set internal properties for testing
        var createdAtProperty = typeof(WorkUnitDefinition).GetProperty(nameof(WorkUnitDefinition.CreatedAt));
        var updatedAtProperty = typeof(WorkUnitDefinition).GetProperty(nameof(WorkUnitDefinition.UpdatedAt));

        createdAtProperty?.SetValue(workUnit, _createdAt);
        updatedAtProperty?.SetValue(workUnit, _updatedAt);

        return workUnit;
    }
}

/// <summary>
/// Common exception scenarios for testing
/// </summary>
public static class ExceptionScenarios
{
    public static readonly Exception DatabaseException = new("Database connection failed");
    public static readonly Exception TimeoutException = new TimeoutException("Operation timed out");
    public static readonly Exception ArgumentException = new ArgumentException("Invalid argument");
    public static readonly Exception UnauthorizedException = new UnauthorizedAccessException("Access denied");
}

/// <summary>
/// Test constants for consistent test data
/// </summary>
public static class TestConstants
{
    public const string DefaultWorkUnitType = "TestWorkUnitType";
    public const string DefaultSchedule = "0 0 * * *";
    public const string DefaultIdentifier = "test-work-unit";
    public const string DefaultWorkUnitName = "Test Work unit";

    public static readonly DateTime DefaultDateTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
