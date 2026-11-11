namespace Emberdrop.Scheduling;

/// <summary>The outcome category of a scheduler operation that can legitimately be refused.</summary>
public enum SchedulerResultStatus
{
    /// <summary>The operation was carried out.</summary>
    Ok = 0,

    /// <summary>The thing the operation refers to does not exist.</summary>
    NotFound = 1,

    /// <summary>The engine does not declare the feature the operation needs. See <see cref="SchedulerResult{T}.MissingFeature"/>.</summary>
    Unsupported = 2,
}
