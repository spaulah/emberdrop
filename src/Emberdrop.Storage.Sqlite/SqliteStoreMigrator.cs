using Dapper;
using Emberdrop.Storage.Relational;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;

namespace Emberdrop.Storage.Sqlite;

/// <summary>Creates and upgrades the SQLite schema.</summary>
public sealed class SqliteStoreMigrator : IStoreMigrator
{
    private readonly FluentMigratorStoreMigrator _inner;

    /// <summary>Creates the migrator.</summary>
    public SqliteStoreMigrator(SqliteStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var connectionString = options.ConnectionString;
        _inner = new FluentMigratorStoreMigrator(
            connectionString,
            rb => rb.AddSQLite(),
            typeof(SqliteStoreMigrator).Assembly,
            async cancellationToken =>
            {
                // Write-ahead logging lets readers and the writer work at the same time; the setting is stored in the database file.
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await connection.ExecuteAsync(new CommandDefinition("PRAGMA journal_mode=WAL;", cancellationToken: cancellationToken)).ConfigureAwait(false);
            });
    }

    /// <inheritdoc />
    public Task MigrateAsync(CancellationToken cancellationToken = default) => _inner.MigrateAsync(cancellationToken);
}
