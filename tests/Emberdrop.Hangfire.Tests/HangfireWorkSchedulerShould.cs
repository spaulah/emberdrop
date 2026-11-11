using FluentAssertions;
using Emberdrop.Hangfire;
using Emberdrop.Scheduling;
using global::Hangfire;
using global::Hangfire.Common;
using Moq;

namespace Emberdrop.Hangfire.Tests;

public class HangfireWorkSchedulerShould
{
    private readonly Mock<IRecurringJobManager> _recurring = new();
    private readonly HangfireDashboardInfo _dashboard = new();
    private readonly HangfireWorkScheduler _scheduler;

    public HangfireWorkSchedulerShould()
    {
        _scheduler = new HangfireWorkScheduler(_recurring.Object, Mock.Of<IBackgroundJobClient>(), Mock.Of<JobStorage>(), _dashboard, new TriggerKindRegistry());
    }

    [Fact]
    public void DeclareOnlyWhatItImplements()
    {
        _scheduler.Capabilities.EngineName.Should().Be("Hangfire");
        _scheduler.Capabilities.Supports(SchedulerFeatures.Recurring).Should().BeTrue();
        _scheduler.Capabilities.Supports(SchedulerFeatures.CancelRunning).Should().BeFalse("Hangfire cancels running work units only cooperatively through a token Emberdrop's work unit contract does not expose");
        _scheduler.Capabilities.DashboardPath.Should().BeNull("the dashboard is optional and not mounted yet");
    }

    [Fact]
    public async Task ScheduleTheStableRunnerWithPlainDataArguments()
    {
        Job? captured = null;
        _recurring.Setup(m => m.AddOrUpdate("k", It.IsAny<Job>(), "*/5 * * * *", It.IsAny<RecurringJobOptions>()))
            .Callback<string, Job, string, RecurringJobOptions>((_, job, _, _) => captured = job);

        await _scheduler.UpsertRecurringAsync(new RecurringWorkSpec("k", "*/5 * * * *", true, new WorkInvocation("MyPluginWorkUnit", 3, new RecurringTrigger("*/5 * * * *"))));

        captured.Should().NotBeNull();
        captured!.Type.Should().Be(typeof(EmberdropHangfireRunner));
        captured.Method.Name.Should().Be(nameof(EmberdropHangfireRunner.RunAsync));
        captured.Args[0].Should().Be("MyPluginWorkUnit");
        captured.Args[1].Should().Be(3);
        captured.Args[2].Should().Be("recurring:*/5 * * * *", "the trigger travels as plain text");
        captured.Args[3].Should().BeNull("no per-run input was given");
    }

    [Fact]
    public async Task ScheduleDisabledWorkUnitsWithACronThatNeverFires()
    {
        await _scheduler.UpsertRecurringAsync(new RecurringWorkSpec("k", "*/5 * * * *", false, new WorkInvocation("MyWorkUnit", 3)));

        _recurring.Verify(m => m.AddOrUpdate("k", It.IsAny<Job>(), "0 0 31 2 *", It.IsAny<RecurringJobOptions>()), Times.Once);
    }

    [Fact]
    public async Task RemoveRecurringEntries()
    {
        await _scheduler.RemoveRecurringAsync("k");

        _recurring.Verify(m => m.RemoveIfExists("k"), Times.Once);
    }

    [Fact]
    public void ReportTheDashboardPathOnceTheHostMountsIt()
    {
        _dashboard.Path = "/hangfire";

        _scheduler.Capabilities.DashboardPath.Should().Be("/hangfire");
    }
}
