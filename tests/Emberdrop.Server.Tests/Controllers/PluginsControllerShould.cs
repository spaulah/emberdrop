using FluentAssertions;
using Emberdrop.Plugins;
using Emberdrop.WorkUnits;
using Emberdrop.Server.Controllers;
using Emberdrop.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Emberdrop.Server.Tests.Controllers;

public class PluginsControllerShould
{
    private static readonly PluginVersion V1 = PluginVersion.Parse("1.0.0");
    private readonly Mock<IPluginCatalog> _catalog = new();

    private PluginsController Controller() => new(_catalog.Object);

    [Fact]
    public void ListEveryVersionWithItsLifecycleStateStuckFlagAndWhetherItsCodeWasFreed()
    {
        var since = DateTimeOffset.UtcNow;
        _catalog.Setup(c => c.GetVersions()).Returns(
        [
            new PluginVersionStatus("acme", PluginVersion.Parse("2.0.0"), PluginState.Active, since, null, 3, false, null, "/secret/path"),
            new PluginVersionStatus("acme", V1, PluginState.Draining, since, "2 execution(s) did not stop", 2, true, null, null),
            new PluginVersionStatus("old", V1, PluginState.Unloaded, since, "still alive", 0, false, true, null),
            new PluginVersionStatus("(unknown)", null, PluginState.Quarantined, since, "not a zip", 0, false, null, null),
        ]);

        var result = Controller().GetPlugins();

        var body = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResponse<IEnumerable<PluginVersionDto>>>().Subject;
        var versions = body.Data!.ToList();
        versions.Select(v => (v.Id, v.Version, v.State)).Should().Equal(
            ("acme", "2.0.0", "Active"), ("acme", "1.0.0", "Draining"), ("old", "1.0.0", "Unloaded"), ("(unknown)", null, "Quarantined"));
        versions[0].InFlightExecutions.Should().Be(3);
        versions[1].Stuck.Should().BeTrue();
        versions[2].ContextAlive.Should().BeTrue();
        typeof(PluginVersionDto).GetProperties().Select(p => p.Name).Should().NotContain("Location", "server paths are not exposed");
    }

    [Fact]
    public void ReturnTheDiagnosticReportForAVersion()
    {
        var started = DateTimeOffset.UtcNow;
        _catalog.Setup(c => c.GetDiagnosticReport("acme", V1)).Returns(new PluginDiagnosticReport(
            "acme", V1, PluginState.Draining, true, null,
            [new RunningExecutionInfo("abc123", "nightly", started, CancellationRequested: true, Abandoned: false)],
            [new PluginActivityInfo("thread", "worker", started)],
            ["a static event"], "take a gcdump", DateTimeOffset.UtcNow));

        var result = Controller().GetReport("acme", "1.0.0");

        var report = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResponse<PluginReportDto>>().Subject.Data!;
        report.Stuck.Should().BeTrue();
        report.Executions.Should().ContainSingle().Which.CancellationRequested.Should().BeTrue();
        report.Activities.Should().ContainSingle().Which.Kind.Should().Be("thread");
        report.CommonCauses.Should().Equal("a static event");
        report.Guidance.Should().Be("take a gcdump");
    }

    [Fact]
    public void Return404ForAnUnknownVersionAnd400ForAMalformedOne()
    {
        _catalog.Setup(c => c.GetDiagnosticReport(It.IsAny<string>(), It.IsAny<PluginVersion>())).Returns((PluginDiagnosticReport?)null);

        Controller().GetReport("acme", "1.0.0").Result.Should().BeOfType<NotFoundObjectResult>();
        Controller().GetReport("acme", "not-a-version").Result.Should().BeOfType<BadRequestObjectResult>();
    }
}
