using Dapper;
using Emberdrop.Storage;

namespace Emberdrop.Storage.Relational;

/// <summary>
/// An <see cref="IWorkStore"/> over a relational database, written against standard SQL with a
/// small <see cref="ISqlDialect"/> for the differences. Storage providers derive from it.
/// </summary>
/// <remarks>All timestamps are written as UTC.</remarks>
public abstract class RelationalWorkStore : IWorkStore
{
    private const string Columns =
        "\"WorkUnitId\", \"Identifier\", \"Name\", \"Type\", \"Schedule\", \"Enabled\", \"Input\", \"CreatedAt\", \"UpdatedAt\", " +
        "\"PluginId\", \"WorkUnitName\", \"VersionConstraint\", \"InputSchemaVersion\", \"DefaultSchedule\", \"ScheduleOverride\", " +
        "\"Kind\", \"Description\", \"Capabilities\", \"InputSchema\", \"OutputSchema\"";

    private const string TriggerSelect = "SELECT \"TriggerId\", \"WorkUnitId\", \"Kind\", \"Definition\", \"Enabled\", \"CreatedAt\" FROM \"WorkUnitTrigger\"";

    private readonly ISqlDialect _dialect;
    private readonly RelationalStoreOptions _options;

    /// <summary>Creates the store.</summary>
    /// <param name="dialect">The database dialect.</param>
    /// <param name="options">Connection and clock settings.</param>
    protected RelationalWorkStore(ISqlDialect dialect, RelationalStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("The work unit store has no connection string configured.");
        }

        _dialect = dialect;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkUnitDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _dialect.CreateConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"SELECT {Columns} FROM \"WorkUnitDefinition\" ORDER BY \"WorkUnitId\"";
        var rows = await connection.QueryAsync<WorkUnitDefinition>(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<WorkUnitDefinition?> GetAsync(int workUnitId, CancellationToken cancellationToken = default)
    {
        await using var connection = _dialect.CreateConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"SELECT {Columns} FROM \"WorkUnitDefinition\" WHERE \"WorkUnitId\" = @Id";
        return await connection.QuerySingleOrDefaultAsync<WorkUnitDefinition>(
            new CommandDefinition(sql, new { Id = workUnitId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> AddAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workUnit);

        var now = _options.TimeProvider.GetUtcNow().UtcDateTime;
        workUnit.CreatedAt = now;
        workUnit.UpdatedAt = now;

        await using var connection = _dialect.CreateConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql =
            "INSERT INTO \"WorkUnitDefinition\" (\"Identifier\", \"Name\", \"Type\", \"Schedule\", \"Enabled\", \"Input\", \"CreatedAt\", \"UpdatedAt\", " +
            "\"PluginId\", \"WorkUnitName\", \"VersionConstraint\", \"InputSchemaVersion\", \"DefaultSchedule\", \"ScheduleOverride\", " +
            "\"Kind\", \"Description\", \"Capabilities\", \"InputSchema\", \"OutputSchema\") " +
            "VALUES (@Identifier, @Name, @Type, @Schedule, @Enabled, @Input, @CreatedAt, @UpdatedAt, " +
            "@PluginId, @WorkUnitName, @VersionConstraint, @InputSchemaVersion, @DefaultSchedule, @ScheduleOverride, " +
            "@Kind, @Description, @Capabilities, @InputSchema, @OutputSchema); " +
            _dialect.SelectLastInsertedId;
        return await connection.QuerySingleAsync<int>(new CommandDefinition(sql, workUnit, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(WorkUnitDefinition workUnit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workUnit);

        workUnit.UpdatedAt = _options.TimeProvider.GetUtcNow().UtcDateTime;

        await using var connection = _dialect.CreateConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // CreatedAt is deliberately not touched.
        var sql =
            "UPDATE \"WorkUnitDefinition\" SET \"Identifier\" = @Identifier, \"Name\" = @Name, \"Type\" = @Type, \"Schedule\" = @Schedule, " +
            "\"Enabled\" = @Enabled, \"Input\" = @Input, \"UpdatedAt\" = @UpdatedAt, " +
            "\"PluginId\" = @PluginId, \"WorkUnitName\" = @WorkUnitName, \"VersionConstraint\" = @VersionConstraint, " +
            "\"InputSchemaVersion\" = @InputSchemaVersion, \"DefaultSchedule\" = @DefaultSchedule, \"ScheduleOverride\" = @ScheduleOverride, " +
            "\"Kind\" = @Kind, \"Description\" = @Description, \"Capabilities\" = @Capabilities, " +
            "\"InputSchema\" = @InputSchema, \"OutputSchema\" = @OutputSchema " +
            "WHERE \"WorkUnitId\" = @WorkUnitId";
        await connection.ExecuteAsync(new CommandDefinition(sql, workUnit, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteAsync(int workUnitId, CancellationToken cancellationToken = default)
    {
        await using var connection = _dialect.CreateConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // A work unit's operator triggers go with it.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM \"WorkUnitTrigger\" WHERE \"WorkUnitId\" = @Id", new { Id = workUnitId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var sql = "DELETE FROM \"WorkUnitDefinition\" WHERE \"WorkUnitId\" = @Id";
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = workUnitId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredTrigger>> ListTriggersAsync(int? workUnitId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = _dialect.CreateConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = TriggerSelect + (workUnitId is null ? string.Empty : " WHERE \"WorkUnitId\" = @Id") + " ORDER BY \"TriggerId\"";
        var rows = await connection.QueryAsync<StoredTrigger>(new CommandDefinition(sql, new { Id = workUnitId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<int> AddTriggerAsync(StoredTrigger trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        trigger.CreatedAt = _options.TimeProvider.GetUtcNow().UtcDateTime;

        await using var connection = _dialect.CreateConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql =
            "INSERT INTO \"WorkUnitTrigger\" (\"WorkUnitId\", \"Kind\", \"Definition\", \"Enabled\", \"CreatedAt\") " +
            "VALUES (@WorkUnitId, @Kind, @Definition, @Enabled, @CreatedAt); " + _dialect.SelectLastInsertedId;
        return await connection.QuerySingleAsync<int>(new CommandDefinition(sql, trigger, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteTriggerAsync(int triggerId, CancellationToken cancellationToken = default)
    {
        await using var connection = _dialect.CreateConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM \"WorkUnitTrigger\" WHERE \"TriggerId\" = @Id", new { Id = triggerId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
