using Cronos;
using Emberdrop.Scheduling;

namespace Emberdrop.InMemory;

/// <summary>A registered recurring work unit and the timer that wakes it.</summary>
internal sealed class RecurringEntry
{
    public RecurringEntry(RecurringWorkSpec spec, CronExpression? cron)
    {
        Spec = spec;
        Cron = cron;
    }

    public RecurringWorkSpec Spec { get; }

    /// <summary>Parsed schedule; <see langword="null"/> for a disabled entry, which never fires on its own.</summary>
    public CronExpression? Cron { get; }

    public DateTimeOffset? NextFire { get; set; }
    public ITimer? Timer { get; set; }
    public bool Removed { get; set; }
}
