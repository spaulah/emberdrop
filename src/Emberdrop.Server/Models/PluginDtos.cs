namespace Emberdrop.Server.Models;

public class PluginVersionDto
{
    public string Id { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset StateSince { get; set; }
    public string? Reason { get; set; }
    public int InFlightExecutions { get; set; }
    public bool Stuck { get; set; }

    /// <summary>For an unloaded version: whether the runtime still holds its code. Null for versions that are not unloaded.</summary>
    public bool? ContextAlive { get; set; }
}

public class RunningExecutionDto
{
    public string ExecutionId { get; set; } = string.Empty;
    public string WorkUnitName { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public bool CancellationRequested { get; set; }
    public bool Abandoned { get; set; }
}

public class PluginActivityDto
{
    public string Kind { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
}

public class PluginReportDto
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public bool Stuck { get; set; }
    public bool? ContextAlive { get; set; }
    public IEnumerable<RunningExecutionDto> Executions { get; set; } = [];
    public IEnumerable<PluginActivityDto> Activities { get; set; } = [];
    public IEnumerable<string> CommonCauses { get; set; } = [];
    public string Guidance { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
}
