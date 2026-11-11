namespace Emberdrop.Scheduling;

/// <summary>Describes a recurring work unit registration.</summary>
/// <param name="ScheduleKey">Stable key identifying the recurring entry in the engine.</param>
/// <param name="Cron">Cron expression.</param>
/// <param name="Enabled">When <see langword="false"/> the entry must exist but never fire.</param>
/// <param name="Invocation">What to run when the schedule fires.</param>
public sealed record RecurringWorkSpec(string ScheduleKey, string Cron, bool Enabled, WorkInvocation Invocation);
