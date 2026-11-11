using FluentAssertions;
using Emberdrop.Hangfire;
using Emberdrop.WorkUnits;
using Emberdrop.Scheduling;
using global::Hangfire;
using Moq;

namespace Emberdrop.Hangfire.Tests;

public class EmberdropHangfireRunnerShould
{
    [Fact]
    public async Task InvokeTheWorkUnitWithHangfiresShutdownToken()
    {
        using var cts = new CancellationTokenSource();
        var hangfireToken = new Mock<IJobCancellationToken>();
        hangfireToken.SetupGet(t => t.ShutdownToken).Returns(cts.Token);
        var invoker = new Mock<IWorkInvoker>();

        await new EmberdropHangfireRunner(invoker.Object, new TriggerKindRegistry()).RunAsync("MyWorkUnit", 9, "recurring:*/5 * * * *", """{"a":1}""", hangfireToken.Object);

        invoker.Verify(i => i.InvokeAsync(new WorkInvocation("MyWorkUnit", 9, new RecurringTrigger("*/5 * * * *"), """{"a":1}"""), cts.Token), Times.Once);
    }

    [Fact]
    public async Task PropagateWorkUnitFailuresSoHangfireCanRetry()
    {
        var invoker = new Mock<IWorkInvoker>();
        invoker.Setup(i => i.InvokeAsync(It.IsAny<WorkInvocation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = () => new EmberdropHangfireRunner(invoker.Object, new TriggerKindRegistry()).RunAsync("MyWorkUnit", 1, null, null, new Mock<IJobCancellationToken>().Object);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
