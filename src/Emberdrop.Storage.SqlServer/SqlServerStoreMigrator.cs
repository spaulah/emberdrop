using Emberdrop.Storage.Relational;
using FluentMigrator.Runner;

namespace Emberdrop.Storage.SqlServer;

/// <summary>Creates and upgrades the SQL Server schema. The target database must already exist.</summary>
public sealed class SqlServerStoreMigrator : IStoreMigrator
{
    private readonly FluentMigratorStoreMigrator _inner;

    /// <summary>Creates the migrator.</summary>
    public SqlServerStoreMigrator(SqlServerStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _inner = new FluentMigratorStoreMigrator(
            options.ConnectionString,
            rb => rb.AddSqlServer(),
            typeof(SqlServerStoreMigrator).Assembly);
    }

    /// <inheritdoc />
    public Task MigrateAsync(CancellationToken cancellationToken = default) => _inner.MigrateAsync(cancellationToken);
}
