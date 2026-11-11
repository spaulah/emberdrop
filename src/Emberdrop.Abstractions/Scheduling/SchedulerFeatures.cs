namespace Emberdrop.Scheduling;

/// <summary>Optional abilities a scheduler engine may or may not provide.</summary>
[Flags]
public enum SchedulerFeatures
{
    /// <summary>No optional features.</summary>
    None = 0,

    /// <summary>Recurring execution driven by a cron expression, including triggering a recurring work unit on demand.</summary>
    Recurring = 1 << 0,

    /// <summary>Running a work unit once at a future time (<see cref="IWorkScheduler.ScheduleAsync"/>).</summary>
    Delayed = 1 << 1,

    /// <summary>Cancelling a run that has not started yet (queued or scheduled).</summary>
    CancelQueued = 1 << 2,

    /// <summary>Cooperatively cancelling a run that is already executing, through the work unit's cancellation token.</summary>
    CancelRunning = 1 << 3,

    /// <summary>Listing finished runs (<see cref="IWorkScheduler.GetHistoryAsync"/>).</summary>
    History = 1 << 4,

    /// <summary>The engine automatically retries failed runs, so a failed run may be rescheduled instead of terminal.</summary>
    Retries = 1 << 5,
}
