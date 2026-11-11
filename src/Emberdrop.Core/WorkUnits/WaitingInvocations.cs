using Emberdrop.Plugins;
using System.Collections.Concurrent;
using Emberdrop.Scheduling;

namespace Emberdrop.WorkUnits;

/// <summary>
/// Runs that fired while their plugin was not available and are waiting to be repeated when it is. Held in memory only:
/// after a restart the plugins load first and recurring schedules resume by themselves.
/// </summary>
public sealed class WaitingInvocations
{
    private readonly ConcurrentDictionary<int, (WorkInvocation Invocation, string Reason, DateTimeOffset Since)> _waiting = new();

    /// <summary>Records that a run is waiting. A second run of the same work unit replaces the first: one repeat is enough.</summary>
    public void Add(WorkInvocation invocation, string reason) => _waiting[invocation.WorkUnitId] = (invocation, reason, DateTimeOffset.UtcNow);

    /// <summary>Removes and returns the waiting run of a work unit, if there is one.</summary>
    public bool TryTake(int workUnitId, out WorkInvocation invocation)
    {
        var found = _waiting.TryRemove(workUnitId, out var entry);
        invocation = entry.Invocation;
        return found;
    }

    /// <summary>The ids of the work units that have a run waiting.</summary>
    public IReadOnlyList<int> WorkUnitIds => _waiting.Keys.ToList();
}
