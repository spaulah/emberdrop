using Emberdrop.Plugins;
using Emberdrop.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace Emberdrop.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PluginsController : ControllerBase
{
    private readonly IPluginCatalog _catalog;

    public PluginsController(IPluginCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <summary>
    /// Lists every plugin version the host knows about with its lifecycle state, including versions that are draining
    /// (with a stuck flag), unloaded (with whether the runtime freed their code), failed or quarantined.
    /// </summary>
    [HttpGet]
    public ActionResult<ApiResponse<IEnumerable<PluginVersionDto>>> GetPlugins()
    {
        var versions = _catalog.GetVersions().Select(v => new PluginVersionDto
        {
            Id = v.Id,
            Version = v.Version?.ToString(),
            State = v.State.ToString(),
            StateSince = v.StateSince,
            Reason = v.Reason,
            InFlightExecutions = v.InFlightExecutions,
            Stuck = v.Stuck,
            ContextAlive = v.ContextAlive,
        }).ToList();

        return Ok(new ApiResponse<IEnumerable<PluginVersionDto>> { Data = versions, Message = "Plugins retrieved successfully" });
    }

    /// <summary>
    /// Diagnostic report for one plugin version: executions still running on it, activity it declared, the usual
    /// reasons a plugin refuses to unload, and how to investigate further.
    /// </summary>
    [HttpGet("{id}/{version}/report")]
    public ActionResult<ApiResponse<PluginReportDto>> GetReport(string id, string version)
    {
        if (!PluginVersion.TryParse(version, out var parsed))
        {
            return BadRequest(new ApiResponse<PluginReportDto> { Success = false, Message = $"'{version}' is not a valid version." });
        }

        var report = _catalog.GetDiagnosticReport(id, parsed);
        if (report is null)
        {
            return NotFound(new ApiResponse<PluginReportDto> { Success = false, Message = $"Plugin {id} {version} not found" });
        }

        return Ok(new ApiResponse<PluginReportDto>
        {
            Data = new PluginReportDto
            {
                Id = report.Id,
                Version = report.Version.ToString(),
                State = report.State.ToString(),
                Stuck = report.Stuck,
                ContextAlive = report.ContextAlive,
                Executions = report.Executions.Select(e => new RunningExecutionDto
                {
                    ExecutionId = e.ExecutionId,
                    WorkUnitName = e.ExportName,
                    StartedAt = e.StartedAt,
                    CancellationRequested = e.CancellationRequested,
                    Abandoned = e.Abandoned,
                }).ToList(),
                Activities = report.Activities.Select(a => new PluginActivityDto { Kind = a.Kind, Description = a.Description, StartedAt = a.StartedAt }).ToList(),
                CommonCauses = report.CommonCauses,
                Guidance = report.Guidance,
                GeneratedAt = report.GeneratedAt,
            },
            Message = "Report generated successfully",
        });
    }
}
