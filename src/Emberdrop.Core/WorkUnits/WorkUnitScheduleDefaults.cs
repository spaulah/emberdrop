namespace Emberdrop.WorkUnits;

/// <summary>Schedule values used when work units are registered without an explicit schedule.</summary>
public static class WorkUnitScheduleDefaults
{
    /// <summary>
    /// Stored for newly discovered work units, which start disabled. It is a valid cron expression
    /// (31 February) that never matches a real date.
    /// </summary>
    public const string NeverSchedule = "0 0 31 2 *";

    /// <summary>Default schedule for dynamically registered work units (every hour).</summary>
    public const string DefaultSchedule = "0 * * * *";
}
