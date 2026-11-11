namespace Emberdrop.Storage;

/// <summary>
/// Persistence for <see cref="WorkUnitDefinition"/> records. Implementations are storage providers
/// (for example SQL Server or SQLite).
/// </summary>
public interface IWorkStore
{
    /// <summary>Returns every stored work unit definition.</summary>
    Task<IReadOnlyList<WorkUnitDefinition>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the work unit with the given id, or <see langword="null"/> if it does not exist.</summary>
    Task<WorkUnitDefinition?> GetAsync(int workUnitId, CancellationToken cancellationToken = default);

    /// <summary>Stores a new work unit definition and returns its assigned id.</summary>
    Task<int> AddAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default);

    /// <summary>Overwrites an existing work unit definition.</summary>
    Task UpdateAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default);

    /// <summary>Deletes a work unit definition and returns the number of rows removed.</summary>
    Task<int> DeleteAsync(int workUnitId, CancellationToken cancellationToken = default);

    /// <summary>Lists the operator-created triggers of one work unit, or of all work units when <paramref name="workUnitId"/> is <see langword="null"/>.</summary>
    Task<IReadOnlyList<StoredTrigger>> ListTriggersAsync(int? workUnitId = null, CancellationToken cancellationToken = default);

    /// <summary>Adds an operator-created trigger and returns its id. <see cref="StoredTrigger.CreatedAt"/> is set by the store.</summary>
    Task<int> AddTriggerAsync(StoredTrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>Deletes one trigger; returns how many rows were removed.</summary>
    Task<int> DeleteTriggerAsync(int triggerId, CancellationToken cancellationToken = default);
}
