using FluentAssertions;
using Emberdrop.Scheduling;
using Emberdrop.Server.Controllers;
using Emberdrop.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Emberdrop.Server.Tests.Controllers;

public class SchedulerControllerShould
{
    private static SchedulerInfoDto Describe(SchedulerCapabilities capabilities)
    {
        var scheduler = new Mock<IWorkScheduler>();
        scheduler.SetupGet(s => s.Capabilities).Returns(capabilities);

        var result = new SchedulerController(scheduler.Object).GetScheduler();

        return result.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<ApiResponse<SchedulerInfoDto>>().Subject.Data!;
    }

    [Fact]
    public void DescribeTheEngineItsFeaturesAndItsDashboard()
    {
        var info = Describe(new SchedulerCapabilities("Hangfire", SchedulerFeatures.Recurring, "/hangfire"));

        info.Engine.Should().Be("Hangfire");
        info.Features.Should().Equal("Recurring");
        info.DashboardPath.Should().Be("/hangfire");
    }

    [Fact]
    public void ReportNoDashboardForEnginesWithoutOne()
    {
        var info = Describe(new SchedulerCapabilities("InMemory", SchedulerFeatures.Recurring));

        info.DashboardPath.Should().BeNull();
    }
}
