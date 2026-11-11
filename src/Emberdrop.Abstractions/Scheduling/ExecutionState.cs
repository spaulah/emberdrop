namespace Emberdrop.Scheduling;

/// <summary>Where a run is in its life.</summary>
public enum ExecutionState
{
    /// <summary>Waiting to be picked up.</summary>
    Queued = 0,

    /// <summary>Waiting for a future time: a delayed run, or a failed run awaiting an automatic retry.</summary>
    Scheduled = 1,

    /// <summary>Currently executing.</summary>
    Running = 2,

    /// <summary>Finished without error.</summary>
    Succeeded = 3,

    /// <summary>Finished with an error and will not be retried.</summary>
    Failed = 4,

    /// <summary>Cancelled before completing.</summary>
    Canceled = 5,
}
