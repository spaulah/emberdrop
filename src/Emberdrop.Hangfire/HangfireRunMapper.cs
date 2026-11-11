using Emberdrop.Scheduling;
using Hangfire.Common;
using Hangfire.Storage.Monitoring;

namespace Emberdrop.Hangfire;

/// <summary>Translates Hangfire's job records into Emberdrop's engine-neutral run model.</summary>
internal static class HangfireRunMapper
{
    private static readonly WorkInvocation Unknown = new(string.Empty, 0);

    public static ExecutionState? StateOf(JobDetailsDto? details)
    {
        var name = details?.History?.FirstOrDefault()?.StateName;
        return name switch
        {
            null => null,
            "Enqueued" => ExecutionState.Queued,
            "Scheduled" or "Awaiting" => ExecutionState.Scheduled,
            "Processing" => ExecutionState.Running,
            "Succeeded" => ExecutionState.Succeeded,
            "Failed" => ExecutionState.Failed,
            "Deleted" => ExecutionState.Canceled,
            _ => ExecutionState.Queued,
        };
    }

    public static ExecutionInfo? ToRunInfo(string id, JobDetailsDto? details, TriggerKindRegistry triggerKinds)
    {
        var state = StateOf(details);
        if (state is null || details is null)
        {
            return null;
        }

        var latest = details.History![0];
        var terminal = state is ExecutionState.Succeeded or ExecutionState.Failed or ExecutionState.Canceled;
        latest.Data.TryGetValue("ExceptionMessage", out var error);

        return new ExecutionInfo(
            new ExecutionId(id),
            InvocationOf(details.Job, triggerKinds),
            state.Value,
            terminal ? AsUtc(latest.CreatedAt) : null,
            error ?? (state == ExecutionState.Scheduled ? latest.Reason : null));
    }

    public static ExecutionInfo Finished(TriggerKindRegistry triggerKinds, string id, Job? job, ExecutionState state, DateTime? at, string? error) =>
        new(new ExecutionId(id), InvocationOf(job, triggerKinds), state, at is null ? null : AsUtc(at.Value), error);

    private static WorkInvocation InvocationOf(Job? job, TriggerKindRegistry triggerKinds)
    {
        if (job is null || job.Type != typeof(EmberdropHangfireRunner) || job.Args.Count < 4)
        {
            return Unknown;
        }

        return new WorkInvocation((string)job.Args[0], (int)job.Args[1], job.Args[2] is string token ? TryParse(triggerKinds, token) : null, job.Args[3] as string);
    }

    private static Trigger? TryParse(TriggerKindRegistry triggerKinds, string token)
    {
        try
        {
            return triggerKinds.Parse(token);
        }
        catch (FormatException)
        {
            // A history entry written with a trigger kind this host no longer knows is still shown, without its trigger.
            return null;
        }
    }

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
