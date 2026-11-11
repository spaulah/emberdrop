using Emberdrop.Storage;

namespace Emberdrop.InMemory;

/// <summary>
/// An <see cref="IWorkStore"/> that keeps work unit definitions in process memory. Everything is lost on restart.
/// </summary>
/// <remarks>The store hands out copies, so changing a returned <see cref="WorkUnitDefinition"/> has no effect until it is passed to <see cref="UpdateAsync"/>.</remarks>
public sealed class InMemoryWorkStore : IWorkStore
{
    private readonly object _gate = new();
    private readonly SortedDictionary<int, WorkUnitDefinition> _workUnits = [];
    private readonly TimeProvider _time;
    private readonly SortedDictionary<int, StoredTrigger> _triggers = [];
    private int _lastId;
    private int _lastTriggerId;

    /// <summary>Creates the store using the system clock.</summary>
    public InMemoryWorkStore() : this(TimeProvider.System)
    {
    }

    /// <summary>Creates the store using the given clock.</summary>
    public InMemoryWorkStore(TimeProvider time)
    {
        _time = time;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkUnitDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<WorkUnitDefinition> all = _workUnits.Values.Select(Copy).ToList();
            return Task.FromResult(all);
        }
    }

    /// <inheritdoc />
    public Task<WorkUnitDefinition?> GetAsync(int workUnitId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_workUnits.TryGetValue(workUnitId, out var workUnit) ? Copy(workUnit) : null);
        }
    }

    /// <inheritdoc />
    public Task<int> AddAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workUnit);

        lock (_gate)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var id = ++_lastId;
            workUnit.CreatedAt = now;
            workUnit.UpdatedAt = now;
            var stored = Copy(workUnit);
            stored.WorkUnitId = id;
            _workUnits[id] = stored;
            return Task.FromResult(id);
        }
    }

    /// <inheritdoc />
    public Task UpdateAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workUnit);

        lock (_gate)
        {
            if (_workUnits.TryGetValue(workUnit.WorkUnitId, out var existing))
            {
                var createdAt = existing.CreatedAt;
                workUnit.UpdatedAt = _time.GetUtcNow().UtcDateTime;
                _workUnits[workUnit.WorkUnitId] = Copy(workUnit);
                _workUnits[workUnit.WorkUnitId].CreatedAt = createdAt;
            }

            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<int> DeleteAsync(int workUnitId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            foreach (var id in _triggers.Values.Where(t => t.WorkUnitId == workUnitId).Select(t => t.TriggerId).ToList())
            {
                _triggers.Remove(id);
            }

            return Task.FromResult(_workUnits.Remove(workUnitId) ? 1 : 0);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredTrigger>> ListTriggersAsync(int? workUnitId = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<StoredTrigger> all = _triggers.Values.Where(t => workUnitId is null || t.WorkUnitId == workUnitId).Select(Copy).ToList();
            return Task.FromResult(all);
        }
    }

    /// <inheritdoc />
    public Task<int> AddTriggerAsync(StoredTrigger trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        lock (_gate)
        {
            trigger.CreatedAt = _time.GetUtcNow().UtcDateTime;
            var stored = Copy(trigger);
            stored.TriggerId = ++_lastTriggerId;
            _triggers[stored.TriggerId] = stored;
            return Task.FromResult(stored.TriggerId);
        }
    }

    /// <inheritdoc />
    public Task<int> DeleteTriggerAsync(int triggerId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_triggers.Remove(triggerId) ? 1 : 0);
        }
    }

    private static WorkUnitDefinition Copy(WorkUnitDefinition workUnit) => new()
    {
        WorkUnitId = workUnit.WorkUnitId,
        Identifier = workUnit.Identifier,
        Name = workUnit.Name,
        Type = workUnit.Type,
        Schedule = workUnit.Schedule,
        Input = workUnit.Input,
        Enabled = workUnit.Enabled,
        CreatedAt = workUnit.CreatedAt,
        UpdatedAt = workUnit.UpdatedAt,
        PluginId = workUnit.PluginId,
        WorkUnitName = workUnit.WorkUnitName,
        VersionConstraint = workUnit.VersionConstraint,
        InputSchemaVersion = workUnit.InputSchemaVersion,
        DefaultSchedule = workUnit.DefaultSchedule,
        ScheduleOverride = workUnit.ScheduleOverride,
        Kind = workUnit.Kind,
        Description = workUnit.Description,
        Capabilities = workUnit.Capabilities,
        InputSchema = workUnit.InputSchema,
        OutputSchema = workUnit.OutputSchema,
    };

    private static StoredTrigger Copy(StoredTrigger trigger) => new()
    {
        TriggerId = trigger.TriggerId,
        WorkUnitId = trigger.WorkUnitId,
        Kind = trigger.Kind,
        Definition = trigger.Definition,
        Enabled = trigger.Enabled,
        CreatedAt = trigger.CreatedAt,
    };
}
