using FluentAssertions;
using Emberdrop.Hangfire;
using global::Hangfire.Common;
using global::Hangfire.Storage;

namespace Emberdrop.Hangfire.Tests;

/// <summary>
/// Pins why the adapter schedules a stable runner instead of the work unit object: what Hangfire
/// persists for a job is derived from the expression, and an instance puts its own type in storage.
/// </summary>
public class HangfireSerializationShould
{
    [Fact]
    public void PersistTheRuntimeTypeOfAWorkUnitInstanceWhenGivenAnInstanceExpression()
    {
        IRunnable instance = new PluginLikeWorkUnit();

        var job = Job.FromExpression(() => instance.Run(1));

        // This is what the previous design stored: a type that would have to come from a plugin assembly.
        job.Type.Should().Be(typeof(PluginLikeWorkUnit));
        InvocationData.SerializeJob(job).Type.Should().Contain(nameof(PluginLikeWorkUnit));
    }

    [Fact]
    public void PersistOnlyHostTypesAndPlainDataForTheRunner()
    {
        var job = Job.FromExpression<EmberdropHangfireRunner>(r => r.RunAsync("PluginLikeWorkUnit", 1, null, null, global::Hangfire.JobCancellationToken.Null));

        var data = InvocationData.SerializeJob(job);

        data.Type.Should().Contain(nameof(EmberdropHangfireRunner));
        data.Type.Should().NotContain(nameof(PluginLikeWorkUnit));
        data.Arguments.Should().Contain("PluginLikeWorkUnit");
    }

    private interface IRunnable
    {
        Task Run(int id);
    }

    private sealed class PluginLikeWorkUnit : IRunnable
    {
        public Task Run(int id) => Task.CompletedTask;
    }
}
