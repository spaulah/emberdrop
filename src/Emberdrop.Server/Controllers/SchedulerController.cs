using Emberdrop.Scheduling;
using Emberdrop.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace Emberdrop.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SchedulerController : ControllerBase
{
    private readonly IWorkScheduler _scheduler;

    public SchedulerController(IWorkScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <summary>
    /// Describes the active scheduler engine and what it supports. UIs use this to show
    /// engine-specific links, such as the engine's own dashboard, only when they exist.
    /// </summary>
    [HttpGet]
    public ActionResult<ApiResponse<SchedulerInfoDto>> GetScheduler()
    {
        var capabilities = _scheduler.Capabilities;
        var features = Enum.GetValues<SchedulerFeatures>()
            .Where(f => f != SchedulerFeatures.None && capabilities.Supports(f))
            .Select(f => f.ToString())
            .ToList();

        return Ok(new ApiResponse<SchedulerInfoDto>
        {
            Data = new SchedulerInfoDto
            {
                Engine = capabilities.EngineName,
                Features = features,
                DashboardPath = capabilities.DashboardPath
            },
            Message = "Scheduler retrieved successfully"
        });
    }

    /// <summary>Lists recent finished executions, newest first, each with the trigger that caused it.</summary>
    [HttpGet("executions")]
    public async Task<ActionResult<ApiResponse<IEnumerable<ExecutionDto>>>> GetExecutions([FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        if (skip < 0 || take < 1 || take > 500)
        {
            return BadRequest(new ApiResponse<IEnumerable<ExecutionDto>> { Success = false, Message = "Invalid request data", Errors = ["skip must be 0 or more and take must be between 1 and 500."] });
        }

        var result = await _scheduler.GetHistoryAsync(new ExecutionHistoryQuery { Skip = skip, Take = take });
        if (!result.IsOk)
        {
            return StatusCode(501, new ApiResponse<IEnumerable<ExecutionDto>> { Success = false, Message = "The scheduler cannot do that", Errors = [$"The scheduler does not support {result.MissingFeature}."] });
        }

        var executions = result.Value!.Select(e => new ExecutionDto
        {
            ExecutionId = e.Id.Value,
            State = e.State.ToString(),
            WorkUnitType = e.Invocation.WorkUnitType,
            WorkUnitId = e.Invocation.WorkUnitId,
            Trigger = e.Invocation.Trigger?.ToToken(),
            FinishedAt = e.FinishedAt,
            Error = e.Error,
        }).ToList();

        return Ok(new ApiResponse<IEnumerable<ExecutionDto>> { Data = executions, Message = "Executions retrieved successfully" });
    }
}
