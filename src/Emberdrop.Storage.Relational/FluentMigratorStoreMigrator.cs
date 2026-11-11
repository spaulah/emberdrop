using System.Reflection;
using Emberdrop.Storage;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace Emberdrop.Storage.Relational;

/// <summary>
/// Runs a provider's FluentMigrator migrations in a private service provider, so a host that uses
/// FluentMigrator for its own schema is not affected and Emberdrop does not scan the host's assemblies.
/// </summary>
public sealed class FluentMigratorStoreMigrator : IStoreMigrator
{
    private readonly string _connectionString;
    private readonly Action<IMigrationRunnerBuilder> _addDatabase;
    private readonly Assembly _migrationsAssembly;
    private readonly Func<CancellationToken, Task>? _afterMigrate;

    /// <summary>Creates the migrator.</summary>
    /// <param name="connectionString">Connection string of the database to migrate.</param>
    /// <param name="addDatabase">Selects the database dialect, for example <c>rb =&gt; rb.AddSQLite()</c>.</param>
    /// <param name="migrationsAssembly">Assembly that contains the provider's migrations and version table metadata.</param>
    /// <param name="afterMigrate">Optional provider-specific step to run after a successful migration.</param>
    public FluentMigratorStoreMigrator(
        string connectionString,
        Action<IMigrationRunnerBuilder> addDatabase,
        Assembly migrationsAssembly,
        Func<CancellationToken, Task>? afterMigrate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(addDatabase);
        ArgumentNullException.ThrowIfNull(migrationsAssembly);

        _connectionString = connectionString;
        _addDatabase = addDatabase;
        _migrationsAssembly = migrationsAssembly;
        _afterMigrate = afterMigrate;
    }

    /// <inheritdoc />
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        // FluentMigrator has no async API; keep the blocking work off the caller's thread.
        await Task.Run(() =>
        {
            using var provider = new ServiceCollection()
                .AddFluentMigratorCore()
                .ConfigureRunner(rb =>
                {
                    _addDatabase(rb);
                    rb.WithGlobalConnectionString(_connectionString)
                        .ScanIn(_migrationsAssembly).For.Migrations().For.VersionTableMetaData();
                })
                .BuildServiceProvider(validateScopes: false);

            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
        }, cancellationToken).ConfigureAwait(false);

        if (_afterMigrate is not null)
        {
            await _afterMigrate(cancellationToken).ConfigureAwait(false);
        }
    }
}
